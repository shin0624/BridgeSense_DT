"""
AI-Hub "교량 외관점검 입면정사영상 데이터"(데이터셋 번호 71774) 원본을 내려받는다.

AI-Hub는 aihubshell(공식 CLI)로만 대용량 배치 다운로드가 된다. 이 스크립트는
aihubshell을 (없으면) 설치하고, 지정한 데이터셋의 파일 트리를 조회한 뒤, 파일 키를
지정해 Training/Validation 원천+라벨 zip을 받는다.

인증: AI-Hub 마이페이지에서 발급한 API Key가 필요하다. 우선순위대로:
  1) 인자:      --apikey 발급받은_키
  2) 환경변수:  export AIHUB_APIKEY="발급받은_키"
  3) 파일:      --apikey-file <경로> (기본값: 이 스크립트와 같은 폴더의 api_key.txt)
키는 로그·git에 남기지 말 것(이 스크립트는 키를 출력하지 않는다. api_key.txt는
.gitignore에 등록되어 있음).

주의: AI-Hub 데이터 이용약관상 데이터 재배포가 금지된다. 받은 원본(zip)과 압축 해제본,
그로부터 만든 coco json은 전부 .gitignore 대상이다(6절, 8절).

전형적 사용 흐름:
    # 0. 먼저 파일 트리만 확인 (어떤 fileKey를 받을지 파악)
    python download_aihub.py --apikey "$AIHUB_APIKEY" --list

    # 1. 전체 다운로드 (fileKey=all — 수백 GB, 시간 오래 걸림)
    python download_aihub.py --apikey "$AIHUB_APIKEY" \
        --out-root /home/elicer/BridgeSense_DT/data/aihub \
        --file-key all

    # 2. 특정 파일만 (예: --list 결과에서 고른 키들)
    python download_aihub.py --apikey "$AIHUB_APIKEY" \
        --out-root /home/elicer/BridgeSense_DT/data/aihub --file-key 1,2,5
"""
import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

DATASET_KEY = "71774"  # AI-Hub "교량 외관점검 입면정사영상 데이터" 데이터셋 번호
AIHUBSHELL_URL = "https://api.aihub.or.kr/api/aihubshell.do"  # 공식 aihubshell 설치 스크립트 배포 주소


def ensure_aihubshell(bin_dir: Path) -> Path:
    """aihubshell 실행 파일을 확보한다. PATH에 이미 있으면 그걸 쓰고,
    없으면 공식 배포 스크립트를 bin_dir에 내려받아 실행 권한을 준다."""
    found = shutil.which("aihubshell")
    if found:
        return Path(found)

    bin_dir.mkdir(parents=True, exist_ok=True)
    target = bin_dir / "aihubshell"
    if not target.exists():
        print(f"aihubshell이 없어 새로 내려받는다 -> {target}")
        # AI-Hub가 제공하는 설치 스크립트 본문을 그대로 저장 (curl 사용, 키 불필요)
        subprocess.run(
            ["curl", "-sL", "-o", str(target), AIHUBSHELL_URL],
            check=True,
        )
        target.chmod(0o755)
    return target


def run_aihubshell(shell: Path, apikey: str, extra_args: list[str], cwd: Path) -> int:
    """aihubshell 호출 공통 래퍼. -mode 는 호출부에서 extra_args로 넘긴다."""
    cmd = ["bash", str(shell), "-aihubapikey", apikey, *extra_args]
    # 키가 포함된 실제 커맨드는 출력하지 않는다 — 대신 마스킹한 형태만 보여준다
    shown = ["aihubshell", "-aihubapikey", "****", *extra_args]
    print("실행:", " ".join(shown), f"(cwd={cwd})")
    proc = subprocess.run(cmd, cwd=str(cwd))
    return proc.returncode


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--apikey",
        default=os.environ.get("AIHUB_APIKEY"),
        help="AI-Hub API Key. 미지정 시 환경변수 AIHUB_APIKEY, 그다음 --apikey-file 순으로 사용",
    )
    parser.add_argument(
        "--apikey-file",
        type=str,
        default=str(Path(__file__).with_name("api_key.txt")),
        help="API Key가 담긴 텍스트 파일 경로 (--apikey/환경변수가 없을 때만 읽음)",
    )
    parser.add_argument(
        "--out-root",
        type=str,
        default="/home/elicer/BridgeSense_DT/data/aihub",
        help="원본(zip)을 받을 루트 디렉터리",
    )
    parser.add_argument(
        "--file-key",
        type=str,
        default="all",
        help="받을 파일 키(콤마 구분). 'all'이면 데이터셋 전체. --list로 먼저 확인할 것",
    )
    parser.add_argument(
        "--list",
        action="store_true",
        help="다운로드하지 않고 데이터셋 파일 트리(파일 키 목록)만 출력",
    )
    parser.add_argument(
        "--bin-dir",
        type=str,
        default="/home/elicer/BridgeSense_DT/.tools",
        help="aihubshell을 설치할 위치(PATH에 이미 있으면 무시)",
    )
    args = parser.parse_args()

    apikey = args.apikey
    if not apikey and args.apikey_file:
        key_path = Path(args.apikey_file)
        if key_path.is_file():
            apikey = key_path.read_text(encoding="utf-8").strip()
            print(f"API Key를 파일에서 읽음: {key_path}")
    if not apikey:
        sys.exit(
            "에러: API Key가 없다. --apikey / 환경변수 AIHUB_APIKEY / "
            f"{args.apikey_file} 중 하나를 준비할 것"
        )
    args.apikey = apikey

    shell = ensure_aihubshell(Path(args.bin_dir))

    if args.list:
        # -mode l : 파일 트리 조회 (다운로드 안 함)
        rc = run_aihubshell(
            shell, args.apikey, ["-mode", "l", "-datasetkey", DATASET_KEY], Path.cwd()
        )
        sys.exit(rc)

    out_root = Path(args.out_root)
    out_root.mkdir(parents=True, exist_ok=True)

    # -mode d : 다운로드. aihubshell은 현재 작업 디렉터리 밑에 풀어놓으므로 cwd를 out_root로
    rc = run_aihubshell(
        shell,
        args.apikey,
        ["-mode", "d", "-datasetkey", DATASET_KEY, "-filekey", args.file_key],
        out_root,
    )
    if rc != 0:
        sys.exit(f"aihubshell 다운로드가 exit {rc} 로 실패했다")

    print(f"\n완료. 받은 위치: {out_root}")
    print("다음 단계: extract_aihub_zips.py 로 압축 해제 -> convert_to_coco.py")


if __name__ == "__main__":
    main()
