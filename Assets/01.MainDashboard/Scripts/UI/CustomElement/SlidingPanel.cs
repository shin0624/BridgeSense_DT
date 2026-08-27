using DG.Tweening;
using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 화면 가장자리에서 밀려 들어오고 나가는 패널.
    ///
    /// 열린 위치는 씬에 배치해둔 그대로를 쓰고, 닫힌 위치는 그 지점에서 패널 크기만큼
    /// 화면 밖으로 밀어낸 자리로 계산한다. 두 위치를 인스펙터에서 따로 맞출 필요가 없어
    /// 레이아웃을 바꿔도 애니메이션이 따라온다.
    /// </summary>
    public class SlidingPanel : MonoBehaviour
    {
        public enum SlideFrom
        {
            Left,
            Right,
            Top,
            Bottom,
        }

        [SerializeField] private SlideFrom slideFrom = SlideFrom.Left;
        [SerializeField] private float duration = 0.35f;

        [Tooltip("들어올 때의 감속 곡선")]
        [SerializeField] private Ease showEase = Ease.OutCubic;

        [Tooltip("나갈 때의 가속 곡선")]
        [SerializeField] private Ease hideEase = Ease.InCubic;

        [Tooltip("함께 서서히 나타나고 사라지게 할 CanvasGroup. 비워두면 이동만 한다")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Tooltip("화면 밖으로 얼마나 더 밀어낼지(픽셀). 그림자나 테두리가 삐져나올 때 올린다")]
        [SerializeField] private float extraOffset = 20f;

        private RectTransform rect;
        private Vector2 shownPosition;
        private Vector2 hiddenPosition;
        private bool positionsCaptured;
        private Tween slideTween;

        public bool IsShown { get; private set; }

        private void OnDestroy()
        {
            slideTween?.Kill();
        }

        public void Show()
        {
            CapturePositions();

            slideTween?.Kill();
            gameObject.SetActive(true); // 움직이는 모습을 보여줘야 하므로 먼저 켠다

            rect.anchoredPosition = hiddenPosition;
            if (canvasGroup != null)
                canvasGroup.alpha = 0f;

            IsShown = true;
            slideTween = BuildTween(shownPosition, 1f, showEase);
        }

        public void Hide()
        {
            if (!positionsCaptured)
            {
                // 한 번도 연 적이 없으면 움직일 자리도 정해지지 않았다. 그냥 끈다.
                gameObject.SetActive(false);
                IsShown = false;
                return;
            }

            slideTween?.Kill();
            IsShown = false;

            // 다 밀려난 뒤에 끈다. 먼저 끄면 움직이는 모습이 보이지 않는다.
            slideTween = BuildTween(hiddenPosition, 0f, hideEase)
                .OnComplete(() => gameObject.SetActive(false));
        }

        public void Toggle()
        {
            if (IsShown)
                Hide();
            else
                Show();
        }

        private Sequence BuildTween(Vector2 targetPosition, float targetAlpha, Ease ease)
        {
            var sequence = DOTween.Sequence().SetLink(gameObject);

            sequence.Join(rect.DOAnchorPos(targetPosition, duration).SetEase(ease));

            if (canvasGroup != null)
                sequence.Join(canvasGroup.DOFade(targetAlpha, duration).SetEase(ease));

            return sequence;
        }

        /// <summary>
        /// 열린 위치와 닫힌 위치를 정한다.
        ///
        /// 처음 열릴 때 한 번만 계산한다. 이후에는 패널이 애니메이션 중간 위치에 있을 수 있어
        /// 그때의 좌표를 열린 위치로 잘못 기억하게 된다.
        ///
        /// Awake가 아니라 여기서 계산하는 이유는, 패널이 꺼진 채로 시작하면 Awake가 실행되지 않기 때문이다.
        /// </summary>
        private void CapturePositions()
        {
            if (positionsCaptured)
                return;

            rect = GetComponent<RectTransform>();
            shownPosition = rect.anchoredPosition;

            Rect size = rect.rect;
            hiddenPosition = shownPosition;

            switch (slideFrom)
            {
                case SlideFrom.Left:
                    hiddenPosition.x -= size.width + extraOffset;
                    break;
                case SlideFrom.Right:
                    hiddenPosition.x += size.width + extraOffset;
                    break;
                case SlideFrom.Top:
                    hiddenPosition.y += size.height + extraOffset;
                    break;
                case SlideFrom.Bottom:
                    hiddenPosition.y -= size.height + extraOffset;
                    break;
            }

            positionsCaptured = true;
        }
    }
}
