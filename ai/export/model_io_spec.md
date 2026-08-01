# Model I/O Spec — Unity Sentis 연동 계약

> RT-DETR v2 / SegFormer MiT-B2 파인튜닝 결과를 ONNX로 익스포트할 때 지켜야 할 입출력
> 텐서 이름·shape·전처리·후처리 규약. `export_onnx.py` 구현과 Unity 쪽 Sentis 코드가
> 이 문서 하나를 공통 계약으로 삼는다. (배경: `ai/docs/AI_PIPELINE_PLAN.md` 8장)

---

## 0. 공통 규칙

- **opset**: 7~25 범위(Unity Sentis 확인된 지원 범위) 내에서 **17로 고정** (Resize의
  bilinear 모드가 필요한 SegFormer 때문에 opset 11 이상 필요 — 17이면 여유 있게 충족)
- **배치 축만 동적**(`batch`), 나머지 차원은 고정 shape으로 익스포트
- **후처리는 그래프 밖(Unity C#)에서 수행**이 원칙. 단, SegFormer의 업샘플처럼 "안 넣으면
  Unity에서 같은 연산을 재구현해야 하는" 경우는 예외적으로 그래프 안에 포함(2.3절 참고)
- 두 모델은 **완전히 독립된 ONNX 파일 2개**(`rtdetr.onnx`, `segformer.onnx`)로 익스포트한다.
  하나의 그래프로 합치지 않는다 — 백본이 서로 다른 별개 모델이라 합쳐도 연산 공유 이득이
  없고, Sentis는 애초에 모델별로 Worker를 따로 로드하는 구조라 분리하는 쪽이 자연스럽다.

---

## 1. RT-DETR v2 (`rtdetr.onnx`)

### 1.1 입력

| 텐서명 | dtype | shape | 설명 |
|---|---|---|---|
| `pixel_values` | float32 | `[batch, 3, 640, 640]` | RGB, HWC가 아니라 CHW |

**전처리** (베이스 체크포인트 `PekingU/rtdetr_v2_r18vd`의 `preprocessor_config.json` 실측 확인):
1. 640×640으로 resize (bilinear)
2. `pixel / 255.0` 스케일링만 적용
3. **ImageNet mean/std 정규화는 하지 않는다** (`do_normalize: false`) — DETR 계열과 달리
   RT-DETR 베이스 프로세서는 정규화 생략이 기본값. Unity C# 전처리 코드에서 실수로
   정규화를 추가하지 않도록 주의.

### 1.2 출력

| 텐서명 | dtype | shape | 설명 |
|---|---|---|---|
| `logits` | float32 | `[batch, 300, 9]` | 쿼리(300개)별 9개 결함 클래스 로짓, **sigmoid 적용 전** |
| `pred_boxes` | float32 | `[batch, 300, 4]` | `[cx, cy, w, h]`, 입력 640×640 기준 `[0,1]` 정규화 좌표 |

- `num_queries=300`은 베이스 체크포인트 기본값 그대로 사용 (파인튜닝 시 변경 안 함)
- Deformable-DETR 계열 방식이라 별도의 "no-object/background" 클래스가 없다 —
  `score = sigmoid(logits)`로 각 쿼리×클래스 쌍의 confidence를 얻는다

### 1.3 Unity 쪽 후처리 (그래프 밖, C#에서 구현)

1. `logits`에 sigmoid 적용
2. 300쿼리 × 9클래스 조합 중 상위 K개(예: 100) score 순 정렬·선택
3. score threshold 적용 (예: 0.5, 실측 후 조정)
4. `pred_boxes`(cxcywh, 정규화) → 원본 이미지 픽셀 좌표(xyxy)로 변환: 원본 이미지 W/H를 곱함
5. **NMS 불필요** — RT-DETR은 end-to-end 검출 모델이라 중복 박스 억제가 모델 자체에 내장됨

---

## 2. SegFormer MiT-B2 (`segformer.onnx`)

### 2.1 입력

| 텐서명 | dtype | shape | 설명 |
|---|---|---|---|
| `pixel_values` | float32 | `[batch, 3, 512, 512]` | RGB, CHW |

**전처리** (베이스 체크포인트 `nvidia/mit-b2`의 `preprocessor_config.json` 실측 확인):
1. 512×512로 resize
2. `pixel / 255.0` 스케일링
3. ImageNet mean/std로 정규화: `mean=[0.485, 0.456, 0.406]`, `std=[0.229, 0.224, 0.225]`
   (RT-DETR과 달리 **여기는 정규화를 적용한다** — 두 모델 전처리를 헷갈리지 말 것)

512×512는 AI-Hub 원본 이미지 해상도와 동일해서 별도 업/다운스케일 없이 그대로 맞아떨어짐.

### 2.2 출력

| 텐서명 | dtype | shape | 설명 |
|---|---|---|---|
| `logits` | float32 | `[batch, 10, 512, 512]` | 픽셀별 클래스 로짓. **입력과 동일한 512×512 해상도로 업샘플된 상태로 출력** (2.3절 참고) |

- **클래스 10개** = 배경/정상(id 0) + 결함 9종(id 1~9, RT-DETR과 동일 순서로 +1 shift, 4절 표 참고)
- HF 원본 `SegformerForSemanticSegmentation`은 디코더 구조상 입력의 **1/4 해상도**(512→128)로
  `logits`를 출력한다. 이걸 그대로 익스포트하면 Unity에서 bilinear upsample을 다시 구현해야
  하므로, **익스포트 래퍼로 업샘플까지 그래프에 포함시켜서 내보낸다** (아래 2.3절).

### 2.3 익스포트 래퍼 (`export_onnx.py`에 구현)

```python
import torch.nn as nn
import torch.nn.functional as F

class SegformerExportWrapper(nn.Module):
    def __init__(self, model, out_size=(512, 512)):
        super().__init__()
        self.model = model
        self.out_size = out_size

    def forward(self, pixel_values):
        logits = self.model(pixel_values=pixel_values).logits  # [B, 10, 128, 128]
        return F.interpolate(
            logits, size=self.out_size, mode="bilinear", align_corners=False
        )  # [B, 10, 512, 512]
```

`F.interpolate(mode="bilinear")` → ONNX `Resize` 연산자로 변환됨, opset 11 이상 필요
(0절에서 opset 17로 고정했으므로 문제없음).

### 2.4 Unity 쪽 후처리 (그래프 밖)

1. `argmax(logits, axis=class)` → 픽셀별 클래스 인덱스 맵 (0~9)
2. 필요 시 `softmax(logits, axis=class)`로 클래스별 확신도 시각화

---

## 3. 두 모델의 관계 (Unity 쪽 참고)

- `rtdetr.onnx`, `segformer.onnx`는 각자 독립적인 Sentis Worker로 로드해서 따로 추론한다
- (후속 검토, 이번 범위 아님) RT-DETR이 찾은 bbox 영역만 크롭해서 SegFormer에 넣는 2단계
  캐스케이드를 하고 싶어질 수 있는데, 이런 동적 크롭/분기 로직은 ONNX 정적 그래프로 못 담기
  때문에 **Unity C# 오케스트레이션 레벨**에서 구현해야 한다 (모델 파일 자체는 지금 스펙 그대로)

---

## 4. 클래스 인덱스 매핑

`convert_to_coco.py`의 `CLASS_NAMES` 순서가 기준. RT-DETR은 이 순서 그대로(0~8),
SegFormer는 배경 클래스가 0번을 차지하므로 **전부 +1 shift**됨.

| RT-DETR id | SegFormer id | 클래스명 |
|---|---|---|
| — | 0 | (배경/정상, 결함 없음) |
| 0 | 1 | 콘크리트_균열 |
| 1 | 2 | 백태 |
| 2 | 3 | 누수 |
| 3 | 4 | 박락 |
| 4 | 5 | 철근_노출 |
| 5 | 6 | 강재_부식 |
| 6 | 7 | 도장_박리 |
| 7 | 8 | 아스팔트_균열 |
| 8 | 9 | 함몰 |

---

## 5. 확정 전 재확인 필요 (열린 이슈)

- 위 전처리·shape 값은 **베이스 체크포인트**(파인튜닝 전) 설정 기준으로 실측한 것 — 학습
  코드(`train_rtdetr.py`, `train_segformer.py`)에서 이 전처리를 그대로 유지하는지 반드시
  확인하고, 바꿨다면 이 문서도 같이 업데이트할 것
- opset 17 익스포트가 실제로 Sentis에서 두 모델(특히 SegFormer의 `Resize`) 다 문제없이
  로드되는지 `verify_onnx.py` + Unity 쪽에서 실기 검증 필요
- score/threshold 기본값(0.5 등)은 실제 검증 데이터로 PR curve 확인 후 조정
