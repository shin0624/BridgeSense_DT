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

// InputAndAnalyzePanel 등 UI 쪽에서 AI 추론이 필요할 때 유일하게 참조해야 하는 진입점.
// RtdetrModel/SegformerModel을 직접 소유·조율하는 오케스트레이터 - MainDashboardManager와
// 동일한 씬 로컬 싱글톤 패턴(DontDestroyOnLoad 없음)을 쓴다.
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

    private void Awake()
    {
        if (Instance != null && Instance != this) // 씬에 이 매니저가 중복으로 존재하면
        {
            Destroy(gameObject); // 중복 인스턴스는 제거
            return; // 아래 초기화는 진짜 Instance만 수행하도록 여기서 종료
        }

        Instance = this;

        rtdetrModel = new RtdetrModel(rtdetrModelAsset);           // RT-DETR 워커·텐서 초기화
        segformerModel = new SegformerModel(segformerModelAsset);  // SegFormer 워커·텐서 초기화
    }

    // UI 쪽에서 호출할 유일한 진입점 - 두 모델의 존재를 몰라도 이미지 한 장만 넘기면 됨
    public BridgeAnalysisResult AnalyzeImage(Texture2D image)
    {
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
        rtdetrModel?.Dispose();    // RT-DETR 워커·텐서 메모리 해제
        segformerModel?.Dispose(); // SegFormer 워커·텐서 메모리 해제

        if (Instance == this) // 내가 진짜 Instance였을 때만(중복이라 Destroy된 경우는 Awake에서 이미 return해서 여기 값이 비어있음)
            Instance = null;  // 참조 해제
    }
}
