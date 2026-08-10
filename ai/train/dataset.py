"""
COCO 포맷(convert_to_coco.py 산출물) 공용 Dataset 로더.

train_rtdetr.py / train_segformer.py에서 공유. 강재(강재_부식, 도장_박리) 클래스가
전체의 0.1%뿐인 불균형 문제 대응으로, 강재 어노테이션을 포함한 이미지를 오버샘플링하는
옵션을 제공한다 (배경: ai/docs/AI_PIPELINE_PLAN.md 5.4절).
"""
import random
import time
from pathlib import Path

import numpy as np
from PIL import Image
from pycocotools.coco import COCO
from torch.utils.data import Dataset

# convert_to_coco.py의 CLASS_NAMES 순서와 동일한 이름으로 강재 클래스를 식별한다.
STEEL_CLASS_NAMES = {"강재_부식", "도장_박리"}  # 오버샘플링 대상 판별용 강재 클래스명 집합

# 클래스명 -> 재질 매핑. 다수 클래스(콘크리트/아스팔트/정상데이터) 다운샘플링에 사용
MATERIAL_MAP = {
    "콘크리트_균열": "콘크리트", "백태": "콘크리트", "누수": "콘크리트", "박락": "콘크리트", "철근_노출": "콘크리트",
    "아스팔트_균열": "아스팔트", "함몰": "아스팔트",
    "강재_부식": "강재", "도장_박리": "강재",
}


def open_image_with_retry(path: Path, retries: int = 3, delay: float = 1.0):
    """/workspace가 네트워크 볼륨이라 파일 읽기가 가끔(OSError: No such device or address)
    일시적으로 실패한다 — 장시간 학습 도중 이런 이유로 전체 프로세스가 죽는 걸 막기 위한 재시도."""
    last_error = None  # 마지막으로 발생한 예외를 기억해뒀다가 재시도 다 실패하면 그대로 올리기 위함
    for attempt in range(retries):  # 지정된 횟수만큼 시도
        try:
            return Image.open(path).convert("RGB")  # 성공하면 바로 반환
        except OSError as e:  # 네트워크 볼륨의 일시적 I/O 오류(예: Errno 6) 캐치
            last_error = e  # 실패 원인 기록
            time.sleep(delay * (attempt + 1))  # 재시도 전 잠깐 대기(뒤로 갈수록 조금씩 더 기다림)
    raise last_error  # 재시도를 다 써도 안 되면 원래 예외를 그대로 발생시킴


def build_balanced_index(coco: COCO, oversample_steel: int = 1, max_per_material: int | None = None):
    """다수 클래스(콘크리트/아스팔트/정상데이터) 다운샘플링 + 강재 오버샘플링을 함께 적용해서
    최종 image_id 리스트를 만든다(검출/분할 공용). max_per_material을 지정하면 재질당 최대
    이 장수까지만 남기고 무작위로 줄인다(강재는 원래도 희소해서 절대 줄이지 않음) — 학습
    스텝 수 자체를 줄여서 시간을 단축하기 위함(ai/docs/AI_PIPELINE_PLAN.md 5.4절 참고)."""
    image_ids = sorted(coco.imgs.keys())  # 전체 이미지 id를 정렬된 리스트로 확보(순서 고정용)

    if max_per_material is not None:  # 다수 클래스 다운샘플링을 쓰는 경우
        cat_material = {
            cid: MATERIAL_MAP.get(cat["name"], "기타") for cid, cat in coco.cats.items()
        }  # category_id -> 재질명 매핑
        image_materials = {img_id: set() for img_id in image_ids}  # 이미지별로 어떤 재질을 포함하는지 모을 자리
        for ann in coco.anns.values():  # 모든 어노테이션을 순회하며
            image_materials[ann["image_id"]].add(cat_material[ann["category_id"]])  # 해당 이미지의 재질 집합에 추가

        rng = random.Random(42)  # 재현 가능하도록 고정 시드 사용
        selected, selected_set = [], set()  # 최종 선택된 이미지 목록과 중복 방지용 집합

        steel_ids = [i for i in image_ids if "강재" in image_materials[i]]  # 강재 포함 이미지는 무조건 전부 유지
        selected.extend(steel_ids)
        selected_set.update(steel_ids)

        for material in ["콘크리트", "아스팔트", "정상"]:  # 다수 클래스들을 하나씩 처리
            if material == "정상":  # 정상데이터는 어노테이션이 아예 없는 이미지
                candidates = [i for i in image_ids if not image_materials[i] and i not in selected_set]
            else:
                candidates = [i for i in image_ids if material in image_materials[i] and i not in selected_set]
            if len(candidates) > max_per_material:  # 상한을 넘으면 무작위로 줄임
                candidates = rng.sample(candidates, max_per_material)
            selected.extend(candidates)
            selected_set.update(candidates)

        image_ids = sorted(selected)  # 다운샘플링된 최종 이미지 id 목록
        print(f"다수 클래스 다운샘플링: 재질당 최대 {max_per_material}장으로 제한 (결과 {len(image_ids)}장)")

    if oversample_steel <= 1:  # 오버샘플링을 안 쓰는 경우(기본값 1 이하)
        return image_ids  # 지금까지 결정된 순서 그대로 반환하고 종료

    steel_cat_ids = {
        cid for cid, cat in coco.cats.items() if cat["name"] in STEEL_CLASS_NAMES
    }  # 카테고리 이름이 강재 계열인 category_id들만 모음
    steel_image_ids = {
        ann["image_id"] for ann in coco.anns.values() if ann["category_id"] in steel_cat_ids
    }  # 그 강재 category_id를 어노테이션으로 갖고 있는 image_id 집합(중복 제거)
    extra = [img_id for img_id in image_ids if img_id in steel_image_ids]  # 강재 포함 이미지만 원본 순서대로 추출
    result = image_ids + extra * (oversample_steel - 1)  # 원본 전체 + 강재 이미지를 (배수-1)번 추가로 이어붙임
    print(
        f"강재 오버샘플링: 강재 포함 이미지 {len(steel_image_ids)}장을 "
        f"{oversample_steel}배로 반복 (총 {len(result)}장, 원본 {len(image_ids)}장)"
    )  # 오버샘플링 결과를 콘솔에 로그로 남김(실제 반복 배수·최종 장수 확인용)
    return result  # 오버샘플링이 반영된 최종 인덱스 리스트 반환


class CocoDetectionDataset(Dataset):
    """RT-DETR류(HF DetrImageProcessor 인터페이스) 학습용 COCO Dataset."""

    def __init__(
        self,
        images_dir: str,
        annotation_json: str,
        image_processor,
        oversample_steel: int = 1,
        max_samples: int | None = None,
        max_per_material: int | None = None,
    ):
        self.images_dir = Path(images_dir)  # 원본 이미지가 들어있는 디렉터리 경로 저장
        self.coco = COCO(annotation_json)  # COCO json을 읽어서 이미지/어노테이션/카테고리 인덱스를 구성
        self.image_processor = image_processor  # RT-DETR용 HF 이미지 프로세서(리사이즈·정규화·라벨 인코딩 담당)
        self.image_ids = build_balanced_index(self.coco, oversample_steel, max_per_material)  # 다운샘플링+오버샘플링 반영된 이미지 id 목록
        if max_samples is not None:  # 스모크 테스트처럼 일부만 쓰고 싶을 때
            self.image_ids = self.image_ids[:max_samples]  # 앞에서부터 max_samples개만 남기고 잘라냄

    def __len__(self):
        return len(self.image_ids)  # Dataset 프로토콜: 전체 샘플 수(오버샘플링 반영된 길이) 반환

    def __getitem__(self, idx, _skip_count: int = 0):
        image_id = self.image_ids[idx]  # idx번째로 순회할 실제 image_id를 조회(중복 있을 수 있음)
        img_info = self.coco.imgs[image_id]  # 해당 image_id의 메타정보(파일명, 폭/높이 등) 조회
        path = self.images_dir / img_info["file_name"]  # 실제 이미지 파일 경로
        try:
            image = open_image_with_retry(path)  # 이미지 파일을 열어 RGB로 통일(네트워크 볼륨 일시 오류는 재시도)
        except OSError as e:  # 재시도까지 다 실패 = 파일 자체가 깨졌을 가능성이 높음(일시 오류가 아님)
            if _skip_count >= 20:  # 연속으로 너무 많이 실패하면 진짜 심각한 문제라 그대로 예외를 올림
                raise
            print(f"경고: 이미지 열기 실패, 건너뜀 - {path} ({e})")  # 어떤 파일이 문제인지 기록으로 남김
            return self.__getitem__((idx + 1) % len(self), _skip_count=_skip_count + 1)  # 학습이 죽지 않도록 다음 샘플로 대체

        ann_ids = self.coco.getAnnIds(imgIds=image_id)  # 이 이미지에 달린 어노테이션 id들을 조회
        annotations = self.coco.loadAnns(ann_ids)  # 어노테이션 id들을 실제 bbox/카테고리 정보로 로드

        target = {"image_id": image_id, "annotations": annotations}  # HF 이미지 프로세서가 기대하는 COCO 스타일 타깃 포맷 구성
        encoding = self.image_processor(images=image, annotations=target, return_tensors="pt")  # 리사이즈·정규화 및 라벨 인코딩을 한 번에 수행
        return {
            "pixel_values": encoding["pixel_values"][0],  # 배치 차원(0번째)을 제거한 전처리된 이미지 텐서
            "labels": encoding["labels"][0],  # 배치 차원을 제거한 인코딩된 라벨(class_labels/boxes 등) 딕셔너리
        }


class CocoSegmentationDataset(Dataset):
    """SegFormer 학습용 COCO Dataset. segmentation polygon을 픽셀 단위 클래스 마스크로 rasterize한다."""

    def __init__(
        self,
        images_dir: str,
        annotation_json: str,
        image_processor,
        oversample_steel: int = 1,
        max_samples: int | None = None,
        max_per_material: int | None = None,
    ):
        self.images_dir = Path(images_dir)  # 원본 이미지가 들어있는 디렉터리 경로 저장
        self.coco = COCO(annotation_json)  # COCO json을 읽어서 이미지/어노테이션/카테고리 인덱스를 구성
        self.image_processor = image_processor  # SegFormer용 HF 이미지 프로세서(리사이즈·정규화·라벨 인코딩 담당)
        self.image_ids = build_balanced_index(self.coco, oversample_steel, max_per_material)  # 다운샘플링+오버샘플링 반영된 이미지 id 목록
        if max_samples is not None:  # 스모크 테스트처럼 일부만 쓰고 싶을 때
            self.image_ids = self.image_ids[:max_samples]  # 앞에서부터 max_samples개만 남기고 잘라냄

    def __len__(self):
        return len(self.image_ids)  # Dataset 프로토콜: 전체 샘플 수(오버샘플링 반영된 길이) 반환

    def __getitem__(self, idx, _skip_count: int = 0):
        image_id = self.image_ids[idx]  # idx번째로 순회할 실제 image_id를 조회(중복 있을 수 있음)
        img_info = self.coco.imgs[image_id]  # 해당 image_id의 메타정보(파일명, 폭/높이 등) 조회
        path = self.images_dir / img_info["file_name"]  # 실제 이미지 파일 경로
        try:
            image = open_image_with_retry(path)  # 이미지 파일을 열어 RGB로 통일(네트워크 볼륨 일시 오류는 재시도)
        except OSError as e:  # 재시도까지 다 실패 = 파일 자체가 깨졌을 가능성이 높음(일시 오류가 아님)
            if _skip_count >= 20:  # 연속으로 너무 많이 실패하면 진짜 심각한 문제라 그대로 예외를 올림
                raise
            print(f"경고: 이미지 열기 실패, 건너뜀 - {path} ({e})")  # 어떤 파일이 문제인지 기록으로 남김
            return self.__getitem__((idx + 1) % len(self), _skip_count=_skip_count + 1)  # 학습이 죽지 않도록 다음 샘플로 대체

        height, width = img_info["height"], img_info["width"]  # 원본 이미지 픽셀 크기(마스크 크기와 맞추기 위함)
        segmentation_map = np.zeros((height, width), dtype=np.uint8)  # 0 = 배경/정상(결함 없음)으로 초기화된 픽셀 마스크

        ann_ids = self.coco.getAnnIds(imgIds=image_id)  # 이 이미지에 달린 어노테이션 id들을 조회
        for ann in self.coco.loadAnns(ann_ids):  # 어노테이션(결함 폴리곤)을 하나씩 순회하며 마스크에 칠함
            mask = self.coco.annToMask(ann).astype(bool)  # 폴리곤 좌표를 이미지 크기의 이진 픽셀 마스크로 변환(rasterize)
            segmentation_map[mask] = ann["category_id"] + 1  # RT-DETR과 클래스 id를 맞추기 위해 +1 shift(0번은 배경 전용)

        encoding = self.image_processor(
            images=image, segmentation_maps=segmentation_map, return_tensors="pt"
        )  # 리사이즈·정규화 및 라벨(마스크) 인코딩을 한 번에 수행
        return {
            "pixel_values": encoding["pixel_values"][0],  # 배치 차원(0번째)을 제거한 전처리된 이미지 텐서
            "labels": encoding["labels"][0],  # 배치 차원을 제거한 픽셀별 클래스 id 마스크
        }


def build_collate_fn(image_processor):
    def collate_fn(batch):
        pixel_values = [item["pixel_values"] for item in batch]  # 배치 내 각 샘플의 이미지 텐서를 리스트로 모음
        labels = [item["labels"] for item in batch]  # 배치 내 각 샘플의 라벨 딕셔너리를 리스트로 모음(스택하지 않음)
        encoding = image_processor.pad(pixel_values, return_tensors="pt")  # 서로 다른 크기의 이미지들을 배치 텐서로 패딩·스택
        result = {"pixel_values": encoding["pixel_values"], "labels": labels}  # Trainer에 넘길 최종 배치 딕셔너리 구성
        if "pixel_mask" in encoding:  # 프로세서가 패딩 마스크를 함께 반환하는 경우(가변 크기일 때)
            result["pixel_mask"] = encoding["pixel_mask"]  # 마스크도 배치 딕셔너리에 포함시킴
        return result  # 최종 collate 결과 반환

    return collate_fn  # image_processor를 캡처한 collate_fn 함수 자체를 반환(RT-DETR의 Trainer data_collator로 사용, SegFormer는 기본 collate로 충분)
