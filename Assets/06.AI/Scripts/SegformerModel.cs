using System;
using Unity.InferenceEngine;
using UnityEngine;

// SegFormer 픽셀 분할 결과 - 512x512 모델 좌표계 기준 그대로 반환한다.
// 원본 이미지 해상도로 되돌리는 업스케일은 이 클래스의 책임이 아니라 시각화 단계에서 처리한다.
public class SegformerResult
{
    public int Width;      // 512 고정 (model_io_spec.md 2.1절)
    public int Height;     // 512 고정
    public int[] ClassMap; // 픽셀별 클래스 id(0~9), 행 우선 인덱싱: index = y * Width + x
}

// segformer.onnx 한 개만 감싸는 래퍼 - 전처리·추론·후처리를 이 클래스 안에서 전부 캡슐화한다.
public class SegformerModel : IDisposable
{
    private const int InputSize = 512; // model_io_spec.md 2.1절: 고정 입력·출력 해상도
    private const int NumClasses = 10; // model_io_spec.md 2.2절: 배경/정상(0) + 결함 9종(1~9)

    // model_io_spec.md 2.1절: RT-DETR과 달리 SegFormer는 ImageNet mean/std 정규화를 적용한다
    private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

    private readonly Worker worker;                // segformer.onnx를 실행하는 Sentis 워커
    private readonly Tensor<float> rawInputTensor;  // 텍스처 리샘플링 결과(0~1, 정규화 전)를 담아 매 호출마다 재사용하는 텐서

    public SegformerModel(ModelAsset modelAsset, BackendType backendType = BackendType.GPUCompute)
    {
        var model = ModelLoader.Load(modelAsset); // 임포트된 ModelAsset을 실행 가능한 Model 그래프로 로드
        worker = new Worker(model, backendType); // 로드한 모델을 지정한 backend(GPU/CPU)로 실행할 워커 생성
        rawInputTensor = new Tensor<float>(new TensorShape(1, 3, InputSize, InputSize)); // [batch,3,512,512] 고정 shape, 정규화 전 원시 텐서
    }

    public SegformerResult Run(Texture2D image)
    {
        var layout = new TextureTransform().SetTensorLayout(0, 1, 2, 3); // 텐서 축 순서를 N,C,H,W로 명시(기본값과 동일하지만 명시적으로 고정)
        TextureConverter.ToTensor(image, rawInputTensor, layout); // 원본 텍스처를 512x512로 리샘플링하며 픽셀값(0~1)을 그대로 채움 → pixel/255 스케일링과 동일 효과

        var rawPixels = rawInputTensor.DownloadToArray(); // 정규화를 CPU에서 계산하기 위해 0~1 원시 값을 배열로 readback
        var normalizedPixels = new float[rawPixels.Length]; // 정규화된 값을 담을 새 배열(최종적으로 워커에 넣을 입력이 됨)

        int pixelsPerChannel = InputSize * InputSize; // NCHW 레이아웃에서 채널 하나가 차지하는 원소 개수(512*512)
        for (int i = 0; i < rawPixels.Length; i++) // 전체 3*512*512 원소를 순회
        {
            int channel = i / pixelsPerChannel; // NCHW는 채널이 가장 바깥쪽 축이므로, 인덱스를 채널 크기로 나누면 채널 번호가 나옴
            normalizedPixels[i] = (rawPixels[i] - Mean[channel]) / Std[channel]; // model_io_spec.md 2.1절 3단계: 채널별 ImageNet mean/std 정규화
        }

        using var normalizedInput = new Tensor<float>(rawInputTensor.shape, normalizedPixels); // 정규화된 배열로 새 입력 텐서 생성(추론 후 바로 폐기할 것이라 using으로 처리)
        worker.Schedule(normalizedInput); // 정규화까지 끝난 입력으로 추론 1회 실행

        var logitsTensor = worker.PeekOutput("logits") as Tensor<float>; // [1,10,512,512] 픽셀별 클래스 로짓(입력과 동일한 512x512로 이미 업샘플된 상태, 2.3절)
        var logits = logitsTensor.DownloadToArray(); // GPU/워커 내부 데이터를 CPU 배열로 동기 readback

        var result = new SegformerResult { Width = InputSize, Height = InputSize, ClassMap = new int[pixelsPerChannel] }; // 픽셀별 클래스 id를 담을 결과 객체
        var bestLogit = new float[pixelsPerChannel]; // 픽셀별로 지금까지 찾은 최고 로짓값을 담는 버퍼(클래스 순회 사이에 유지됨)

        // 픽셀을 바깥 루프로 두고 클래스 10개를 안쪽에서 훑으면, 클래스마다 262144개씩 떨어진
        // 위치(pixelsPerChannel 간격, 약 1MB)를 왔다갔다 읽게 돼서 캐시 미스가 거의 매번 발생한다.
        // 그래서 루프 순서를 뒤집어 클래스를 바깥, 픽셀을 안쪽으로 둔다 - 비교 횟수(262만 번)는
        // 그대로지만, logits 배열을 클래스 블록 단위로 처음부터 끝까지 순차로만 읽게 되어
        // (스캔 10번), CPU가 다음에 읽을 데이터를 미리 캐시에 올려두는 프리페처 효과를 그대로 받는다.
        Array.Copy(logits, 0, bestLogit, 0, pixelsPerChannel); // 클래스 0(배경) 블록을 초기값으로 그대로 복사(순차 메모리 접근)

        for (int c = 1; c < NumClasses; c++) // 나머지 9개 결함 클래스를 바깥 루프로 순회
        {
            int offset = c * pixelsPerChannel; // 이 클래스 블록이 logits 배열에서 시작하는 위치
            for (int p = 0; p < pixelsPerChannel; p++) // 이 클래스 블록 안에서는 픽셀을 처음부터 끝까지 순서대로만 읽음
            {
                float logit = logits[offset + p]; // 순차 접근 - 바로 앞에서 읽은 인덱스의 다음 원소
                if (logit > bestLogit[p]) // 지금까지 이 픽셀의 최댓값보다 크면 갱신
                {
                    bestLogit[p] = logit;
                    result.ClassMap[p] = c; // 2.4절 1단계: argmax 결과(픽셀별 클래스 인덱스) 갱신
                }
            }
        }

        return result; // 512x512 해상도의 픽셀별 클래스 인덱스 맵 반환
    }

    public void Dispose()
    {
        rawInputTensor?.Dispose(); // 재사용해온 원시 입력 텐서의 네이티브/GPU 메모리 해제
        worker?.Dispose();         // 워커(백엔드, 내부 메모리 풀 등) 해제
    }
}
