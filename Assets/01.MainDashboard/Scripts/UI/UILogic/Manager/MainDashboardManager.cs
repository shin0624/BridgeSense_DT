using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
public class MainDashboardManager : MonoBehaviour
{
    //씬 내에서 패널을 소유하는 매니저 스크립트
    // 메인 대시보드의 3개 패널을 참조하고, 다른 스크립트는 이 매니저를 통해 패널을 제어하도록 한다.
    // -> 패널 참조 변수, 각 패널 상태값(bool)을 가져야 함
    // 팝업 패널을 제외한 두 패널은 상호 배타적이므로, enum으로 패널 상태를 관리해서 둘 다 켜지거나 하는 상태를 방지
    // 팝업은 독립적으로 켜고 끌 수 있으니까 별도의 bool로 관리

    public static MainDashboardManager Instance {get; private set;}
    private MainDashboardPanelState currentPanelState = MainDashboardPanelState.Input;// 기본 활성화된 패널은 InputAndAnalyzePanel
    [SerializeField] private GameObject inputAndAnalyzePanel;
    [SerializeField] private GameObject overviewPanel;

    private void Awake() 
    {
        if(Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            Instance = this;
        }
        
    }
    private void Start()
    {
        inputAndAnalyzePanel.SetActive(true);// InputAndAnalyzePanel을 기본 활성화
        overviewPanel.SetActive(false);
    }

    public void SwitchToOverviewPanel()// InputAndAnalyzePanel에서 OverviewPanel로 전환하는 메서드
    {
        if(currentPanelState != MainDashboardPanelState.OverView)
        {
            inputAndAnalyzePanel.SetActive(false);
            overviewPanel.SetActive(true);
            currentPanelState = MainDashboardPanelState.OverView;
        }
    }

    public void SwitchToInputPanel()// OverviewPanel에서 InputAndAnalyzePanel로 전환하는 메서드
    {
        if(currentPanelState != MainDashboardPanelState.Input)
        {
            overviewPanel.SetActive(false);
            inputAndAnalyzePanel.SetActive(true);
            currentPanelState = MainDashboardPanelState.Input;
        }
    }

    public void OpenPopupPanel(GameObject popupPanel)// 팝업 패널을 여는 메서드
    {
        if(popupPanel != null)
        {
            popupPanel.SetActive(true);
        }
    }

    public MainDashboardPanelState GetCurrentPanelState()// 현재 활성화된 패널 상태를 반환하는 메서드
    {
        return currentPanelState;
    }



    private void OnDestroy()
    {
        if(Instance == this)
        {
            Instance = null;
        }

    }
}
