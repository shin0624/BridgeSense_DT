using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

// RT-DETR v2 결함 검출 결과 1건 (ai/export/model_io_spec.md 1절, 4절 계약 기준)
public struct RtdetrDetection
{
    public int ClassId;   // 0~8, 결함 클래스 id (model_io_spec.md 4절 표: co=0, ef=1, le=2 ...)
    public float Score;   // sigmoid 적용된 confidence, 0~1
    public float X1;      // 원본 이미지 픽셀 좌표계 기준 좌상단 x
    public float Y1;      // 원본 이미지 픽셀 좌표계 기준 좌상단 y
    public float X2;      // 원본 이미지 픽셀 좌표계 기준 우하단 x
    public float Y2;      // 원본 이미지 픽셀 좌표계 기준 우하단 y
}

// rtdetr.onnx 한 개만 감싸는 래퍼 - 전처리·추론·후처리를 이 클래스 안에서 전부 캡슐화한다.
public class RtdetrModel : IDisposable
{
    private const int InputSize = 640;  // model_io_spec.md 1.1절: 고정 입력 해상도
    private const int NumQueries = 300; // model_io_spec.md 1.2절: 쿼리(검출 후보) 개수, 파인튜닝에서도 안 바뀜
    private const int NumClasses = 9;   // model_io_spec.md 4절: 결함 클래스 9종

    private readonly Worker worker;            // rtdetr.onnx를 실행하는 Sentis 워커
    private readonly Tensor<float> inputTensor; // 매 프레임 새로 할당하지 않도록 미리 만들어두고 재사용하는 입력 텐서

    public RtdetrModel(ModelAsset modelAsset, BackendType backendType = BackendType.GPUCompute)
    {
        var model = ModelLoader.Load(modelAsset); // 임포트된 ModelAsset을 실행 가능한 Model 그래프로 로드
        worker = new Worker(model, backendType); // 로드한 모델을 지정한 backend(GPU/CPU)로 실행할 워커 생성
        inputTensor = new Tensor<float>(new TensorShape(1, 3, InputSize, InputSize)); // [batch,3,640,640] 고정 shape 입력 텐서 1회 할당
    }

    public List<RtdetrDetection> Run(Texture2D image, float scoreThreshold = 0.5f, int topK = 100)
    {
        var layout = new TextureTransform().SetTensorLayout(0, 1, 2, 3); // 텐서 축 순서를 N,C,H,W로 명시(기본값과 동일하지만 명시적으로 고정)
        TextureConverter.ToTensor(image, inputTensor, layout); // 원본 텍스처를 640x640으로 리샘플링하며 픽셀값(0~1)을 그대로 채움 → pixel/255 스케일링과 동일 효과, 별도 정규화는 하지 않음(1.1절)

        worker.Schedule(inputTensor); // 전처리된 입력으로 추론 1회 실행

        var logitsTensor = worker.PeekOutput("logits") as Tensor<float>;    // [1,300,9] 클래스 로짓(시그모이드 적용 전) 참조
        var boxesTensor = worker.PeekOutput("pred_boxes") as Tensor<float>; // [1,300,4] cxcywh 정규화 박스좌표 참조

        var logits = logitsTensor.DownloadToArray(); // GPU/워커 내부 데이터를 CPU 배열로 동기 readback (블로킹, 추후 필요하면 비동기로 교체)
        var boxes = boxesTensor.DownloadToArray();   // 위와 동일하게 박스좌표 readback

        var candidates = new List<RtdetrDetection>(NumQueries * NumClasses); // 300*9 = 2700개 후보를 담을 임시 리스트

        for (int q = 0; q < NumQueries; q++) // 쿼리(검출 후보) 300개를 순회
        {
            for (int c = 0; c < NumClasses; c++) // 쿼리마다 9개 결함 클래스를 순회
            {
                float rawLogit = logits[q * NumClasses + c]; // batch=1, 행 우선 배열이므로 (쿼리,클래스) 위치를 이렇게 인덱싱
                float score = 1f / (1f + Mathf.Exp(-rawLogit)); // sigmoid 적용 → 0~1 confidence (1.2절: no-object 클래스가 없어 sigmoid만 씀)

                float cx = boxes[q * 4 + 0]; // 정규화된 박스 중심 x (0~1, 640x640 기준)
                float cy = boxes[q * 4 + 1]; // 정규화된 박스 중심 y (0~1)
                float w = boxes[q * 4 + 2];  // 정규화된 박스 너비 (0~1)
                float h = boxes[q * 4 + 3];  // 정규화된 박스 높이 (0~1)

                float x1 = (cx - w * 0.5f) * image.width;  // 정규화 좌표라 리사이즈 비율과 무관하게 원본 W를 그대로 곱하면 됨(1.3절 4단계)
                float y1 = (cy - h * 0.5f) * image.height; // 좌상단 y를 원본 이미지 픽셀 좌표로 변환
                float x2 = (cx + w * 0.5f) * image.width;  // 우하단 x를 원본 이미지 픽셀 좌표로 변환
                float y2 = (cy + h * 0.5f) * image.height; // 우하단 y를 원본 이미지 픽셀 좌표로 변환

                candidates.Add(new RtdetrDetection // 아직 top-K/threshold 적용 전, 일단 후보로만 추가
                {
                    ClassId = c,
                    Score = score,
                    X1 = x1, Y1 = y1, X2 = x2, Y2 = y2
                });
            }
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score)); // score 내림차순 정렬 (1.3절 2단계: 상위 K개 선정을 위한 정렬)

        int take = Mathf.Min(topK, candidates.Count); // 상위 K개(기본 100개)만 남기되 후보 수가 K보다 적으면 있는 만큼만
        var result = new List<RtdetrDetection>(take); // 최종 반환할 리스트

        for (int i = 0; i < take; i++) // 정렬된 상위 K개만 순회
        {
            if (candidates[i].Score < scoreThreshold) // 1.3절 3단계: threshold 미만이면 제외
                break; // 내림차순 정렬이므로 여기부터는 전부 threshold 미만 → 순회 중단

            result.Add(candidates[i]); // threshold를 통과한 검출만 최종 결과에 추가
        }

        return result; // 이미지 한 장에 대한 최종 검출 결과(원본 픽셀 좌표 기준), NMS는 불필요(1.3절 5단계)
    }

    public void Dispose()
    {
        inputTensor?.Dispose(); // 입력 텐서가 들고 있는 네이티브/GPU 메모리 해제
        worker?.Dispose();      // 워커(백엔드, 내부 메모리 풀 등) 해제
    }
}
