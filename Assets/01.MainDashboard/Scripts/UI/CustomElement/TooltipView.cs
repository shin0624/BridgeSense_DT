using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 마우스 커서 옆에 떠서 설명을 보여주는 툴팁.
    ///
    /// 커서 위치는 EventSystem이 넘겨주는 값을 받아서 쓴다.
    /// Input을 직접 읽지 않는 이유는 이 프로젝트가 새 Input System을 쓰고 있어
    /// 예전 Input API가 동작하지 않을 수 있기 때문이다.
    /// </summary>
    public class TooltipView : MonoBehaviour
    {
        [Tooltip("실제로 움직일 툴팁 상자. 비워두면 이 오브젝트를 쓴다")]
        [SerializeField] private RectTransform tooltipRect;

        [SerializeField] private TMP_Text tooltipText;

        [Tooltip("커서로부터 얼마나 떨어뜨릴지. x는 오른쪽, y는 아래쪽이 음수")]
        [SerializeField] private Vector2 cursorOffset = new Vector2(16f, -12f);

        private Canvas rootCanvas;
        private RectTransform canvasRect;

        private void Awake()
        {
            if (tooltipRect == null)
                tooltipRect = GetComponent<RectTransform>();

            Canvas parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                Debug.LogError("툴팁이 Canvas 아래에 있지 않습니다.", this);
                enabled = false;
                return;
            }

            rootCanvas = parentCanvas.rootCanvas;
            canvasRect = rootCanvas.GetComponent<RectTransform>();

            // 최상위 캔버스 바로 아래로 옮긴다.
            // 다른 패널 안에 들어있으면 좌표 기준이 그 패널이 되어 위치 계산이 어긋나고,
            // 그 패널이 꺼지거나 잘리면 툴팁도 함께 사라진다.
            // 마지막 자식으로 두면 다른 UI 위에 그려지는 것도 함께 해결된다.
            tooltipRect.SetParent(canvasRect, false);
            tooltipRect.SetAsLastSibling();

            // 커서의 오른쪽 아래로 펼쳐지도록 기준점을 좌상단에 둔다.
            // 기준점이 가운데면 툴팁이 커서를 절반쯤 덮는다.
            tooltipRect.pivot = new Vector2(0f, 1f);

            // 앵커가 늘어난 상태(Stretch)면 위치를 좌표로 정할 수 없으므로 한 점으로 고정한다.
            tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
            tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);

            BlockRaycasts();

            gameObject.SetActive(false);
        }

        /// <summary>
        /// 툴팁이 마우스 입력을 가로채지 않도록 막는다.
        ///
        /// 툴팁은 커서 바로 옆에 뜨기 때문에, 입력을 받으면 버튼에서 벗어난 것으로 판정된다.
        /// 그러면 툴팁이 꺼지고, 꺼지는 순간 다시 버튼이 감지되어 깜빡임이 반복된다.
        /// </summary>
        private void BlockRaycasts()
        {
            var group = tooltipRect.GetComponent<CanvasGroup>();
            if (group == null)
                group = tooltipRect.gameObject.AddComponent<CanvasGroup>();

            group.blocksRaycasts = false;
            group.interactable = false;
        }

        /// <summary>툴팁을 켜고 주어진 커서 위치 옆에 놓는다.</summary>
        public void Show(string message, Vector2 screenPosition)
        {
            if (!enabled)
                return;

            if (tooltipText != null)
                tooltipText.text = message;

            gameObject.SetActive(true);

            // 글자가 바뀌면 상자 크기도 달라진다. 위치를 잡기 전에 크기를 확정시킨다.
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);

            UpdatePosition(screenPosition);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>커서를 따라 움직인다.</summary>
        public void UpdatePosition(Vector2 screenPosition)
        {
            if (canvasRect == null)
                return;

            // Overlay 방식은 카메라를 넘기면 안 된다. 그 외 방식은 캔버스에 지정된 카메라를 써야 한다.
            Camera eventCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : rootCanvas.worldCamera;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPosition, eventCamera, out Vector2 localPoint))
                return;

            // 부모가 캔버스이므로 캔버스 기준 좌표를 그대로 위치로 쓸 수 있다.
            tooltipRect.localPosition = ClampInsideCanvas(localPoint + cursorOffset);
        }

        /// <summary>
        /// 화면 밖으로 나가지 않도록 위치를 안쪽으로 당긴다.
        /// 화면 오른쪽이나 아래쪽 끝에서 툴팁이 잘려 읽을 수 없게 되는 것을 막는다.
        /// </summary>
        private Vector2 ClampInsideCanvas(Vector2 desired)
        {
            Rect bounds = canvasRect.rect; // 캔버스 피벗이 어디에 있든 그대로 반영된 범위
            Vector2 size = tooltipRect.rect.size;

            // 기준점이 좌상단이라 상자는 x는 오른쪽으로, y는 아래쪽으로 뻗어나간다.
            desired.x = Mathf.Clamp(desired.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - size.x));
            desired.y = Mathf.Clamp(desired.y, Mathf.Min(bounds.yMax, bounds.yMin + size.y), bounds.yMax);

            return desired;
        }
    }
}
