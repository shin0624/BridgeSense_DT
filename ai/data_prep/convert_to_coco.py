"""
AI-Hub "교량 외관점검 입면정사영상 데이터"(71774) 원본 라벨(LabelMe 스타일 JSON)을
COCO 포맷으로 변환한다.

원본 라벨 구조 (실제 다운로드 후 확인, DataInfo_AIHUB.pdf p.5-6 카테고리 정의서와 대조 완료):
    {
      "meta_info": {"ID": ..., "NAME": 교량명, "TYPE": 교량종류코드, "GRADE": 교량등급, ...},
      "shapes": [
        {"label": "SteelDefect", "points": [[x,y], ...], "shape_type": "polygon"}, ...
      ],
      "imagePath": "st158UN0P03_000107.jpg",
      "imageData": null,
      "imageHeight": 512,
      "imageWidth": 512
    }

클래스 판별은 shapes[].label 문자열이 아니라 파일명 접두 2글자 코드로 한다.
DataInfo_AIHUB.pdf p.5 "파일명 명명 규칙" 표가 co/ef/le/... 코드를 공식적으로 못박아 두고
있는 반면, label 문자열은 예시(ConcreteCrack, Spalling 등)만 있고 전 클래스에 대해
일관되게 문서화되어 있지 않다 (실제로 강재_강재손상 폴더의 label은 "SteelDefect"였음).

실행:
    python convert_to_coco.py \
        --images-dir /workspace/data/aihub_extracted/Training/원천데이터 \
        --labels-dir /workspace/data/aihub_extracted/Training/라벨링데이터 \
        --out /workspace/data/coco_format/train.json
"""
import argparse
import json
from pathlib import Path

# DataInfo_AIHUB.pdf p.5 "파일명 명명 규칙" 표 (co/ef/le/sp/ex/st/pa/as/po/no)
# 순서가 category id(0-8)로 고정된다. "정상데이터"(no)는 결함 객체가 아니므로
# 검출 카테고리에는 넣지 않고, 해당 이미지는 어노테이션 없이 images에만 포함시킨다.
PREFIX_TO_CLASS = {
    "co": "콘크리트_균열",
    "ef": "백태",
    "le": "누수",
    "sp": "박락",
    "ex": "철근_노출",
    "st": "강재_부식",
    "pa": "도장_박리",
    "as": "아스팔트_균열",
    "po": "함몰",
}
NORMAL_PREFIX = "no"
CLASS_NAMES = list(PREFIX_TO_CLASS.values())


def class_from_filename(stem: str):
    prefix = stem[:2].lower()
    if prefix == NORMAL_PREFIX:
        return None  # 정상데이터: 어노테이션 없음
    
    if prefix not in PREFIX_TO_CLASS:
        raise ValueError(f"알 수 없는 파일명 접두 코드: {stem!r} (prefix={prefix!r})")
    return PREFIX_TO_CLASS[prefix]


def polygon_to_bbox_and_area(points, width, height):
    xs = [max(0.0, min(width, p[0])) for p in points]
    ys = [max(0.0, min(height, p[1])) for p in points]
    x_min, x_max = min(xs), max(xs)
    y_min, y_max = min(ys), max(ys)

    # Shoelace formula
    area = 0.0
    n = len(points)
    
    for i in range(n):
        x1, y1 = xs[i], ys[i]
        x2, y2 = xs[(i + 1) % n], ys[(i + 1) % n]
        area += x1 * y2 - x2 * y1
    area = abs(area) / 2.0

    return [x_min, y_min, x_max - x_min, y_max - y_min], area


def convert(images_dir: Path, labels_dir: Path, out_path: Path):
    label_files = sorted(labels_dir.rglob("*.json"))
    print(f"라벨 파일 {len(label_files)}개 발견 ({labels_dir})")

    images_by_stem = {p.stem: p for p in images_dir.rglob("*.jpg")}

    coco = {
        "images": [],
        "annotations": [],
        "categories": [{"id": i, "name": name} for i, name in enumerate(CLASS_NAMES)],
    }
    category_id = {name: i for i, name in enumerate(CLASS_NAMES)}

    image_id = 0
    ann_id = 0
    skipped_no_image = 0
    skipped_bad_shape = 0

    for label_path in label_files:
        stem = label_path.stem
        image_path = images_by_stem.get(stem)
        
        if image_path is None:
            skipped_no_image += 1
            continue

        try:
            class_name = class_from_filename(stem)
            
        except ValueError as e:
            print(f"경고: {e} — 건너뜀")
            continue

        with open(label_path, encoding="utf-8") as f:
            raw = json.load(f)

        width = raw["imageWidth"]
        height = raw["imageHeight"]

        coco["images"].append({
            "id": image_id,
            "file_name": str(image_path.relative_to(images_dir)),
            "width": width,
            "height": height,
        })

        if class_name is not None:
            for shape in raw.get("shapes", []):
                points = shape.get("points", [])
                
                if len(points) < 3:
                    skipped_bad_shape += 1
                    continue
                
                bbox, area = polygon_to_bbox_and_area(points, width, height)
                if bbox[2] <= 0 or bbox[3] <= 0:
                    skipped_bad_shape += 1
                    continue

                segmentation = [[coord for point in points for coord in point]]
                coco["annotations"].append({
                    "id": ann_id,
                    "image_id": image_id,
                    "category_id": category_id[class_name],
                    "bbox": bbox,
                    "area": area,
                    "segmentation": segmentation,
                    "iscrowd": 0,
                })
                ann_id += 1

        image_id += 1

    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(coco, f, ensure_ascii=False)

    print(f"완료: 이미지 {len(coco['images'])}개, 어노테이션 {len(coco['annotations'])}개")
    if skipped_no_image:
        print(f"경고: 대응하는 이미지 파일을 못 찾아 건너뛴 라벨 {skipped_no_image}개")
    if skipped_bad_shape:
        print(f"경고: 점 개수 부족/면적 0인 shape {skipped_bad_shape}개 건너뜀")
    print(f"저장 위치: {out_path}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--images-dir", type=str, required=True, help="원천데이터(jpg) 폴더")
    parser.add_argument("--labels-dir", type=str, required=True, help="라벨링데이터(json) 폴더")
    parser.add_argument("--out", type=str, required=True, help="COCO json 저장 경로")
    args = parser.parse_args()
    convert(Path(args.images_dir), Path(args.labels_dir), Path(args.out))
