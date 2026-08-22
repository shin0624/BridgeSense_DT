"""
AI-Hub 교량 외관점검 입면정사영상 데이터 원본(카테고리별 zip) 압축 해제 스크립트

data/aihub/260730_FullData/**/{Training,Validation}/{01.원천데이터,02.라벨링데이터}/*.zip을 찾아서 data/aihub_extracted/{Training,Validation}/{원천데이터,라벨링데이터}/ 밑에 압축 해제한다.

convert_to_coco.py는 이미지/라벨 디렉터리를 rglob(재귀 탐색)으로 읽으므로, 카테고리별 zip 내부 폴더 구조를 평탄화할 필요 없음
각 zip을 해당 split/type 목적지에 풀어놓기만 하면 된다.

실행:
    python extract_aihub_zips.py \
        --src-root /workspace/data/aihub/260730_FullData \
        --out-root /workspace/data/aihub_extracted
"""
import argparse
import zipfile
from pathlib import Path

SPLITS = ["Training", "Validation"]
TYPE_DIRS = {
    "01.원천데이터": "원천데이터",
    "02.라벨링데이터": "라벨링데이터",
}


def extract_all(src_root: Path, out_root: Path): 
    total_zips = 0
    total_members = 0

    for split in SPLITS:
        for src_type_dir, out_type_name in TYPE_DIRS.items():
            zips = sorted(src_root.rglob(f"{split}/{src_type_dir}/*.zip"))
            if not zips:
                print(f"경고: {split}/{src_type_dir} 밑에서 zip을 못 찾음")
                continue

            out_dir = out_root / split / out_type_name
            out_dir.mkdir(parents=True, exist_ok=True)

            for zpath in zips:
                print(f"[{split}/{out_type_name}] {zpath.name} 압축 해제 중...")
                with zipfile.ZipFile(zpath) as z:
                    bad = z.testzip()
                    if bad is not None:
                        raise RuntimeError(f"손상된 zip: {zpath} (멤버: {bad})")
                    z.extractall(out_dir)
                    total_members += len(z.namelist())
                total_zips += 1

    print(f"완료: zip {total_zips}개, 총 {total_members}개 항목 압축 해제")
    print(f"저장 위치: {out_root}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--src-root", type=str, required=True, help="AI-Hub 원본 zip이 있는 루트 폴더")
    parser.add_argument("--out-root", type=str, required=True, help="압축 해제 결과 저장 루트")
    args = parser.parse_args()
    extract_all(Path(args.src_root), Path(args.out_root))
