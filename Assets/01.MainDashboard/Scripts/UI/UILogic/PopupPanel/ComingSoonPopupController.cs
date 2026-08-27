using UnityEngine;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 아직 구현되지 않은 기능(예: 시뮬레이션)의 버튼을 눌렀을 때 뜨는 "준비중" 안내 팝업.
    ///
    /// x버튼과 닫기 버튼 모두 같은 동작(팝업 닫기)이라 별도 분기 없이 한 메서드에 묶는다.
    /// 여러 미구현 버튼이 같은 팝업 하나를 공유해도 되므로(내용이 항상 "준비중입니다"로 동일),
    /// 열 때 특별히 넘겨줄 데이터가 없다.
    /// </summary>
    public class ComingSoonPopupController : MonoBehaviour
    {
        [SerializeField] private Button closeXButton; // 우상단 x버튼
        [SerializeField] private Button closeButton;   // "닫기" 버튼

        private void OnEnable()
        {
            if (closeXButton != null)
                closeXButton.onClick.AddListener(Close);

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
        }

        private void OnDisable()
        {
            if (closeXButton != null)
                closeXButton.onClick.RemoveListener(Close);

            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
        }

        /// <summary>시뮬레이션 버튼 등, 미구현 기능의 버튼에서 호출한다.</summary>
        public void Open()
        {
            MainDashboardManager.Instance.OpenPopupPanel(gameObject);
        }

        public void Close()
        {
            MainDashboardManager.Instance.ClosePopupPanel(gameObject);
        }
    }
}
