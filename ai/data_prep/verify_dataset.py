"""
COCO 포맷 데이터셋(convert_to_coco.py 산출물)의 무결성을 점검한다.

확인 항목:
1. 이미지-라벨 매칭: json에 등록된 이미지 파일이 실제 디스크에 있는지
2. 클래스별 분포: 카테고리별 어노테이션 수, 이미지 수 (정상데이터=어노테이션 없는 이미지)
3. (선택, --check-images) 손상된 이미지 파일 탐지: PIL로 직접 열어서 확인 — 대용량 데이터셋에서
   느릴 수 있어 기본은 끔

실행:
    python verify_dataset.py \
        --json /workspace/data/coco_format/train.json \
        --images-dir /workspace/data/aihub_extracted_full/Training/원천데이터

    # 손상 이미지까지 검사(전체 스캔이라 느림)
    python verify_dataset.py \
        --json /workspace/data/coco_format/train.json \
        --images-dir /workspace/data/aihub_extracted_full/Training/원천데이터 \
        --check-images
"""
import argparse
from collections import Counter
from pathlib import Path

from PIL import Image
from pycocotools.coco import COCO


def check_missing_files(coco: COCO, images_dir: Path) -> list[str]:
    """json에는 등록돼 있는데 실제 디스크에 없는 이미지 파일명 목록."""
    missing = []
    for img in coco.imgs.values():
        if not (images_dir / img["file_name"]).exists():
            missing.append(img["file_name"])
    return missing


def check_class_distribution(coco: COCO) -> tuple[Counter, int]:
    """카테고리별 어노테이션 수와, 어노테이션이 하나도 없는 이미지(정상데이터) 수를 센다."""
    cat_names = {c["id"]: c["name"] for c in coco.loadCats(coco.getCatIds())}
    counter = Counter(cat_names[ann["category_id"]] for ann in coco.anns.values())

    annotated_image_ids = {ann["image_id"] for ann in coco.anns.values()}
    no_annotation_count = len(coco.imgs) - len(annotated_image_ids)
    return counter, no_annotation_count


def check_corrupted_images(coco: COCO, images_dir: Path) -> list[str]:
    """PIL로 직접 열어서 깨진 이미지 파일을 찾는다 (느림, --check-images일 때만 실행)."""
    corrupted = []
    total = len(coco.imgs)
    for i, img in enumerate(coco.imgs.values(), 1):
        path = images_dir / img["file_name"]
        if not path.exists():
            continue  # 누락 파일은 check_missing_files에서 이미 보고하므로 중복 보고 안 함
        try:
            with Image.open(path) as im:
                im.verify()
        except Exception as e:
            corrupted.append(f"{img['file_name']} ({e})")
        if i % 50000 == 0:
            print(f"  ...{i}/{total}장 확인함")
    return corrupted


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--json", type=str, required=True, help="COCO json 경로 (train.json/val.json)")
    parser.add_argument("--images-dir", type=str, required=True, help="원본 이미지 디렉터리")
    parser.add_argument("--check-images", action="store_true", help="손상된 이미지 파일까지 검사(전체 스캔, 느림)")
    args = parser.parse_args()

    coco = COCO(args.json)
    images_dir = Path(args.images_dir)

    print(f"\n총 이미지 {len(coco.imgs)}장, 어노테이션 {len(coco.anns)}개\n")

    print("=== 1. 이미지-라벨 매칭 ===")
    missing = check_missing_files(coco, images_dir)
    if missing:
        print(f"경고: 디스크에 없는 이미지 {len(missing)}개")
        for name in missing[:20]:
            print(f"  - {name}")
        if len(missing) > 20:
            print(f"  ... 외 {len(missing) - 20}개")
    else:
        print("정상: json에 등록된 이미지가 전부 디스크에 있음")

    print("\n=== 2. 클래스별 분포 ===")
    distribution, no_annotation_count = check_class_distribution(coco)
    for name, count in distribution.most_common():
        print(f"  {name}: {count}개")
    print(f"  (정상데이터, 어노테이션 없는 이미지): {no_annotation_count}장")

    if args.check_images:
        print("\n=== 3. 손상 이미지 검사 (전체 스캔) ===")
        corrupted = check_corrupted_images(coco, images_dir)
        if corrupted:
            print(f"경고: 손상된 이미지 {len(corrupted)}개")
            for name in corrupted:
                print(f"  - {name}")
        else:
            print("정상: 손상된 이미지 없음")
    else:
        print("\n=== 3. 손상 이미지 검사: 생략 (--check-images로 실행하면 검사함) ===")


if __name__ == "__main__":
    main()
