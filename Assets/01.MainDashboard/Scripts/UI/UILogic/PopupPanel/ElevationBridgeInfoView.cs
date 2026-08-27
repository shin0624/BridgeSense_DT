using BridgeSenseDT.BridgeData;
using BridgeSenseDT.Session;
using TMPro;
using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 입면도 팝업 좌측의 교량 개요 패널.
    /// 교량명과 총 교장·경간수·최대경간장을 현황조서에서 찾아 표시한다.
    ///
    /// 자료에 없는 교량이면 값을 지어내지 않고 빈칸으로 둔다.
    /// 점검 자료로 쓰이는 화면이라 임의 값은 신뢰를 해친다.
    /// </summary>
    public class ElevationBridgeInfoView : MonoBehaviour
    {
        [SerializeField] private TMP_Text bridgeNameText;  // 교량명
        [SerializeField] private TMP_Text lengthText;      // 총 교장
        [SerializeField] private TMP_Text spanCountText;   // 경간 수
        [SerializeField] private TMP_Text maxSpanText;     // 최대 경간장

        private const string Unknown = "-";

        private void OnEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            var session = AnalysisSessionManager.Instance != null
                ? AnalysisSessionManager.Instance.CurrentSession
                : null;

            if (session == null)
            {
                SetText(bridgeNameText, Unknown);
                SetText(lengthText, Unknown, "총 교장 : ");
                SetText(spanCountText, Unknown, "경간 수 : ");
                SetText(maxSpanText, Unknown, "최대경간장 : ");
                return;
            }

            SetText(bridgeNameText, session.BridgeName);

            BridgeSpec spec = BridgeSpecRepository.Find(session.BridgeName, session.Location);

            if (spec == null)
            {
                SetText(lengthText, Unknown, "총 교장 : ");
                SetText(spanCountText, Unknown, "경간 수 : ");
                SetText(maxSpanText, Unknown, "최대경간장 : ");
                return;
            }

            SetText(lengthText, FormatMeters(spec.len), "총 교장 : ");
            SetText(spanCountText, string.IsNullOrWhiteSpace(spec.spans) ? Unknown : spec.spans, "경간 수 : ");
            SetText(maxSpanText, FormatMeters(spec.maxSpan), "최대경간장 : ");
        }

        /// <summary>조서의 숫자를 소수 한 자리 + 단위로 다듬는다. 숫자가 아니면 원문을 그대로 둔다.</summary>
        private static string FormatMeters(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Unknown;

            return float.TryParse(value, out float meters) ? $"{meters:F1}m" : value;
        }

        /// <summary>
        /// 값 앞에 항목 이름을 붙여 표시한다.
        /// 값이 비어 있어도 항목 이름은 남겨서 어떤 항목이 비어 있는지 알 수 있게 한다.
        /// </summary>
        private static void SetText(TMP_Text target, string value, string description = "")
        {
            if (target == null)
                return;

            string shown = string.IsNullOrWhiteSpace(value) ? Unknown : value;
            target.text = description + shown;
        }
    }
}
