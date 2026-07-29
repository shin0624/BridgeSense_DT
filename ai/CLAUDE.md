# CLAUDE.md — BridgeSense DT / ai (Python, RunPod 환경)

이 파일은 `ai/` 디렉토리에서 작업하는 Claude Code 세션을 위한 프로젝트 컨텍스트다.
지금은 **RunPod GPU Pod에 VSCode TCP(SSH) 직접 연결로 접속한 상태**이며, 이 세션에서 실제
AI 학습 코드 작성이 처음 시작된다. 아래 "지금 해야 할 일"부터 순서대로 진행할 것.

## 지금 해야 할 일 (이 세션의 최우선 순서)

1. **환경부터 확인할 것 — 절대 가정하지 말 것.** `python --version`, `nvidia-smi`,
   `pip list | grep -E "torch|transformers"`를 실행해서 실제 설치된 버전을 확인한다.
   RunPod PyTorch 템플릿 기준이라 로컬 개발 계획에서 가정했던 "Python 3.10.11 + venv"와
   다를 수 있다. venv가 없으면 새로 만들지 말지부터 판단할 것(템플릿에 이미 격리된
   환경이면 굳이 또 venv를 씌울 필요 없음).
2. **AI-Hub 데이터가 `/workspace`에 실제로 받아졌는지 확인.** `ls -la /workspace/data`로
   존재 여부·용량 확인. 아직이면 데이터 다운로드부터(`aihubshell`) 완료할 것 — 반드시
   `/workspace` 하위(Volume Disk, 영구 저장)에 받을 것. 컨테이너 기본 경로에 받으면
   Pod 재시작 시 사라질 수 있다.
3. **AI-Hub 원본 라벨 JSON 구조를 실제로 열어서 확인.** 지금 `data_prep/convert_to_coco.py`에
   있는 필드명(`image_filename`, `image_width`, `annotations` 등)은 실제 스키마를 모르는
   상태에서 추측으로 채워둔 자리표시자다. 실제 라벨 파일 1개를 열어 구조를 확인하고
   해당 스크립트의 TODO 부분을 정확히 고칠 것.
4. 소규모 샘플(수십 장)로 변환 스크립트를 먼저 검증한 뒤, 전체 데이터로 확대할 것.
5. `train/train_rtdetr.py`로 파인튜닝 실행 — **반드시 `tmux` 세션 안에서 실행**
   (`tmux new -s train`). SSH/VSCode 연결이 끊겨도 학습이 죽지 않게 하기 위함.
6. 학습이 정상적으로 몇 스텝 도는 것을 확인할 때까지는 짧게(예: 1 epoch, 작은 서브셋)
   돌려서 파이프라인 자체를 검증하고, 그다음에 본 학습을 길게 돌릴 것.

## 이 프로젝트가 하는 일 / 하지 않는 일

**한다**: AI-Hub 데이터로 RT-DETR v2·SegFormer를 파인튜닝하고, 결과를 ONNX로 익스포트한다.
**하지 않는다**: 실시간 추론 서버를 띄우지 않는다. FastAPI, Flask 등 웹 서버 코드는 이
프로젝트의 범위가 아니다. 실시간 추론은 Unity 프로젝트(`unity/`, 별도 세션/별도
CLAUDE.md)에서 **Sentis(Inference Engine)로 인프로세스 실행**한다. 이 세션은 Unity/UI와
무관하다 — 대시보드, 3D 뷰어 등은 이미 별도로 완료돼 있으니 건드릴 필요 없음.

## 전체 프로젝트 진행 상황 (참고용)

- ✅ Unity 메인 대시보드 UI 개발 완료 (업로드/분석 상태, 3D 확인 상태, 입면도·등급분포 모달)
- ✅ 극락교 3D 모델(Blender, PSCI거더교 형식) 및 셰이더 기반 부재 컬러 시각화 구조
- ✅ 국토교통부 현황조서 → 극락교 스펙 추출 계획 (`data_prep/extract_bridge_spec.py`, 이 세션의 범위 밖이면 후순위)
- ✅ AI-Hub 데이터 이용 승인 완료
- 🔲 **AI 모델 학습 — 이 세션에서 처음 시작. 데이터 변환·학습·ONNX 익스포트 전부 미완료**
- 🔲 학습된 모델을 Unity Sentis에 연동 (이 세션 이후, Unity 쪽 세션에서 진행)

마감(2026년 8월 27일 오픈소스 개발자대회 출품)까지 시간이 넉넉하지 않으니, 완벽한 정확도보다
**엔드투엔드 파이프라인이 끝까지 도는 것**을 먼저 검증하는 걸 우선순위로 둘 것.

## 환경 (실제 값은 위 1번 항목으로 직접 확인할 것)

- RunPod GPU Pod, VSCode Remote(TCP/SSH) 연결
- 데이터·체크포인트·산출물은 전부 `/workspace` 하위에 저장 (Volume Disk, 영구)
- 로컬 PC의 RTX 4050(6GB)은 이 세션과 무관 — 학습은 전적으로 이 Pod의 GPU에서 수행

## 디렉토리 구조

```
ai/
├── data_prep/
│   ├── extract_bridge_spec.py   # 국토교통부 교량 제원 → JSON (이 세션과 별개 작업, 후순위)
│   └── convert_to_coco.py       # AI-Hub 라벨 → COCO 포맷 (TODO 부분 실제 스키마로 수정 필요)
├── train/
│   └── train_rtdetr.py          # RT-DETR v2 파인튜닝 (HuggingFace Trainer 기반, 뼈대 작성됨)
├── export/                       # ONNX 변환 + onnxruntime 검증 (아직 미작성)
└── requirements.txt
```

## 모델 및 데이터

| 모델 | 역할 | 베이스 | 라이선스 |
|---|---|---|---|
| RT-DETR v2 | 손상 객체 검출 (bbox) | `PekingU/rtdetr_v2_r18vd` | Apache 2.0 |
| SegFormer MiT-B2 | 균열 픽셀 분할 (mask) | `nvidia/mit-b2` | Apache 2.0 |

데이터 우선순위: 1) 교량 외관점검 입면정사영상 데이터(AI-Hub, 메인) 2) SOC 시설물 균열패턴
이미지 데이터(보강용) 3) 건물 균열 탐지 이미지(증강 전용, 도메인 다름).

결함 클래스: 공식 카테고리 정의서(`/workspace/BridgeSenseDT/DataInfo_AIHUB.pdf` p.3-6) 기준
9종 + 정상데이터. "7종(요철 포함)"은 실제 스펙 확인 전 추정이었고 부정확했음 — 정정함.
- 콘크리트: 균열, 박락, 백태, 누수, 철근 노출
- 아스팔트: 균열, 함몰
- 강재: 부식, 도장 박리
- 정상데이터 (결함 없음 — 검출 카테고리에는 포함하지 않고 네거티브 샘플로만 사용)

파일명 접두 2글자 코드로 클래스 판별 (`convert_to_coco.py`의 `PREFIX_TO_CLASS` 참고):
co=콘크리트균열, ef=백태, le=누수, sp=박락, ex=철근노출, st=강재부식, pa=도장박리,
as=아스팔트균열, po=함몰, no=정상데이터.

대상 교량: 극락교(광주광역시 서구 마륵동, PSCI거더교, DB-18, 총길이 380m, 13경간).

## ONNX 익스포트 규약 (학습 끝난 뒤 적용 — Unity Sentis와의 계약)

- opset 버전 **7~25** 범위 준수 (Unity Sentis `com.unity.ai.inference` 기준 확인된 값)
- 입출력 텐서의 이름·shape을 고정하고 문서화할 것 (`export/model_io_spec.md`)
- 익스포트 후 `onnxruntime`으로 PyTorch 원본과 출력 일치 여부 반드시 검증
- 완성된 `.onnx`는 HuggingFace Hub(CC BY 4.0) 또는 `/workspace/models/`에 버전 태그와 함께 보관

## 시간·비용 관리 (RunPod 관련 — 이 세션에서 신경 쓸 것)

- 학습을 tmux 밖에서 그냥 돌리지 말 것 (연결 끊기면 죽음)
- 장시간 자리 비울 때는 Pod을 **Stop**(Volume Disk 유지, GPU 과금만 중단)할지 사용자에게
  먼저 물어볼 것 — Claude Code가 임의로 Pod을 종료/정지하지는 않되, 비용이 걱정되는
  장시간 유휴 상태가 감지되면 알려줄 것
- 큰 산출물(체크포인트, 데이터)을 git에 직접 커밋하지 말 것 — `.gitignore` 처리하고
  HuggingFace Hub나 `/workspace` 로컬 보관으로 대체

## 하지 말 것

- 웹 서버/API 엔드포인트를 만들지 말 것
- AI-Hub 데이터 원본을 git에 커밋하지 말 것
- 파인튜닝 가중치(.pt, .onnx) 같은 대용량 바이너리를 git에 직접 커밋하지 말 것
- Unity/C#/UI 관련 코드나 파일을 이 세션에서 건드리지 말 것 (별도 프로젝트·별도 세션)