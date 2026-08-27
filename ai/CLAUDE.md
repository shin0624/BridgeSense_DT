# CLAUDE.md — BridgeSense DT / ai (Python, RunPod 환경)

이 파일은 `ai/` 디렉토리에서 작업하는 Claude Code 세션을 위한 프로젝트 컨텍스트다.

## 현재 상태 (2026-08-10 기준) — 먼저 읽을 것

**RT-DETR v2·SegFormer 파인튜닝 + ONNX 익스포트까지 전부 완료됐다.** 이 세션이 처음
시작하는 게 아니라, 이전 세션에서 데이터 준비 → 학습 → ONNX 변환까지 끝낸 상태에서
Pod을 재생성해서 이어받는 것이다. 아래 순서로 상황을 파악할 것.

1. **산출물이 그대로 있는지 먼저 확인**: `/workspace`는 Network Volume이라 Pod을
   terminate해도 내용은 보존된다. `ls /workspace/ai/models/`에 `rtdetr.onnx`,
   `segformer.onnx`가 있고, `ls /workspace/ai/checkpoints/{rtdetr_v2,segformer}/final/`에
   각 모델의 최종 가중치가 있으면 학습은 이미 끝난 것 — **처음부터 다시 학습시키지 말 것**.
2. **재접속 후 환경 복원**: `bash /workspace/scripts/bootstrap_env.sh` 먼저 실행
   (tmux/gh CLI/venv/VS Code 확장/Claude 설정 심볼릭 링크 전부 복원). 새로 만든 Pod이면
   torch가 다시 컨테이너 기본값(구버전)일 수 있으니 `ai/requirements.txt`가 요구하는
   `torch==2.7.1+cu128` 등이 venv에 실제로 깔려있는지 확인할 것 — 자세한 이유는
   "환경 관련 중요 교훈" 절 참고.
3. **다음 할 일이 궁금하면**: 이 프로젝트의 AI 파트(`ai/`)는 사실상 끝났다. 남은 건
   `data_prep/extract_bridge_spec.py`(후순위, 이 세션과 별개 작업)와, **Unity Sentis
   연동은 별도 Unity 세션에서** 진행한다 — 이 세션에서 손댈 필요 없음.
4. 사용자가 "다시 파인튜닝"을 요청하면 — 데이터 우선순위나 하이퍼파라미터를 바꿔서 재학습하는
   상황일 것. 그 경우 아래 "환경 관련 중요 교훈"에 정리된 문제들(특히 lr, OOM 두 종류)을
   반드시 먼저 읽고 같은 실수를 반복하지 말 것.

## 이 프로젝트가 하는 일 / 하지 않는 일

**한다**: AI-Hub 데이터로 RT-DETR v2·SegFormer를 파인튜닝하고, 결과를 ONNX로 익스포트한다.
**하지 않는다**: 실시간 추론 서버를 띄우지 않는다. FastAPI, Flask 등 웹 서버 코드는 이
프로젝트의 범위가 아니다(`ai/train/status_dashboard.py`는 예외 — 학습 진행률만 읽기
전용으로 보여주는 로컬 모니터링 도구지 추론 서버가 아님). 실시간 추론은 Unity
프로젝트(별도 세션)에서 **Sentis(Inference Engine)로 인프로세스 실행**한다. 이 세션은
Unity/UI와 무관 — 대시보드, 3D 뷰어 등은 이미 별도로 완료돼 있으니 건드릴 필요 없음.

## 전체 프로젝트 진행 상황

- ✅ Unity 메인 대시보드 UI, 극락교 3D 모델, AI-Hub 데이터 이용 승인 — 전부 완료
- ✅ **AI-Hub 데이터 준비**: 전체 420,074장 다운로드+압축해제+COCO 변환 완료
  (`data/coco_format/{train,val}.json`)
- ✅ **RT-DETR v2 파인튜닝 완료**: `ai/checkpoints/rtdetr_v2/final/` (7 epoch, batch 32,
  lr 1e-5, eval_map 0.047~0.056 안정)
- ✅ **SegFormer 파인튜닝 완료**: `ai/checkpoints/segformer/final/` (10 epoch 중 베스트
  epoch 5, eval_mean_iou 0.47)
- ✅ **ONNX 익스포트 + 검증 완료**: `ai/models/rtdetr.onnx`, `ai/models/segformer.onnx`
  (onnxruntime으로 PyTorch 원본과 오차 0.00002 이하 확인됨)
- 🔲 국토교통부 현황조서 → 교량별 기본정보 추출 (`data_prep/extract_bridge_spec.py`, 후순위)
- 🔲 **Unity Sentis 연동** — 별도 Unity 세션에서 진행. `.onnx` 파일 2개 + 아래 "ONNX 익스포트
  규약" 절 + `ai/export/model_io_spec.md`가 그쪽에 넘길 계약 문서

마감은 2026년 8월 27일 오픈소스 개발자대회 출품.

## 환경 (RunPod)

- RunPod GPU Pod, VSCode Remote(TCP/SSH) 연결. GPU는 RTX PRO 4000 Blackwell.
- 데이터·체크포인트·산출물·venv·Claude 설정까지 전부 `/workspace`(Network Volume, Pod을
  지워도 보존됨) 하위에 있음. **컨테이너 디스크(`/`, 홈 디렉토리 포함)는 Pod 재생성 시
  전부 초기화됨** — 자세한 구조는 `ai/docs/AI_PIPELINE_PLAN.md` 4장 참고.
- 파이썬은 `/workspace/.venv` 사용 (`bash /workspace/scripts/bootstrap_env.sh`로 복원/생성).
  VS Code Python 인터프리터를 `/workspace/.venv/bin/python`으로 지정해둘 것.
- 장시간 학습은 반드시 `train_rtdetr_auto.sh`/`train_segformer_auto.sh`(tmux 세션 안에서
  실행 추천) — 죽으면 최신 체크포인트에서 자동 재시작하는 래퍼 스크립트. 직접
  `python train_*.py`로 장시간 학습을 돌리지 말 것.
- 진행 상황은 `ai/train/status_dashboard.py`(포트 6007) 또는 TensorBoard(포트 6006, `--logdir
  /workspace/ai/checkpoints`)로 확인.

## 환경 관련 중요 교훈 (재발 방지 — 재학습 전 반드시 읽을 것)

- **torch 버전**: 이 Pod의 GPU(Blackwell, sm_120)는 컨테이너에 기본으로 깔린 구버전 torch가
  지원하지 않는다(`torch.cuda.is_available()`는 True를 반환해서 안 걸리고, 실제 forward
  pass에서 "no kernel image is available"로 터짐). venv에 `torch==2.7.1+cu128` 등
  `ai/requirements.txt`에 명시된 버전이 실제로 깔려있는지 확인할 것. `transformers`는 `5.0`
  이상이 구버전 torch의 DTensor API 부재로 깨진 이력이 있어 `4.49.0`으로 고정돼 있음.
- **학습률**: RT-DETR을 lr 1e-4로 사전학습 백본까지 균일하게 학습시켰다가 eval_map이 epoch
  1(0.07)부터 계속 떨어져 epoch 9~10에 0.0으로 완전히 발산한 적 있음. **사전학습 모델을
  파인튜닝할 땐 lr을 낮게(1e-5 근처) 시작할 것**, 특히 새 데이터셋/새 태스크로 처음
  시도할 때는 몇 epoch만 짧게 돌려서 eval 지표가 정상적으로 오르는지 먼저 확인.
- **평가 단계 메모리 폭발 (호스트 RAM, GPU 아님)**: 검증셋 전체를 한 번에 처리하는
  `compute_metrics`는 컨테이너 메모리 한도(31GB, `/sys/fs/cgroup/memory.max`)를 넘겨
  `Killed`(exit 137, CUDA 트레이스백 없음)로 죽인다. RT-DETR에서는
  `post_process_object_detection(threshold=0.0)`으로 박스를 무제한 누적해서, SegFormer에서는
  검증셋 전체 로짓을 한 번에 업샘플해서(2000장이면 약 21GB) 각각 터졌음. 학습 중 매 epoch
  평가는 서브셋(`--max-eval-samples`)으로, 대량 처리가 필요하면 반드시 청크 단위로 나눠서
  처리할 것. **SegFormer는 이렇게 고친 뒤에도 맨 마지막(최종) 평가에서만 한 번 더 죽었는데,
  정확한 원인(누적성 메모리 누수로 추정)은 못 찾았음** — 이미 학습이 100% 끝난 상태였어서
  Trainer의 마무리 로직을 다시 태우는 대신 `trainer_state.json`의 `best_model_checkpoint`가
  가리키는 체크포인트를 수동으로 `final/`에 복사해서 완료 처리했음. 비슷한 상황(학습은
  다 끝났는데 마무리 단계에서만 죽음) 나오면 같은 방법 쓸 것.
- **SegFormer는 RT-DETR보다 GPU 메모리를 훨씬 많이 씀**: 같은 batch 32에서 RT-DETR은
  8.5GB인데 SegFormer는 23GB+ 넘겨서 OOM. `--gradient-accumulation-steps`로 유효 배치는
  유지하면서 실제 배치 크기(및 메모리)를 줄여서 대응함(SegFormer는 batch 8 + accum 4 사용).
- **ONNX 익스포트는 반드시 dynamo 방식으로**: `torch.onnx.export`의 기본(레거시 tracing)
  방식으로 RT-DETR을 내보냈더니 opset과 무관하게 출력 자체가 PyTorch 원본과 틀렸음(deformable
  attention 같은 데이터 의존적 제어흐름을 tracing이 못 담음). `dynamo=True`로 바꿔서 해결—
  `ai/export/export_onnx.py`에 이미 반영돼 있으니 그대로 쓰면 되지만, 혹시 이 방식이
  아닌 새 익스포트 코드를 짤 일이 있으면 반드시 dynamo 방식을 쓰고 `verify_onnx.py`로
  실제 수치까지 확인할 것 — "에러 없이 저장됨"과 "출력이 맞음"은 다른 문제.
- 그 외 상세 사고 기록(스텝별 원인 분석, 실측 수치 등)은 Claude Code 메모리
  (`project-bridgesense-ai-status`)에도 남아있지만, Pod을 완전히 새로 만들면 그 메모리도
  `/workspace/.pod_home/claude`에 심볼릭 링크로 저장돼 있던 것이라 **bootstrap_env.sh를
  먼저 실행해야 복원됨** — 이 문서(git으로 버전관리됨)가 그보다 더 확실한 소스.

## 디렉토리 구조

```
ai/
├── docs/AI_PIPELINE_PLAN.md      # 아키텍처/기술스택/데이터 파이프라인 기획 문서
├── data_prep/
│   ├── extract_aihub_zips.py     # AI-Hub 카테고리별 zip 일괄 압축 해제
│   ├── convert_to_coco.py        # AI-Hub 라벨 → COCO 포맷
│   ├── verify_dataset.py         # 데이터셋 무결성 점검(이미지-라벨 매칭, 클래스 분포, 손상 이미지)
│   └── extract_bridge_spec.py    # 국토교통부 교량 제원 → JSON (후순위, 별개 작업)
├── train/
│   ├── dataset.py                 # 공용 COCO Dataset(오버샘플링+다운샘플링+재시도)
│   ├── train_rtdetr.py            # RT-DETR v2 파인튜닝 ✅ 완료
│   ├── train_segformer.py         # SegFormer 파인튜닝 ✅ 완료
│   ├── train_rtdetr_auto.sh       # 자동 재시도 래퍼(RT-DETR)
│   ├── train_segformer_auto.sh    # 자동 재시도 래퍼(SegFormer)
│   └── status_dashboard.py        # 학습 현황 웹 대시보드(포트 6007)
├── export/
│   ├── export_onnx.py            # ONNX 변환(dynamo 방식, opset 18) ✅ 완료
│   ├── verify_onnx.py            # onnxruntime 검증 ✅ 완료
│   └── model_io_spec.md          # 입출력 텐서 계약 문서(Unity Sentis와 공유)
├── checkpoints/{rtdetr_v2,segformer}/final/  # 최종 학습된 모델(git 미포함)
├── models/{rtdetr,segformer}.onnx            # 최종 ONNX 산출물(git 미포함)
└── requirements.txt
```

## 모델 및 데이터

| 모델 | 역할 | 베이스 | 상태 |
|---|---|---|---|
| RT-DETR v2 | 손상 객체 검출 (bbox) | `PekingU/rtdetr_v2_r18vd` | ✅ 완료, ONNX 검증됨 |
| SegFormer MiT-B2 | 균열 픽셀 분할 (mask) | `nvidia/mit-b2` | ✅ 완료, ONNX 검증됨 |

전체 420,074장 재질별 분포: 콘크리트 41.6%, 아스팔트 34.9%, 정상데이터 23.4%, **강재
0.1%(434장, 심각한 불균형)** — 오버샘플링으로 대응했으나, 2차 보강용 "건물 균열 탐지
이미지"(강재 서브셋 약 155,000건 확인됨)는 아직 미착수. 강재 클래스(부식/도장 박리) 성능이
낮으면 이 보강 데이터부터 검토할 것.

결함 클래스: 9종 + 정상데이터.
- 콘크리트: 균열, 박락, 백태, 누수, 철근 노출
- 아스팔트: 균열, 함몰
- 강재: 부식, 도장 박리
- 정상데이터 (결함 없음 — 검출 카테고리에는 포함하지 않고 네거티브 샘플로만 사용)

파일명 접두 2글자 코드로 클래스 판별 (`convert_to_coco.py`의 `PREFIX_TO_CLASS` 참고):
co=콘크리트균열, ef=백태, le=누수, sp=박락, ex=철근노출, st=강재부식, pa=도장박리,
as=아스팔트균열, po=함몰, no=정상데이터.

학습 데이터에 극락교(광주광역시 서구 마륵동, PSCI거더교, DB-18, 총길이 380m, 13경간) 등이
포함되지만, 모델은 극락교 전용이 아니라 사용자가 Unity 대시보드에 업로드하는 임의의 교량
이미지를 대상으로 동작해야 한다.

## ONNX 익스포트 규약 (완료됨 — Unity Sentis와의 계약)

- opset **18** (Sentis 지원범위 7~25 안, dynamo exporter가 18 미만을 거부해서 17에서 올림)
- 입출력 텐서 이름·shape 고정, 상세 스펙은 `ai/export/model_io_spec.md` 참고
- `verify_onnx.py`로 onnxruntime 출력이 PyTorch 원본과 일치함을 확인 완료
  (RT-DETR 오차 0.00002, SegFormer 오차 0.000008)
- `.onnx` 결과물은 `/workspace/ai/models/`에 있음(git에는 커밋 안 함)
- **Unity Sentis에서 실제 로드·추론되는지는 아직 미검증** — Unity 세션에서 확인 필요

## 시간·비용 관리 (RunPod 관련)

- 장시간 자리 비울 때는 Pod을 Stop/Terminate할지 사용자에게 먼저 확인할 것 — Claude Code가
  임의로 Pod을 종료/정지하지 않음
- 큰 산출물(체크포인트, 데이터, ONNX)을 git에 직접 커밋하지 말 것 — `.gitignore` 처리돼 있음

## 하지 말 것

- 웹 서버/API 엔드포인트를 만들지 말 것 (`status_dashboard.py`는 읽기 전용 모니터링 도구라 예외)
- AI-Hub 데이터 원본이나 파인튜닝 가중치(.pt, .onnx) 같은 대용량 바이너리를 git에 커밋하지 말 것
- Unity/C#/UI 관련 코드나 파일을 이 세션에서 건드리지 말 것 (별도 프로젝트·별도 세션)
- git push/PR은 사용자가 명시적으로 요청할 때만 — 요청받은 범위를 벗어나서 임의로 다음 단계로
  진행하지 말 것
