"""
SegFormer MiT-B2 파인튜닝 스크립트 (HuggingFace Trainer 기반).

data/coco_format/{train,val}.json + 원본 이미지 디렉터리로 `nvidia/mit-b2`를 10개 클래스
(배경/정상 1개 + 결함 9종)로 파인튜닝한다. 전처리(ImageNet 정규화, 512x512 리사이즈)는
베이스 체크포인트의 preprocessor_config.json을 그대로 따르며, 자세한 내용은
ai/export/model_io_spec.md 2절 참고.

COCO segmentation polygon을 픽셀 단위 클래스 마스크로 변환하는 로직은 dataset.py의
CocoSegmentationDataset에 있다(pycocotools의 annToMask로 rasterize).

강재 클래스 불균형 대응 오버샘플링은 train_rtdetr.py와 동일하게 적용
(ai/docs/AI_PIPELINE_PLAN.md 5.4절).

nvidia/mit-b2는 인코더(백본)만 있는 체크포인트라 디코드 헤드는 이번 학습에서 처음부터
학습된다 — RT-DETR처럼 사전학습된 헤드를 재사용하는 게 아니므로 필요 epoch 수가 더 많을 수 있다.

실행 (스모크 테스트, 소규모 샘플로 파이프라인 검증):
    python train_segformer.py \
        --train-json /workspace/data/coco_format/train.json \
        --train-images-dir /workspace/data/aihub_extracted_full/Training/원천데이터 \
        --val-json /workspace/data/coco_format/val.json \
        --val-images-dir /workspace/data/aihub_extracted_full/Validation/원천데이터 \
        --output-dir /workspace/ai/checkpoints/segformer_smoke \
        --epochs 1 --max-train-samples 64 --max-eval-samples 32 --oversample-steel 1

실행 (본 학습, tmux 세션 안에서):
    python train_segformer.py \
        --train-json /workspace/data/coco_format/train.json \
        --train-images-dir /workspace/data/aihub_extracted_full/Training/원천데이터 \
        --val-json /workspace/data/coco_format/val.json \
        --val-images-dir /workspace/data/aihub_extracted_full/Validation/원천데이터 \
        --output-dir /workspace/ai/checkpoints/segformer \
        --epochs 15 --bf16
"""
import argparse
from pathlib import Path

import evaluate
import numpy as np
import torch
import torch.nn.functional as F
from pycocotools.coco import COCO
from transformers import AutoImageProcessor, AutoModelForSemanticSegmentation, Trainer, TrainingArguments

from dataset import CocoSegmentationDataset

UPSAMPLE_CHUNK = 50  # compute_metrics에서 한 번에 업샘플할 이미지 수(메모리 폭발 방지, 아래 설명 참고)

BASE_CHECKPOINT = "nvidia/mit-b2"  # 인코더(백본)만 있는 체크포인트, 디코드 헤드는 새로 학습됨


def load_label_maps(coco_json: str):
    coco = COCO(coco_json)  # COCO json을 읽어서 카테고리 정보에 접근
    categories = coco.loadCats(coco.getCatIds())  # 전체 카테고리(9개 결함 클래스) 목록 로드
    id2label = {0: "배경_정상"}  # 0번은 결함 없는 배경/정상 픽셀 전용 클래스
    for cat in categories:  # RT-DETR과 순서를 맞추되 전부 +1 shift(model_io_spec.md 4절 참고)
        id2label[cat["id"] + 1] = cat["name"]  # 검출 모델의 category_id에 1을 더해 SegFormer 클래스 id로 사용
    label2id = {name: idx for idx, name in id2label.items()}  # 반대 방향(클래스명 -> id) 매핑도 함께 구성
    return id2label, label2id  # 둘 다 반환(모델 헤드 생성 시 그대로 전달됨)


def build_compute_metrics(num_labels: int):
    metric = evaluate.load("mean_iou")  # 픽셀 단위 분할 성능 지표(mIoU) 계산기 로드

    def compute_metrics(eval_pred):
        predictions = eval_pred.predictions  # 모델이 출력한 저해상도(1/4) 클래스 로짓
        if isinstance(predictions, tuple):  # 모델 출력이 여러 개라 튜플로 오는 경우 대비
            predictions = predictions[0]  # 그중 logits에 해당하는 첫 번째 요소만 사용
        logits = torch.as_tensor(predictions)  # numpy 배열을 torch 텐서로 변환, 아직 (N,10,128,128) 저해상도
        labels = eval_pred.label_ids  # 원본 해상도의 정답 클래스 마스크, (N,512,512) numpy
        target_hw = labels.shape[-2:]

        # 검증셋 전체(N장)를 한 번에 업샘플하면 float32 기준 N*10*512*512*4바이트가 필요해서
        # (2000장이면 약 21GB) 호스트 메모리를 터뜨려 OOM-killer에 죽는다(중요 버그 기록 참고).
        # 청크 단위로 나눠서 업샘플+argmax까지 끝낸 뒤 압축된 결과(uint8)만 누적한다.
        preds_chunks = []
        for start in range(0, logits.shape[0], UPSAMPLE_CHUNK):
            chunk = logits[start : start + UPSAMPLE_CHUNK]
            upsampled = F.interpolate(chunk, size=target_hw, mode="bilinear", align_corners=False)
            preds_chunks.append(upsampled.argmax(dim=1).to(torch.uint8).numpy())
        preds = np.concatenate(preds_chunks, axis=0)  # 픽셀별로 가장 확률 높은 클래스를 선택한 압축 결과

        result = metric.compute(
            predictions=preds,
            references=labels,
            num_labels=num_labels,
            ignore_index=255,  # 프로세서가 패딩 등에 채워 넣는 무시용 인덱스
            reduce_labels=False,  # 배경(0번)을 무시하지 않고 실제 클래스로 평가
        )  # 클래스별/평균 IoU, 정확도 계산
        return {"mean_iou": result["mean_iou"], "mean_accuracy": result["mean_accuracy"]}  # Trainer 로그에 남길 값만 추출

    return compute_metrics  # num_labels를 캡처한 compute_metrics 함수를 반환(Trainer에 전달됨)


def main():
    parser = argparse.ArgumentParser()  # 커맨드라인 인자 파서 생성
    parser.add_argument("--train-json", type=str, required=True)  # 학습용 COCO json 경로
    parser.add_argument("--train-images-dir", type=str, required=True)  # 학습용 이미지가 있는 디렉터리
    parser.add_argument("--val-json", type=str, required=True)  # 검증용 COCO json 경로
    parser.add_argument("--val-images-dir", type=str, required=True)  # 검증용 이미지가 있는 디렉터리
    parser.add_argument("--output-dir", type=str, required=True)  # 체크포인트·로그를 저장할 출력 디렉터리
    parser.add_argument("--epochs", type=float, default=15)  # 학습 epoch 수(디코드 헤드를 처음부터 학습하므로 넉넉하게)
    parser.add_argument("--batch-size", type=int, default=16)  # GPU당 배치 크기(train/eval 공용)
    parser.add_argument("--lr", type=float, default=6e-5)  # 학습률(SegFormer 원 논문 기준값과 비슷한 수준)
    parser.add_argument("--oversample-steel", type=int, default=10, help="강재 포함 이미지 반복 횟수")  # 강재 이미지 반복 배수
    parser.add_argument("--max-images-per-material", type=int, default=None, help="콘크리트/아스팔트/정상데이터 재질당 최대 이미지 수(강재는 항상 전부 유지)")  # 다수 클래스 다운샘플링 상한
    parser.add_argument("--max-train-samples", type=int, default=None, help="스모크 테스트용 서브셋 크기")  # 학습 서브셋 크기 제한
    parser.add_argument("--max-eval-samples", type=int, default=None, help="스모크 테스트용 서브셋 크기")  # 평가 서브셋 크기 제한
    parser.add_argument("--num-workers", type=int, default=4)  # 데이터로더 워커 프로세스 수
    parser.add_argument("--gradient-accumulation-steps", type=int, default=1, help="배치를 줄이는 대신 여러 스텝에 걸쳐 그래디언트를 누적해서 유효 배치 크기를 유지")  # OOM 대응용
    parser.add_argument("--fp16", action="store_true")  # fp16 혼합정밀 학습 여부
    parser.add_argument("--bf16", action="store_true")  # bf16 혼합정밀 학습 여부(Blackwell GPU에 권장)
    parser.add_argument(
        "--resume-from-checkpoint", type=str, default=None,
        help="이어서 학습할 체크포인트 경로 (예: /workspace/ai/checkpoints/segformer/checkpoint-XXXXX). 중단됐을 때 처음부터 다시 돌리지 않기 위함",
    )  # 지정하면 모델 가중치뿐 아니라 optimizer/scheduler/RNG 상태까지 그대로 복원해서 이어서 학습
    args = parser.parse_args()  # 실제 커맨드라인 인자를 파싱

    id2label, label2id = load_label_maps(args.train_json)  # train.json의 categories로부터 클래스 매핑 생성(배경 포함)
    print(f"클래스 {len(id2label)}개: {id2label}")  # 실제 로드된 클래스 구성을 로그로 확인

    image_processor = AutoImageProcessor.from_pretrained(BASE_CHECKPOINT)  # 베이스 체크포인트의 전처리 설정(512x512, ImageNet 정규화)을 그대로 로드
    model = AutoModelForSemanticSegmentation.from_pretrained(
        BASE_CHECKPOINT,
        id2label=id2label,  # 분류 헤드가 우리 10개 클래스(배경 포함) 이름을 알도록 지정
        label2id=label2id,  # 위와 반대 방향 매핑도 함께 지정
        ignore_mismatched_sizes=True,  # 인코더 전용 체크포인트라 디코드 헤드는 어차피 새로 초기화됨
    )  # 파인튜닝할 SegFormer 모델 로드(디코드 헤드는 새로 초기화됨)

    train_dataset = CocoSegmentationDataset(
        images_dir=args.train_images_dir,
        annotation_json=args.train_json,
        image_processor=image_processor,
        oversample_steel=args.oversample_steel,  # 학습셋에는 강재 오버샘플링 적용
        max_samples=args.max_train_samples,
        max_per_material=args.max_images_per_material,  # 다수 클래스(콘크리트/아스팔트/정상) 다운샘플링
    )  # 학습용 Dataset 인스턴스 생성
    eval_dataset = CocoSegmentationDataset(
        images_dir=args.val_images_dir,
        annotation_json=args.val_json,
        image_processor=image_processor,
        oversample_steel=1,  # 평가셋은 실제 분포를 왜곡하면 안 되므로 오버샘플링 미적용
        max_samples=args.max_eval_samples,
    )  # 검증용 Dataset 인스턴스 생성
    print(f"train {len(train_dataset)}장, eval {len(eval_dataset)}장")  # 실제 사용될 학습/검증 샘플 수 확인용 로그

    training_args = TrainingArguments(
        output_dir=args.output_dir,  # 체크포인트/로그 저장 경로
        num_train_epochs=args.epochs,  # 총 학습 epoch 수
        per_device_train_batch_size=args.batch_size,  # 학습 배치 크기
        gradient_accumulation_steps=args.gradient_accumulation_steps,  # 여러 스텝 누적으로 유효 배치 크기 유지(OOM 대응)
        per_device_eval_batch_size=args.batch_size,  # 평가 배치 크기
        learning_rate=args.lr,  # 학습률
        warmup_ratio=0.05,  # 전체 스텝의 5%를 워밍업 구간으로 사용
        weight_decay=1e-4,  # 가중치 감쇠(정규화) 계수
        eval_strategy="epoch",  # 매 epoch 종료 시 평가 수행
        save_strategy="epoch",  # 매 epoch 종료 시 체크포인트 저장
        save_total_limit=2,  # 최근 체크포인트 2개만 남기고 나머지는 자동 삭제(디스크 절약)
        logging_steps=20,  # 20 스텝마다 학습 로그 기록
        load_best_model_at_end=True,  # 학습 종료 후 가장 좋은 체크포인트를 자동으로 불러옴
        metric_for_best_model="mean_iou",  # "가장 좋은 모델"의 기준 지표로 mIoU 사용
        greater_is_better=True,  # mIoU는 높을수록 좋은 지표임을 명시
        remove_unused_columns=False,  # 커스텀 Dataset의 컬럼(labels 등)을 Trainer가 임의로 제거하지 않도록 설정
        fp16=args.fp16,  # fp16 사용 여부 전달
        bf16=args.bf16,  # bf16 사용 여부 전달
        dataloader_num_workers=args.num_workers,  # 데이터로더 워커 수 전달
        report_to=["tensorboard"],  # 학습 로그를 TensorBoard로 기록
    )  # HuggingFace Trainer에 넘길 학습 설정 객체 구성

    trainer = Trainer(
        model=model,  # 파인튜닝 대상 모델
        args=training_args,  # 위에서 구성한 학습 설정
        train_dataset=train_dataset,  # 학습 데이터셋
        eval_dataset=eval_dataset,  # 평가 데이터셋
        compute_metrics=build_compute_metrics(len(id2label)),  # 평가 시 mIoU를 계산할 함수
    )  # 학습을 실제로 수행할 Trainer 인스턴스 생성(가변 길이 라벨이 없어 기본 collate로 충분)

    trainer.train(resume_from_checkpoint=args.resume_from_checkpoint)  # 학습 루프 실행(체크포인트 지정 시 그 지점부터 이어서)

    final_dir = Path(args.output_dir) / "final"  # 최종 모델을 저장할 하위 디렉터리 경로
    trainer.save_model(str(final_dir))  # 학습이 끝난(또는 best) 모델 가중치를 저장
    image_processor.save_pretrained(str(final_dir))  # 나중에 추론/익스포트 시 동일 전처리를 재현할 수 있도록 프로세서 설정도 함께 저장
    print(f"최종 모델 저장 위치: {final_dir}")  # 저장 완료 위치를 로그로 안내


if __name__ == "__main__":
    main()  # 스크립트로 직접 실행될 때만 학습 진입점 호출
