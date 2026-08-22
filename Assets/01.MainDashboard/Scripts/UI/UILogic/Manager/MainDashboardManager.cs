using System.Collections.Generic;
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
    private HashSet<GameObject> activePopupPanels = new HashSet<GameObject>();// 활성화된 팝업 패널의 GameObject를 저장하는 HashSet
    [SerializeField] private GameObject inputAndAnalyzePanel;
    [SerializeField] private GameObject overviewPanel;
    [SerializeField] private GameObject popupPanelParent;// 팝업패널 부모 오브젝트

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

        // 팝업패널 부모는 레이캐스트 타겟이라서 활성화된 채로 두면 메인 대시보드 클릭이 전부 막힌다.
        // 씬에 저장된 상태에 의존하지 않도록 시작 시 항상 꺼둔 상태에서 출발한다.
        popupPanelParent.SetActive(false);
        activePopupPanels.Clear();// 부모를 껐으므로 열린 팝업 목록도 실제 상태(비어있음)와 맞춰둔다
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

    public void OpenPopupPanel(GameObject popupPanel)// 팝업 패널을 여는 메서드. popupPanel은 항상 팝업 부모의 자식 팝업
    {
        if(popupPanelParent!=null && !popupPanelParent.activeSelf)// 팝업패널 부모가 비활성화 상태이면 활성화
        {
            popupPanelParent.SetActive(true);
        }

        if(popupPanel != null)
        {
            if(!activePopupPanels.Contains(popupPanel))// 이미 활성화된 팝업 패널이 아니면
            {   
                activePopupPanels.Add(popupPanel);// 활성화된 팝업 패널 목록에 추가
                popupPanel.SetActive(true);// 팝업 패널 활성화
            }
            
        }
    }

    public void ClosePopupPanel(GameObject popupPanel)// 팝업 패널을 닫는 메서드. popupPanel은 항상 팝업 부모의 자식 팝업
    {
        if(popupPanel != null)
        {
            if(activePopupPanels.Contains(popupPanel))// 활성화된 팝업 패널이면
            {
                activePopupPanels.Remove(popupPanel);// 활성화된 팝업 패널 목록에서 제거
                popupPanel.SetActive(false);// 팝업 패널 비활성화
                if(activePopupPanels.Count == 0)// 활성화된 팝업 패널이 없으면
                {
                    popupPanelParent.SetActive(false);// 팝업패널 부모 비활성화
                }
            }
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
