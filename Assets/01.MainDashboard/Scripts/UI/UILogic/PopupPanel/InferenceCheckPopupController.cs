using BridgeSenseDT.Assessment;
using BridgeSenseDT.Session;
using UnityEngine;
using UnityEngine.UI;

// inferenceCheckPopup 자신에게 붙는 전용 컨트롤러 - "이 팝업 안에서 일어나는 확인 로직"만 담당한다.
// 팝업을 여닫는 건 PopupPanelController/MainDashboardManager의 몫이고,
// 분석 결과를 어디에 어떻게 그릴지는 AnalysisSessionManager와 AnalysisResultListView의 몫이다.
// 이 스크립트는 "네"를 눌렀을 때 추론을 돌려 세션에 결과를 채워 넣는 것까지만 책임진다.
public class InferenceCheckPopupController : MonoBehaviour
{
    [SerializeField] private Button inferenceCheckYesButton; // 이 팝업의 "네" 버튼
    [SerializeField] private BridgeImageRegistrationController bridgeImageRegistrationController; // 화면에 떠 있는 등록 항목들을 조회하기 위한 참조

    //이 팝업은 열고 닫힐 때마다 SetActive가 토글된다.
    private void OnEnable()
    {
        inferenceCheckYesButton.onClick.AddListener(OnYesClicked); // "네" 클릭 시 OnYesClicked 호출
    }

    private void OnDisable()
    {
        inferenceCheckYesButton.onClick.RemoveListener(OnYesClicked); // OnEnable과 짝을 맞춰 제거
    }

    private void OnYesClicked() // "네" 클릭 시 등록된 이미지 전체를 순회하며 AI 추론을 수행하고 결과를 세션에 기록
    {
        var session = AnalysisSessionManager.Instance.CurrentSession;

        foreach (var view in bridgeImageRegistrationController.GetRegisteredEntries()) // 화면에 떠 있는 InputImageObject 전체를 순회
        {
            var entry = session.FindEntry(view.EntryId); // 화면 항목에 대응하는 세션 데이터를 찾는다
            if (entry == null)
                continue;

            BridgeAnalysisResult analysis = AiInferenceManager.Instance.AnalyzeImage(view.Thumbnail); // 이미지 한 장당 RT-DETR+SegFormer 추론 1회 실행

            entry.Detections = analysis.Detections;                // bbox 원본은 향후 오버레이 시각화를 위해 보관
            entry.Defects = DefectExtractor.Extract(analysis);     // 등급 산정 입력으로 쓸 결함 목록으로 축약
            entry.Analyzed = true;
        }

        // 등급 산정, 결과 카드 렌더링, 화면 상태 전환은 세션 매니저가 처리한다.
        // 저장본을 불러오는 경로도 같은 메서드를 거치므로 두 경로의 결과가 어긋나지 않는다.
        AnalysisSessionManager.Instance.NotifyAnalysisCompleted();

        MainDashboardManager.Instance.ClosePopupPanel(gameObject); // 분석이 끝났으니 확인 팝업도 닫음
    }
}
