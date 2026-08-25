using System.Collections.Generic;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.Session;
using TMPro;
using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// "AI 비전 분석 결과" 영역에 이번 분석의 요약 한 줄을 표시한다.
    ///
    /// 결과 카드는 이미지별로 나열되기 때문에 전체가 몇 건인지 한눈에 들어오지 않는다.
    /// 여기서 부재 수와 위험 건수를 합쳐 보여주고, 자세히 볼 수 있는 곳으로 안내한다.
    /// </summary>
    public class AnalysisSummaryView : MonoBehaviour
    {
        [SerializeField] private TMP_Text summaryText;

        [Tooltip("결함이 하나도 없을 때 보여줄 문구")]
        [SerializeField] private string emptyMessage = "감지된 위험 / 손상이 없습니다.";

        [Tooltip("아직 분석하지 않았을 때 보여줄 문구")]
        [SerializeField] private string notAnalyzedMessage = "이미지를 등록한 뒤 AI 분석을 시작해 주세요.";

        private bool subscribed;

        // OnEnable과 Start 양쪽에서 구독을 시도한다.
        // Unity는 오브젝트마다 Awake와 OnEnable을 이어서 호출하므로,
        // 씬 로드 시점부터 켜져 있는 이 영역은 AnalysisSessionManager.Awake보다 먼저 실행될 수 있다.
        // 그때 그냥 넘어가면 영영 구독하지 못한 채로 남는다.
        // Start는 모든 Awake가 끝난 뒤에 실행되므로 늦어도 여기서는 매니저가 준비돼 있다.
        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (AnalysisSessionManager.Instance != null)
                AnalysisSessionManager.Instance.ReportChanged -= Refresh;

            subscribed = false;
        }

        private void TrySubscribe()
        {
            if (subscribed)
                return;

            var manager = AnalysisSessionManager.Instance;
            if (manager == null)
                return;

            manager.ReportChanged += Refresh;
            subscribed = true;

            // 이 영역이 꺼져 있는 동안 분석이 끝났을 수 있으므로 현재 결과를 한 번 반영한다.
            Refresh(manager.LastReport);
        }

        public void Refresh(BridgeAssessmentReport report)
        {
            if (summaryText == null)
                return;

            if (report?.PerImage == null || report.PerImage.Count == 0)
            {
                summaryText.text = notAnalyzedMessage;
                return;
            }

            CountFindings(report, out int partCount, out int defectCount);

            summaryText.text = defectCount == 0
                ? emptyMessage
                : $"부재 {partCount}개에서 총 {defectCount}개의 위험이 발견되었습니다. " +
                  "3D 모델 또는 입면도 메뉴에서 확인해주세요";
        }

        /// <summary>
        /// 결함이 발견된 부재 수와 전체 결함 건수를 센다.
        ///
        /// 같은 부재를 여러 장 촬영했을 수 있으므로 부재 수는 중복을 제거한다.
        /// 부재를 구분하는 기준은 종류와 번호의 조합이다. 교각7과 교각3은 서로 다른 부재이지만,
        /// 번호 없이 "교각"이라고만 입력한 것끼리는 같은 대상으로 본다.
        /// </summary>
        private static void CountFindings(BridgeAssessmentReport report, out int partCount, out int defectCount)
        {
            var parts = new HashSet<(BridgeChecklistItem item, int index)>();
            defectCount = 0;

            foreach (var image in report.PerImage)
            {
                int found = image.Evaluation?.defects?.Count ?? 0;
                if (found == 0)
                    continue;

                defectCount += found;
                parts.Add((image.ChecklistItem, image.ComponentIndex));
            }

            partCount = parts.Count;
        }
    }
}
