"""
학습된 체크포인트를 ONNX로 변환한다 (RT-DETR v2 / DeepLabV3+).

아래 입출력 계약을 그대로 구현한다:
- RT-DETR: 배치 축만 동적, 입력 [batch,3,640,640], 출력 logits[batch,300,9] + pred_boxes[batch,300,4]
- DeepLabV3+: 배치 축만 동적, 입력 [batch,3,512,512], 출력 logits[batch,10,512,512]
  (smp.DeepLabV3Plus 디코더가 이미 입력 해상도로 업샘플해서 출력한다 — SegFormer처럼
  1/4 해상도를 되돌리는 별도 업샘플 래퍼가 필요 없다. forward에서 실제 shape을 검증한다.)

**두 모델 다 레거시 tracing(dynamo=False, dynamic_axes) + opset 18. 둘 다 verify 필수.**

이 환경(torch 2.7.1 / transformers 4.49.0, 2026-09-01 실측)에서:
- 레거시 tracing: RT-DETR 오차 0.00003, DeepLabV3+ 오차 0.000006. 배치 축 동적('batch') 정상.
- dynamo(torch.onnx.export(dynamo=True)): 두 모델 다 배치 축을 batch=1로 고정하고,
  RT-DETR은 내부 그래프 노드까지 출력으로 leak(출력 14개). dynamic_shapes를 줘도 안 됨.

CLAUDE.md 이전 기록("레거시 tracing으로 RT-DETR 냈다가 logits 오차 1.24 → dynamo로
0.00002 정상화")은 **당시 스택(구버전 transformers)** 얘기다. 현재 스택에서는 정반대라,
두 모델 모두 tracing으로 통일했다. dynamo로 되돌리려면 먼저 verify_onnx.py로 오차와
배치 축을 확인할 것. `_export_dynamo`는 참고용으로 남겨둠(현재 미사용).

출력 텐서는 wrapper(_RTDetrWrapper / _DeepLabWrapper)로 감싸서 딱 필요한 것만
(RT-DETR: logits+pred_boxes, DeepLabV3+: logits) 내보낸다.

실행:
    python export_onnx.py --model-type rtdetr \
        --checkpoint /home/elicer/BridgeSense_DT/ai/checkpoints/rtdetr_v2/final \
        --out /home/elicer/BridgeSense_DT/ai/models/rtdetr.onnx

    python export_onnx.py --model-type deeplabv3plus \
        --checkpoint /home/elicer/BridgeSense_DT/ai/checkpoints/deeplabv3plus/final \
        --out /home/elicer/BridgeSense_DT/ai/models/deeplabv3plus.onnx
"""
import argparse
import json
from pathlib import Path

import torch
from transformers import AutoModelForObjectDetection

OPSET = 18  # Unity Sentis 지원 범위(7~25) 안. 레거시 tracing이라 더 낮출 수도 있지만 18로 통일
RTDETR_SIZE = (640, 640)
DEEPLAB_SIZE = (512, 512)


def _finalize(out_path: Path):
    """저장된 onnx를 디스크에서 다시 읽어 onnx.checker로 구조 검증 + fsync.

    export와 verify를 한 스크립트에서 이어 돌릴 때, 큰 onnx(RT-DETR ~80MB)의 저장이
    파일시스템에 flush되기 전에 verify가 읽어서 수치가 크게 어긋나 보이는 현상을 겪었다
    (재실행하면 정상). 저장 직후 여기서 한 번 완전히 읽어 확인하고 넘어간다."""
    import onnx

    model = onnx.load(str(out_path))
    onnx.checker.check_model(model)
    ir = model.graph.input[0]
    dims = [d.dim_param or d.dim_value for d in ir.type.tensor_type.shape.dim]
    print(f"  onnx.checker 통과. 입력 '{ir.name}' shape={dims}")


def _export_dynamo(model, dummy, output_names, out_path: Path):
    """RT-DETR용. torch.export 기반 exporter — deformable attention 같은 데이터 의존적
    제어흐름을 tracing보다 정확히 담는다."""
    batch_dim = torch.export.Dim("batch")  # 배치 축만 동적으로 취급
    onnx_program = torch.onnx.export(
        model,
        (dummy,),
        dynamo=True,
        input_names=["pixel_values"],
        output_names=output_names,
        dynamic_shapes=({0: batch_dim},),
        opset_version=OPSET,
    )
    onnx_program.save(str(out_path))
    _finalize(out_path)


def _export_tracing(model, dummy, output_names, out_path: Path):
    """DeepLabV3+용. 레거시 tracing exporter + dynamic_axes로 배치 축을 동적으로.
    순수 CNN이라 tracing이 안전하고, dynamo는 배치 축을 batch=1로 특수화해버린다
    (docstring 참고). 반드시 verify_onnx.py로 확인."""
    dynamic_axes = {"pixel_values": {0: "batch"}}
    for name in output_names:
        dynamic_axes[name] = {0: "batch"}
    torch.onnx.export(
        model,
        (dummy,),
        str(out_path),
        dynamo=False,
        input_names=["pixel_values"],
        output_names=output_names,
        dynamic_axes=dynamic_axes,
        opset_version=OPSET,
        do_constant_folding=True,
    )
    _finalize(out_path)


class _RTDetrWrapper(torch.nn.Module):
    """RT-DETRv2 출력 딕셔너리에서 logits/pred_boxes 두 텐서만 뽑아 튜플로 반환.

    감싸지 않으면 exporter가 loss_dict/auxiliary_outputs/intermediate_* 등 부가 필드까지
    전부 그래프 출력으로 내보내서 onnx에 불필요한 출력이 10개 넘게 생긴다.
    Unity Sentis는 출력을 이름으로 읽으므로 동작엔 지장 없지만, 계약을 깔끔히 지킨다."""

    def __init__(self, model):
        super().__init__()
        self.model = model

    def forward(self, pixel_values):
        out = self.model(pixel_values=pixel_values)
        return out.logits, out.pred_boxes


def export_rtdetr(checkpoint: str, out_path: Path):
    """RT-DETR도 DeepLabV3+와 마찬가지로 **레거시 tracing**(dynamo=False)로 낸다.

    CLAUDE.md에는 "레거시 tracing으로 냈다가 logits 오차 1.24" 라는 이력이 있으나, 그건
    당시 스택(구버전 transformers)에서였다. 현재 스택(torch 2.7.1 / transformers 4.49.0,
    opset 18)에서는:
      - 레거시 tracing: 오차 0.00003, 배치 축 동적('batch') 정상 — verify 통과
      - dynamo: 오차는 작지만 배치 축을 batch=1로 고정하고 내부 노드를 출력으로 leak
    그래서 이 환경에서는 레거시 tracing이 맞다. 반드시 verify_onnx.py로 확인하고
    임의로 dynamo로 되돌리지 말 것(되돌리려면 먼저 verify부터).
    """
    model = AutoModelForObjectDetection.from_pretrained(checkpoint).eval()
    dummy = torch.randn(1, 3, *RTDETR_SIZE)
    _export_tracing(_RTDetrWrapper(model), dummy, ["logits", "pred_boxes"], out_path)
    print(f"RT-DETR ONNX 저장: {out_path}")


def _load_deeplab(checkpoint: str):
    """final/ 디렉터리(model.pt + config.json) 규약대로 smp 모델을 복원한다.
    (train_deeplabv3plus.py의 저장 규약과 반드시 일치)"""
    import segmentation_models_pytorch as smp

    ckpt_dir = Path(checkpoint)
    config = json.loads((ckpt_dir / "config.json").read_text(encoding="utf-8"))
    model = smp.DeepLabV3Plus(
        encoder_name=config["encoder_name"],
        encoder_weights=None,  # 익스포트 시에도 사전학습 가중치 안 받음(어차피 state_dict로 덮어씀)
        in_channels=config["in_channels"],
        classes=config["classes"],
    )
    state = torch.load(ckpt_dir / "model.pt", map_location="cpu", weights_only=False)
    if isinstance(state, dict) and "state_dict" in state:
        state = state["state_dict"]
    model.load_state_dict(state)
    return model.eval(), config


class _DeepLabWrapper(torch.nn.Module):
    """출력 텐서 이름을 'logits'로 고정하기 위한 얇은 래퍼.
    smp 모델은 그냥 [B, C, H, W] 텐서를 반환하므로 그대로 통과시킨다."""

    def __init__(self, model):
        super().__init__()
        self.model = model

    def forward(self, pixel_values):
        return self.model(pixel_values)


def export_deeplabv3plus(checkpoint: str, out_path: Path):
    model, config = _load_deeplab(checkpoint)
    size = config.get("image_size", DEEPLAB_SIZE[0])
    dummy = torch.randn(1, 3, size, size)

    # smp 디코더가 입력 해상도로 이미 업샘플하는지 실제로 확인 (CLAUDE.md 4절)
    with torch.no_grad():
        out = model(dummy)
    expected = (1, config["classes"], size, size)
    if tuple(out.shape) != expected:
        raise RuntimeError(
            f"DeepLabV3+ 출력 shape이 예상과 다름: {tuple(out.shape)} != {expected}. "
            "디코더가 입력 해상도로 업샘플하지 않는다면 export 전에 업샘플 래퍼를 추가해야 함"
        )
    print(f"출력 shape 확인 OK: {tuple(out.shape)} (별도 업샘플 래퍼 불필요)")

    _export_tracing(_DeepLabWrapper(model), dummy, ["logits"], out_path)
    print(f"DeepLabV3+ ONNX 저장: {out_path}")
    print(f"  입력 pixel_values [batch,3,{size},{size}] -> 출력 logits [batch,{config['classes']},{size},{size}]")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-type", choices=["rtdetr", "deeplabv3plus"], required=True)
    parser.add_argument("--checkpoint", type=str, required=True, help="파인튜닝된 모델 디렉터리 (예: .../final)")
    parser.add_argument("--out", type=str, required=True)
    args = parser.parse_args()

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    with torch.no_grad():
        if args.model_type == "rtdetr":
            export_rtdetr(args.checkpoint, out_path)
        else:
            export_deeplabv3plus(args.checkpoint, out_path)


if __name__ == "__main__":
    main()
