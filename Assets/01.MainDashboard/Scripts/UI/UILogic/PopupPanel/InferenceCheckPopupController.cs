using System.Collections.Generic;
using BridgeSenseDT.Assessment;
using UnityEngine;
using UnityEngine.UI;

// inferenceCheckPopup 자신에게 붙는 전용 컨트롤러 - "이 팝업 안에서 일어나는 확인 로직"만 담당한다.
// 팝업을 여닫는 건 PopupPanelController/MainDashboardManager의 몫이고, 이 스크립트는 "네"를 눌렀을 때
// 실제로 AI 분석을 시작시키고 그 결과를 화면에 넘기는 것까지 책임진다.
public class InferenceCheckPopupController : MonoBehaviour
{
    [SerializeField] private Button inferenceCheckYesButton; // 이 팝업의 "네" 버튼
    [SerializeField] private BridgeImageRegistrationController bridgeImageRegistrationController; // "분석 대상 교량" 리스트를 들고 있는 컨트롤러
    [SerializeField] private GameObject imageUploadPanel;    // 분석 완료 후 비활성화할 이미지 업로드 패널
    [SerializeField] private Transform analysisResultArea;   // 결과가 표시될 영역(HorizontalLayoutGroup)
    [SerializeField] private GameObject analyzeResultObjectPrefab; // 결과 1건을 표시할 AnalyzeResultObject 프리팹

    // 방금 실행한 분석의 종합 리포트 - 3D 뷰어 등급 시각화 등 후속 단계에서 참조할 수 있도록 보관
    public BridgeAssessmentReport LastReport { get; private set; }

    //이 팝업은 열고 닫힐 때마다 SetActive가 토글된다.
    private void OnEnable()
    {
        inferenceCheckYesButton.onClick.AddListener(OnYesClicked); // "네" 클릭 시 OnYesClicked 호출
    }

    private void OnDisable()
    {
        inferenceCheckYesButton.onClick.RemoveListener(OnYesClicked); // OnEnable과 짝을 맞춰 제거
    }

    private void OnYesClicked() // "네" 클릭 시 등록된 이미지 전체를 순회하며 AI 분석 + 등급 산정을 수행
    {
        var inputs = new List<ImageAnalysisInput>();

        foreach (var entry in bridgeImageRegistrationController.GetRegisteredEntries()) // 등록된 InputImageObject 전체를 순회
        {
            BridgeAnalysisResult analysis = AiInferenceManager.Instance.AnalyzeImage(entry.Thumbnail); // 이미지 한 장당 RT-DETR+SegFormer 추론 1회 실행

            inputs.Add(new ImageAnalysisInput
            {
                EntryId = entry.EntryId,
                CapturedPart = entry.CapturedPart,
                Thumbnail = entry.Thumbnail,
                Analysis = analysis,
            });
        }

        LastReport = BridgeAssessmentCoordinator.Assess(inputs); // 추론 결과 → 이미지별 예상 등급 + 교량 종합 안전등급

        DisplayResults(LastReport);

        imageUploadPanel.SetActive(false); // 업로드 패널을 내려서 뒤에 있던 AnalysisResultArea가 보이게 함
        MainDashboardManager.Instance.ClosePopupPanel(gameObject); // 분석이 끝났으니 확인 팝업도 닫음
    }

    private void DisplayResults(BridgeAssessmentReport report) // 이미지별 결과를 AnalyzeResultObject로 찍어 AnalysisResultArea에 채우는 메서드
    {
        for (int i = analysisResultArea.childCount - 1; i >= 0; i--) // 이전 분석 결과가 남아있으면 먼저 비움(재분석 시 중복 방지)
            Destroy(analysisResultArea.GetChild(i).gameObject);

        foreach (var imageResult in report.PerImage) // 등록된 이미지 수만큼 결과 오브젝트 생성
        {
            GameObject prefabInstance = Instantiate(analyzeResultObjectPrefab, analysisResultArea);
            prefabInstance.GetComponent<AnalyzeResultObject>().Initialize(imageResult);
        }

        if (report.UnresolvedCapturedParts.Count > 0) // 촬영부재를 체크리스트 항목으로 해석하지 못한 입력이 있으면 경고
        {
            Debug.LogWarning(
                $"촬영 부재를 인식하지 못해 종합 안전등급 산정에서 제외된 항목이 있습니다: " +
                $"{string.Join(", ", report.UnresolvedCapturedParts)}");
        }
    }
}
