using UnityEngine;
using UnityEngine.EventSystems;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 마우스를 올리면 툴팁을 띄우고 벗어나면 감추는 컴포넌트.
    ///
    /// 커서 위치를 Input에서 직접 읽지 않고 EventSystem이 넘겨주는 값을 쓴다.
    /// 이 프로젝트는 새 Input System을 사용하므로 예전 Input API가 동작하지 않을 수 있고,
    /// EventSystem 경유로 받으면 어느 쪽이든 그대로 동작한다.
    /// </summary>
    public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        [SerializeField] private TooltipView tooltip;

        [TextArea]
        [SerializeField] private string message = "분석 도구 모음을 엽니다";

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (tooltip != null)
                tooltip.Show(message, eventData.position);
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (tooltip != null)
                tooltip.UpdatePosition(eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (tooltip != null)
                tooltip.Hide();
        }

        private void OnDisable()
        {
            // 버튼이 사라지면 나가는 이벤트를 받지 못해 툴팁이 화면에 남는다.
            if (tooltip != null)
                tooltip.Hide();
        }
    }
}
