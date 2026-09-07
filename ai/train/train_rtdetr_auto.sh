#!/bin/bash
# RT-DETR 본 학습을 자동 재시도(폴백)하며 실행하는 래퍼 스크립트.
#
# train_rtdetr.py가 어떤 이유로든(네트워크 볼륨 I/O 오류, 일시적 OOM 등) 죽으면,
# 최신 체크포인트를 자동으로 찾아 --resume-from-checkpoint로 이어서 재시작한다.
# 크래시가 나면 무조건 재시도한다(MAX_RETRIES 횟수까지).
#
# 경로는 엘리스 AI 클라우드 기준(2026-09-01). 데이터 준비(convert_to_coco.py)를
# 먼저 끝내서 data/coco_format/{train,val}.json 이 있어야 한다.
#
# 실행: bash train_rtdetr_auto.sh
set -uo pipefail

REPO=/home/elicer/BridgeSense_DT
VENV=$REPO/.venv/bin/python
OUTPUT_DIR=$REPO/ai/checkpoints/rtdetr_v2
MAX_RETRIES=30

cd "$REPO/ai/train"

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

  "$VENV" train_rtdetr.py \
    --train-json "$REPO/data/coco_format/train.json" \
    --train-images-dir "$REPO/data/aihub_extracted/Training/원천데이터" \
    --val-json "$REPO/data/coco_format/val.json" \
    --val-images-dir "$REPO/data/aihub_extracted/Validation/원천데이터" \
    --output-dir "$OUTPUT_DIR" \
    --epochs 15 --batch-size 32 --lr 1e-5 --bf16 \
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
