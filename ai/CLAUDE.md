# CLAUDE.md — BridgeSense DT / ai (Python, 엘리스 AI 클라우드 환경)

이 파일은 `ai/` 디렉토리에서 작업하는 Claude Code 세션(엘리스 AI 클라우드)을 위한
프로젝트 컨텍스트다. **이 세션은 처음 시작하는 것이다** — 이전에 RunPod에서 학습했던
산출물(체크포인트, `.onnx`)은 이 환경에 없다. 아래 순서대로 진행할 것.

## 0-A. 검증된 환경 정보 (2026-09-01 실측 — 다음 세션은 이 절을 신뢰해도 됨)

- **GPU**: NVIDIA A100 80GB PCIe, 단 **MIG 3g.40gb 슬라이스**로 분할되어 실사용 가능
  메모리는 **~40GB**. sm_80. `nvidia-smi`에서 "MIG M. Enabled"로 나옴. 실제 텐서
  연산(conv2d, matmul) GPU에서 정상 동작 확인 — RunPod의 sm_120 커널 부재 문제는 **없음**.
- **torch**: `2.7.1` (설치 시 cu126 휠이 딸려옴, sm_80에서 정상). **2.4.1은 쓰지 말 것** —
  `torch.onnx.export`가 `dynamic_shapes` 인자를 안 받아서 ONNX 익스포트가 깨진다.
  `transformers==4.49.0`은 torch 2.7.1과 정상 동작(RT-DETR import 확인).
- **Python**: 3.10.14. venv는 `/home/elicer/BridgeSense_DT/.venv` 에 생성됨.
  `pip install -r ai/requirements.txt` 로 전체 설치 완료(torch 2.7.1로 오버라이드됨).
  `albumentations==1.4.18` (1.4.15는 albucore 0.2.16과 import 충돌).
- **영구 저장소**: `/home/elicer` 아래를 쓴다(사용자 지정). 프로젝트 `/home/elicer/BridgeSense_DT`,
  데이터는 `/home/elicer/BridgeSense_DT/data/` (`.gitignore`에 `data/` 등록됨).
  **`/home/elicer`는 overlay 파일시스템(128G, 실측 여유 ~114G)** — `/dev/sda4`(860G)가
  아니다. 데이터셋 71774는 tar ~43G + 압축해제 ~45G ≈ 88G라 빠듯하다:
  extract 직후 `download.tar`와 중간 zip을 삭제해서 공간을 회수할 것.
- **세션/저장소 영속성 (2026-09-01 조사)**: 이 환경은 **code-server(웹 VSCode)** 기반이고,
  `/home/elicer` 전체가 별도 영구 디스크(`/dev/loop0` = `/mnt/elice/.main_disk/diff`, 128G xfs)에
  실재한다. overlay 루트의 쓰기 레이어가 이 디스크에 매핑됨 — `/home/elicer/**`의 파일과
  `/mnt/elice/.main_disk/diff/home/elicer/**`가 동일 inode. 따라서 **세션을 껐다 다시
  들어와도 코드·`data/`·`.venv`·`.claude/`(메모리)는 그대로 유지된다.** 단:
  - `/tmp/**`, 스크래치패드, 실행 중이던 백그라운드 프로세스는 재시작 시 사라진다.
  - **인스턴스를 완전 삭제/재생성하면 이 디스크도 날아간다** → 코드는 git repo로 만들어
    원격에 push 해두는 게 안전(현재 git repo 아님). `data/aihub_extracted`(36GB)는
    재다운로드에 시간이 걸리니 인스턴스 삭제 전 반드시 확인.
  - 100% 확신하려면 엘리스 콘솔/문서의 영구 저장소 정책을 직접 볼 것.
- **백그라운드 프로세스**: `tmux`/`setsid`/`nohup` 모두 설치됨. 본 학습은 tmux 안에서
  돌릴 것(세션 끊겨도 학습 지속). nohup+&로도 이번 세션 내 스모크는 잘 돌았음.
- **ONNX 익스포터**: 이 스택에서는 **두 모델 다 레거시 tracing**(dynamo가 배치 축을
  batch=1로 고정 + RT-DETR은 출력 leak). CLAUDE.md 7절의 "dynamo 필수"는 구버전 스택
  얘기다. 자세한 이유·실측 오차는 `ai/export/export_onnx.py` docstring, 4절 참고.
- **aihubshell**: 시스템에 없음. `ai/data_prep/download_aihub.py`가 공식 배포 스크립트를
  `/home/elicer/BridgeSense_DT/.tools/`에 자동 설치하고 데이터셋 71774를 받는다.
  API Key는 `ai/data_prep/api_key.txt`(36자, `.gitignore` 등록됨)에서 자동으로 읽는다.
  주의: aihubshell이 키를 curl 커맨드라인 인자로 넘겨서 `ps aux`에 노출된다(단일 사용자
  인스턴스라 실무상 위험 낮음, 하지만 인지할 것).
- **데이터 다운로드 (2026-09-01 진행 중/완료)**: `download_aihub.py --file-key all` 로
  `data/aihub/download.tar` 하나로 받는다(~43GB, ~20MB/s). curl `-C -` 라 중단 시 재개됨.
  받은 뒤: `extract_aihub_zips.py`(tar 해제 + 분할 zip 병합 — 큰 카테고리는 .zip+.z01+...
  분할압축이라 zip 3.0의 `zip -s 0 --out` 로 합침. 7z는 이 환경에 없음) →
  `convert_to_coco.py` (train/val 각각) → `verify_dataset.py`.

## 0. 가장 먼저 할 일 — 환경 파악 (위 0-A가 이미 답한 항목은 재확인 불필요)

이 문서는 RunPod가 아니라 엘리스 AI 클라우드 기준으로 새로 작성됐고, **엘리스 쪽의
정확한 디렉토리 구조(영구 저장소 경로, GPU 스펙, 세션 유지 방식)는 아직 검증되지
않았다.** 작업을 시작하기 전에 직접 확인하고, 확인한 내용을 이 파일 맨 위에 추가해 둘 것
(다음 세션이 또 헤매지 않도록):

1. 영구 저장소(재시작해도 사라지지 않는 디스크)가 어느 경로에 마운트되어 있는지 확인
   (`df -h`, 플랫폼 문서, 또는 관리 콘솔에서 확인). RunPod의 `/workspace`에 대응하는
   경로를 찾아서 이 문서의 `<PERSIST>` 표기를 전부 실제 경로로 바꿔 쓸 것.
2. GPU 종류·메모리 확인 (`nvidia-smi`). 이전 RunPod 환경은 RTX PRO 4000 Blackwell
   24GB였는데, sm_120 아키텍처라 구버전 torch가 아예 안 돌아가는 특수 사정이 있었다
   (아래 "환경 관련 교훈" 참고). 엘리스 쪽 GPU가 다르면 이 문제가 없을 수도 있으니
   `torch.cuda.is_available()`뿐 아니라 실제로 텐서 연산 한 번 돌려서 확인할 것.
3. 세션이 끊겼을 때(SSH 재접속, 웹 터미널 새로고침 등) 백그라운드 프로세스가 유지되는지,
   아니면 RunPod처럼 tmux 같은 걸 직접 써야 하는지 확인.
4. Python/CUDA/torch 버전이 이미 컨테이너에 깔려 있는지, 새로 설치해야 하는지 확인.

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

- 스크립트: `ai/train/train_rtdetr.py` (이미 있음, 그대로 재사용)
- 이전 학습 결과: 7 epoch, lr 1e-5, batch 32 → eval_map 0.047~0.056 (낮음)
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

### 학습 스크립트 — 새로 작성해야 함

`ai/train/train_deeplabv3plus.py`가 아직 없다. 다음을 참고해서 새로 작성할 것:

- **재사용 가능한 기존 코드**: `ai/train/dataset.py`의 `build_balanced_index`,
  `open_image_with_retry`, `MATERIAL_MAP`, `STEEL_CLASS_NAMES`는 태스크 무관 공용
  유틸이므로 그대로 재사용. `CocoDetectionDataset`은 RT-DETR(HF image_processor 인터페이스)
  전용이라 분할에는 안 맞는다 — 새 `CocoSegmentationDataset`을 `dataset.py`에 추가할 것
  (예전에 있다가 SegFormer와 함께 삭제된 클래스와 거의 같은 구조이면 됨: COCO
  segmentation polygon을 `pycocotools`의 `annToMask`로 픽셀 마스크로 rasterize).
  다만 `smp`는 HF `image_processor`가 없으므로 전처리(리사이즈, 정규화)를
  `albumentations`나 `torchvision.transforms`로 직접 구현해야 한다.
- **HF `Trainer`를 쓰지 않는다** — `smp`는 순수 PyTorch 모델이라 HF Trainer API에
  맞지 않는다. 직접 학습 루프(옵티마이저·스케줄러·체크포인트 저장까지 손으로 작성)를
  구현하거나, `pytorch-lightning`을 새로 도입할 수도 있다(도입한다면 그것도 라이선스
  확인 — PyTorch Lightning은 Apache-2.0이라 안전함).
- **손실 함수**: `smp.losses`에 Dice, Focal, Jaccard 등이 포함되어 있다(smp 패키지
  자체가 MIT라 이것도 안전). 클래스 불균형이 심하므로(강재 관련 클래스가 0.1%) Dice
  Loss나 Focal Loss, 혹은 둘의 조합을 우선 고려할 것 — 단순 CrossEntropy는 배경 클래스에
  압도당하기 쉽다.
- **평가지표**: mean IoU (예전 SegFormer 학습 때와 동일 기준으로 비교 가능하게).
  `smp.metrics`에 IoU 계산 유틸이 있다(`smp.metrics.iou_score`).
- **오버샘플링/다운샘플링**: RT-DETR과 동일하게 `build_balanced_index`를 그대로 써서
  강재 클래스 불균형에 대응할 것.
- **체크포인트 저장 형식**: HF Trainer가 아니므로 `torch.save(model.state_dict(), ...)`
  방식이 된다. ONNX 익스포트 스크립트(`ai/export/export_onnx.py`, 아래 4절)가
  로드할 수 있는 형태로 저장 규약을 스스로 정하고 문서화할 것(예: `final/model.pt` +
  `final/config.json`에 encoder_name·classes 등 메타 저장).

### 예상되는 특성 (설계 의도, 실측 후 조정)

- 사전학습 가중치를 안 쓰므로 SegFormer 때보다 수렴이 느리다 — epoch을 넉넉히 잡을 것
  (SegFormer는 10 epoch에 mean_iou 0.47이었는데, 이번엔 그보다 훨씬 많은 epoch이
  필요할 수 있다). 몇 epoch 안에 loss가 정상적으로 떨어지는지 스모크 테스트로 먼저
  확인.
- CNN(ResNet)이라 ViT(SegFormer의 MiT 인코더) 대비 지역성(locality)·이동 불변성이
  구조에 내장돼 있어, 사전학습 없이도 ViT보다 데이터 효율이 좋을 것으로 기대된다 —
  이게 애초에 SegFormer 대신 DeepLabV3+를 고른 이유. 다만 실측 전까지는 가설이다.
- 모델 크기는 `resnet34` 인코더 기준 대략 45~55MB로 예상(SegFormer 112MB보다 가벼움) —
  ONNX 변환 후 실측할 것.

## 4. ONNX 익스포트 (2026-09-01 스모크 검증 완료)

`ai/export/export_onnx.py` + `verify_onnx.py`가 두 모델 다 지원한다:
```
python export_onnx.py --model-type {rtdetr|deeplabv3plus} --checkpoint <.../final> --out <....onnx>
python verify_onnx.py --model-type {rtdetr|deeplabv3plus} --checkpoint <.../final> --onnx <....onnx>
```

- **둘 다 레거시 tracing 방식**(`torch.onnx.export(dynamo=False, dynamic_axes=...)`, opset 18).
  이 환경(torch 2.7.1 / transformers 4.49.0)에서 dynamo 방식은 두 모델 다 배치 축을
  batch=1로 고정하고 RT-DETR은 내부 노드를 출력으로 leak한다. 레거시 tracing은
  배치 축 동적('batch') + 수치 정확(RT-DETR 오차 3e-5, DeepLabV3+ 6e-6).
  **CLAUDE.md 이전 기록("tracing으로 RT-DETR 오차 1.24 → dynamo로 해결")은 구버전
  transformers 스택 얘기다 — 현재 스택에서는 정반대.** export_onnx.py docstring 참고.
- 출력은 wrapper로 감싸 딱 필요한 것만: RT-DETR `logits[b,300,9]`+`pred_boxes[b,300,4]`
  (입력 `[b,3,640,640]`), DeepLabV3+ `logits[b,10,512,512]` (입력 `[b,3,512,512]`).
- DeepLabV3+ 디코더가 이미 입력 해상도로 업샘플해서 출력하므로 별도 업샘플 래퍼 불필요
  (SegFormer는 1/4 해상도라 래퍼가 필요했음) — export 시 실제 shape을 assert로 확인함.
- `export_onnx.py`가 저장 직후 `onnx.checker`로 한 번 더 읽어 검증한다(export+verify를
  한 스크립트에서 이어 돌릴 때 flush 전에 읽어서 수치가 어긋나 보였던 이력 때문).
- 두 모델 다 `verify_onnx.py`로 PyTorch 원본과 onnxruntime 출력 일치를 반드시 확인할 것.

## 5. 데이터 준비 (2026-09-01 완료 — 아래는 재현 절차 + 실측값)

`data/coco_format/{train,val}.json`을 아래 순서로 생성했다:

1. `ai/data_prep/download_aihub.py` — aihubshell로 데이터셋 71774 다운로드.
   `api_key.txt`에서 키 자동 로드. 받은 위치:
   `data/aihub/040.교량_3D_외관점검_영상_데이터/3.개방데이터/1.데이터/`
   (큰 카테고리는 병합 실패해 `*.zip.part<offset>` 조각으로 옴)
2. `ai/data_prep/extract_aihub_zips.py` — `.part` 조각 offset순 병합 → unzip →
   해제 후 zip 삭제(공간 회수). 결과:
   `data/aihub_extracted/{Training,Validation}/{원천데이터,라벨링데이터}/`
3. `ai/data_prep/convert_to_coco.py` — AI-Hub 라벨(LabelMe 스타일 폴리곤) → COCO.
   파일명 접두 2글자로 클래스 판별(`PREFIX_TO_CLASS`): co=콘크리트균열, ef=백태,
   le=누수, sp=박락, ex=철근노출, st=강재부식, pa=도장박리, as=아스팔트균열, po=함몰,
   no=정상데이터. 이 순서가 그대로 category id(0~8). **실측으로 접두어 검증됨**
   (as/no/po/st/pa/ex 확인, 라벨 JSON 키 구조도 스크립트 가정과 일치).
   train/val 각각 한 번씩 실행.
4. `ai/data_prep/verify_dataset.py` — 무결성 점검.
5. **실측 데이터 규모**: Training 336,068장 + Validation 42,004장 = 378,072장
   (이미지-라벨 1:1 매칭). CLAUDE.md 이전 기록(420,074장)보다 적음 — 데이터셋
   버전 차이로 보임. 강재(st) 클래스가 Training에 244장뿐 = 0.07% →
   오버샘플링(`build_balanced_index`)이 필수.

`data/aihub/`, `data/coco_format/` 등은 용량이 크므로 git에 커밋하지 말 것
(`.gitignore`에 이미 패턴이 있으면 그대로 따르고, 없으면 추가할 것).

## 6. 디렉토리 구조 (현재)

```
ai/
├── data_prep/
│   ├── download_aihub.py         # aihubshell 자동 설치 + 데이터셋 71774 다운로드 (신규)
│   ├── extract_aihub_zips.py     # AI-Hub 카테고리별 zip 일괄 압축 해제
│   ├── convert_to_coco.py        # AI-Hub 라벨 → COCO 포맷
│   └── verify_dataset.py         # 데이터셋 무결성 점검
├── train/
│   ├── dataset.py                 # 공용 COCO Dataset 유틸(오버샘플링+다운샘플링+재시도)
│   │                              # + CocoDetectionDataset(RT-DETR용)
│   │                              # + CocoSegmentationDataset + build_seg_transforms(DeepLabV3+용, 신규)
│   ├── train_rtdetr.py            # RT-DETR v2 재학습 (그대로 재사용)
│   ├── train_rtdetr_auto.sh       # 자동 재시도 래퍼(RT-DETR, 엘리스 경로로 갱신됨)
│   ├── train_deeplabv3plus.py     # DeepLabV3+ 학습 (신규, 순수 PyTorch 루프)
│   └── status_dashboard.py        # 학습 현황 웹 대시보드(포트 6007, RT-DETR 전용 —
│                                  # DeepLab은 tensorboard --logdir <out>/tb 로 볼 것)
├── export/
│   ├── export_onnx.py            # RT-DETR(dynamo) + DeepLabV3+(레거시 tracing) ONNX 변환
│   └── verify_onnx.py            # onnxruntime 수치 검증 (두 모델 다)
└── requirements.txt               # torch 2.7.1 고정, smp/albumentations 포함 (엘리스 기준 재작성됨)
```

체크포인트/모델 산출물 경로 (엘리스):
- `ai/checkpoints/<job>/` — 학습 중간·최종 체크포인트 (`.gitignore` 대상)
- `ai/models/*.onnx` — 익스포트된 ONNX (`.gitignore` 대상, Unity로 전달할 최종물)

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
  (원래 0.0으로 뒀다가 메모리 초과로 죽었던 이력). DeepLabV3+ 평가(mean IoU 계산)도
  검증셋 전체를 한 번에 업샘플/누적하지 말고 청크 단위로 처리할 것 — SegFormer 때도
  같은 문제로 청크 처리(`UPSAMPLE_CHUNK`)를 도입했었다.
- **ONNX 익스포트 방식**: RunPod 시절엔 "dynamo 필수, 레거시 tracing은 RT-DETR을
  잘못 변환(오차 1.24)"이었다. 그러나 엘리스의 현재 스택(torch 2.7.1 / transformers
  4.49.0)에서는 **정반대** — dynamo가 배치 축을 batch=1로 고정하고 RT-DETR은 내부
  노드를 출력으로 leak한다. 레거시 tracing이 오히려 정확(오차 3e-5)하고 배치 축도 동적.
  결론: **스택이 바뀌면 두 방식 다 시도해보고 verify_onnx.py 수치로 판단할 것.**
  현재는 두 모델 다 레거시 tracing 사용(4절, export_onnx.py docstring 참고).

## 8. 하지 말 것

- **가중치 라이선스를 "코드가 Apache-2.0이니까 괜찮겠지"로 넘겨짚지 말 것.** 이번 세션
  시작의 원인이 정확히 이 실수였다. 사전학습 가중치를 실제로 받아 쓸 일이 생기면
  (이번 DeepLabV3+ 계획에서는 없지만, 나중에 다른 백본으로 바꾸는 경우 등) 반드시
  Hugging Face 카드의 `license` 필드와 원 저장소 LICENSE 파일을 직접 대조할 것.
- 웹 서버/API 엔드포인트를 만들지 말 것(`status_dashboard.py`는 읽기 전용 모니터링
  도구라 예외).
- AI-Hub 데이터 원본이나 학습 가중치(.pt, .onnx) 같은 대용량 바이너리를 git에 커밋하지
  말 것.
- Unity/C#/UI 관련 코드는 이 세션에서 건드리지 말 것(별도 프로젝트·별도 세션. Unity
  쪽 통합은 `.onnx` 파일과 입출력 shape 정보를 사용자에게 전달하는 것까지가 이 세션의
  범위).
- git push/PR은 사용자가 명시적으로 요청할 때만 진행할 것 — 임의로 다음 단계로
  진행하지 말 것.
- 학습을 시작하기 전에 항상 몇 epoch/스텝만 스모크 테스트로 돌려서 파이프라인이
  정상 동작하는지 먼저 확인할 것(본 학습을 몇 시간 돌린 뒤에야 버그를 발견하는 낭비를
  피하기 위함).
