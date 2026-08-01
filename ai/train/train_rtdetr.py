"""
RT-DETR v2 파인튜닝 스크립트 (HuggingFace Trainer 기반).

data/coco_format/{train,val}.json + 원본 이미지 디렉터리로 `PekingU/rtdetr_v2_r18vd`를
9개 결함 클래스로 파인튜닝한다. 전처리(정규화 안 함, 640x640 리사이즈)는 베이스
체크포인트의 preprocessor_config.json을 그대로 따르며, 자세한 내용은
ai/export/model_io_spec.md 1절 참고.

강재(강재_부식, 도장_박리) 클래스 불균형(전체의 0.1%) 대응으로 학습 데이터에서 강재
포함 이미지를 오버샘플링한다 (ai/docs/AI_PIPELINE_PLAN.md 5.4절). RT-DETR의 손실은
Hungarian 매칭 기반 sigmoid focal loss라 클래스별 loss 가중치를 직접 넣으려면 모델
손실 함수 자체를 수정해야 하는 더 큰 작업이 필요해서, 이번 스크립트는 오버샘플링만
적용한다 — 부족하면 후속 작업으로 검토.

실행 (스모크 테스트, 소규모 샘플로 파이프라인 검증):
    python train_rtdetr.py \
        --train-json /workspace/data/coco_format/train.json \
        --train-images-dir /workspace/data/aihub_extracted/Training/원천데이터 \
        --val-json /workspace/data/coco_format/train.json \
        --val-images-dir /workspace/data/aihub_extracted/Training/원천데이터 \
        --output-dir /workspace/ai/checkpoints/rtdetr_smoke \
        --epochs 1 --max-train-samples 64 --max-eval-samples 32 --oversample-steel 1

실행 (본 학습, tmux 세션 안에서):
    python train_rtdetr.py \
        --train-json /workspace/data/coco_format/train.json \
        --train-images-dir /workspace/data/aihub_extracted/Training/원천데이터 \
        --val-json /workspace/data/coco_format/val.json \
        --val-images-dir /workspace/data/aihub_extracted/Validation/원천데이터 \
        --output-dir /workspace/ai/checkpoints/rtdetr \
        --epochs 15 --bf16
"""
import argparse
import types
from pathlib import Path

import torch
from pycocotools.coco import COCO
from torchmetrics.detection.mean_ap import MeanAveragePrecision
from transformers import AutoImageProcessor, AutoModelForObjectDetection, Trainer, TrainingArguments
from transformers.image_transforms import center_to_corners_format

from dataset import CocoDetectionDataset

BASE_CHECKPOINT = "PekingU/rtdetr_v2_r18vd"


def load_label_maps(coco_json: str):
    coco = COCO(coco_json)
    categories = coco.loadCats(coco.getCatIds())
    id2label = {cat["id"]: cat["name"] for cat in categories}
    label2id = {name: cat_id for cat_id, name in id2label.items()}
    return id2label, label2id


def convert_bbox_yolo_to_pascal(boxes: torch.Tensor, image_size) -> torch.Tensor:
    """cxcywh 정규화 좌표 -> xyxy 절대 픽셀 좌표. (HF object-detection 예제와 동일 로직)"""
    boxes = center_to_corners_format(boxes)
    height, width = image_size
    scale = torch.tensor([[width, height, width, height]], dtype=boxes.dtype)
    return boxes * scale


def collate_fn(batch):
    return {
        "pixel_values": torch.stack([item["pixel_values"] for item in batch]),
        "labels": [item["labels"] for item in batch],
    }


def _as_size_tuple(size):
    return tuple(size.tolist()) if hasattr(size, "tolist") else tuple(size)


def build_compute_metrics(image_processor):
    def compute_metrics(eval_pred):
        # TrainingArguments(eval_do_concat_batches=False) 덕분에 배치 단위 리스트로 들어온다.
        predictions_per_batch = eval_pred.predictions
        labels_per_batch = eval_pred.label_ids

        all_preds, all_targets = [], []
        for (logits, pred_boxes), labels in zip(predictions_per_batch, labels_per_batch):
            logits = torch.as_tensor(logits)
            pred_boxes = torch.as_tensor(pred_boxes)
            batch_size = logits.shape[0]
            target_sizes = [_as_size_tuple(labels[i]["size"]) for i in range(batch_size)]

            outputs = types.SimpleNamespace(logits=logits, pred_boxes=pred_boxes)
            processed = image_processor.post_process_object_detection(
                outputs, threshold=0.0, target_sizes=target_sizes
            )
            all_preds.extend(processed)

            for i in range(batch_size):
                boxes = convert_bbox_yolo_to_pascal(
                    torch.as_tensor(labels[i]["boxes"]), target_sizes[i]
                )
                class_labels = torch.as_tensor(labels[i]["class_labels"])
                all_targets.append({"boxes": boxes, "labels": class_labels})

        metric = MeanAveragePrecision(box_format="xyxy", class_metrics=False)
        metric.update(all_preds, all_targets)
        result = metric.compute()
        return {"map": result["map"].item(), "map_50": result["map_50"].item()}

    return compute_metrics


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--train-json", type=str, required=True)
    parser.add_argument("--train-images-dir", type=str, required=True)
    parser.add_argument("--val-json", type=str, required=True)
    parser.add_argument("--val-images-dir", type=str, required=True)
    parser.add_argument("--output-dir", type=str, required=True)
    parser.add_argument("--epochs", type=float, default=15)
    parser.add_argument("--batch-size", type=int, default=16)
    parser.add_argument("--lr", type=float, default=1e-4)
    parser.add_argument("--oversample-steel", type=int, default=10, help="강재 포함 이미지 반복 횟수")
    parser.add_argument("--max-train-samples", type=int, default=None, help="스모크 테스트용 서브셋 크기")
    parser.add_argument("--max-eval-samples", type=int, default=None, help="스모크 테스트용 서브셋 크기")
    parser.add_argument("--num-workers", type=int, default=4)
    parser.add_argument("--fp16", action="store_true")
    parser.add_argument("--bf16", action="store_true")
    args = parser.parse_args()

    id2label, label2id = load_label_maps(args.train_json)
    print(f"클래스 {len(id2label)}개: {id2label}")

    image_processor = AutoImageProcessor.from_pretrained(BASE_CHECKPOINT)
    model = AutoModelForObjectDetection.from_pretrained(
        BASE_CHECKPOINT,
        id2label=id2label,
        label2id=label2id,
        ignore_mismatched_sizes=True,
    )

    train_dataset = CocoDetectionDataset(
        images_dir=args.train_images_dir,
        annotation_json=args.train_json,
        image_processor=image_processor,
        oversample_steel=args.oversample_steel,
        max_samples=args.max_train_samples,
    )
    eval_dataset = CocoDetectionDataset(
        images_dir=args.val_images_dir,
        annotation_json=args.val_json,
        image_processor=image_processor,
        oversample_steel=1,
        max_samples=args.max_eval_samples,
    )
    print(f"train {len(train_dataset)}장, eval {len(eval_dataset)}장")

    training_args = TrainingArguments(
        output_dir=args.output_dir,
        num_train_epochs=args.epochs,
        per_device_train_batch_size=args.batch_size,
        per_device_eval_batch_size=args.batch_size,
        learning_rate=args.lr,
        warmup_ratio=0.05,
        weight_decay=1e-4,
        eval_strategy="epoch",
        save_strategy="epoch",
        save_total_limit=2,
        logging_steps=20,
        load_best_model_at_end=True,
        metric_for_best_model="map",
        greater_is_better=True,
        remove_unused_columns=False,
        eval_do_concat_batches=False,
        fp16=args.fp16,
        bf16=args.bf16,
        dataloader_num_workers=args.num_workers,
        report_to=["tensorboard"],
    )

    trainer = Trainer(
        model=model,
        args=training_args,
        train_dataset=train_dataset,
        eval_dataset=eval_dataset,
        data_collator=collate_fn,
        compute_metrics=build_compute_metrics(image_processor),
    )

    trainer.train()

    final_dir = Path(args.output_dir) / "final"
    trainer.save_model(str(final_dir))
    image_processor.save_pretrained(str(final_dir))
    print(f"최종 모델 저장 위치: {final_dir}")


if __name__ == "__main__":
    main()
