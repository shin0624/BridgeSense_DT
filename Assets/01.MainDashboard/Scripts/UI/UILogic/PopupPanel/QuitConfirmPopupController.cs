using UnityEngine;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 프로그램 종료 확인 팝업 자신에 붙는 뷰. StartScene과 MainDashboardScene 양쪽에서 쓰인다.
    ///
    /// MainDashboardScene에는 팝업을 총괄하는 MainDashboardManager(popupPanelParent 모달 배경을
    /// 활성 팝업 수에 따라 스스로 켜고 끄는 씬 로컬 싱글톤)가 있지만, StartScene에는 없다.
    /// 그래서 이 컴포넌트는 MainDashboardManager.Instance가 있으면 그것을 통해 열고 닫아
    /// 다른 팝업과 배경을 공유하는 규칙을 따르고, 없으면(StartScene) 자기 GameObject만 직접 켜고 끈다.
    ///
    /// ESC로 이 팝업을 "여는" 동작은 이 컴포넌트가 맡지 않는다. 팝업은 평소 꺼져 있는 오브젝트라
    /// 자기 자신에서 Update를 돌려도 비활성 상태에서는 호출되지 않기 때문이다.
    /// 그 역할은 씬에서 항상 켜져 있는 오브젝트에 붙는 QuitShortcutListener가 담당한다.
    /// </summary>
    public class QuitConfirmPopupController : MonoBehaviour
    {
        [SerializeField] private Button quitButton;   // "종료"
        [SerializeField] private Button cancelButton;  // "돌아가기"

        private void OnEnable()
        {
            quitButton.onClick.AddListener(OnQuitClicked);
            cancelButton.onClick.AddListener(Close);
        }

        private void OnDisable()
        {
            quitButton.onClick.RemoveListener(OnQuitClicked);
            cancelButton.onClick.RemoveListener(Close);
        }

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>사이드 툴바의 "종료" 버튼, QuitShortcutListener(ESC) 등에서 팝업을 열 때 호출한다.</summary>
        public void Open()
        {
            if (MainDashboardManager.Instance != null)
                MainDashboardManager.Instance.OpenPopupPanel(gameObject);
            else
                gameObject.SetActive(true); // StartScene: 관리 매니저가 없으므로 직접 켠다
        }

        public void Close()
        {
            if (MainDashboardManager.Instance != null)
                MainDashboardManager.Instance.ClosePopupPanel(gameObject);
            else
                gameObject.SetActive(false);
        }

        private void OnQuitClicked()
        {
            Application.Quit();

#if UNITY_EDITOR
            // 빌드에서는 Application.Quit()이 즉시 앱을 종료하지만, 에디터 플레이 모드에서는
            // 아무 일도 일어나지 않는다. 실제로 확인 흐름이 동작하는지 에디터에서도 검증할 수 있도록
            // 플레이 모드를 대신 정지시킨다.
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
