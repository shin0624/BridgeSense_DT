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
        --train-images-dir /workspace/data/aihub_extracted_full/Training/원천데이터 \
        --val-json /workspace/data/coco_format/val.json \
        --val-images-dir /workspace/data/aihub_extracted_full/Validation/원천데이터 \
        --output-dir /workspace/ai/checkpoints/rtdetr_smoke \
        --epochs 1 --max-train-samples 64 --max-eval-samples 32 --oversample-steel 1

실행 (본 학습, tmux 세션 안에서):
    python train_rtdetr.py \
        --train-json /workspace/data/coco_format/train.json \
        --train-images-dir /workspace/data/aihub_extracted_full/Training/원천데이터 \
        --val-json /workspace/data/coco_format/val.json \
        --val-images-dir /workspace/data/aihub_extracted_full/Validation/원천데이터 \
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

BASE_CHECKPOINT = "PekingU/rtdetr_v2_r18vd"  # 파인튜닝할 베이스 체크포인트(COCO 80클래스로 사전학습됨)


def load_label_maps(coco_json: str):
    coco = COCO(coco_json)  # COCO json을 읽어서 카테고리 정보에 접근
    categories = coco.loadCats(coco.getCatIds())  # 전체 카테고리(9개 결함 클래스) 목록 로드
    id2label = {cat["id"]: cat["name"] for cat in categories}  # category_id -> 클래스명 매핑(모델 config에 필요)
    label2id = {name: cat_id for cat_id, name in id2label.items()}  # 반대 방향(클래스명 -> id) 매핑도 함께 구성
    
    return id2label, label2id  # 둘 다 반환(모델 헤드 리사이즈 시 그대로 전달됨)


def convert_bbox_yolo_to_pascal(boxes: torch.Tensor, image_size) -> torch.Tensor:
    """cxcywh 정규화 좌표를 xyxy 절대 픽셀 좌표로 변환하는 메서드"""
    boxes = center_to_corners_format(boxes)  # (cx,cy,w,h) -> (x_min,y_min,x_max,y_max), 아직 [0,1] 정규화 상태
    
    height, width = image_size  # 타깃 이미지의 (높이, 너비) 픽셀 크기
    
    scale = torch.tensor([[width, height, width, height]], dtype=boxes.dtype)  # 정규화 좌표를 픽셀 단위로 바꿀 스케일 벡터
    
    return boxes * scale  # 브로드캐스팅 곱셈으로 절대 픽셀 좌표의 xyxy 박스 반환


def collate_fn(batch):
    return {
        "pixel_values": torch.stack([item["pixel_values"] for item in batch]),  # 고정 640x640이라 그냥 스택만 하면 됨(패딩 불필요)
        "labels": [item["labels"] for item in batch],  # 라벨은 샘플마다 박스 개수가 달라서 텐서로 안 합치고 리스트 그대로 둠
    }


def _as_size_tuple(size):
    return tuple(size.tolist()) if hasattr(size, "tolist") else tuple(size)  # 텐서든 리스트든 (height, width) 파이썬 튜플로 통일


class DetectionTrainer(Trainer):
    """RT-DETRv2 출력에 loss_dict/auxiliary_outputs/intermediate_* 등 부가 필드가 많아서,
    Trainer 기본 동작(로스 제외 전부를 predictions로 수집)에 맡기면 compute_metrics에서
    (logits, pred_boxes) 2개만 기대한 언패킹이 깨진다. logits/pred_boxes만 명시적으로 뽑도록
    prediction_step을 오버라이드한다."""

    def prediction_step(self, model, inputs, prediction_loss_only, ignore_keys=None):
        inputs = self._prepare_inputs(inputs)  # 텐서들을 모델과 같은 디바이스(GPU)로 이동
        labels = inputs.get("labels")  # compute_metrics에서 정답으로 쓸 라벨을 미리 꺼내둠
        with torch.no_grad():  # 평가 단계라 그래디언트 계산 불필요
            outputs = model(**inputs)  # 순전파(라벨이 있으니 loss도 함께 계산됨)
        loss = outputs.loss.detach() if outputs.loss is not None else None  # 로깅용 loss 값
        if prediction_loss_only:  # loss만 필요한 호출인 경우(이번 스크립트에서는 안 쓰임)
            return (loss, None, None)  # 예측값 없이 loss만 반환
        predictions = (outputs.logits.detach(), outputs.pred_boxes.detach())  # 필요한 두 값만 명시적으로 추출
        return (loss, predictions, labels)  # compute_metrics가 기대하는 (logits, pred_boxes) 튜플로 고정


def build_compute_metrics(image_processor):
    def compute_metrics(eval_pred):
        # TrainingArguments(eval_do_concat_batches=False) 덕분에 배치 단위 리스트로 들어온다.
        predictions_per_batch = eval_pred.predictions  # 배치별 (logits, pred_boxes) 튜플들의 리스트
        labels_per_batch = eval_pred.label_ids  # 배치별 라벨(딕셔너리 리스트)들의 리스트

        all_preds, all_targets = [], []  # torchmetrics에 넘길 전체 예측/정답 리스트(이미지 단위로 누적)
        
        for (logits, pred_boxes), labels in zip(predictions_per_batch, labels_per_batch):  # 배치를 하나씩 순회
            logits = torch.as_tensor(logits)  # numpy로 넘어온 값을 다시 torch 텐서로 변환
            pred_boxes = torch.as_tensor(pred_boxes)  # 마찬가지로 pred_boxes도 torch 텐서로 변환
            batch_size = logits.shape[0]  # 이번 배치에 들어있는 이미지 수
            target_sizes = [_as_size_tuple(labels[i]["size"]) for i in range(batch_size)]  # 각 샘플이 실제 리사이즈된 (h, w) 목록

            outputs = types.SimpleNamespace(logits=logits, pred_boxes=pred_boxes)  # post_process 메서드가 요구하는 형태(.logits/.pred_boxes 속성)로 감싸기
            processed = image_processor.post_process_object_detection(
                outputs, threshold=0.05, target_sizes=target_sizes
            )  # sigmoid+박스 변환까지 포함한 공식 후처리로 예측을 {scores,labels,boxes} 딕셔너리 리스트로 변환. threshold=0.0으로 두면 이미지당 300개 쿼리를 전부 유지해서 검증셋 전체(4만 장+)에서 메모리 초과로 죽었던 이력이 있어 0.05로 필터링
            all_preds.extend(processed)  # 이번 배치의 이미지별 예측들을 전체 리스트에 추가

            for i in range(batch_size):  # 이번 배치의 이미지들을 하나씩 순회하며 정답(target)도 같은 형식으로 변환
                boxes = convert_bbox_yolo_to_pascal(
                    torch.as_tensor(labels[i]["boxes"]), target_sizes[i]
                )  # 정답 박스도 예측과 같은 xyxy 절대 좌표 공간으로 변환
                class_labels = torch.as_tensor(labels[i]["class_labels"])  # 정답 클래스 id들
                all_targets.append({"boxes": boxes, "labels": class_labels})  # torchmetrics가 기대하는 타깃 딕셔너리 형태로 추가

        metric = MeanAveragePrecision(box_format="xyxy", class_metrics=False)  # xyxy 좌표 기준 mAP 계산기 생성
        metric.update(all_preds, all_targets)  # 전체 평가셋에 대한 예측/정답을 한 번에 누적
        result = metric.compute()  # 누적된 값으로 최종 mAP 지표 계산
        return {"map": result["map"].item(), "map_50": result["map_50"].item()}  # Trainer 로그에 남길 스칼라 값만 추출해서 반환

    return compute_metrics  # image_processor를 캡처한 compute_metrics 함수를 반환(Trainer에 전달됨)


def main():
    parser = argparse.ArgumentParser()  # 커맨드라인 인자 파서 생성
    parser.add_argument("--train-json", type=str, required=True)  # 학습용 COCO json 경로
    parser.add_argument("--train-images-dir", type=str, required=True)  # 학습용 이미지가 있는 디렉터리
    parser.add_argument("--val-json", type=str, required=True)  # 검증용 COCO json 경로
    parser.add_argument("--val-images-dir", type=str, required=True)  # 검증용 이미지가 있는 디렉터리
    parser.add_argument("--output-dir", type=str, required=True)  # 체크포인트·로그를 저장할 출력 디렉터리
    parser.add_argument("--epochs", type=float, default=15)  # 학습 epoch 수(기본 15, 스모크 테스트 시 1 등으로 낮춰 사용)
    parser.add_argument("--batch-size", type=int, default=16)  # GPU당 배치 크기(train/eval 공용)
    parser.add_argument("--lr", type=float, default=1e-5)  # 학습률(1e-4는 사전학습 백본까지 통째로 망가뜨려 eval_map이 0으로 발산했던 이력이 있어 1e-5로 낮춤)
    parser.add_argument("--oversample-steel", type=int, default=10, help="강재 포함 이미지 반복 횟수")  # 강재 이미지 반복 배수
    parser.add_argument("--max-images-per-material", type=int, default=None, help="콘크리트/아스팔트/정상데이터 재질당 최대 이미지 수(강재는 항상 전부 유지)")  # 다수 클래스 다운샘플링 상한
    parser.add_argument("--max-train-samples", type=int, default=None, help="스모크 테스트용 서브셋 크기")  # 학습 서브셋 크기 제한
    parser.add_argument("--max-eval-samples", type=int, default=None, help="스모크 테스트용 서브셋 크기")  # 평가 서브셋 크기 제한
    parser.add_argument("--num-workers", type=int, default=4)  # 데이터로더 워커 프로세스 수
    parser.add_argument("--fp16", action="store_true")  # fp16 혼합정밀 학습 여부
    parser.add_argument("--bf16", action="store_true")  # bf16 혼합정밀 학습 여부(Blackwell GPU에 권장)
    parser.add_argument(
        "--resume-from-checkpoint", type=str, default=None,
        help="이어서 학습할 체크포인트 경로 (예: /workspace/ai/checkpoints/rtdetr/checkpoint-127200). 중단됐을 때 처음부터 다시 돌리지 않기 위함",
    )  # 지정하면 모델 가중치뿐 아니라 optimizer/scheduler/RNG 상태까지 그대로 복원해서 이어서 학습
    args = parser.parse_args()  # 실제 커맨드라인 인자를 파싱

    id2label, label2id = load_label_maps(args.train_json)  # train.json의 categories로부터 클래스 매핑 생성
    print(f"클래스 {len(id2label)}개: {id2label}")  # 실제 로드된 클래스 구성을 로그로 확인

    image_processor = AutoImageProcessor.from_pretrained(BASE_CHECKPOINT)  # 베이스 체크포인트의 전처리 설정(640x640, 정규화 없음)을 그대로 로드
    model = AutoModelForObjectDetection.from_pretrained(
        BASE_CHECKPOINT,
        id2label=id2label,  # 분류 헤드가 9개 클래스 이름을 알도록 지정
        label2id=label2id,  # 위와 반대 방향 매핑도 함께 지정
        ignore_mismatched_sizes=True,  # 80클래스 사전학습 헤드를 9클래스로 새로 초기화하도록 허용
    )  # 파인튜닝할 RT-DETR v2 모델 로드(분류 헤드는 새로 초기화됨)

    train_dataset = CocoDetectionDataset(
        images_dir=args.train_images_dir,
        annotation_json=args.train_json,
        image_processor=image_processor,
        oversample_steel=args.oversample_steel,  # 학습셋에는 강재 오버샘플링 적용
        max_samples=args.max_train_samples,
        max_per_material=args.max_images_per_material,  # 다수 클래스(콘크리트/아스팔트/정상) 다운샘플링
    )  # 학습용 Dataset 인스턴스 생성
    
    eval_dataset = CocoDetectionDataset(
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
        per_device_eval_batch_size=args.batch_size,  # 평가 배치 크기
        learning_rate=args.lr,  # 학습률
        warmup_ratio=0.05,  # 전체 스텝의 5%를 워밍업 구간으로 사용
        weight_decay=1e-4,  # 가중치 감쇠(정규화) 계수
        eval_strategy="epoch",  # 매 epoch 종료 시 평가 수행
        save_strategy="epoch",  # 매 epoch 종료 시 체크포인트 저장
        save_total_limit=2,  # 최근 체크포인트 2개만 남기고 나머지는 자동 삭제(디스크 절약)
        logging_steps=20,  # 20 스텝마다 학습 로그 기록
        load_best_model_at_end=True,  # 학습 종료 후 가장 좋은 체크포인트를 자동으로 불러옴
        metric_for_best_model="map",  # "가장 좋은 모델"의 기준 지표로 mAP 사용
        greater_is_better=True,  # mAP는 높을수록 좋은 지표임을 명시
        remove_unused_columns=False,  # 커스텀 Dataset의 컬럼(labels 등)을 Trainer가 임의로 제거하지 않도록 설정
        eval_do_concat_batches=False,  # 라벨이 리스트-of-딕셔너리라 배치 결과를 강제로 이어붙이지 않도록 설정
        fp16=args.fp16,  # fp16 사용 여부 전달
        bf16=args.bf16,  # bf16 사용 여부 전달
        dataloader_num_workers=args.num_workers,  # 데이터로더 워커 수 전달
        report_to=["tensorboard"],  # 학습 로그를 TensorBoard로 기록
    )  # HuggingFace Trainer에 넘길 학습 설정 객체 구성

    trainer = DetectionTrainer(
        model=model,  # 파인튜닝 대상 모델
        args=training_args,  # 위에서 구성한 학습 설정
        train_dataset=train_dataset,  # 학습 데이터셋
        eval_dataset=eval_dataset,  # 평가 데이터셋
        data_collator=collate_fn,  # 배치 구성 방식(커스텀 collate_fn)
        compute_metrics=build_compute_metrics(image_processor),  # 평가 시 mAP를 계산할 함수
    )  # 학습을 실제로 수행할 Trainer 인스턴스 생성

    trainer.train(resume_from_checkpoint=args.resume_from_checkpoint)  # 학습 루프 실행(체크포인트 지정 시 그 지점부터 이어서)

    final_dir = Path(args.output_dir) / "final"  # 최종 모델을 저장할 하위 디렉터리 경로
    trainer.save_model(str(final_dir))  # 학습이 끝난(또는 best) 모델 가중치를 저장
    image_processor.save_pretrained(str(final_dir))  # 나중에 추론/익스포트 시 동일 전처리를 재현할 수 있도록 프로세서 설정도 함께 저장
    print(f"최종 모델 저장 위치: {final_dir}")  # 저장 완료 위치를 로그로 안내


if __name__ == "__main__":
    main()  # 스크립트로 직접 실행될 때만 학습 진입점 호출
