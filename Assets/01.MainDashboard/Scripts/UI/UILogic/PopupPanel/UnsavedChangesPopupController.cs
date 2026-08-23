using System;
using BridgeSenseDT.Session;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 저장하지 않은 변경이 있는 상태에서 현재 세션을 버리는 동작("새 분석 시작", "분석 이력 불러오기")을
/// 시도했을 때 뜨는 확인 팝업.
///
/// 예/아니오 두 개가 아니라 세 갈래로 두는 이유는, "저장 안 함"과 "취소"가 전혀 다른 의도이기 때문이다.
/// 둘을 합쳐두면 사용자가 실수로 작업 내용을 잃을 수 있다.
/// </summary>
public class UnsavedChangesPopupController : MonoBehaviour
{
    [SerializeField] private Button saveAndContinueButton;    // 저장하고 계속
    [SerializeField] private Button discardAndContinueButton; // 저장하지 않고 계속
    [SerializeField] private Button cancelButton;             // 취소(하던 일로 돌아가기)
    [SerializeField] private Button closeButton;              // 우상단 닫기. 취소와 같은 동작

    // 팝업 부모(popupPanelParent)는 여기서 참조하지 않는다.
    // 부모를 켜고 끄는 일은 MainDashboardManager가 활성 팝업 수를 보고 스스로 판단하는 내부 동작이라,
    // 밖에서 직접 끄면 아직 열려 있는 다른 팝업이 논리적으로는 열린 채 화면에서만 사라지는 상태가 된다.

    private Action pendingAction; // 확인이 끝나면 실행할 동작(새 세션 시작 또는 불러오기)

    // 이 팝업도 열고 닫힐 때마다 SetActive가 토글되므로 리스너를 OnEnable/OnDisable 짝으로 관리한다.
    private void OnEnable()
    {
        saveAndContinueButton.onClick.AddListener(OnSaveAndContinue);
        discardAndContinueButton.onClick.AddListener(OnDiscardAndContinue);
        cancelButton.onClick.AddListener(OnCancel);
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCancel);
    }

    private void OnDisable()
    {
        saveAndContinueButton.onClick.RemoveListener(OnSaveAndContinue);
        discardAndContinueButton.onClick.RemoveListener(OnDiscardAndContinue);
        cancelButton.onClick.RemoveListener(OnCancel);
        if (closeButton != null)
            closeButton.onClick.RemoveListener(OnCancel);
    }

    /// <summary>확인을 받은 뒤 실행할 동작을 넘기며 팝업을 연다.</summary>
    public void Show(Action onConfirmed)
    {
        pendingAction = onConfirmed; // 활성화 이전에 먼저 담아둔다(OnEnable에서 리스너가 붙기 때문)
        MainDashboardManager.Instance.OpenPopupPanel(gameObject);
    }

    private void OnSaveAndContinue()
    {
        // 저장에 실패하거나 사용자가 파일 다이얼로그를 취소하면 팝업을 닫지 않는다.
        // 여기서 그대로 진행해버리면 저장하려던 내용이 사라진다.
        if (!AnalysisSessionManager.Instance.Save())
            return;

        RunPendingActionAndClose();
    }

    private void OnDiscardAndContinue()
    {
        RunPendingActionAndClose();
    }

    private void OnCancel()
    {
        pendingAction = null;
        MainDashboardManager.Instance.ClosePopupPanel(gameObject);
    }

    private void RunPendingActionAndClose()
    {
        Action action = pendingAction;
        pendingAction = null;

        // 팝업을 먼저 닫는다. 실행할 동작이 화면을 다시 구성하므로 그 전에 정리해두는 편이 안전하다.
        MainDashboardManager.Instance.ClosePopupPanel(gameObject);

        action?.Invoke();
    }
}
