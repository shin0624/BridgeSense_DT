using UnityEngine;
using UnityEngine.UI;

public class PopupPanelController : MonoBehaviour
{
    [SerializeField] private GameObject securityLevelPopup;// 부재별 안전등급 입면도 팝업
    [SerializeField] private GameObject elementLevelPopup;// 부재등급분포 팝업
    [SerializeField] private GameObject inferenceCheckPopup; // 추론 여부 체크 팝업
    [SerializeField] private Button securityLevelButton;
    [SerializeField] private Button elementLevelButton;
    [SerializeField] private Button inferenceStartButton; // "AI 분석 시작" 버튼
    [SerializeField] private Button reportButton;
    [SerializeField] private Button securityLevelCloseButton;
    [SerializeField] private Button elementLevelCloseButton;
    [SerializeField] private Button inferenceCheckCloseButton; // 추론 여부 체크 팝업 닫기 버튼
    
    void Start()
    {
        securityLevelButton.onClick.AddListener(OpenSecurityLevelPopup);
        elementLevelButton.onClick.AddListener(OpenElementLevelPopup);
        securityLevelCloseButton.onClick.AddListener(CloseSecurityLevelPopup);
        elementLevelCloseButton.onClick.AddListener(CloseElementLevelPopup);
        inferenceStartButton.onClick.AddListener(OpenInferenceCheckPopup);
        inferenceCheckCloseButton.onClick.AddListener(CloseInferenceCheckPopup);
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



    private void OnDestroy() {
        securityLevelButton.onClick.RemoveListener(OpenSecurityLevelPopup);
        elementLevelButton.onClick.RemoveListener(OpenElementLevelPopup);
        securityLevelCloseButton.onClick.RemoveListener(CloseSecurityLevelPopup);
        elementLevelCloseButton.onClick.RemoveListener(CloseElementLevelPopup);
        inferenceStartButton.onClick.RemoveListener(OpenInferenceCheckPopup);
        inferenceCheckCloseButton.onClick.RemoveListener(CloseInferenceCheckPopup);
    }
}
