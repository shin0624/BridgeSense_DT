using BridgeSenseDT.Assessment;
using BridgeSenseDT.BridgeData;
using BridgeSenseDT.Session;
using TMPro;
using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// "교량 상세 정보" 패널에 현황조서에서 찾은 제원을 표시한다.
    ///
    /// 사용자가 입력한 교량명·주소로 국토교통부 자료를 조회하며,
    /// 자료에 없는 교량이면 입력값만 그대로 두고 나머지는 빈칸으로 남긴다.
    /// 임의로 값을 지어내면 점검 자료로서 신뢰할 수 없게 되기 때문이다.
    /// </summary>
    public class BridgeInfoPanelView : MonoBehaviour
    {
        [SerializeField] private TMP_Text bridgeNameText;     // 교량명
        [SerializeField] private TMP_Text addressText;        // 주소
        [SerializeField] private TMP_Text agencyText;         // 관리 기관
        [SerializeField] private TMP_Text superstructureText; // 상부구조
        [SerializeField] private TMP_Text substructureText;   // 하부구조
        [SerializeField] private TMP_Text completionYearText; // 준공년도

        private const string Unknown = "-";

        private void OnEnable()
        {
            // 3D 패널이 켜지는 시점에 갱신한다. 분석이 끝난 뒤 전환하는 흐름이라 이때 값이 확정돼 있다.
            Refresh();

            if (AnalysisSessionManager.Instance != null)
                AnalysisSessionManager.Instance.ReportChanged += HandleReportChanged;
        }

        private void OnDisable()
        {
            if (AnalysisSessionManager.Instance != null)
                AnalysisSessionManager.Instance.ReportChanged -= HandleReportChanged;
        }

        private void HandleReportChanged(BridgeAssessmentReport report)
        {
            Refresh();
        }

        /// <summary>현재 세션의 교량 정보로 패널을 다시 채운다.</summary>
        public void Refresh()
        {
            var session = AnalysisSessionManager.Instance != null
                ? AnalysisSessionManager.Instance.CurrentSession
                : null;

            if (session == null)
            {
                ShowEmpty();
                return;
            }

            // 사용자가 입력한 값은 조회 성공 여부와 무관하게 그대로 보여준다.
            SetText(bridgeNameText, session.BridgeName);
            SetText(addressText, session.Location);

            BridgeSpec spec = BridgeSpecRepository.Find(session.BridgeName, session.Location);

            if (spec == null)
            {
                SetText(agencyText, Unknown);
                SetText(superstructureText, Unknown);
                SetText(substructureText, Unknown);
                SetText(completionYearText, Unknown);
                return;
            }

            // 조회에 성공하면 주소는 조서상 표기로 바꿔준다. 사용자 입력보다 정확한 행정구역 표기다.
            SetText(addressText, spec.GetAddress());
            SetText(agencyText, spec.agency);
            SetText(superstructureText, spec.sup);
            SetText(substructureText, spec.sub);
            SetText(completionYearText, spec.year);
        }

        private void ShowEmpty()
        {
            SetText(bridgeNameText, Unknown);
            SetText(addressText, Unknown);
            SetText(agencyText, Unknown);
            SetText(superstructureText, Unknown);
            SetText(substructureText, Unknown);
            SetText(completionYearText, Unknown);
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target == null)
                return;

            target.text = string.IsNullOrWhiteSpace(value) ? Unknown : value;
        }
    }
}
