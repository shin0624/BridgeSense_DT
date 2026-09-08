using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PanelButtonColorChanger : MonoBehaviour
{
    [SerializeField] private Button mainDashboardButton;
    [SerializeField] private TextMeshProUGUI mainDashboardButtonText;
    [SerializeField] private Button simulationButton;
    [SerializeField] private TextMeshProUGUI simulationButtonText;
    private Color brownColor = new Color(0.7098039f, 0.3294118f, 0.1137255f, 1f); 
    private Color whiteColor = Color.white; 

    void Update()
    {
        if(MainDashboardManager.Instance.GetCurrentPanelState() != MainDashboardPanelState.Simulation)
        {
            simulationButton.image.color = whiteColor;
            simulationButtonText.color = brownColor;
            mainDashboardButton.image.color = brownColor;
            mainDashboardButtonText.color = whiteColor;
        }
        else// 시뮬레이션이면
        {
            mainDashboardButton.image.color = whiteColor;
            mainDashboardButtonText.color = brownColor;
            simulationButton.image.color = brownColor;
            simulationButtonText.color = whiteColor;
        }
    }
}
