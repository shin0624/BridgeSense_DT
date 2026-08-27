using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 버튼이 눈에 띄도록 밝기와 크기를 천천히 오르내리게 한다.
    /// 처음 쓰는 사용자가 어떤 버튼을 눌러야 할지 알려주는 용도다.
    ///
    /// 화면에 보이는 동안 계속 반복하며, 꺼질 때는 원래 모습으로 되돌린다.
    /// </summary>
    public class PulsingHighlight : MonoBehaviour
    {
        [Tooltip("밝기를 조절할 대상. 비워두면 이 오브젝트에서 찾는다")]
        [SerializeField] private Graphic target;

        [Tooltip("한 번 밝아졌다 어두워지는 데 걸리는 시간(초)")]
        [SerializeField] private float period = 1.1f;

        [Tooltip("가장 흐릴 때의 투명도. 1이면 변화 없음")]
        [SerializeField, Range(0f, 1f)] private float minAlpha = 0.5f;

        [Tooltip("커졌다 작아지는 정도. 0이면 크기는 그대로 둔다")]
        [SerializeField, Range(0f, 0.3f)] private float scaleAmount = 0.06f;

        private Tween pulseTween;
        private Color originalColor;
        private Vector3 originalScale;
        private bool captured;

        private void OnEnable()
        {
            Capture();
            StartPulse();
        }

        private void OnDisable()
        {
            StopPulse();
        }

        private void Capture()
        {
            if (captured)
                return;

            if (target == null)
                target = GetComponent<Graphic>();

            if (target != null)
                originalColor = target.color;

            originalScale = transform.localScale;
            captured = true;
        }

        private void StartPulse()
        {
            StopPulse();

            var sequence = DOTween.Sequence().SetLink(gameObject);

            if (target != null && minAlpha < 1f)
            {
                Color dimmed = originalColor;
                dimmed.a = originalColor.a * minAlpha;

                target.color = dimmed; // 어두운 쪽에서 시작해 밝아지는 흐름으로 맞춘다
                sequence.Join(target.DOColor(originalColor, period));
            }

            if (scaleAmount > 0f)
            {
                transform.localScale = originalScale;
                sequence.Join(transform.DOScale(originalScale * (1f + scaleAmount), period));
            }

            pulseTween = sequence
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        private void StopPulse()
        {
            if (pulseTween != null)
            {
                pulseTween.Kill();
                pulseTween = null;
            }

            if (!captured)
                return;

            // 중간 값에서 멈추면 원래보다 흐리거나 커진 채로 남는다.
            if (target != null)
                target.color = originalColor;

            transform.localScale = originalScale;
        }
    }
}
