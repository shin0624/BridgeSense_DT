using DG.Tweening;
using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 팝업이 열릴 때 정중앙 기준으로 1.1배에서 원래 크기로 줄어들며 나타나는 연출.
    ///
    /// 팝업 자신의 RectTransform에 붙여 쓴다. MainDashboardManager.OpenPopupPanel이
    /// SetActive(true)로 팝업을 켠 직후 이 컴포넌트의 Play()를 호출하면 된다.
    /// 크기 변화는 스케일로만 주므로 앵커/피벗이 중앙(0.5, 0.5)일 때 정중앙 기준으로 보인다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PopupPopInAnimator : MonoBehaviour
    {
        [SerializeField] private float duration = 0.25f;
        [SerializeField] private float startScale = 1.1f;
        [SerializeField] private Ease ease = Ease.OutBack;

        [Tooltip("함께 서서히 나타나게 할 CanvasGroup. 비워두면 크기 변화만 한다")]
        [SerializeField] private CanvasGroup canvasGroup;

        private RectTransform rect;
        private Tween popTween;
        private Vector3 originalScale;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();

            // 씬에 배치된 그대로의 스케일을 "원래 크기"로 기억해 둔다.
            // Play()가 처음 호출되는 시점에는 이미 SetActive(true) 직후라 스케일이
            // 조작되기 전이므로, 여기서 한 번 잡아두면 몇 번을 열고 닫아도 기준이 흔들리지 않는다.
            originalScale = rect.localScale;
        }

        private void OnDestroy()
        {
            popTween?.Kill();
        }

        /// <summary>
        /// 시작 스케일로 되돌린 뒤 원래 크기까지 애니메이션한다.
        /// 팝업이 이미 열려 있는 상태에서 다시 호출해도 매번 같은 연출로 재생된다.
        ///
        /// "원래 크기"는 무조건 스케일 1이 아니라, 에디터에서 설정해 둔 실제 localScale(Awake에서 캐싱)이다.
        /// 이 값을 1로 고정해버리면 팝업의 원래 스케일이 1이 아닌 경우(예: 부모 쪽에서 보정된 값)
        /// 애니메이션이 끝난 뒤 크기가 에디터에서 본 것과 달라진다.
        /// </summary>
        public void Play()
        {
            popTween?.Kill();

            rect.localScale = originalScale * startScale;

            if (canvasGroup != null)
                canvasGroup.alpha = 0f;

            var sequence = DOTween.Sequence().SetLink(gameObject);
            sequence.Join(rect.DOScale(originalScale, duration).SetEase(ease));

            if (canvasGroup != null)
                sequence.Join(canvasGroup.DOFade(1f, duration).SetEase(Ease.OutQuad));

            popTween = sequence;
        }
    }
}
