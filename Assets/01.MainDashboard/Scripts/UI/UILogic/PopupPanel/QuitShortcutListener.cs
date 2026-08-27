using UnityEngine;
using UnityEngine.InputSystem;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// ESC 키를 감지해 종료 확인 팝업을 여닫는 리스너.
    ///
    /// 팝업 자신(QuitConfirmPopupController)이 아니라 씬에서 항상 활성 상태인 오브젝트에 붙여야 한다.
    /// 팝업은 평소 꺼져 있는 오브젝트라, 거기서 Update를 돌리면 정작 닫혀 있을 때(ESC로 "열어야" 할 때)
    /// 아무 것도 감지하지 못한다. StartScene과 MainDashboardScene 양쪽에 하나씩 배치해 두면
    /// 같은 방식으로 ESC 단축키가 동작한다.
    ///
    /// 프로젝트의 Active Input Handling이 Input System 패키지로 전환돼 있어
    /// UnityEngine.Input 대신 Keyboard.current를 사용한다.
    /// </summary>
    public class QuitShortcutListener : MonoBehaviour
    {
        [SerializeField] private QuitConfirmPopupController quitConfirmPopup;

        private void Update()
        {
            // 키보드가 연결되지 않은 환경(이론상) 대비 null 체크.
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
                return;

            // 이미 열려 있으면 "돌아가기"와 같은 동작으로 닫는다.
            // ESC 한 번으로 열고 ESC 한 번으로 닫는 대칭적인 동작이 되도록 한다.
            if (quitConfirmPopup.IsOpen)
                quitConfirmPopup.Close();
            else
                quitConfirmPopup.Open();
        }
    }
}
