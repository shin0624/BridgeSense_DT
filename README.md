# - 2026 오픈소스 개발자대회 출품작 BridgeSense DT

- BridgeSense DT는 사용자가 업로드한 교량 외관 이미지에서 AI로 결함(균열, 박락, 백태, 누수, 철근 노출, 부식, 도장 박리 등)을 탐지하고, 국토안전관리원 제3종시설물 안전등급 평가 방식에 따라 부재별/종합 안전등급을 산정한 후 3D 교량 모델과 대시보드 UI 위에 시각화하는 Unity 6 기반 교량 안전점검 플랫폼 SW입니다.

# 시연 영상

[시연 영상(Youtube)](https://youtu.be/4TjQZReOfdU)

## 주요 기능

- **AI 결함 탐지** : RT-DETR v2(객체 검출) 모델을 Unity Inference Engine(Sentis)으로 인프로세스 실행. 별도 추론 서버 없이 동작합니다.
- **안전등급 자동 산정** : 검출된 결함을 국토안전관리원 제3종시설물 안전등급 평가 방식에 따라 부재별/종합 등급으로 환산합니다.
- **3D 디지털 트윈 뷰어** : 교량 3D 모델의 부재를 등급에 따라 색상으로 표시하고, 결함 목록에서 클릭하면 해당 부재로 카메라가 이동합니다.
- **이미지 업로드** : 파일 탐색기 다이얼로그와 Windows 네이티브 드래그 앤 드롭을 모두 지원합니다.
- **분석 세션 저장/불러오기** : 등록한 이미지·교량 정보·분석 결과를 `.bsdt` 파일로 저장하고 다시 불러올 수 있습니다.
- **보고서 출력** : 분석 결과를 CSV 또는 HTML(브라우저 인쇄로 PDF 저장 가능) 형식의 안전점검 보고서로 내보냅니다. 외부 라이브러리 없이 순수 SVG 차트를 사용합니다.

## 프로젝트 구조

```
Assets/
├── 01.MainDashboard/       # Unity C# 소스 (UI, 3D 뷰어, 세션, 보고서 등)
│   └── Scripts/
├── 06.AI/                  # Sentis 추론 래퍼, 에디터 검증 도구
│   ├── Scripts/            # AiInferenceManager, RtdetrModel
│   ├── Editor/             # 모델 로딩/추론 검증용 에디터 툴
│   ├── TestImages/         # 수동 검증용 샘플 이미지
│   └── models/             # .onnx 가중치 (저장소에 미포함, 아래 "AI 모델" 참고)
├── 04.Scenes/              # StartScene, MainDashboardScene
└── (그 외 폰트/UI 에셋 등, THIRD-PARTY-NOTICES.md 참고)

ai/                         # AI 모델 학습 파이프라인 (Python, 이 저장소와 별도 실행 환경)
├── data_prep/              # AI-Hub 원본 데이터 압축 해제, COCO 포맷 변환, 데이터 검증
├── train/                  # RT-DETR v2 학습
└── export/                 # ONNX 변환 및 검증

data/                       # 교량 제원 참고 데이터
scripts/                    # 개발 환경 부트스트랩 스크립트
```

## AI 모델

RT-DETR v2(`PekingU/rtdetr_v2_r18vd` 기반, Apache License 2.0)를 AI-Hub 교량 외관점검 데이터로 파인튜닝한 뒤 ONNX로 변환해 사용합니다. 학습 파이프라인 전체(`ai/`)는 이 저장소에 포함되어 있습니다.

파인튜닝된 가중치는 Hugging Face에 공개되어 있습니다.

- RT-DETR v2 (결함 검출): https://huggingface.co/shin0624/bridgesense-rtdetr

용량 문제로 이 저장소에는 `.onnx` 파일을 직접 포함하지 않았습니다.
소스 코드를 직접 빌드해 실행하려면 Unity Engine 6000.0.69f1 환경에서 위 링크의
`rtdetr.onnx`를 내려받아 `Assets/06.AI/models/`에 넣어야 합니다.

## 개발 환경

### 엔진 / 언어

- Unity 6000.0.69f1 (Universal Render Pipeline 17.0.4)
- C# 12.0
- 대상 플랫폼: Windows 스탠드얼론(x64)

### Unity 패키지

- `com.unity.ai.inference` 2.5.0 — Unity Sentis, AI 모델 인프로세스 추론
- `com.unity.inputsystem` 1.19.0 — 키보드 입력(ESC 종료 확인 등) 처리
- `com.unity.render-pipelines.universal` 17.0.4 — URP 렌더링
- `com.unity.ai.navigation` 2.0.11, `com.unity.postprocessing` 3.5.4, `com.unity.timeline` 1.8.10, `com.unity.visualscripting` 1.9.7

### 서드파티 라이브러리/플러그인

- DOTween — 패널 전환, 팝업 pop-in 등 UI 애니메이션
- Newtonsoft.Json(`com.unity.nuget.newtonsoft-json` 3.2.1) — 분석 세션(`.bsdt`) 저장/불러오기 직렬화
- UnityWindowsFileDrag-Drop — Windows 네이티브 드래그 앤 드롭 이미지 업로드
- Standalone File Browser — 이미지 업로드/보고서 저장 파일 다이얼로그

라이선스 등 상세 출처는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 확인하세요.

### AI 모델 학습 환경 (`ai/`)

- Python 3.10.11
- PyTorch 2.7.1+cu128, torchvision 0.22.1+cu128, torchaudio 2.7.1+cu128
- Transformers 4.49.0
- GPU: RTX PRO 4000 (Blackwell, CUDA 12.8)
- 전체 의존성은 `ai/requirements.txt` 참고

## 학습 데이터 출처

본 프로젝트의 AI 모델은 아래 데이터셋으로 학습되었습니다.

- 데이터셋: 교량 외관점검 입면정사영상 데이터
- 출처: AI 허브(https://aihub.or.kr)

AI 허브 이용정책에 따라 원본 데이터는 본 저장소에 포함되어 있지 않으며, 학습 결과물(가중치)만 공개합니다. 데이터가 필요한 경우 AI 허브에서 직접 이용 신청하시기 바랍니다.

## 라이선스

이 저장소에서 직접 작성한 소스 코드(Unity/C# 스크립트, `ai/` 파이썬 코드)는 [MIT License](LICENSE.md)로 배포합니다.

프로젝트에 포함된 서드파티 플러그인, 폰트, Unity 패키지, AI 베이스 모델 등은 각자의 원 라이선스를 따릅니다. 자세한 내용은 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 확인하세요.
