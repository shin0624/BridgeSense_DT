# BridgeSense DT

BridgeSense DT는 교량 외관점검 이미지를 업로드하면 AI로 결함(균열, 박락, 백태, 누수, 철근 노출, 부식, 도장 박리 등)을 탐지·분할하고, 국토안전관리원 제3종시설물 안전등급 평가 방식에 따라 부재별/종합 안전등급을 산정해 3D 디지털 트윈 위에서 시각화하는 Unity 6 기반 교량 안전점검 애플리케이션입니다.

2026년 8월 27일 오픈소스 개발자대회 출품작

## 주요 기능

- **AI 결함 탐지/분할**: RT-DETR v2(객체 검출) + SegFormer(픽셀 분할) 두 모델을 Unity Inference Engine(Sentis)으로 인프로세스 실행. 별도 추론 서버 없이 동작합니다.
- **안전등급 자동 산정**: 검출된 결함을 국토안전관리원 제3종시설물 안전등급 평가 방식에 따라 부재별/종합 등급으로 환산합니다.
- **3D 디지털 트윈 뷰어**: 교량 3D 모델의 부재를 등급에 따라 색상으로 표시하고, 결함 목록에서 클릭하면 해당 부재로 카메라가 이동합니다.
- **이미지 업로드**: 파일 탐색기 다이얼로그와 Windows 네이티브 드래그 앤 드롭을 모두 지원합니다.
- **분석 세션 저장/불러오기**: 등록한 이미지·교량 정보·분석 결과를 `.bsdt` 파일로 저장하고 다시 불러올 수 있습니다.
- **보고서 출력**: 분석 결과를 CSV 또는 HTML(브라우저 인쇄로 PDF 저장 가능) 형식의 안전점검 보고서로 내보냅니다. 외부 라이브러리 없이 순수 SVG 차트를 사용합니다.

## 프로젝트 구조

```
Assets/
├── 01.MainDashboard/       # Unity C# 소스 (UI, 3D 뷰어, 세션, 보고서 등)
│   └── Scripts/
├── 06.AI/                  # Sentis 추론 래퍼, 에디터 검증 도구, 테스트 이미지
│   ├── Scripts/            # AiInferenceManager, RtdetrModel, SegformerModel
│   ├── Editor/             # 모델 로딩/추론 검증용 에디터 툴
│   ├── TestImages/         # 수동 검증용 샘플 이미지
│   └── models/             # .onnx 가중치 (저장소에 미포함, 아래 "AI 모델" 참고)
├── 04.Scenes/              # StartScene, MainDashboardScene
└── (그 외 폰트/UI 에셋 등, THIRD-PARTY-NOTICES.md 참고)

ai/                         # AI 모델 학습 파이프라인 (Python, 이 저장소와 별도 실행 환경)
├── data_prep/              # AI-Hub 원본 데이터 압축 해제, COCO 포맷 변환, 데이터 검증
├── train/                  # RT-DETR v2 / SegFormer 파인튜닝
├── export/                 # ONNX 변환 및 검증, 입출력 텐서 계약 문서(model_io_spec.md)
└── docs/                   # 아키텍처/파이프라인 기획 문서

data/                       # 교량 제원 참고 데이터
scripts/                    # 개발 환경 부트스트랩 스크립트
```

## AI 모델

RT-DETR v2(`PekingU/rtdetr_v2_r18vd` 기반)와 SegFormer MiT-B2(`nvidia/mit-b2` 기반)를 AI-Hub 교량 외관점검 데이터로 파인튜닝한 뒤 ONNX로 변환해 사용합니다. 학습 파이프라인 전체(`ai/`)는 이 저장소에 포함되어 있습니다.

파인튜닝된 가중치 파일(`rtdetr.onnx`, `segformer.onnx`)은 학습에 사용한 AI-Hub 데이터의 이용 약관이 아직 확인되지 않아 이 저장소에는 포함하지 않았습니다(`.gitignore`로 제외).

- 소스 코드를 직접 빌드해 실행하려면 Unity Engine 6000.0.69f1 환경과, 호환되는 `rtdetr.onnx` / `segformer.onnx`를 직접 준비해 `Assets/06.AI/models/`에 넣어야 합니다. 입출력 텐서 규격은 `ai/export/model_io_spec.md`를 참고하세요.
- 미리 빌드된 실행 파일(가중치 포함)을 바로 사용하려면 아래 다운로드 링크를 이용하세요.

## 빌드된 실행 파일 다운로드

(다운로드 링크 추가 예정 — Google Drive)

## 개발 환경

- Unity 6000.0.69f1 (Universal Render Pipeline)
- Unity Inference Engine(Sentis) — AI 모델 인프로세스 추론
- DOTween — UI 애니메이션
- Newtonsoft.Json — 세션 저장/불러오기 직렬화
- C# 12.0 
- Python 3.10.11 (RT-DETR v2 / SegFormer 파인튜닝, `ai/requirements.txt` 참고)

## 라이선스

이 저장소에서 직접 작성한 소스 코드(Unity/C# 스크립트, `ai/` 파이썬 코드)는 [MIT License](LICENSE.md)로 배포합니다.

프로젝트에 포함된 서드파티 플러그인, 폰트, Unity 패키지, AI 베이스 모델 등은 각자의 원 라이선스를 따릅니다. 자세한 내용은 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 확인하세요.
