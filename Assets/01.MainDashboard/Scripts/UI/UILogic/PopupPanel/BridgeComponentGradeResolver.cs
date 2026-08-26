using System;
using System.Collections.Generic;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.Bridge3D;

namespace BridgeSenseDT.UI
{
    /// <summary>경간·교각별 등급 산출 결과.</summary>
    public class ComponentGradeMap
    {
        public List<int> SpanIndices = new List<int>();
        public List<int> PierIndices = new List<int>();

        // 등급이 정해진 것만 담는다. 여기에 없는 번호는 촬영되지 않아 판정할 수 없는 부재다.
        public Dictionary<int, string> SpanGrades = new Dictionary<int, string>();
        public Dictionary<int, string> PierGrades = new Dictionary<int, string>();

        /// <summary>3D 모델에 존재하는 전체 부재 수(경간 + 교각).</summary>
        public int TotalComponentCount => SpanIndices.Count + PierIndices.Count;

        /// <summary>등급이 매겨진 부재 수. 촬영되지 않은 부재는 제외된다.</summary>
        public int GradedComponentCount => SpanGrades.Count + PierGrades.Count;

        /// <summary>등급이 매겨진 모든 부재의 등급을 차례로 돌려준다.</summary>
        public IEnumerable<string> EnumerateGrades()
        {
            foreach (var grade in SpanGrades.Values)
                yield return grade;

            foreach (var grade in PierGrades.Values)
                yield return grade;
        }
    }

    /// <summary>
    /// 분석 결과를 경간·교각 단위 등급으로 환산한다.
    ///
    /// 입면도와 등급 분포 팝업이 같은 숫자를 보여줘야 하므로 산출 규칙을 이 한 곳에 둔다.
    /// 각자 계산하면 한쪽만 고쳐졌을 때 두 화면이 서로 다른 값을 말하게 된다.
    /// </summary>
    public static class BridgeComponentGradeResolver
    {
        /// <summary>경간(상부)으로 묶이는 부재.</summary>
        public static readonly BridgeChecklistItem[] DeckItems =
        {
            BridgeChecklistItem.Girder,
            BridgeChecklistItem.Slab,
            BridgeChecklistItem.Parapet,
            BridgeChecklistItem.Pavement,
            BridgeChecklistItem.ExpansionJoint,
            BridgeChecklistItem.CrossBeam,
            BridgeChecklistItem.Drainage,
        };

        /// <summary>교각(하부)으로 묶이는 부재.</summary>
        public static readonly BridgeChecklistItem[] SubstructureItems =
        {
            BridgeChecklistItem.PierAbutment,
            BridgeChecklistItem.Bearing,
            BridgeChecklistItem.Foundation,
        };

        public static ComponentGradeMap Resolve(BridgeAssessmentReport report, BridgeModelRegistry registry)
        {
            var map = new ComponentGradeMap();
            if (registry == null)
                return map;

            map.SpanIndices = CollectIndices(registry, DeckItems);
            map.PierIndices = CollectIndices(registry, SubstructureItems);

            map.SpanGrades = ResolveGrades(report, DeckItems, map.SpanIndices);
            map.PierGrades = ResolveGrades(report, SubstructureItems, map.PierIndices);

            return map;
        }

        public static List<int> CollectIndices(BridgeModelRegistry registry, BridgeChecklistItem[] items)
        {
            var unique = new SortedSet<int>();

            foreach (var item in items)
                registry.CollectComponentIndices(item, unique);

            return new List<int>(unique);
        }

        /// <summary>
        /// 경간·교각별 등급을 정한다.
        ///
        /// 번호가 붙은 판정이 번호 없는 판정보다 우선한다. 구체적인 지목이 일괄 판정을 이긴다.
        /// 같은 우선순위 안에서 여러 판정이 겹치면 더 나쁜 등급을 택한다.
        /// 한 경간에 거더는 양호하고 난간은 불량이라면 그 경간은 불량으로 보는 것이 안전 판단에 맞다.
        /// </summary>
        public static Dictionary<int, string> ResolveGrades(
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
                // 어느 쪽도 없으면 넣지 않는다. 촬영되지 않아 판정할 수 없는 부재다.
            }

            return result;
        }

        /// <summary>두 등급 중 더 나쁜 쪽을 돌려준다.</summary>
        public static string Worse(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;

            return SafetyGradeEvaluator.GradeToRank(b) < SafetyGradeEvaluator.GradeToRank(a) ? b : a;
        }
    }
}
