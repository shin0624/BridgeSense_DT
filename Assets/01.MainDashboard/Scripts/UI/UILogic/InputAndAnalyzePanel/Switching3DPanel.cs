using UnityEngine;
using UnityEngine.UI;

public class Switching3DPanel : MonoBehaviour
{
    [SerializeField] private Button switching3DPanelButton;
    
    void Start()
    {
        switching3DPanelButton.onClick.AddListener(OnSwitchingButtonClicked);
    }

    void Update()
    {
        
    }

    private void OnSwitchingButtonClicked()// Input 패널에서 "3d모델로 확인하기"버튼 클릭 시 호출되는 메서드
    {
        MainDashboardPanelState currentState = MainDashboardManager.Instance.GetCurrentPanelState();// 현재 활성화된 패널 상태를 가져옴
        if(currentState == MainDashboardPanelState.Input)
        {
            MainDashboardManager.Instance.SwitchToOverviewPanel();// OverviewPanel로 전환
        }  
    }
}
