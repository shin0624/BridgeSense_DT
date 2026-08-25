using System;
using System.Collections.Generic;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.Bridge3D;
using BridgeSenseDT.Session;
using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// "부재별 안전등급 입면도" 팝업에 분석 결과를 채워 넣는 컨트롤러.
    ///
    /// 입면도는 경간·교각을 하나씩 그리는데 등급 산정은 부재 종류 단위로 이루어진다.
    /// 그 간극을 메우는 것이 이 클래스의 일이다.
    /// 사용자가 "교각7"처럼 번호를 붙여 입력했으면 그 번호의 칸에만 적용하고,
    /// 번호 없이 "교각"이라고만 했으면 매뉴얼상 그 종류 전체에 해당하므로 모든 교각에 적용한다.
    /// </summary>
    public class ElevationDiagramController : MonoBehaviour
    {
        [SerializeField] private UIElevationDiagram diagram;
        [SerializeField] private BridgeModelRegistry registry;   // 비워두면 싱글톤 인스턴스를 사용

        [Tooltip("세그먼트를 누르면 3D 카메라를 그 부재로 옮긴다. 필요 없으면 비워둔다")]
        [SerializeField] private BridgeViewerCameraController cameraController;

        [Tooltip("팝업 우측의 부재 상세 패널")]
        [SerializeField] private ElevationDetailView detailView;

        [Tooltip("팝업 좌측의 교량 개요 패널")]
        [SerializeField] private ElevationBridgeInfoView bridgeInfoView;

        [Tooltip("3D 모델에서 경간 길이를 재지 못했을 때 사용할 기본값(m)")]
        [SerializeField] private float defaultSpanLengthMeters = 30f;

        /// <summary>입면도에서 경간(상부)으로 묶이는 부재.</summary>
        private static readonly BridgeChecklistItem[] DeckItems =
        {
            BridgeChecklistItem.Girder,
            BridgeChecklistItem.Slab,
            BridgeChecklistItem.Parapet,
            BridgeChecklistItem.Pavement,
            BridgeChecklistItem.ExpansionJoint,
            BridgeChecklistItem.CrossBeam,
            BridgeChecklistItem.Drainage,
        };

        /// <summary>입면도에서 교각(하부)으로 묶이는 부재.</summary>
        private static readonly BridgeChecklistItem[] SubstructureItems =
        {
            BridgeChecklistItem.PierAbutment,
            BridgeChecklistItem.Bearing,
            BridgeChecklistItem.Foundation,
        };

        private const string SpanIdPrefix = "GirderSpan_";
        private const string PierIdPrefix = "Pier_";

        private BridgeModelRegistry Registry => registry != null ? registry : BridgeModelRegistry.Instance;

        private void OnEnable()
        {
            diagram.OnSegmentSelected += HandleSegmentSelected;

            if (AnalysisSessionManager.Instance != null)
            {
                AnalysisSessionManager.Instance.ReportChanged += Rebuild;

                // 이 팝업은 닫혀 있다가 열리는 구조라 구독만으로는 그동안의 결과를 놓친다.
                Rebuild(AnalysisSessionManager.Instance.LastReport);
            }
        }

        private void OnDisable()
        {
            diagram.OnSegmentSelected -= HandleSegmentSelected;

            if (AnalysisSessionManager.Instance != null)
                AnalysisSessionManager.Instance.ReportChanged -= Rebuild;
        }

        /// <summary>분석 결과로 입면도를 다시 구성한다.</summary>
        public void Rebuild(BridgeAssessmentReport report)
        {
            var modelRegistry = Registry;
            if (modelRegistry == null)
            {
                Debug.LogWarning("BridgeModelRegistry를 찾지 못해 입면도를 그릴 수 없습니다.");
                return;
            }

            var spanIndices = CollectIndices(modelRegistry, DeckItems);
            var pierIndices = CollectIndices(modelRegistry, SubstructureItems);

            var spanGrades = ResolveGrades(report, DeckItems, spanIndices);
            var pierGrades = ResolveGrades(report, SubstructureItems, pierIndices);

            int longAxis = modelRegistry.GetLongitudinalAxis();

            var spans = new List<ElevationSpanData>(spanIndices.Count);
            foreach (int index in spanIndices)
            {
                spans.Add(new ElevationSpanData
                {
                    id = SpanIdPrefix + index,
                    lengthMeters = MeasureSpanLength(modelRegistry, index, longAxis),
                    grade = spanGrades.TryGetValue(index, out string grade) ? grade : string.Empty,
                });
            }

            var piers = new List<ElevationPierData>(pierIndices.Count);
            foreach (int index in pierIndices)
            {
                piers.Add(new ElevationPierData
                {
                    id = PierIdPrefix + index,
                    label = "P" + index,
                    grade = pierGrades.TryGetValue(index, out string grade) ? grade : string.Empty,
                });
            }

            diagram.SetData(spans, piers);

            // 새 결과가 들어왔으므로 이전 선택으로 떠 있던 상세는 지운다.
            if (detailView != null)
                detailView.Clear();

            if (bridgeInfoView != null)
                bridgeInfoView.Refresh();
        }

        private static List<int> CollectIndices(BridgeModelRegistry modelRegistry, BridgeChecklistItem[] items)
        {
            var unique = new SortedSet<int>();

            foreach (var item in items)
                modelRegistry.CollectComponentIndices(item, unique);

            return new List<int>(unique);
        }

        /// <summary>
        /// 경간·교각별 등급을 정한다.
        ///
        /// 번호가 붙은 판정이 번호 없는 판정보다 우선한다. 구체적인 지목이 일괄 판정을 이긴다.
        /// 같은 우선순위 안에서 여러 판정이 겹치면 더 나쁜 등급을 택한다.
        /// 한 경간에 거더는 양호하고 난간은 불량이라면 그 경간은 불량으로 보는 것이 안전 판단에 맞다.
        /// </summary>
        private static Dictionary<int, string> ResolveGrades(
            BridgeAssessmentReport report, BridgeChecklistItem[] items, List<int> indices)
        {
            var result = new Dictionary<int, string>();
            if (report?.PerImage == null)
                return result;

            var numbered = new Dictionary<int, string>(); // 번호가 붙은 판정
            string general = null;                        // 번호 없는 판정들 중 가장 나쁜 것

            foreach (var image in report.PerImage)
            {
                if (!image.ChecklistItemResolved || Array.IndexOf(items, image.ChecklistItem) < 0)
                    continue;

                if (image.ComponentIndex > 0)
                {
                    numbered[image.ComponentIndex] = Worse(
                        numbered.TryGetValue(image.ComponentIndex, out string existing) ? existing : null,
                        image.DisplayGrade);
                }
                else
                {
                    general = Worse(general, image.DisplayGrade);
                }
            }

            foreach (int index in indices)
            {
                if (numbered.TryGetValue(index, out string grade))
                    result[index] = grade;
                else if (general != null)
                    result[index] = general;
                // 어느 쪽도 없으면 넣지 않는다. 입면도가 미평가(회색)로 그린다.
            }

            return result;
        }

        /// <summary>두 등급 중 더 나쁜 쪽을 돌려준다.</summary>
        private static string Worse(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;

            return SafetyGradeEvaluator.GradeToRank(b) < SafetyGradeEvaluator.GradeToRank(a) ? b : a;
        }

        /// <summary>3D 모델에서 해당 경간의 실제 길이를 잰다. 재지 못하면 기본값을 쓴다.</summary>
        private float MeasureSpanLength(BridgeModelRegistry modelRegistry, int index, int longAxis)
        {
            if (modelRegistry.TryGetIndexedBounds(BridgeChecklistItem.Girder, index, out Bounds bounds)
                || modelRegistry.TryGetIndexedBounds(BridgeChecklistItem.Slab, index, out bounds))
            {
                float length = bounds.size[longAxis];
                if (length > 0.01f)
                    return length;
            }

            return defaultSpanLengthMeters;
        }

        /// <summary>
        /// 입면도에서 칸을 누르면 그 부재의 상세를 우측 패널에 띄우고 3D 카메라도 옮긴다.
        /// </summary>
        private void HandleSegmentSelected(string segmentId)
        {
            if (string.IsNullOrEmpty(segmentId))
                return;

            if (!TryParseSegmentId(segmentId, out BridgeChecklistItem[] category, out BridgeChecklistItem focusItem, out int index))
                return;

            if (cameraController != null)
                cameraController.FocusOn(focusItem, index);

            if (detailView != null)
            {
                var result = FindResultForSegment(category, index);
                detailView.Show(result, BuildPartLabel(category, index));
            }
        }

        /// <summary>세그먼트 id를 부재 분류와 번호로 되돌린다.</summary>
        private static bool TryParseSegmentId(
            string segmentId, out BridgeChecklistItem[] category, out BridgeChecklistItem focusItem, out int index)
        {
            if (segmentId.StartsWith(SpanIdPrefix, StringComparison.Ordinal)
                && int.TryParse(segmentId.Substring(SpanIdPrefix.Length), out index))
            {
                category = DeckItems;
                focusItem = BridgeChecklistItem.Girder; // 경간을 대표하는 부재로 카메라를 맞춘다
                return true;
            }

            if (segmentId.StartsWith(PierIdPrefix, StringComparison.Ordinal)
                && int.TryParse(segmentId.Substring(PierIdPrefix.Length), out index))
            {
                category = SubstructureItems;
                focusItem = BridgeChecklistItem.PierAbutment;
                return true;
            }

            category = null;
            focusItem = default;
            index = 0;
            return false;
        }

        /// <summary>
        /// 그 칸에 해당하는 분석 결과를 찾는다.
        /// 등급을 정할 때와 같은 우선순위를 쓴다. 번호가 붙은 결과가 먼저이고,
        /// 없으면 번호 없이 그 분류 전체에 적용된 결과를 쓴다.
        /// 여러 개가 겹치면 등급이 가장 나쁜 것을 보여준다.
        /// </summary>
        private ImageAssessmentResult FindResultForSegment(BridgeChecklistItem[] category, int index)
        {
            var report = AnalysisSessionManager.Instance != null
                ? AnalysisSessionManager.Instance.LastReport
                : null;

            if (report?.PerImage == null)
                return null;

            ImageAssessmentResult numbered = null;
            ImageAssessmentResult general = null;

            foreach (var image in report.PerImage)
            {
                if (!image.ChecklistItemResolved || Array.IndexOf(category, image.ChecklistItem) < 0)
                    continue;

                if (image.ComponentIndex == index)
                    numbered = PickWorse(numbered, image);
                else if (image.ComponentIndex == 0)
                    general = PickWorse(general, image);
            }

            return numbered ?? general;
        }

        private static ImageAssessmentResult PickWorse(ImageAssessmentResult current, ImageAssessmentResult candidate)
        {
            if (current == null)
                return candidate;

            return SafetyGradeEvaluator.GradeToRank(candidate.DisplayGrade)
                   < SafetyGradeEvaluator.GradeToRank(current.DisplayGrade)
                ? candidate
                : current;
        }

        private static string BuildPartLabel(BridgeChecklistItem[] category, int index)
        {
            return category == SubstructureItems ? $"교각 {index}" : $"{index}경간";
        }
    }
}
