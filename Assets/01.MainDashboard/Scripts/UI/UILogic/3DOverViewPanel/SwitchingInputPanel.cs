using UnityEngine;
using UnityEngine.UI;
public class SwitchingInputPanel : MonoBehaviour
{
    [SerializeField] private Button switchingInputPanelButton;
    
    void Start()
    {
        switchingInputPanelButton.onClick.AddListener(OnSwitchingButtonClicked);
    }

    void Update()
    {
        
    }

    private void OnSwitchingButtonClicked()// Overview 패널에서 "AI 비전 분석 결과로 돌아가기"버튼 클릭 시 호출되는 메서드
    {
        MainDashboardPanelState currentState = MainDashboardManager.Instance.GetCurrentPanelState();// 현재 활성화된 패널 상태를 가져옴
        if(currentState == MainDashboardPanelState.OverView)
        {
            MainDashboardManager.Instance.SwitchToInputPanel();// InputAndAnalyzePanel로 전환
        }
    }
}
