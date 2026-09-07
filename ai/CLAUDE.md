# CLAUDE.md — BridgeSense DT / ai (Python, 엘리스 AI 클라우드 환경)

이 파일은 `ai/` 디렉토리에서 작업하는 Claude Code 세션(엘리스 AI 클라우드)을 위한
프로젝트 컨텍스트다.

## 현재 상태 (2026-09-07 기준) — 먼저 읽을 것

RT-DETR v2 재학습, DeepLabV3+ 신규 학습, 두 모델 ONNX 익스포트, Unity Sentis 통합까지
**1차로 끝까지 돌아간 상태**다. 스모크 테스트(Sentis 로드, shape 검증, TestImages 픽셀
분포 확인)도 Unity 쪽에서 통과했다. 다만 DeepLabV3+ 정확도가 목표에 크게 못 미쳐서
**2차 재학습이 필요하다** — 아래 "DeepLabV3+ 1차 학습 결과와 다음 개선안"을 반드시
먼저 읽고 시작할 것. RT-DETR은 이번 재학습 결과가 아직 실측·기록되지 않았다(4절 참고,
확인 필요).

### DeepLabV3+ 1차 본 학습 결과 (2026-09-02~03, 22 epoch 후 중단)

- 50 epoch 계획이었으나 **epoch 13부터 val mean IoU 0.25~0.28 정체** → epoch 22 중단.
- **best = epoch 19, val mean IoU 0.2775 / macro-F1 0.310 / val loss 0.107.**
- 클래스별 IoU(ep19): 배경 0.99, 아스팔트균열 0.47, 콘크리트균열 0.39, 박락 0.26,
  철근노출 0.16, 누수 0.15, 함몰 0.14, 도장박리 0.09, 강재부식 0.07, **백태 0.01(사실상 실패)**.
- 균열류(주력 데이터)는 무작위 초기화 CNN의 현실적 상한 근처라 OK. 문제는:
  - 백태: 어노 10만 개인데 IoU 0.01. 저대비·경계 애매 + 라벨 품질 의심.
  - 강재부식(258장·10배 오버샘플): 초반 0.28 → 후반 0.07로 퇴화(다수 클래스에 밀림).
  - Dice+Focal 1:1 + oversample 10이 희소/저대비 클래스를 못 살림.
- train loss는 계속 하강(0.18)했으나 val IoU 정체 = 다수 클래스는 수렴, 소수 클래스는 한계.
- Unity 쪽 실측(TestImages 60장, `DeeplabImageValidator`)도 이 진단과 정확히 일치했다:
  Normal(정상) 100% 배경 판별, Asphalt crack 평균 면적률 1.77%로 뚜렷하게 잡힘. 반면
  Efflorescence(백태)는 10장 전부 기대 클래스 면적률 0.00% — 단 한 픽셀도 못 잡고 대신
  cls4~6(철근노출·강재부식·도장박리) 쪽으로 새는 경향이 뚜렷했다.

### 다음 재학습 개선안 (우선순위 아님, 실험하며 조정)

- **Tversky loss**(α<β로 FN 페널티↑) 또는 Focal 비중 대폭↑ — 현재 Dice+Focal 1:1이
  희소 클래스를 못 살림.
- oversample 20~30으로 상향(현재 10) 또는 클래스별 손실 가중치 도입.
- 인코더를 `resnet34`→`resnet50`으로(용량 여유 있으면).
- **백태 라벨 품질 재검토** — IoU 0.01은 모델보다 라벨/데이터 문제일 가능성도 있음
  (`ai/data_prep/verify_dataset.py`로 백태 어노테이션 샘플을 직접 눈으로 확인해볼 것).
- **무엇보다 데이터 보강**(백태·강재부식·도장박리 어노테이션·증강) — 오버샘플링만으로는
  한계가 뚜렷하게 보였다.
- 재학습은 `--resume <output-dir>/last.pt`로 중단 지점부터 이어가지 말고, 손실
  함수·oversample 배수를 바꾸는 순간 **처음부터 다시 학습할 것**(옵티마이저 상태가
  달라진 손실 함수에 안 맞음).

## 0. 환경 (확인 완료)

- 작업/영구 저장 경로: `/home/elicer/BridgeSense_DT` (RunPod의 `/workspace`에 대응).
  체크포인트는 `/home/elicer/BridgeSense_DT/ai/checkpoints/<job>/`, 모델 산출물은
  `/home/elicer/BridgeSense_DT/ai/models/`.
- venv: `/home/elicer/BridgeSense_DT/.venv` (`status_dashboard.py` 실행 예시 참고).
- GPU/torch 버전은 1차 학습이 실제로 완주(중단은 정체 판단에 의한 것이지 환경 문제가
  아니었음)했으므로 이 스택에서 정상 동작 확인됨 — torch 2.7.1 / transformers 4.49.0.
- 포트: TensorBoard 6006, `status_dashboard.py` 6007 (VS Code PORTS 탭에서 forward).

## 1. 이 세션의 목표

**BridgeSense DT**는 2026 오픈소스 개발자대회 출품작(교량 안전점검 디지털 트윈, Unity 6)의
AI 파트다. 결함 검출(RT-DETR v2)과 결함 분할(DeepLabV3+, 이번에 새로 추가)을 재학습해서
Unity Sentis에서 쓸 `.onnx`를 만드는 것이 이 세션의 범위다.

### 왜 SegFormer가 아니라 DeepLabV3+인가 (반드시 읽을 것)

이전에는 RT-DETR v2(검출) + SegFormer MiT-B2(분할) 조합을 썼다. 그런데 SegFormer의
베이스 체크포인트(`nvidia/mit-b2`)가 제공하는 **사전학습 가중치**가
[NVIDIA Source Code License](https://github.com/NVlabs/SegFormer/blob/master/LICENSE)
3.3조에 따라 "비상업적 용도(연구·평가)로만 사용 가능"하고, 이 제약이 그 가중치로
파인튜닝한 파생물에도 승계된다는 게 뒤늦게 확인됐다. 오픈소스로 결과물을 공개해야 하는
이 프로젝트와 양립할 수 없어 SegFormer를 완전히 제거했다.

**같은 실수를 반복하지 말 것.** 이번에 새로 도입하는 모델은 다음 두 가지를 반드시
만족해야 한다:
- 아키텍처 코드가 OSI 인증 오픈소스 라이선스(MIT/Apache-2.0 등)일 것
- **가중치를 실제로 다운로드해 쓸 경우, 그 가중치 자체의 라이선스도 별도로 확인할 것.**
  "이 모델의 코드는 Apache-2.0"이라는 말이 "이 모델의 사전학습 가중치도 자유롭게 써도
  된다"를 보장하지 않는다 — 이번 SegFormer 건이 정확히 이 함정이었다. Hugging Face 모델
  카드의 `license` 필드가 `other`이면 반드시 원 저장소의 LICENSE 파일을 직접 열어
  확인할 것.

이번에 선택한 조합(아래 3절)은 **사전학습 가중치를 아예 쓰지 않는다**
(`encoder_weights=None`, 무작위 초기화 후 AI-Hub 데이터로 처음부터 학습). 그래서
어떤 백본을 encoder로 골라도 가중치 라이선스 문제 자체가 발생하지 않는다 — 오직
아키텍처 코드 라이선스만 확인하면 된다.

## 2. 검출 모델: RT-DETR v2 (재학습 — 정확도 개선)

기존과 동일하게 `PekingU/rtdetr_v2_r18vd`(Apache License 2.0, COCO 80클래스 사전학습)를
파인튜닝한다. 이 모델은 라이선스 문제가 전혀 없었다 — 재학습하는 이유는 순전히 정확도
개선이다.

- 스크립트: `ai/train/train_rtdetr.py` (있음, 그대로 재사용)
- RunPod 시절 학습 결과: 7 epoch, lr 1e-5, batch 32 → eval_map 0.047~0.056 (낮음)
- **엘리스에서 재학습한 결과가 아직 이 문서에 기록되지 않았다.** RT-DETR도 재학습을
  돌렸다면 학습 로그(최종 eval_map, epoch 수)를 여기 추가할 것 — 없으면 먼저 확인.
- 개선 방향(시도해볼 것, 순서는 우선순위 아님 — 실험하며 조정):
  - epoch 수를 늘려서(현재 15가 기본값) 더 오래 학습 — 이전엔 7 epoch에서 멈췄을 수 있음
  - `--oversample-steel` 배수 조정 (현재 기본 10)
  - `--lr`을 1e-5 근처에서 미세 조정(그리드 서치까지는 아니어도 2~3개 값 비교)
  - data augmentation 강화가 필요하면 `dataset.py`의 `CocoDetectionDataset`을 확장
    (현재는 augmentation 없음 — HF image_processor의 리사이즈/정규화만 적용됨)
- **주의**: 예전에 lr 1e-4로 시도했다가 eval_map이 epoch 1(0.07)부터 계속 떨어져
  epoch 9~10에 0.0으로 완전히 발산한 이력이 있다. 새 lr을 시도할 때는 반드시 몇 epoch만
  짧게 돌려서 eval 지표가 정상적으로 오르는지 먼저 확인할 것.
- 목표: mAP를 최소 0.1대, 가능하면 그 이상으로 끌어올리는 것. 다만 RT-DETR류 검출기가
  균열처럼 경계가 불명확하고 가늘고 긴 객체를 bbox로 표현하는 것 자체가 본질적으로
  어려운 태스크라는 점은 감안할 것 — 극적인 개선을 보장할 수는 없다.

## 3. 분할 모델: DeepLabV3+ (신규 — SegFormer 대체)

### 라이브러리 및 구성

- 라이브러리: [`segmentation_models_pytorch`](https://github.com/qubvel-org/segmentation_models.pytorch) (`smp`, **MIT License**)
  - 설치: `pip install segmentation-models-pytorch`
- 아키텍처: `smp.DeepLabV3Plus`
- 인코더: `resnet34` (1순위, 가볍고 검증된 표준. smp 내부에 구현체 포함, Apache-2.0/MIT
  계열) — 필요하면 `resnet50`(정확도↑ 무게↑) 또는 `efficientnet-b0`(더 가벼움)로 조정
  가능. **어떤 인코더를 고르든 `encoder_weights=None`을 반드시 유지할 것** —
  사전학습 가중치를 받는 순간 그 가중치의 라이선스를 다시 확인해야 하는 원점으로
  돌아간다.
- 클래스 수: 10 (배경/정상 1 + 결함 9종), 기존 SegFormer 때와 동일한 클래스 순서
  (`ai/data_prep/convert_to_coco.py`의 `CLASS_NAMES` 순서, id 0은 배경, RT-DETR
  클래스 id에 +1 shift)
- 입력 해상도: 512×512 (AI-Hub 원본 해상도와 동일해서 업/다운스케일 불필요 — 예전
  SegFormer 스펙과 맞춰 Unity 쪽 재사용성을 높인다)

```python
import segmentation_models_pytorch as smp

model = smp.DeepLabV3Plus(
    encoder_name="resnet34",
    encoder_weights=None,      # 반드시 None — 사전학습 가중치 다운로드 안 함
    in_channels=3,
    classes=10,                # 배경 1 + 결함 9종
)
```

### 학습 스크립트 — 이미 작성됨, 1차 학습에 사용됨

`ai/train/train_deeplabv3plus.py`가 이미 있고, 위 "DeepLabV3+ 1차 본 학습 결과"가 이
스크립트로 만들어졌다. 재작성이 아니라 **하이퍼파라미터/손실 함수 조정 후 재실행**이
필요한 단계다. 구조 참고:

- `ai/train/dataset.py`에 `CocoSegmentationDataset`, `build_seg_transforms`가 이미
  구현돼 있다(COCO segmentation polygon → `pycocotools.annToMask`로 rasterize, 전처리는
  자체 transform 함수로 구현 — HF image_processor 없이 동작).
- HF `Trainer`를 안 쓰고 직접 학습 루프(옵티마이저 AdamW, 스케줄러 OneCycleLR,
  체크포인트 저장까지 전부 수동 구현)로 되어 있다.
- 손실 함수는 현재 `ComboLoss`(Dice + Focal, smp.losses 기반, 1:1 가중치) — 위 1차
  결과에 따르면 이 조합이 희소 클래스를 못 살렸으므로 다음 재학습에서 조정 대상이다.
- 평가지표는 mean IoU, `smp.metrics.get_stats`+`iou_score`로 청크 단위 누적 계산.
- 오버샘플링은 `build_balanced_index`(RT-DETR과 공용)를 그대로 사용 중, 현재 배수 10.
- 체크포인트 저장 규약: `<output-dir>/final/model.pt`(state_dict) +
  `<output-dir>/final/config.json`(encoder_name/classes/in_channels/image_size/
  class_names/normalize_mean/std). `ai/export/export_onnx.py`가 이 규약대로 로드한다 —
  바꾸면 export 쪽도 같이 고칠 것.

### 실측된 특성 (1차 학습 결과 기준, 위 "DeepLabV3+ 1차 본 학습 결과" 참고)

- 사전학습 가중치를 안 써서 SegFormer(10 epoch에 mean_iou 0.47)보다 수렴이 훨씬 느리다
  — 22 epoch까지 돌렸는데도 mean_iou 0.28 근처에서 정체됐다. CNN이라 ViT보다는 낫겠지만
  가설만큼 극적이지는 않았다.
- 모델 크기: 실측 약 89.7MB(`deeplabv3plus.onnx`) — 애초 예상(45~55MB)보다 크다.
  SegFormer(112MB)보다는 여전히 작음.
- 클래스별 성능 격차가 크다(아스팔트균열 0.47 vs 백태 0.01) — 단순 데이터 양보다
  클래스별 시각적 특징(대비, 경계 명확성)과 오버샘플링 전략이 더 크게 작용하는 것으로
  보인다.

## 4. ONNX 익스포트 (완료됨 — 두 모델 다 통일된 방식으로 검증 완료)

`ai/export/export_onnx.py`, `ai/export/verify_onnx.py`가 이미 RT-DETR/DeepLabV3+ 둘 다
지원한다(`--model-type rtdetr|deeplabv3plus`). **이 환경(torch 2.7.1 / transformers
4.49.0, 2026-09-01 실측)에서는 두 모델 다 레거시 tracing(dynamo=False) + opset 18로
통일돼 있다** — 아래 이유 때문에 애초 계획(dynamo 방식)에서 바뀌었다:

- 레거시 tracing: RT-DETR 오차 0.00003, DeepLabV3+ 오차 0.000006. 배치 축 동적('batch')
  정상 동작.
- dynamo(`torch.onnx.export(dynamo=True)`): 두 모델 다 배치 축을 `batch=1`로 고정해버리고,
  RT-DETR은 내부 그래프 노드까지 출력으로 leak(출력 14개로 늘어남). `dynamic_shapes`를
  줘도 해결 안 됨.

**옛 기록**("레거시 tracing으로 RT-DETR 냈다가 logits 오차 1.24 → dynamo로 0.00002 정상화")은
RunPod 시절 **구버전 transformers 스택** 기준 얘기다. 지금 스택에서는 정반대이므로,
dynamo로 되돌리기 전에 반드시 `verify_onnx.py`로 오차와 배치 축을 먼저 확인할 것.

DeepLabV3+ 출력 `logits`는 실측 `[batch, 10, 512, 512]`로 확인됨 — `smp.DeepLabV3Plus`
디코더가 이미 입력 해상도로 업샘플해서 출력하므로 별도 업샘플 래퍼가 필요 없었다
(`export_onnx.py`의 `export_deeplabv3plus`가 이 shape을 export 시점에 직접 검증하고,
다르면 예외를 던지도록 되어 있음).

Unity Sentis 쪽에서도 두 모델 모두 로드·추론·shape 검증까지 통과했다(`SentisSmokeTest`,
`DeeplabImageValidator` 결과, 2026-09-07).

## 5. 데이터 준비 (완료됨 — 보강 시에만 재실행)

`data/coco_format/{train,val}.json`이 이미 존재하고 1차 학습에 실제로 쓰였다.
재학습(2차) 시에는 이 데이터를 그대로 재사용하면 되고, 데이터 자체를 보강할 경우에만
아래 순서를 다시 밟을 것:

1. AI-Hub에서 "교량 외관점검 입면정사영상 데이터"를 내려받는다.
2. `ai/data_prep/extract_aihub_zips.py` — 카테고리별 zip 일괄 압축 해제
3. `ai/data_prep/convert_to_coco.py` — AI-Hub 라벨(폴리곤) → COCO 포맷 변환.
   파일명 접두 2글자로 클래스 판별(`PREFIX_TO_CLASS`): co=콘크리트균열, ef=백태,
   le=누수, sp=박락, ex=철근노출, st=강재부식, pa=도장박리, as=아스팔트균열, po=함몰,
   no=정상데이터. 이 순서가 그대로 category id(0~8)가 된다 — RT-DETR/DeepLabV3+ 둘 다
   이 순서를 그대로 따라야 서로 어긋나지 않는다.
4. `ai/data_prep/verify_dataset.py` — 변환 결과 무결성 점검(이미지-라벨 매칭, 클래스
   분포, 손상 이미지 확인). **백태 라벨 품질을 재검토할 때 특히 이 스크립트로 직접
   샘플을 눈으로 확인해볼 것** (위 1차 학습 결과에서 백태 IoU가 사실상 0이었음).
5. 전체 데이터 규모는 약 420,074장(콘크리트 41.6%, 아스팔트 34.9%, 정상데이터 23.4%,
   강재 0.1%) — 강재 클래스가 극도로 희소하므로 오버샘플링(`build_balanced_index`)이
   필수. 1차 학습 결과 오버샘플 배수 10으로는 부족해 보였다(위 개선안 참고).

`data/aihub/`, `data/coco_format/` 등은 용량이 크므로 git에 커밋하지 말 것
(`.gitignore`에 이미 패턴이 있으면 그대로 따르고, 없으면 추가할 것).

## 6. 디렉토리 구조 (현재, 전부 구현 완료)

```
ai/
├── data_prep/
│   ├── extract_aihub_zips.py     # AI-Hub 카테고리별 zip 일괄 압축 해제
│   ├── convert_to_coco.py        # AI-Hub 라벨 → COCO 포맷
│   ├── verify_dataset.py         # 데이터셋 무결성 점검
│   └── download_aihub.py         # AI-Hub 다운로드 스크립트
├── train/
│   ├── dataset.py                 # 공용 COCO Dataset 유틸(오버샘플링+다운샘플링+재시도)
│   │                              # + CocoDetectionDataset(RT-DETR용)
│   │                              # + CocoSegmentationDataset, build_seg_transforms(DeepLabV3+용)
│   ├── train_rtdetr.py            # RT-DETR v2 학습
│   ├── train_rtdetr_auto.sh       # 자동 재시도 래퍼(RT-DETR)
│   ├── train_deeplabv3plus.py     # DeepLabV3+ 학습 (2차 재학습 대상 — 위 개선안 참고)
│   └── status_dashboard.py        # 학습 현황 웹 대시보드(포트 6007). RT-DETR(HF Trainer,
│                                  # trainer_state.json)만 읽음 — DeepLabV3+는 직접 루프라
│                                  # trainer_state.json이 없다. DeepLab 진행 현황은
│                                  # `tensorboard --logdir <output-dir>/tb`로 볼 것.
├── export/
│   ├── export_onnx.py            # RT-DETR + DeepLabV3+ ONNX 변환 (--model-type로 선택)
│   └── verify_onnx.py            # onnxruntime 검증 (두 모델 다 지원)
└── requirements.txt
```

## 7. 환경 관련 교훈 (RunPod 시절 기록 — 엘리스에서도 유효할 수 있으니 참고)

- **torch 버전과 GPU 아키텍처**: RunPod의 GPU(Blackwell, sm_120)는 컨테이너 기본 torch가
  지원하지 않아 `torch==2.7.1+cu128`로 맞춰야 했다. `torch.cuda.is_available()`은
  True를 반환해도 실제 forward pass에서 "no kernel image is available"로 죽는 방식이라
  이 체크만으론 안 걸린다 — 실제 텐서 연산을 한 번 돌려서 확인할 것. 엘리스 GPU가
  다르면 이 문제 자체가 없을 수 있음.
- **`transformers` 버전**: `5.0` 이상은 구버전 torch의 `torch.distributed.tensor.DTensor`
  API 부재로 import가 깨진 이력이 있어 RT-DETR 쪽은 `4.49.0`으로 고정돼 있었다.
  DeepLabV3+ 학습에는 `transformers`가 필수는 아니지만(순수 `smp`+`torch`만으로 가능),
  RT-DETR과 같은 venv를 쓴다면 버전 충돌 없는지 확인할 것.
- **평가 단계 메모리 폭발(호스트 RAM, GPU 아님)**: 검증셋 전체를 한 번에 처리하면
  터진다. RT-DETR 쪽은 `post_process_object_detection(threshold=0.05)`로 대응했음
  (원래 0.0으로 뒀다가 메모리 초과로 죽었던 이력). DeepLabV3+ 평가(mean IoU 계산)는
  `smp.metrics.get_stats`로 배치별 tp/fp/fn/tn만 누적하는 방식으로 이미 이 문제를
  피해서 구현돼 있다(`train_deeplabv3plus.py`의 `evaluate` 함수).
- **ONNX exporter 방식은 dynamo가 항상 정답은 아니다** — 4절 참고. 이 스택(torch 2.7.1
  / transformers 4.49.0)에서는 레거시 tracing이 더 정확했다. exporter 방식을 바꿀
  때마다 반드시 `verify_onnx.py`로 실제 수치와 배치 축 동작을 확인할 것 — "에러 없이
  저장됨"과 "출력이 맞음"은 다른 문제.

## 8. 하지 말 것

- **가중치 라이선스를 "코드가 Apache-2.0이니까 괜찮겠지"로 넘겨짚지 말 것.** 이번 세션
  시작의 원인이 정확히 이 실수였다. 사전학습 가중치를 실제로 받아 쓸 일이 생기면
  (이번 DeepLabV3+ 계획에서는 없지만, 나중에 다른 백본으로 바꾸는 경우 등) 반드시
  Hugging Face 카드의 `license` 필드와 원 저장소 LICENSE 파일을 직접 대조할 것.
- 웹 서버/API 엔드포인트를 만들지 말 것(`status_dashboard.py`는 읽기 전용 모니터링
  도구라 예외).
- AI-Hub 데이터 원본이나 학습 가중치(.pt, .onnx) 같은 대용량 바이너리를 git에 커밋하지
  말 것.
- Unity/C#/UI 관련 코드는 이 세션에서 건드리지 말 것(별도 프로젝트·별도 세션에서 이미
  통합 완료됨 — `DeeplabModel.cs`, `AiInferenceManager.cs` 등). 이 세션은 `.onnx` 파일과
  입출력 shape 정보를 넘겨주는 것까지가 범위.
- git push/PR은 사용자가 명시적으로 요청할 때만 진행할 것 — 임의로 다음 단계로
  진행하지 말 것.
- 학습을 시작하기 전에 항상 몇 epoch/스텝만 스모크 테스트로 돌려서 파이프라인이
  정상 동작하는지 먼저 확인할 것(본 학습을 몇 시간 돌린 뒤에야 버그를 발견하는 낭비를
  피하기 위함).
