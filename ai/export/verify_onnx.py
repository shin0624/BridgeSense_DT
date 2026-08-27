"""
익스포트된 ONNX 모델이 PyTorch 원본과 동일한 출력을 내는지 검증한다
(ai/CLAUDE.md ONNX 익스포트 규약: "익스포트 후 onnxruntime으로 PyTorch 원본과
출력 일치 여부 반드시 검증").

실행:
    python verify_onnx.py --model-type rtdetr \
        --checkpoint /workspace/ai/checkpoints/rtdetr_v2/final \
        --onnx /workspace/ai/models/rtdetr.onnx

    python verify_onnx.py --model-type segformer \
        --checkpoint /workspace/ai/checkpoints/segformer/final \
        --onnx /workspace/ai/models/segformer.onnx
"""
import argparse

import numpy as np
import onnxruntime as ort
import torch
from transformers import AutoModelForObjectDetection, AutoModelForSemanticSegmentation

from export_onnx import RTDETR_SIZE, SEGFORMER_SIZE, SegformerExportWrapper

TOLERANCE = 1e-3  # 이 정도 절대 오차까지는 부동소수점 연산 순서 차이로 보고 통과 처리


def verify_rtdetr(checkpoint: str, onnx_path: str) -> bool:
    model = AutoModelForObjectDetection.from_pretrained(checkpoint).eval()
    dummy = torch.randn(1, 3, *RTDETR_SIZE)
    with torch.no_grad():
        torch_out = model(pixel_values=dummy)
    torch_logits = torch_out.logits.numpy()
    torch_boxes = torch_out.pred_boxes.numpy()

    session = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
    onnx_logits, onnx_boxes = session.run(["logits", "pred_boxes"], {"pixel_values": dummy.numpy()})

    logits_diff = float(np.abs(torch_logits - onnx_logits).max())
    boxes_diff = float(np.abs(torch_boxes - onnx_boxes).max())
    print(f"logits 최대 절대 오차: {logits_diff:.6f}")
    print(f"pred_boxes 최대 절대 오차: {boxes_diff:.6f}")
    return logits_diff < TOLERANCE and boxes_diff < TOLERANCE


def verify_segformer(checkpoint: str, onnx_path: str) -> bool:
    base_model = AutoModelForSemanticSegmentation.from_pretrained(checkpoint).eval()
    wrapped = SegformerExportWrapper(base_model).eval()
    dummy = torch.randn(1, 3, *SEGFORMER_SIZE)
    with torch.no_grad():
        torch_logits = wrapped(dummy).numpy()

    session = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
    (onnx_logits,) = session.run(["logits"], {"pixel_values": dummy.numpy()})

    diff = float(np.abs(torch_logits - onnx_logits).max())
    print(f"logits 최대 절대 오차: {diff:.6f}")
    return diff < TOLERANCE


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-type", choices=["rtdetr", "segformer"], required=True)
    parser.add_argument("--checkpoint", type=str, required=True)
    parser.add_argument("--onnx", type=str, required=True)
    args = parser.parse_args()

    if args.model_type == "rtdetr":
        ok = verify_rtdetr(args.checkpoint, args.onnx)
    else:
        ok = verify_segformer(args.checkpoint, args.onnx)

    print("검증 결과: " + ("통과 ✅" if ok else "실패 ❌ — PyTorch와 ONNX 출력이 다름"))
    if not ok:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
