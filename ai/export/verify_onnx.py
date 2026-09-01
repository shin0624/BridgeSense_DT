"""
익스포트된 ONNX 모델이 PyTorch 원본과 동일한 출력을 내는지 검증한다.
익스포트 후 onnxruntime으로 PyTorch 원본과 출력 일치 여부를 반드시 검증한다
("에러 없이 저장됨"과 "출력이 맞음"은 다른 문제 — RT-DETR을 레거시 tracing exporter로
냈다가 logits 오차 1.24가 났던 이력이 있음).

실행:
    python verify_onnx.py --model-type rtdetr \
        --checkpoint /home/elicer/BridgeSense_DT/ai/checkpoints/rtdetr_v2/final \
        --onnx /home/elicer/BridgeSense_DT/ai/models/rtdetr.onnx

    python verify_onnx.py --model-type deeplabv3plus \
        --checkpoint /home/elicer/BridgeSense_DT/ai/checkpoints/deeplabv3plus/final \
        --onnx /home/elicer/BridgeSense_DT/ai/models/deeplabv3plus.onnx
"""
import argparse

import numpy as np
import onnxruntime as ort
import torch
from transformers import AutoModelForObjectDetection

from export_onnx import RTDETR_SIZE, _load_deeplab

TOLERANCE = 1e-3  # 이 정도 절대 오차까지는 부동소수점 연산 순서 차이로 보고 통과 처리


def verify_rtdetr(checkpoint: str, onnx_path: str) -> bool:
    model = AutoModelForObjectDetection.from_pretrained(checkpoint).eval()
    dummy = torch.randn(1, 3, *RTDETR_SIZE)
    with torch.no_grad():
        torch_out = model(pixel_values=dummy)
    torch_logits = torch_out.logits.numpy()
    torch_boxes = torch_out.pred_boxes.numpy()

    so = ort.SessionOptions()
    so.intra_op_num_threads = 1  # MIG 슬라이스에서 pthread_setaffinity_np 에러 노이즈 방지
    session = ort.InferenceSession(onnx_path, sess_options=so, providers=["CPUExecutionProvider"])
    onnx_logits, onnx_boxes = session.run(
        ["logits", "pred_boxes"], {"pixel_values": dummy.numpy()}
    )

    logits_diff = float(np.abs(torch_logits - onnx_logits).max())
    boxes_diff = float(np.abs(torch_boxes - onnx_boxes).max())
    print(f"logits 최대 절대 오차: {logits_diff:.6f}")
    print(f"pred_boxes 최대 절대 오차: {boxes_diff:.6f}")
    return logits_diff < TOLERANCE and boxes_diff < TOLERANCE


def verify_deeplabv3plus(checkpoint: str, onnx_path: str) -> bool:
    model, config = _load_deeplab(checkpoint)
    size = config.get("image_size", 512)

    # 배치 2로 검증 — 동적 배치 축이 실제로 동작하는지도 같이 확인
    dummy = torch.randn(2, 3, size, size)
    with torch.no_grad():
        torch_logits = model(dummy).numpy()

    so = ort.SessionOptions()
    so.intra_op_num_threads = 1  # MIG 슬라이스에서 pthread_setaffinity_np 에러 노이즈 방지
    session = ort.InferenceSession(onnx_path, sess_options=so, providers=["CPUExecutionProvider"])
    (onnx_logits,) = session.run(["logits"], {"pixel_values": dummy.numpy()})

    if onnx_logits.shape != torch_logits.shape:
        print(f"shape 불일치: torch {torch_logits.shape} vs onnx {onnx_logits.shape}")
        return False

    logits_diff = float(np.abs(torch_logits - onnx_logits).max())
    # argmax(예측 클래스 맵)까지 일치하는지도 확인 — 수치 오차가 있어도 클래스가 안 바뀌면 실용상 OK
    argmax_match = float(
        (torch_logits.argmax(1) == onnx_logits.argmax(1)).mean()
    )
    print(f"logits 최대 절대 오차: {logits_diff:.6f}")
    print(f"argmax(예측 클래스) 일치율: {argmax_match * 100:.4f}%")
    return logits_diff < TOLERANCE and argmax_match > 0.9999


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-type", choices=["rtdetr", "deeplabv3plus"], required=True)
    parser.add_argument("--checkpoint", type=str, required=True)
    parser.add_argument("--onnx", type=str, required=True)
    args = parser.parse_args()

    if args.model_type == "rtdetr":
        ok = verify_rtdetr(args.checkpoint, args.onnx)
    else:
        ok = verify_deeplabv3plus(args.checkpoint, args.onnx)

    print("검증 결과: " + ("통과" if ok else "실패 — PyTorch와 ONNX 출력이 다름"))
    if not ok:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
