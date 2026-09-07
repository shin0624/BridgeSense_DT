"""
DeepLabV3+ (segmentation_models_pytorch) 시맨틱 분할 학습 스크립트.

SegFormer를 라이선스 문제로 제거하고 새로 도입한 분할 모델(CLAUDE.md 3절).
사전학습 가중치를 아예 쓰지 않는다(encoder_weights=None, 무작위 초기화 후 AI-Hub
데이터로 처음부터 학습) — 그래서 가중치 라이선스 문제가 원천적으로 없다.

HF Trainer를 쓰지 않는다(smp는 순수 PyTorch nn.Module). 옵티마이저·스케줄러·
체크포인트 저장·평가 루프를 직접 구현한다.

클래스: 10 (배경/정상 0 + 결함 9종 1..9), COCO category_id 0..8 에 +1 shift.
입력: 512x512 (AI-Hub 원본 해상도와 동일).
손실: Dice + Focal 조합 (강재 클래스가 0.1%로 극단적 불균형 — 단순 CE는 배경에 압도됨).
평가: mean IoU (smp.metrics), 검증셋을 청크로 나눠 누적(호스트 RAM 폭발 방지).

체크포인트 저장 규약 (export_onnx.py가 이 규약대로 로드한다):
    <output-dir>/
      final/
        model.pt        # torch.save(model.state_dict())
        config.json     # {"encoder_name", "classes", "in_channels", "image_size", "class_names"}
      checkpoint-best.pt # 학습 중 best mIoU 시점의 state_dict (재시작용 메타 포함)
      last.pt            # 매 epoch 끝 최신 state_dict (중단 시 이어서 학습)

실행 (스모크 테스트 — 파이프라인만 검증, 본 학습 전 필수):
    python train_deeplabv3plus.py \
        --train-json  /home/elicer/BridgeSense_DT/data/coco_format/train.json \
        --train-images-dir /home/elicer/BridgeSense_DT/data/aihub_extracted/Training/원천데이터 \
        --val-json    /home/elicer/BridgeSense_DT/data/coco_format/val.json \
        --val-images-dir   /home/elicer/BridgeSense_DT/data/aihub_extracted/Validation/원천데이터 \
        --output-dir  /home/elicer/BridgeSense_DT/ai/checkpoints/deeplabv3plus_smoke \
        --epochs 1 --max-train-samples 64 --max-eval-samples 32 --oversample-steel 1

실행 (본 학습):
    python train_deeplabv3plus.py \
        --train-json  /home/elicer/BridgeSense_DT/data/coco_format/train.json \
        --train-images-dir /home/elicer/BridgeSense_DT/data/aihub_extracted/Training/원천데이터 \
        --val-json    /home/elicer/BridgeSense_DT/data/coco_format/val.json \
        --val-images-dir   /home/elicer/BridgeSense_DT/data/aihub_extracted/Validation/원천데이터 \
        --output-dir  /home/elicer/BridgeSense_DT/ai/checkpoints/deeplabv3plus \
        --epochs 40 --batch-size 32 --lr 6e-4 --amp \
        --max-images-per-material 40000 --oversample-steel 10
"""
import argparse
import contextlib
import json
import os
import time
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from pycocotools.coco import COCO
from torch.utils.data import DataLoader
from torch.utils.tensorboard import SummaryWriter

import segmentation_models_pytorch as smp
from segmentation_models_pytorch.losses import DiceLoss, FocalLoss

from dataset import CocoSegmentationDataset, build_seg_transforms

NUM_CLASSES = 10  # 배경 1 + 결함 9
IMAGE_SIZE = 512


def _nullctx():
    return contextlib.nullcontext()


def load_class_names(coco_json: str) -> list[str]:
    """config.json에 남길 클래스 이름 목록. index 0 = 배경, 1..9 = 결함(+1 shift).

    train.json이 2GB에 육박해서 pycocotools COCO()로 열면 수 분 + 대량 RAM이 든다.
    categories 블록만 필요하므로 파일 앞부분을 스트리밍으로 훑어서 categories 배열만
    잘라내 파싱한다(convert_to_coco.py가 categories를 항상 포함해서 씀)."""
    # convert_to_coco.py는 dict를 images -> annotations -> categories 순으로 쓴다.
    # categories는 파일 맨 끝에 있고 작으므로(수 KB) 마지막 1MB만 읽어서 잘라낸다.
    size = os.path.getsize(coco_json)
    with open(coco_json, "rb") as f:
        if size > 1_000_000:
            f.seek(-1_000_000, os.SEEK_END)
        tail = f.read().decode("utf-8", errors="ignore")
    key = '"categories"'
    idx = tail.rfind(key)
    if idx == -1:
        # 못 찾으면(포맷이 다르면) 전체를 pycocotools로 (느리지만 확실)
        coco = COCO(coco_json)
        by_id = {c["id"]: c["name"] for c in coco.loadCats(coco.getCatIds())}
    else:
        rest = tail[idx + len(key):]
        lb = rest.find("[")
        rb = rest.find("]", lb)
        arr = json.loads(rest[lb:rb + 1])
        by_id = {c["id"]: c["name"] for c in arr}

    names = ["배경"]
    for cid in sorted(by_id):
        names.append(by_id[cid])
    # 결함 카테고리가 9개가 아니면 데이터 준비 단계에서 뭔가 어긋난 것
    if len(names) != NUM_CLASSES:
        raise ValueError(
            f"클래스 수 불일치: config는 {NUM_CLASSES}클래스를 기대하는데 "
            f"json에는 결함 {len(names) - 1}개 -> 총 {len(names)}. "
            "convert_to_coco.py 산출물을 확인할 것"
        )
    return names


class ComboLoss(torch.nn.Module):
    """Dice + Focal. 둘 다 smp.losses(MIT). 클래스 불균형에 강한 조합.

    Dice는 영역 겹침(희소 클래스에도 균등한 신호), Focal은 easy negative(배경)의
    기여를 줄인다. 가중치는 실측하며 조정 (기본 1:1)."""

    def __init__(self, dice_weight: float = 1.0, focal_weight: float = 1.0):
        super().__init__()
        self.dice = DiceLoss(mode="multiclass", from_logits=True)
        self.focal = FocalLoss(mode="multiclass")
        self.dw = dice_weight
        self.fw = focal_weight

    def forward(self, logits, target):
        return self.dw * self.dice(logits, target) + self.fw * self.focal(logits, target)


@torch.no_grad()
def evaluate(model, loader, device, num_classes: int, amp: bool):
    """검증셋 전체의 mean IoU를 청크(배치) 단위로 누적 계산.

    검증셋 전체 마스크를 한 번에 메모리에 쌓지 않는다 — SegFormer 때 UPSAMPLE_CHUNK로
    대응했던 것과 같은 이유(호스트 RAM 폭발). smp.metrics.get_stats로 배치별
    tp/fp/fn/tn 만 누적하고 마지막에 IoU를 낸다."""
    model.eval()
    tp_sum = fp_sum = fn_sum = tn_sum = None

    for batch in loader:
        pixel_values = batch["pixel_values"].to(device, non_blocking=True)
        labels = batch["labels"].to(device, non_blocking=True)
        with torch.autocast(device_type="cuda", enabled=amp) if amp else _nullctx():
            logits = model(pixel_values)
        preds = logits.argmax(dim=1)  # (B, H, W)

        tp, fp, fn, tn = smp.metrics.get_stats(
            preds, labels, mode="multiclass", num_classes=num_classes
        )
        # (B, C) -> 클래스별로 배치 합산해서 계속 누적
        tp, fp, fn, tn = tp.sum(0), fp.sum(0), fn.sum(0), tn.sum(0)
        if tp_sum is None:
            tp_sum, fp_sum, fn_sum, tn_sum = tp, fp, fn, tn
        else:
            tp_sum += tp
            fp_sum += fp
            fn_sum += fn
            tn_sum += tn

    # 클래스별 IoU (한 번도 등장하지 않은 클래스는 nan -> 평균에서 제외)
    iou_per_class = smp.metrics.iou_score(
        tp_sum, fp_sum, fn_sum, tn_sum, reduction=None
    )
    present = (tp_sum + fn_sum) > 0  # GT에 실제로 나타난 클래스만
    mean_iou = float(iou_per_class[present].mean()) if present.any() else 0.0
    return mean_iou, iou_per_class.tolist(), present.tolist()


def save_state(path: Path, model, meta: dict):
    path.parent.mkdir(parents=True, exist_ok=True)
    torch.save({"state_dict": model.state_dict(), **meta}, path)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--train-json", required=True)
    parser.add_argument("--train-images-dir", required=True)
    parser.add_argument("--val-json", required=True)
    parser.add_argument("--val-images-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--epochs", type=int, default=40)
    parser.add_argument("--batch-size", type=int, default=32)
    parser.add_argument("--lr", type=float, default=6e-4)
    parser.add_argument("--weight-decay", type=float, default=1e-4)
    parser.add_argument("--encoder-name", default="resnet34",
                        help="smp 인코더. 반드시 encoder_weights=None로 씀(가중치 라이선스 회피)")
    parser.add_argument("--dice-weight", type=float, default=1.0)
    parser.add_argument("--focal-weight", type=float, default=1.0)
    parser.add_argument("--oversample-steel", type=int, default=10,
                        help="강재 포함 이미지 반복 배수(RT-DETR과 동일 메커니즘)")
    parser.add_argument("--max-images-per-material", type=int, default=None,
                        help="콘크리트/아스팔트/정상 재질당 최대 이미지 수(강재는 항상 전부 유지)")
    parser.add_argument("--max-train-samples", type=int, default=None, help="스모크 테스트용")
    parser.add_argument("--max-eval-samples", type=int, default=None, help="스모크 테스트용")
    parser.add_argument("--num-workers", type=int, default=8)
    parser.add_argument("--amp", action="store_true", help="자동 혼합정밀(A100 권장)")
    parser.add_argument("--eval-every", type=int, default=1, help="N epoch마다 평가")
    parser.add_argument("--resume", type=str, default=None,
                        help="이어서 학습할 state 파일(예: .../last.pt)")
    args = parser.parse_args()

    device = "cuda" if torch.cuda.is_available() else "cpu"
    if device == "cpu":
        print("경고: CUDA를 못 찾음 — CPU로 진행(스모크 테스트 외에는 비현실적으로 느림)")
    amp_enabled = args.amp and device == "cuda"  # AMP는 CUDA에서만
    if args.amp and device == "cpu":
        print("경고: --amp는 CUDA에서만 동작 — CPU에서는 무시함")

    out_dir = Path(args.output_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    class_names = load_class_names(args.train_json)
    print(f"클래스 {len(class_names)}개: {class_names}")

    # --- 모델 ---
    model = smp.DeepLabV3Plus(
        encoder_name=args.encoder_name,
        encoder_weights=None,  # 반드시 None — 사전학습 가중치 다운로드 안 함 (CLAUDE.md 3절)
        in_channels=3,
        classes=NUM_CLASSES,
    ).to(device)

    # --- 데이터 ---
    train_ds = CocoSegmentationDataset(
        images_dir=args.train_images_dir,
        annotation_json=args.train_json,
        transform=build_seg_transforms(IMAGE_SIZE, train=True),
        oversample_steel=args.oversample_steel,
        max_samples=args.max_train_samples,
        max_per_material=args.max_images_per_material,
    )
    val_ds = CocoSegmentationDataset(
        images_dir=args.val_images_dir,
        annotation_json=args.val_json,
        transform=build_seg_transforms(IMAGE_SIZE, train=False),
        oversample_steel=1,  # 평가셋은 분포를 왜곡하면 안 됨
        max_samples=args.max_eval_samples,
    )
    print(f"train {len(train_ds)}장, val {len(val_ds)}장")

    train_loader = DataLoader(
        train_ds, batch_size=args.batch_size, shuffle=True,
        num_workers=args.num_workers, pin_memory=True, drop_last=True,
        persistent_workers=args.num_workers > 0,
    )
    val_loader = DataLoader(
        val_ds, batch_size=args.batch_size, shuffle=False,
        num_workers=args.num_workers, pin_memory=True,
        persistent_workers=args.num_workers > 0,
    )

    # --- 옵티마이저/스케줄러/손실 ---
    optimizer = torch.optim.AdamW(
        model.parameters(), lr=args.lr, weight_decay=args.weight_decay
    )
    steps_per_epoch = max(1, len(train_loader))
    scheduler = torch.optim.lr_scheduler.OneCycleLR(
        optimizer, max_lr=args.lr,
        total_steps=args.epochs * steps_per_epoch,
        pct_start=0.05,
    )
    criterion = ComboLoss(args.dice_weight, args.focal_weight)
    scaler = torch.cuda.amp.GradScaler(enabled=amp_enabled)
    writer = SummaryWriter(log_dir=str(out_dir / "tb"))

    start_epoch = 0
    best_miou = -1.0
    if args.resume:
        ckpt = torch.load(args.resume, map_location=device, weights_only=False)
        model.load_state_dict(ckpt["state_dict"])
        if "optimizer" in ckpt:
            optimizer.load_state_dict(ckpt["optimizer"])
        if "scheduler" in ckpt:
            scheduler.load_state_dict(ckpt["scheduler"])
        start_epoch = ckpt.get("epoch", 0)
        best_miou = ckpt.get("best_miou", -1.0)
        print(f"재시작: epoch {start_epoch} 부터, best_miou={best_miou:.4f}")

    config = {
        "encoder_name": args.encoder_name,
        "classes": NUM_CLASSES,
        "in_channels": 3,
        "image_size": IMAGE_SIZE,
        "class_names": class_names,
        "normalize_mean": [0.485, 0.456, 0.406],
        "normalize_std": [0.229, 0.224, 0.225],
    }
    (out_dir / "config.json").write_text(
        json.dumps(config, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    global_step = start_epoch * steps_per_epoch
    for epoch in range(start_epoch, args.epochs):
        model.train()
        epoch_start = time.time()
        running = 0.0
        for i, batch in enumerate(train_loader):
            pixel_values = batch["pixel_values"].to(device, non_blocking=True)
            labels = batch["labels"].to(device, non_blocking=True)

            optimizer.zero_grad(set_to_none=True)
            with torch.autocast(device_type="cuda", enabled=amp_enabled) if amp_enabled else _nullctx():
                logits = model(pixel_values)
                loss = criterion(logits, labels)
            scaler.scale(loss).backward()
            scaler.step(optimizer)
            scaler.update()
            scheduler.step()

            running += loss.item()
            global_step += 1
            if (i + 1) % 20 == 0:
                avg = running / 20
                running = 0.0
                lr_now = scheduler.get_last_lr()[0]
                print(f"epoch {epoch} step {i + 1}/{steps_per_epoch} "
                      f"loss={avg:.4f} lr={lr_now:.2e}")
                writer.add_scalar("train/loss", avg, global_step)
                writer.add_scalar("train/lr", lr_now, global_step)

        # 매 epoch 끝: last.pt 저장 (중단 대비)
        save_state(out_dir / "last.pt", model, {
            "optimizer": optimizer.state_dict(),
            "scheduler": scheduler.state_dict(),
            "epoch": epoch + 1,
            "best_miou": best_miou,
            "config": config,
        })

        if (epoch + 1) % args.eval_every == 0 or epoch + 1 == args.epochs:
            miou, iou_pc, present = evaluate(
                model, val_loader, device, NUM_CLASSES, amp_enabled
            )
            dur = time.time() - epoch_start
            print(f"[eval] epoch {epoch} mean_iou={miou:.4f} ({dur:.0f}s)")
            for name, iou, p in zip(class_names, iou_pc, present):
                tag = "" if p else " (GT 없음)"
                print(f"    {name}: IoU={iou:.4f}{tag}")
            writer.add_scalar("eval/mean_iou", miou, global_step)

            if miou > best_miou:
                best_miou = miou
                save_state(out_dir / "checkpoint-best.pt", model, {
                    "epoch": epoch + 1, "best_miou": best_miou, "config": config,
                })
                print(f"    -> best 갱신 (mean_iou={best_miou:.4f})")

    # --- 최종 저장 (export_onnx.py가 로드하는 규약) ---
    final_dir = out_dir / "final"
    final_dir.mkdir(parents=True, exist_ok=True)
    best_path = out_dir / "checkpoint-best.pt"
    if best_path.exists():
        model.load_state_dict(
            torch.load(best_path, map_location=device, weights_only=False)["state_dict"]
        )
        print(f"final: best 체크포인트(mean_iou={best_miou:.4f}) 가중치로 저장")
    torch.save(model.state_dict(), final_dir / "model.pt")
    (final_dir / "config.json").write_text(
        json.dumps(config, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    writer.close()
    print(f"최종 모델 저장 위치: {final_dir}")
    print(f"  model.pt, config.json  (export_onnx.py --model-type deeplabv3plus 로 변환)")


if __name__ == "__main__":
    main()
