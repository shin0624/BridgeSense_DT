"""
학습된 RT-DETR v2 / SegFormer 체크포인트를 ONNX로 변환한다.

model_io_spec.md에 정의된 입출력 계약을 그대로 구현한다:
- 공통: 배치 축만 동적
- RT-DETR: 입력 [batch,3,640,640], 출력 logits[batch,300,9] + pred_boxes[batch,300,4]
- SegFormer: 입력 [batch,3,512,512], 출력 logits[batch,10,512,512]
  (원본은 1/4 해상도로 나오는데, Unity에서 다시 업샘플 구현 안 해도 되게
  SegformerExportWrapper로 감싸서 입력 해상도로 업샘플까지 그래프에 포함시켜 내보낸다)

**opset 18, dynamo 기반 exporter 사용** — 처음에 레거시 tracing 방식(torch.onnx.export
기본 동작)으로 RT-DETR을 내보냈더니 PyTorch 원본과 출력이 크게 어긋났음(logits 최대
오차 1.24). RT-DETR의 deformable attention처럼 데이터 의존적 제어흐름이 많은 모델은
tracing이 이런 걸 못 담아서 그래프가 실제 연산과 달라짐. `dynamo=True`(torch.export
기반) exporter로 바꾸니 오차가 0.00002 수준으로 정상화됨. SegFormer는 구조가 단순해서
tracing으로도 될 가능성이 높지만, 안정성을 위해 두 모델 다 dynamo 방식으로 통일함 —
반드시 verify_onnx.py로 실제 오차를 확인하고 임의로 되돌리지 말 것.

실행:
    python export_onnx.py --model-type rtdetr \
        --checkpoint /workspace/ai/checkpoints/rtdetr_v2/final \
        --out /workspace/ai/models/rtdetr.onnx

    python export_onnx.py --model-type segformer \
        --checkpoint /workspace/ai/checkpoints/segformer/final \
        --out /workspace/ai/models/segformer.onnx
"""
import argparse
from pathlib import Path

import torch
import torch.nn as nn
import torch.nn.functional as F
from transformers import AutoModelForObjectDetection, AutoModelForSemanticSegmentation

OPSET = 18  # dynamo exporter가 요구하는 최소 opset(18 미만은 변환 자체가 거부됨). model_io_spec.md의 7~25 범위 안에 있음
RTDETR_SIZE = (640, 640)
SEGFORMER_SIZE = (512, 512)


class SegformerExportWrapper(nn.Module):
    """model_io_spec.md 2.3절: 1/4 해상도로 나오는 logits를 입력 해상도로 업샘플해서 내보낸다."""

    def __init__(self, model, out_size=SEGFORMER_SIZE):
        super().__init__()
        self.model = model
        self.out_size = out_size

    def forward(self, pixel_values):
        logits = self.model(pixel_values=pixel_values).logits
        return F.interpolate(logits, size=self.out_size, mode="bilinear", align_corners=False)


def _export(model, dummy, output_names, out_path: Path):
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


def export_rtdetr(checkpoint: str, out_path: Path):
    model = AutoModelForObjectDetection.from_pretrained(checkpoint).eval()
    dummy = torch.randn(1, 3, *RTDETR_SIZE)
    _export(model, dummy, ["logits", "pred_boxes"], out_path)
    print(f"RT-DETR ONNX 저장: {out_path}")


def export_segformer(checkpoint: str, out_path: Path):
    base_model = AutoModelForSemanticSegmentation.from_pretrained(checkpoint).eval()
    wrapped = SegformerExportWrapper(base_model).eval()
    dummy = torch.randn(1, 3, *SEGFORMER_SIZE)
    _export(wrapped, dummy, ["logits"], out_path)
    print(f"SegFormer ONNX 저장: {out_path}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-type", choices=["rtdetr", "segformer"], required=True)
    parser.add_argument("--checkpoint", type=str, required=True, help="파인튜닝된 모델 디렉터리 (예: .../final)")
    parser.add_argument("--out", type=str, required=True)
    args = parser.parse_args()

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    with torch.no_grad():
        if args.model_type == "rtdetr":
            export_rtdetr(args.checkpoint, out_path)
        else:
            export_segformer(args.checkpoint, out_path)


if __name__ == "__main__":
    main()
