using DG.Tweening;
using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 서로 배타적인 두 패널(또는 여러 패널) 사이를 오갈 때 공통으로 쓰는 크로스페이드 전환.
    ///
    /// MainDashboardManager나 AnalysisSessionManager처럼 "이 패널을 끄고 저 패널을 켠다"는
    /// SetActive 두 줄로 전환을 처리하던 곳에서, 그 두 줄 대신 이 컴포넌트의 Show()를 호출하면 된다.
    /// 대상 패널마다 하나씩 붙이는 것이 아니라, 전환을 담당하는 쪽(매니저)이 패널 개수만큼 들고
    /// SetLink(panel)로 각자의 생명주기에 묶어 쓰는 방식을 기본으로 상정했다.
    /// </summary>
    public class PanelCrossfadeTransition : MonoBehaviour
    {
        [SerializeField] private float duration = 0.25f;

        [Tooltip("나타날 때의 감속 곡선")]
        [SerializeField] private Ease showEase = Ease.OutQuad;

        [Tooltip("사라질 때의 가속 곡선")]
        [SerializeField] private Ease hideEase = Ease.InQuad;

        [Tooltip("대상 패널에 CanvasGroup이 없으면 실행 시 자동으로 붙인다")]
        [SerializeField] private bool autoAddCanvasGroup = true;

        private Tween activeTween;

        private void OnDestroy()
        {
            activeTween?.Kill();
        }

        /// <summary>
        /// from을 서서히 지우며 끄고, to를 서서히 나타내며 켠다.
        ///
        /// from이나 to가 비어 있어도 동작한다(예: 첫 진입이라 이전 패널이 없는 경우).
        /// 이미 재생 중인 전환이 있으면 죽이고 새로 시작한다. 버튼을 연타해도 중간 상태가
        /// 꼬이지 않도록 하기 위함이다.
        /// </summary>
        public void Show(GameObject from, GameObject to)
        {
            activeTween?.Kill();

            CanvasGroup fromGroup = from != null ? GetOrAddCanvasGroup(from) : null;
            CanvasGroup toGroup = to != null ? GetOrAddCanvasGroup(to) : null;

            var sequence = DOTween.Sequence().SetLink(gameObject);

            if (fromGroup != null)
            {
                fromGroup.blocksRaycasts = false; // 사라지는 동안 뒤쪽 클릭을 막지 않는다
                sequence.Join(fromGroup.DOFade(0f, duration).SetEase(hideEase));
            }

            if (toGroup != null)
            {
                toGroup.alpha = 0f;
                toGroup.blocksRaycasts = false;
                to.SetActive(true); // 페이드가 보이려면 먼저 켜져 있어야 한다

                sequence.Join(toGroup.DOFade(1f, duration).SetEase(showEase));
            }

            sequence.OnComplete(() =>
            {
                if (from != null)
                    from.SetActive(false); // 다 지워진 뒤에 끈다. 먼저 끄면 사라지는 모습이 보이지 않는다

                if (toGroup != null)
                    toGroup.blocksRaycasts = true;
            });

            activeTween = sequence;
        }

        private CanvasGroup GetOrAddCanvasGroup(GameObject target)
        {
            CanvasGroup group = target.GetComponent<CanvasGroup>();

            if (group == null && autoAddCanvasGroup)
                group = target.AddComponent<CanvasGroup>();

            return group;
        }
    }
}
