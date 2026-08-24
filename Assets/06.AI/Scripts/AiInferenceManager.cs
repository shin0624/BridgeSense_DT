using System;
using System.Collections;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

// RT-DETR 검출 결과 + SegFormer 분할 결과를 하나로 묶은 이미지 분석 결과.
// 주의: Detections는 원본 이미지 픽셀 좌표계, Segmentation은 512x512 모델 좌표계로
// 서로 좌표계가 다르다 - 두 좌표계를 맞추는 건 이 클래스가 아니라 시각화 단계의 책임이다.
public class BridgeAnalysisResult
{
    public List<RtdetrDetection> Detections; // RT-DETR 결함 검출 목록(원본 이미지 픽셀 좌표)
    public SegformerResult Segmentation;     // SegFormer 픽셀 분할 맵(512x512 모델 좌표)
}

// UI 쪽에서 AI 추론이 필요할 때 유일하게 참조해야 하는 진입점.
// RtdetrModel/SegformerModel을 직접 소유·조율하는 오케스트레이터.
//
// 다른 매니저들과 달리 이 매니저만 DontDestroyOnLoad로 씬을 넘어 유지한다.
// 두 모델 파일이 합쳐 185MB라 로딩에 수 초가 걸리는데, 이걸 대시보드에 들어가는 순간
// 부담하면 화면이 그대로 멈춘다. StartScene에서 미리 불러두고 그대로 들고 넘어간다.
// AI 모델은 앱 전체에서 하나만 있으면 되는 자원이라 씬마다 새로 만들 이유도 없다.
public class AiInferenceManager : MonoBehaviour
{
    public static AiInferenceManager Instance { get; private set; }

    [SerializeField] private ModelAsset rtdetrModelAsset;    // Assets/06.AI/models/rtdetr.onnx를 인스펙터에서 연결
    [SerializeField] private ModelAsset segformerModelAsset; // Assets/06.AI/models/segformer.onnx를 인스펙터에서 연결

    // model_io_spec.md 원안 기본값은 0.5였지만, TestImages 실측 검증에서 실제 score가
    // 대부분 0.1~0.27 범위였음이 확인돼 기본값을 현실적인 수준으로 낮춰 잡음
    [SerializeField] private float rtdetrScoreThreshold = 0.1f;

    private RtdetrModel rtdetrModel;       // RT-DETR 래퍼 인스턴스
    private SegformerModel segformerModel; // SegFormer 래퍼 인스턴스

    /// <summary>두 모델이 모두 준비됐는지.</summary>
    public bool IsReady => rtdetrModel != null && segformerModel != null;

    private void Awake()
    {
        if (Instance != null && Instance != this) // 이미 앞선 씬에서 만들어진 매니저가 살아있으면
        {
            Destroy(gameObject); // 중복 인스턴스는 제거. 모델을 불러오기 전에 정리해서 낭비가 없다
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 모델을 즉시 불러온다. 이미 준비됐으면 아무 일도 하지 않는다.
    ///
    /// StartScene을 거치지 않고 대시보드 씬에서 바로 실행하는 개발 중 상황을 위한 안전망이다.
    /// 이 경로로 들어오면 첫 추론 직전에 185MB를 한 번에 읽느라 화면이 멈춘다.
    /// </summary>
    public void EnsureInitialized()
    {
        if (IsReady)
            return;

        rtdetrModel = new RtdetrModel(rtdetrModelAsset);
        segformerModel = new SegformerModel(segformerModelAsset);
    }

    /// <summary>
    /// 모델을 단계별로 불러오며 진행 상황을 알린다. StartScene의 로딩 화면이 사용한다.
    ///
    /// 모델 하나를 읽는 동안은 어차피 프레임이 멈추지만,
    /// 단계 사이에 프레임을 넘겨주면 진행 표시가 갱신되어 멈춘 것처럼 보이지 않는다.
    /// </summary>
    /// <param name="onProgress">0~1 진행률과 현재 단계 설명</param>
    public IEnumerator InitializeRoutine(Action<float, string> onProgress)
    {
        if (IsReady)
        {
            onProgress?.Invoke(1f, "AI 모델 준비 완료");
            yield break;
        }

        onProgress?.Invoke(0f, "결함 검출 모델을 불러오는 중");
        yield return null; // 위 문구가 화면에 그려진 뒤에 로딩을 시작하도록 한 프레임 넘긴다

        rtdetrModel = new RtdetrModel(rtdetrModelAsset);

        onProgress?.Invoke(0.5f, "결함 영역 분할 모델을 불러오는 중");
        yield return null;

        segformerModel = new SegformerModel(segformerModelAsset);

        onProgress?.Invoke(1f, "AI 모델 준비 완료");
    }

    // UI 쪽에서 호출할 유일한 진입점 - 두 모델의 존재를 몰라도 이미지 한 장만 넘기면 됨
    public BridgeAnalysisResult AnalyzeImage(Texture2D image)
    {
        EnsureInitialized(); // StartScene을 건너뛴 경우에도 동작하도록 하는 안전망

        var detections = rtdetrModel.Run(image, rtdetrScoreThreshold); // RT-DETR 검출 실행
        var segmentation = segformerModel.Run(image);                  // SegFormer 분할 실행

        return new BridgeAnalysisResult // 두 결과를 하나로 묶어서 반환
        {
            Detections = detections,
            Segmentation = segmentation
        };
    }

    private void OnDestroy()
    {
        // 씬을 넘어 유지되므로 실제로는 앱을 종료할 때 한 번 실행된다.
        rtdetrModel?.Dispose();    // RT-DETR 워커·텐서 메모리 해제
        segformerModel?.Dispose(); // SegFormer 워커·텐서 메모리 해제

        if (Instance == this) // 내가 진짜 Instance였을 때만(중복이라 Destroy된 경우는 Awake에서 이미 return해서 여기 값이 비어있음)
            Instance = null;  // 참조 해제
    }
}
