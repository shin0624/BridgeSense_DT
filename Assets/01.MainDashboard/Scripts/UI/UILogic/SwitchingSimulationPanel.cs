using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 시뮬레이션 버튼 및 시뮬레이션 패널 안의 돌아가기 버튼에 붙는 전환 스크립트.
// Switching3DPanel/SwitchingInputPanel과 같은 패턴으로, 버튼 하나당 스크립트 하나, 상태 확인 후 매니저에 위임.
public class SwitchingSimulationPanel : MonoBehaviour
{
    [SerializeField] private Button toSimulationButton; // 다른 패널 -> 시뮬레이션 패널
    [SerializeField] private Button backButton;      // 시뮬레이션 패널 -> InputAndAnalyzePanel (돌아가기)

    void Start()
    {
        if (toSimulationButton != null)
            toSimulationButton.onClick.AddListener(OnToSimulationClicked);

        if (backButton != null)
            backButton.onClick.AddListener(OnToBackButtonClicked);
    }

    private void OnToSimulationClicked()
    {
        MainDashboardManager.Instance.SwitchToSimulationPanel();
    }

    private void OnToBackButtonClicked()
    {
        MainDashboardPanelState currentState = MainDashboardManager.Instance.GetCurrentPanelState();
        if (currentState == MainDashboardPanelState.Simulation)
        {
            MainDashboardManager.Instance.SwitchToInputPanel();

        }
    }

    private void OnDestroy()
    {
        if (toSimulationButton != null)
            toSimulationButton.onClick.RemoveListener(OnToSimulationClicked);

        if (backButton != null)
            backButton.onClick.RemoveListener(OnToBackButtonClicked);
    }
}
