#!/bin/bash
# RT-DETR 본 학습을 자동 재시도(폴백)하며 실행하는 래퍼 스크립트.
#
# train_rtdetr.py가 어떤 이유로든(네트워크 볼륨 I/O 오류, 일시적 OOM 등) 죽으면,
# 최신 체크포인트를 자동으로 찾아 --resume-from-checkpoint로 이어서 재시작한다.
# 목표는 마감(2026-08-04 08:00 KST)까지 끝내는 것이지만, 마감을 넘기더라도 학습은
# 끝까지 완료시키는 게 우선이라 시간 기준으로 재시도를 포기하지는 않는다 — 크래시가
# 나면 마감 전후 상관없이 무조건 재시도한다(MAX_RETRIES 횟수까지).
#
# 실행: bash train_rtdetr_auto.sh
set -uo pipefail

OUTPUT_DIR=/workspace/ai/checkpoints/rtdetr_v2
MAX_RETRIES=30

cd /workspace/ai/train

attempt=0
while [ "$attempt" -lt "$MAX_RETRIES" ]; do
  attempt=$((attempt + 1))

  resume_args=()
  latest=$(ls -d "$OUTPUT_DIR"/checkpoint-* 2>/dev/null | sed 's/.*checkpoint-//' | sort -n | tail -1)
  if [ -n "$latest" ]; then
    resume_args=(--resume-from-checkpoint "$OUTPUT_DIR/checkpoint-$latest")
    echo "[래퍼] 시도 $attempt: checkpoint-$latest 부터 이어서 시작"
  else
    echo "[래퍼] 시도 $attempt: 처음부터 시작 (체크포인트 없음)"
  fi

  /workspace/.venv/bin/python train_rtdetr.py \
    --train-json /workspace/data/coco_format/train.json \
    --train-images-dir /workspace/data/aihub_extracted_full/Training/원천데이터 \
    --val-json /workspace/data/coco_format/val.json \
    --val-images-dir /workspace/data/aihub_extracted_full/Validation/원천데이터 \
    --output-dir "$OUTPUT_DIR" \
    --epochs 7 --batch-size 32 --lr 1e-5 --bf16 \
    --max-eval-samples 2000 --max-images-per-material 40000 \
    "${resume_args[@]}"

  exit_code=$?
  if [ "$exit_code" -eq 0 ]; then
    echo "[래퍼] 학습 정상 종료(exit 0) — 종료"
    exit 0
  fi

  echo "[래퍼] 학습이 exit code $exit_code 로 죽음 — 10초 후 재시도 ($attempt/$MAX_RETRIES)"
  sleep 10
done

echo "[래퍼] 재시도 루프 종료 (attempt=$attempt)"
