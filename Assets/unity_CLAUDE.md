# CLAUDE.md — BridgeSense DT / unity (Unity 6)

이 파일은 `unity/` 디렉토리에서 작업하는 Claude Code 세션을 위한 프로젝트 컨텍스트다.

## 이 프로젝트가 하는 일 / 하지 않는 일

**한다**: 대시보드 UI, 3D 디지털 트윈, AI 추론(Sentis/Inference Engine을 통한 **인프로세스** 실행)까지 전부 이 Unity 프로젝트 안에서 처리한다.
**하지 않는다**: 외부 추론 서버에 REST/WebSocket으로 접속하지 않는다. `.onnx` 모델 파일을 직접 로드해서 Unity 프로세스 안에서 추론을 실행한다 — 별도 Python 서버는 존재하지 않는다. Python 프로젝트(`../ai/`)는 오직 학습·ONNX 익스포트만 담당하며, 이 프로젝트는 그 결과물(`Assets/Models/*.onnx`)을 소비하기만 한다.

## 전체 프로젝트에서의 위치

BridgeSense DT는 교량 사진에서 AI가 균열·손상을 검출하고 Unity 디지털 트윈에 시각화하는 오픈소스 안전점검 플랫폼이다(2026 오픈소스 개발자대회 출품작, 마감 8/27). MVP 대상은 **극락교**(광주광역시 서구 마륵동, PSCI거더교, 반중력식 교각, DB-18, 총길이 380m, 총폭 35m, 13경간, 최대경간장 30m, 교고 8m).

## 환경 — AI 추론 패키지 (2026-07 리서치로 확인된 사실)

- Unity 6, **URP** (Built-in RP 아님 — 과거 스카이박스 셰이더 include 경로 혼동으로 확인된 사실이니 다시 헷갈리지 말 것)
- AI 추론 패키지: **표시 이름은 "Sentis"로 되돌아갔지만, 패키지 ID와 네임스페이스는 그대로다.**
  - Package Manager / `manifest.json` ID: **`com.unity.ai.inference`** (`com.unity.sentis`가 아님 — 이건 구버전 계열 ID이므로 쓰지 말 것)
  - C# 네임스페이스: **`Unity.InferenceEngine`** (`Unity.Sentis`가 아님)
  - 워커 클래스: **`Worker`** (구체 클래스, `new Worker(model, backendType)`로 생성). `IWorker`/`WorkerFactory`는 존재하지 않는 옛 API이므로 절대 쓰지 말 것
  - 지원 ONNX opset: **7~25**
  - 즉 "Sentis"라는 이름만 부활했을 뿐 코드 작성 방식은 바뀐 게 없다. 아래가 표준 패턴:
```csharp
using Unity.InferenceEngine;

Model runtimeModel = ModelLoader.Load(modelAsset);
Worker worker = new Worker(runtimeModel, BackendType.GPUCompute);
worker.Schedule(inputTensor);
Tensor<float> output = worker.PeekOutput() as Tensor<float>;
```
  - 다만 이 패키지는 짧은 기간 동안 이름이 여러 번 바뀐 이력이 있으므로(Sentis → Inference Engine → Sentis(표시명만)), 실제 설치 버전이 2.6.x보다 훨씬 위라면 `Packages/manifest.json`과 공식 changelog를 다시 한번 확인할 것
- 대상 빌드: Windows Standalone 우선. WebGL은 검토 대상이나 확정 아님

## 디렉토리 구조 (권장)

```
unity/Assets/
├── Scripts/
│   ├── Bridge/          # BridgeComponentTag, ParametricBridgeAssembler 등
│   ├── AI/               # Sentis Worker 래퍼, 텐서 전/후처리
│   ├── UI/                # 대시보드 상태 A/B, 모달 컨트롤러
│   └── Data/              # BridgeSpec, 국토교통부 데이터 스키마 매핑
├── Models/                # .onnx (ai/ 프로젝트 산출물을 그대로 복사)
├── Prefabs/
│   ├── PSCIGirder/        # 경간 유닛, 교각 유닛 (상부구조 형식별 폴더 분리)
├── Shaders/                # GradientSkybox, BridgeComponentMaterial 등
└── UI/                     # UI Toolkit 또는 uGUI 에셋
```

## 3D 에셋 아키텍처 — 반드시 지킬 원칙

- 교량마다 처음부터 새로 모델링하지 않는다. **상부구조 형식별로 "경간 유닛" + "교각 유닛" 프리팹 1세트**를 만들어 재사용한다. MVP는 PSCI거더교 유닛 1세트만 있으면 된다(극락교가 이 형식).
- 조립은 국토교통부 "도로 교량 및 터널 현황조서" 스키마(총길이·총폭·유효폭·높이·경간수·최대경간장·상부구조·하부구조·설계하중)를 `BridgeSpec`으로 받아, 경간수만큼 유닛을 반복 배치하는 C# 스크립트로 수행한다.
- 부재 오브젝트 명명 규칙: `교각_N`, `거더구간_N`, `교대_시점`/`교대_종점`. 각 부재에는 `BridgeComponentTag` 컴포넌트를 붙여 `componentId`, `safetyGrade`를 관리한다.
- 픽셀 정밀 좌표 투영(카메라 포즈 기반 히트맵)은 채택하지 않는다 — 카메라 포즈 정보가 없어 구조적으로 불가능하다고 이미 결론 내림. 손상 위치는 사용자가 업로드 시 직접 지정한 부재 단위로만 매핑한다.

## 손상 시각화 — 셰이더 + MaterialPropertyBlock

- 부재 컬러 갱신은 `renderer.material`로 인스턴스를 직접 만들지 말고 **`MaterialPropertyBlock`**을 사용할 것 (배치 렌더링 유지, 부재 수가 늘어도 성능 저하 없음)
- 커스텀 셰이더는 `_GradeColor`(등급별 색상), `_AlertPulse`(D·E등급일 때 은은한 발광 펄스) 두 속성을 노출
- 등급 4단계 색상 매핑: 양호(A·B) / 주의(C) / 미흡(D) / 불량(E) — `GradeColorMap` 같은 단일 유틸에서만 정의하고 여기저기서 색상값을 하드코딩하지 말 것
- 참고 구현 패턴:
```csharp
public class BridgeComponentTag : MonoBehaviour
{
    public string componentId;
    public string safetyGrade = "A";
    Renderer rend; MaterialPropertyBlock mpb;
    void Awake(){ rend = GetComponent<Renderer>(); mpb = new MaterialPropertyBlock(); }
    public void SetGrade(string grade)
    {
        safetyGrade = grade;
        rend.GetPropertyBlock(mpb);
        mpb.SetColor("_GradeColor", GradeColorMap.GetColor(grade));
        mpb.SetFloat("_AlertPulse", (grade == "D" || grade == "E") ? 1f : 0f);
        rend.SetPropertyBlock(mpb);
    }
}
```

## 3D 뷰어 배경

저채도 그라데이션 스카이박스(커스텀 URP 셰이더, `_ZenithColor`/`_HorizonColor`/`_GroundColor`)를 사용한다. 채도를 높이거나 브랜드 강조색을 쓰지 말 것 — 등급 색상(초록/노랑/주황/빨강)과 시각적으로 경쟁하면 안 된다.

## UI 구조

메인 대시보드는 **단일 탭, 2가지 상태**다.
- **상태 A (기본)**: 좌측 이미지 업로드+교량 정보 입력, 우측 AI 비전 분석 결과(부재별 카드) + "3D 모델로 확인하기" 버튼
- **상태 B (3D 확인)**: 전체 화면이 3D 뷰어로 전환, 상태 A의 좌/우 정보가 **반투명 패널로 3D 뷰 위에 오버레이**됨. 부재 클릭 시 강조+콜아웃

상단 바의 "부재별 안전등급 입면도"·"부재 등급 분포"는 상태 B 위에 뜨는 모달이다. **"시뮬레이션" 탭은 아직 미구현** — 향후 가상 센서/부하 시뮬레이션 기능이 들어갈 자리이며, 지금 단계에서 관련 화면을 임의로 만들지 말 것(사용자가 별도로 설계를 요청하기 전까지 보류).

## 교량 제원 데이터 — StreamingAssets JSON 로드

원본 국토교통부 엑셀(`data/raw/`, 4만 행 규모)은 이 프로젝트에서 직접 열지 않는다. Python(`../ai/data_prep/extract_bridge_spec.py`)이 미리 필터링해 만든 **교량별 소형 JSON**을 그대로 읽기만 한다.

- 위치: `Assets/StreamingAssets/BridgeData/<slug>.json` (예: `geungnakgyo.json`)
- 필드명은 국토교통부 현황조서 컬럼명을 그대로 사용: `도로종류, 노선명, 시설명, 시도, 시군구, 읍면동, 총길이, 총폭, 유효폭, 높이, 경간수, 최대경간장, 상부구조, 하부구조, 설계하중, 준공년도`. 임의로 필드명을 새로 만들지 말 것
- Unity에서는 `Application.streamingAssetsPath` 기준으로 읽고 `JsonUtility`(단순 구조라 충분) 또는 `Newtonsoft.Json`으로 역직렬화해 `BridgeSpec`으로 변환
- 이 JSON은 `ai/` 프로젝트가 만들어 `unity/Assets/StreamingAssets/BridgeData/`로 복사해 넣는 산출물이다. Unity 쪽에서 이 JSON의 필드를 직접 수정하지 말고, 값이 틀렸으면 `ai/` 쪽 원본 추출 스크립트를 고쳐서 다시 생성할 것 (단일 진실 공급원 유지)

## 라이선스

C# 스크립트·셰이더·프리팹 구성은 MIT로 공개 예정. 빌드된 실행 파일(.exe)은 Unity Runtime을 포함하므로 별도로 Unity Software Terms가 적용됨을 README에 고지해야 한다(스크립트 자체의 MIT 라이선스와는 별개).

## 하지 말 것

- FastAPI/Flask 등 외부 서버와 통신하는 코드를 만들지 말 것 (위 참고)
- Cesium, 실시간 지도 API 등 외부 클라우드 의존성을 추가하지 말 것 — 위치는 하드코딩 좌표 + 정적 텍스트로 표시
- 유료 Asset Store 에셋을 프로젝트 핵심 기능에 사용하지 말 것 (오픈소스 공개·재현성 문제)
- Mesh R-CNN류 단일 사진 기반 3D 재구성을 시도하지 말 것 — 이미 검토 후 기각된 방향
