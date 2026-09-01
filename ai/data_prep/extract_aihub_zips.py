"""
AI-Hub 교량 외관점검 입면정사영상 데이터(71774) 원본 압축 해제 스크립트.

download_aihub.py(aihubshell)가 받아놓은 트리(2026-09-01 실측):
    data/aihub/040.교량_3D_외관점검_영상_데이터/3.개방데이터/1.데이터/
        {Training,Validation}/{01.원천데이터,02.라벨링데이터}/

이 폴더 안 파일은 **두 가지 상태가 섞여** 있다:
  (a) aihubshell이 병합에 성공한 단일 `<이름>.zip`
  (b) 병합 실패해 남은 조각 `<이름>.zip.part<offset>` — offset은 바이트 오프셋(0, 1073741824,
      2147483648, ...). 숫자 오름차순으로 그냥 이어붙이면(cat) 원본 zip이 된다.
      (part0이 실제로 PK\x03\x04 헤더로 시작함을 확인함)

이 스크립트는:
  1) `*.zip.part*`가 있는 베이스는 offset 숫자순으로 병합해 `<이름>.zip` 생성(임시)
  2) 모든 `<이름>.zip`을 unzip으로 목적지에 해제
  3) 해제가 성공하면 그 zip(과 병합 조각)을 삭제해 공간 회수 (--keep-zips로 유지 가능)
  4) 결과를 <out-root>/{Training,Validation}/{원천데이터,라벨링데이터}/ 로 평탄화
     (convert_to_coco.py가 rglob로 읽으므로 카테고리별 내부 구조는 그대로 둬도 됨)

/home/elicer가 overlay 128G(여유 빠듯)라, 기본 동작은 **해제 성공 즉시 원본 zip 삭제**다.

실행:
    python extract_aihub_zips.py \
        --src-root "/home/elicer/BridgeSense_DT/data/aihub/040.교량_3D_외관점검_영상_데이터/3.개방데이터/1.데이터" \
        --out-root /home/elicer/BridgeSense_DT/data/aihub_extracted

    # src-root를 정확히 모르면 data/aihub를 주면 그 밑에서 자동 탐색
    python extract_aihub_zips.py \
        --src-root /home/elicer/BridgeSense_DT/data/aihub \
        --out-root /home/elicer/BridgeSense_DT/data/aihub_extracted
"""
import argparse
import re
import shutil
import subprocess
from pathlib import Path

SPLITS = ["Training", "Validation"]
TYPE_DIRS = {
    "01.원천데이터": "원천데이터",
    "02.라벨링데이터": "라벨링데이터",
}
PART_RE = re.compile(r"^(?P<base>.+\.zip)\.part(?P<offset>\d+)$")


def find_data_root(src_root: Path) -> Path:
    """src_root 밑에서 '{Training,Validation}/01.원천데이터'를 포함하는 실제 데이터 루트를 찾는다.
    src_root 자체가 이미 그 루트면 그대로 반환."""
    if (src_root / "Training" / "01.원천데이터").is_dir():
        return src_root
    for cand in src_root.rglob("01.원천데이터"):
        # .../<데이터루트>/Training/01.원천데이터 -> 데이터루트
        if cand.parent.name in SPLITS:
            return cand.parent.parent
    raise SystemExit(
        f"'{src_root}' 밑에서 Training/01.원천데이터 구조를 못 찾음 — 경로 확인 필요"
    )


def merge_part_files(type_dir: Path) -> list[Path]:
    """type_dir 안의 *.zip.part<offset> 들을 베이스별로 offset 숫자순 병합해 <base>.zip 생성.
    병합으로 새로 만든 zip 경로 목록을 반환(정리 대상 추적용)."""
    groups: dict[str, list[tuple[int, Path]]] = {}
    for p in type_dir.iterdir():
        m = PART_RE.match(p.name)
        if m:
            groups.setdefault(m["base"], []).append((int(m["offset"]), p))

    merged = []
    for base, parts in groups.items():
        parts.sort(key=lambda x: x[0])  # offset 숫자 오름차순 (문자열 정렬 금지)
        target = type_dir / base
        if target.exists():
            print(f"  이미 {base} 존재 — 병합 건너뜀")
            continue
        # offset 연속성 간단 검증: 다음 조각 offset == 이전까지 누적 크기
        expected = 0
        for off, pp in parts:
            if off != expected:
                raise SystemExit(
                    f"조각 오프셋 불연속: {pp.name} (offset={off}, 기대={expected}) — 다운로드 손상 의심"
                )
            expected += pp.stat().st_size
        print(f"  병합: {base}  ({len(parts)}조각, {expected/1e9:.1f}GB)")
        with open(target, "wb") as out:
            for _, pp in parts:
                with open(pp, "rb") as f:
                    shutil.copyfileobj(f, out, length=32 * 1024 * 1024)
        merged.append(target)
        # 병합 성공했으면 조각 삭제(공간 회수)
        for _, pp in parts:
            pp.unlink()
    return merged


def unzip_one(zip_path: Path, out_dir: Path):
    """unzip으로 해제.

    - AI-Hub zip은 엔트리명이 선행 '/'로 저장돼 있어 unzip이 파일마다
      "stripped absolute path spec" 경고를 낸다(수만 개면 로그 폭발) — stderr를 버린다.
      경로에서 '/'만 떼고 파일명은 그대로 풀리므로 결과엔 문제 없음.
    - exit code로만 성공 판정: 0=정상, 1=경고(있는 파일 skip 등)는 허용, 그 외는 실패.
    """
    out_dir.mkdir(parents=True, exist_ok=True)
    r = subprocess.run(
        ["unzip", "-n", "-q", str(zip_path), "-d", str(out_dir)],
        stderr=subprocess.DEVNULL,
    )
    if r.returncode not in (0, 1):
        raise RuntimeError(f"unzip 실패: {zip_path} (exit {r.returncode})")


def extract_all(src_root: Path, out_root: Path, keep_zips: bool):
    data_root = find_data_root(src_root)
    print(f"데이터 루트: {data_root}")

    total = 0
    for split in SPLITS:
        for src_type_name, out_type_name in TYPE_DIRS.items():
            type_dir = data_root / split / src_type_name
            if not type_dir.is_dir():
                print(f"경고: {type_dir} 없음 — 건너뜀")
                continue

            print(f"[{split}/{out_type_name}] .part 조각 병합 확인...")
            merge_part_files(type_dir)

            out_dir = out_root / split / out_type_name
            zips = sorted(type_dir.glob("*.zip"))
            if not zips:
                print(f"경고: {type_dir} 밑에 zip이 없음")
                continue

            for zpath in zips:
                print(f"[{split}/{out_type_name}] {zpath.name} 해제 중...")
                unzip_one(zpath, out_dir)
                total += 1
                if not keep_zips:
                    zpath.unlink()  # 해제 성공 후 원본 삭제(공간 회수)

    print(f"\n완료: 아카이브 {total}개 해제 -> {out_root}")
    if not keep_zips:
        print("원본 zip은 삭제함(--keep-zips로 유지 가능). data/aihub 폴더는 이제 비어있음.")
    print("다음 단계: convert_to_coco.py (train/val 각각) -> verify_dataset.py")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--src-root", type=str, required=True,
                        help="download_aihub.py 결과 루트 (data/aihub 또는 그 아래 1.데이터)")
    parser.add_argument("--out-root", type=str, required=True, help="압축 해제 결과 저장 루트")
    parser.add_argument("--keep-zips", action="store_true",
                        help="해제 후에도 원본 zip 유지 (기본: 삭제해서 공간 회수)")
    args = parser.parse_args()
    extract_all(Path(args.src_root), Path(args.out_root), args.keep_zips)
