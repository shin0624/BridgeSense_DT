using UnityEngine;
using UnityEngine.UI;

public class PopupPanelController : MonoBehaviour
{
    [SerializeField] private GameObject securityLevelPopup;// 부재별 안전등급 입면도 팝업
    [SerializeField] private GameObject elementLevelPopup;// 부재등급분포 팝업
    [SerializeField] private GameObject inferenceCheckPopup; // 추론 여부 체크 팝업
    [SerializeField] private GameObject reportExportPopup;   // 보고서 포맷 선택 팝업
    [SerializeField] private GameObject comingSoonPopup;     // "준비중입니다" 안내 팝업(시뮬레이션 등 미구현 기능용)
    [SerializeField] private GameObject sideToolbar;
    [SerializeField] private Button securityLevelButton;
    [SerializeField] private Button elementLevelButton;
    [SerializeField] private Button inferenceStartButton; // "AI 분석 시작" 버튼
    [SerializeField] private Button reportButton;
    [SerializeField] private Button simulationButton;     // "시뮬레이션" 버튼(아직 미구현)
    [SerializeField] private Button securityLevelCloseButton;
    [SerializeField] private Button elementLevelCloseButton;
    [SerializeField] private Button inferenceCheckCloseButton; // 추론 여부 체크 팝업 닫기 버튼
    [SerializeField] private Button inferenceCheckNoButton; // 추론 여부 체크 팝업 아니오 버튼
    // "네" 버튼 클릭 처리는 InferenceCheckPopupController가 전담 - 여기서는 팝업 열기/닫기만 다룸
    [SerializeField] private Button sideToolbarOpenButton;


    void Start()
    {
        sideToolbarOpenButton.onClick.AddListener(OpenSideToolbar);
        securityLevelButton.onClick.AddListener(OpenSecurityLevelPopup);
        elementLevelButton.onClick.AddListener(OpenElementLevelPopup);
        securityLevelCloseButton.onClick.AddListener(CloseSecurityLevelPopup);
        elementLevelCloseButton.onClick.AddListener(CloseElementLevelPopup);
        inferenceStartButton.onClick.AddListener(OpenInferenceCheckPopup);
        inferenceCheckCloseButton.onClick.AddListener(CloseInferenceCheckPopup);
        inferenceCheckNoButton.onClick.AddListener(CloseInferenceCheckPopup);
        reportButton.onClick.AddListener(OpenReportExportPopup);

        if (simulationButton != null)
            simulationButton.onClick.AddListener(OpenComingSoonPopup);
    }
    // 사이드 툴바는 모달이 아니므로 팝업 관리 대상에서 제외하고 직접 켠다.
    // MainDashboardManager가 관리하는 popupPanelParent는 컨테이너인 동시에 클릭을 막는 모달 배경이라,
    // 툴바를 그 목록에 넣으면 툴바가 열려 있는 동안 활성 팝업 수가 0이 되지 않아 배경이 계속 켜져 있게 된다.
    // 그 결과 다른 팝업을 닫아도 대시보드 클릭이 막히는 문제가 생긴다.
    private void OpenSideToolbar()
    {
        // SlidingPanel이 붙어 있으면 밀려 들어오는 연출로 연다. 없으면 그냥 켠다.
        var slider = sideToolbar.GetComponent<BridgeSenseDT.UI.SlidingPanel>();

        if (slider != null)
            slider.Show();
        else
            sideToolbar.SetActive(true);
    }

    private void OpenSecurityLevelPopup()
    {
        MainDashboardManager.Instance.OpenPopupPanel(securityLevelPopup);// 부재별 안전등급 입면도 팝업을 활성화
    }

    private void OpenElementLevelPopup()
    {
        MainDashboardManager.Instance.OpenPopupPanel(elementLevelPopup);// 부재등급분포 팝업을 활성화
    }

    private void OpenInferenceCheckPopup()
    {
        MainDashboardManager.Instance.OpenPopupPanel(inferenceCheckPopup);// 추론 여부 체크 팝업을 활성화
    }

    // 보고서 포맷 선택 팝업. 닫기는 팝업 자신(ReportExportPopupController)이 처리한다.
    private void OpenReportExportPopup()
    {
        MainDashboardManager.Instance.OpenPopupPanel(reportExportPopup);
    }

    // 시뮬레이션 등 아직 구현하지 않은 기능의 버튼. 닫기는 팝업 자신(ComingSoonPopupController)이 처리한다.
    private void OpenComingSoonPopup()
    {
        MainDashboardManager.Instance.OpenPopupPanel(comingSoonPopup);
    }

    private void CloseSecurityLevelPopup()
    {
        MainDashboardManager.Instance.ClosePopupPanel(securityLevelPopup);// 부재별 안전등급 입면도 팝업을 비활성화
    }

    private void CloseElementLevelPopup()
    {
        MainDashboardManager.Instance.ClosePopupPanel(elementLevelPopup);// 부재등급분포 팝업을 비활성화
    }

    private void CloseInferenceCheckPopup()
    {
        MainDashboardManager.Instance.ClosePopupPanel(inferenceCheckPopup);// 추론 여부 체크 팝업을 비활성화
    }
    
    private void OnDestroy() 
    {
        sideToolbarOpenButton.onClick.RemoveListener(OpenSideToolbar);
        securityLevelButton.onClick.RemoveListener(OpenSecurityLevelPopup);
        elementLevelButton.onClick.RemoveListener(OpenElementLevelPopup);
        securityLevelCloseButton.onClick.RemoveListener(CloseSecurityLevelPopup);
        elementLevelCloseButton.onClick.RemoveListener(CloseElementLevelPopup);
        inferenceStartButton.onClick.RemoveListener(OpenInferenceCheckPopup);
        inferenceCheckCloseButton.onClick.RemoveListener(CloseInferenceCheckPopup);
        inferenceCheckNoButton.onClick.RemoveListener(CloseInferenceCheckPopup);
        reportButton.onClick.RemoveListener(OpenReportExportPopup);

        if (simulationButton != null)
            simulationButton.onClick.RemoveListener(OpenComingSoonPopup);
    }
}
