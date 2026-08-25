using System;
using BridgeSenseDT.Session;
using BridgeSenseDT.UI;
using Ookii.Dialogs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 저장 / 다른 이름으로 저장 / 새 분석 시작 / 분석 이력 불러오기 버튼을 세션 매니저에 연결하는 컨트롤러.
///
/// 실제 저장·불러오기 로직은 전부 AnalysisSessionManager가 들고 있고 이 클래스는 얇은 배선 층이다.
/// 다만 "현재 세션을 버리는 동작(새로 시작, 불러오기)은 저장 여부를 먼저 확인한다"는 규칙은
/// UI 흐름에 속하므로 이쪽에서 담당한다.
/// </summary>
public class AnalysisToolbarController : MonoBehaviour
{
    [SerializeField] private Button sideToolbarCloseButton; // 사이드 툴바 비활성화 버튼
    [SerializeField] private Button saveButton;          // "저장"
    [SerializeField] private Button saveAsButton;        // "다른 이름으로 저장"
    [SerializeField] private Button newAnalysisButton;   // "새 분석 시작"
    [SerializeField] private Button loadHistoryButton;   // "분석 이력 불러오기"

    // 결과 화면에서 이미지 입력 화면으로 돌아가는 버튼. 아직 UI에 두지 않았다면 비워둬도 된다.
    // 다만 이 버튼이 없으면 한 번 분석한 세션에 이미지를 더 추가할 방법이 없다.
    [SerializeField] private Button returnToEditingButton;

    [SerializeField] private UnsavedChangesPopupController unsavedChangesPopup; // 미저장 상태에서 뜨는 확인 팝업

    [SerializeField] private TMP_Text saveStatusText; // 현재 파일명과 저장 여부 표시(선택, 비워둬도 동작함)

    private string lastRenderedStatus; // 매 프레임 문자열을 새로 만들지 않도록 직전 표시값을 기억해둔다

    private void OnEnable()
    {
        sideToolbarCloseButton.onClick.AddListener(OnCloseButtonClicked);
        saveButton.onClick.AddListener(OnSaveClicked);
        saveAsButton.onClick.AddListener(OnSaveAsClicked);
        newAnalysisButton.onClick.AddListener(OnNewAnalysisClicked);
        loadHistoryButton.onClick.AddListener(OnLoadHistoryClicked);
        if (returnToEditingButton != null)
            returnToEditingButton.onClick.AddListener(OnReturnToEditingClicked);
    }

    private void OnDisable()
    {
        sideToolbarCloseButton.onClick.RemoveListener(OnCloseButtonClicked);
        saveButton.onClick.RemoveListener(OnSaveClicked);
        saveAsButton.onClick.RemoveListener(OnSaveAsClicked);
        newAnalysisButton.onClick.RemoveListener(OnNewAnalysisClicked);
        loadHistoryButton.onClick.RemoveListener(OnLoadHistoryClicked);
        if (returnToEditingButton != null)
            returnToEditingButton.onClick.RemoveListener(OnReturnToEditingClicked);
    }

    private void Update()
    {
        RefreshSaveStatus();
    }

    private void OnSaveClicked()
    {
        AnalysisSessionManager.Instance.Save();
    }

    private void OnSaveAsClicked()
    {
        AnalysisSessionManager.Instance.SaveAs();
    }

    private void OnNewAnalysisClicked()
    {
        ConfirmDiscardThen(() => AnalysisSessionManager.Instance.NewSession());
    }

    private void OnLoadHistoryClicked()
    {
        ConfirmDiscardThen(() => AnalysisSessionManager.Instance.LoadWithDialog());
    }

    // 결과 화면에서 입력 화면으로 돌아간다. 세션 내용을 버리지 않으므로 저장 확인이 필요 없다.
    private void OnReturnToEditingClicked()
    {
        AnalysisSessionManager.Instance.ReturnToEditing();
    }

    /// <summary>
    /// 현재 세션을 버리는 동작을 실행하기 전에, 저장하지 않은 변경이 있으면 먼저 확인을 받는다.
    /// 바꿀 내용이 없으면 확인 없이 바로 실행한다.
    /// </summary>
    private void ConfirmDiscardThen(Action action)
    {
        if (AnalysisSessionManager.Instance.IsDirty)
            unsavedChangesPopup.Show(action);
        else
            action();
    }

    private void RefreshSaveStatus()
    {
        if (saveStatusText == null)
            return;

        var manager = AnalysisSessionManager.Instance;
        if (manager == null)
            return;

        // 파일명과 변경 여부만 보면 되므로, 둘이 그대로면 문자열을 다시 만들지 않는다.
        string status = manager.IsDirty
            ? manager.CurrentFileName + " *"
            : manager.CurrentFileName;

        if (status == lastRenderedStatus)
            return;

        lastRenderedStatus = status;
        saveStatusText.text = status;
    }

    // 사이드바 형태의 본 툴바 접기 버튼 클릭 시.
    // 툴바는 모달이 아니라 팝업 목록(MainDashboardManager)의 관리 대상이 아니므로 직접 끈다.
    // PopupPanelController.OpenSideToolbar와 짝을 이룬다.
    private void OnCloseButtonClicked()
    {
        // SlidingPanel이 붙어 있으면 밀려 나가는 연출을 마친 뒤 꺼진다.
        var slider = GetComponent<SlidingPanel>();

        if (slider != null)
            slider.Hide();
        else
            gameObject.SetActive(false);
    }
}
