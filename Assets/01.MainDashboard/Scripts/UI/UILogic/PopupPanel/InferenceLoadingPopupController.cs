using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// AI 분석이 실행되는 동안 떠 있는 로딩 팝업.
    ///
    /// "분석 중입니다.." 문구와 원형 프로그레스 바(Image.fillAmount)로 진행 상황을 보여준다.
    /// 이 스크립트는 진행률을 받아 화면에 반영하는 뷰 역할만 하고, 실제로 언제 얼마나 진행됐는지는
    /// InferenceCheckPopupController처럼 추론을 직접 돌리는 쪽이 Report()를 호출해 알려준다.
    /// 팝업을 열고 닫는 책임도 호출하는 쪽에 있다 - 이 컴포넌트는 자기 자신을 여닫지 않는다.
    /// </summary>
    public class InferenceLoadingPopupController : MonoBehaviour
    {
        [Tooltip("원형 프로그레스 바. Image Type이 Filled(Radial 360 등)로 설정돼 있어야 한다")]
        [SerializeField] private Image progressFillImage;

        [Tooltip("\"분석 중입니다..\" 등 상태 문구. 비워두면 문구 갱신은 건너뛴다")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("진행률을 숫자(%)로도 보여줄 때 사용. 비워두면 표시하지 않는다")]
        [SerializeField] private TMP_Text percentText;

        [SerializeField] private string defaultMessage = "분석 중입니다..";

        /// <summary>
        /// 팝업을 열기 직전에 호출해 진행률을 0으로 리셋한다.
        /// MainDashboardManager.OpenPopupPanel이 SetActive(true)를 실행한 뒤 이어서 호출하면 된다.
        /// </summary>
        public void ResetProgress()
        {
            Report(0f, defaultMessage);
        }

        /// <summary>
        /// 진행 상황을 화면에 반영한다.
        /// progress는 0~1 범위로 받는다. fillAmount가 그 범위를 요구하기 때문이다.
        /// </summary>
        public void Report(float progress, string message = null)
        {
            progress = Mathf.Clamp01(progress);

            if (progressFillImage != null)
                progressFillImage.fillAmount = progress;

            if (percentText != null)
                percentText.text = Mathf.RoundToInt(progress * 100f) + "%";

            if (statusText != null)
                statusText.text = string.IsNullOrEmpty(message) ? defaultMessage : message;
        }
    }
}
