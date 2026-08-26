using System.Collections.Generic;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.Bridge3D;
using BridgeSenseDT.Session;
using BridgeSenseDT.UI.Charts;
using TMPro;
using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// "부재 등급 분포" 팝업에 분석 결과를 채워 넣는 컨트롤러.
    ///
    /// 등급별 부재 수를 세어 막대 그래프와 도넛 그래프에 같은 데이터를 넣는다.
    /// 두 그래프는 같은 숫자를 개수와 비율이라는 다른 방식으로 보여주는 것이므로
    /// 데이터를 따로 만들지 않고 하나를 공유한다.
    ///
    /// 부재 단위 등급은 입면도 팝업과 같은 규칙(BridgeComponentGradeResolver)으로 구한다.
    /// 두 팝업이 같은 교량을 두고 다른 숫자를 말하면 어느 쪽도 믿을 수 없게 된다.
    /// </summary>
    public class GradeDistributionController : MonoBehaviour
    {
        [SerializeField] private UIChart barChart;
        [SerializeField] private UIChart donutChart;
        [SerializeField] private BridgeModelRegistry registry; // 비워두면 싱글톤 인스턴스를 사용

        [Header("요약")]
        [SerializeField] private TMP_Text bridgeNameText;
        [SerializeField] private TMP_Text componentCountText;

        // 표시 순서를 A부터 E까지로 고정한다. 등급은 서열이 있는 값이라 순서가 바뀌면 읽기 어렵다.
        private static readonly string[] GradeOrder = { "A", "B", "C", "D", "E" };

        private BridgeModelRegistry Registry => registry != null ? registry : BridgeModelRegistry.Instance;

        private void OnEnable()
        {
            if (AnalysisSessionManager.Instance == null)
                return;

            AnalysisSessionManager.Instance.ReportChanged += Rebuild;

            // 이 팝업은 닫혀 있다가 열리는 구조라 구독만으로는 그동안의 결과를 놓친다.
            Rebuild(AnalysisSessionManager.Instance.LastReport);
        }

        private void OnDisable()
        {
            if (AnalysisSessionManager.Instance != null)
                AnalysisSessionManager.Instance.ReportChanged -= Rebuild;
        }

        public void Rebuild(BridgeAssessmentReport report)
        {
            var modelRegistry = Registry;
            if (modelRegistry == null)
            {
                Debug.LogWarning("BridgeModelRegistry를 찾지 못해 등급 분포를 그릴 수 없습니다.");
                return;
            }

            var gradeMap = BridgeComponentGradeResolver.Resolve(report, modelRegistry);
            var counts = CountByGrade(gradeMap);

            UpdateSummary(gradeMap);
            UpdateCharts(counts);
        }

        /// <summary>등급별 부재 수를 센다. 등급이 매겨지지 않은 부재는 세지 않는다.</summary>
        private static Dictionary<string, int> CountByGrade(ComponentGradeMap gradeMap)
        {
            var counts = new Dictionary<string, int>();
            foreach (string grade in GradeOrder)
                counts[grade] = 0;

            foreach (string grade in gradeMap.EnumerateGrades())
            {
                if (counts.ContainsKey(grade))
                    counts[grade]++;
            }

            return counts;
        }

        private void UpdateSummary(ComponentGradeMap gradeMap)
        {
            var session = AnalysisSessionManager.Instance != null
                ? AnalysisSessionManager.Instance.CurrentSession
                : null;

            if (bridgeNameText != null)
            {
                string bridgeName = session != null && !string.IsNullOrWhiteSpace(session.BridgeName)
                    ? session.BridgeName
                    : "-";
                bridgeNameText.text = $"교량 명 : {bridgeName}";
            }

            if (componentCountText != null)
                componentCountText.text = $"총 부재 수 : {gradeMap.GradedComponentCount}개";
        }

        private void UpdateCharts(Dictionary<string, int> counts)
        {
            var data = BuildChartData(counts);

            // 두 그래프에 같은 목록을 넣는다. UIChart가 각자 개수와 비율로 표현한다.
            if (barChart != null)
                barChart.SetData(data);

            if (donutChart != null)
                donutChart.SetData(data);
        }

        private static List<ChartDataPoint> BuildChartData(Dictionary<string, int> counts)
        {
            var data = new List<ChartDataPoint>(GradeOrder.Length);

            foreach (string grade in GradeOrder)
            {
                data.Add(new ChartDataPoint
                {
                    label = $"{grade}({GradeColorMap.GetLabel(grade)})",
                    value = counts[grade],
                    color = GradeColorMap.GetColor(grade),
                });
            }

            return data;
        }
    }
}
