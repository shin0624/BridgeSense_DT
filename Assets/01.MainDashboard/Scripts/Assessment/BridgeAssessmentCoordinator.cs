using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BridgeSenseDT.Assessment
{
    /// <summary>평가에 넣을 이미지 1장분 입력. UI 타입(InputImageObject)에 의존하지 않도록 순수 데이터로 받는다.</summary>
    public struct ImageAnalysisInput
    {
        public string EntryId;                  // InputImageObject의 등록 순번
        public string CapturedPart;             // 사용자가 입력한 촬영 부재 문자열
        public Texture2D Thumbnail;             // 분석 대상 이미지 - 평가에는 쓰이지 않고 결과 카드에 그대로 실어 보내기 위한 값

        // 이미 추출이 끝난 결함 목록.
        // BridgeAnalysisResult(AI 원본 출력)가 아니라 추출 결과를 받는 이유는 입력 경로가 둘이기 때문이다.
        // 방금 추론한 경우에는 DefectExtractor.Extract로 만들어 넣고,
        // 저장본을 불러온 경우에는 파일에 기록돼 있던 결함 목록을 그대로 넣는다(AI 재실행 불필요).
        public List<DetectedDefect> Defects;
    }

    /// <summary>AnalyzeResultObject 1개에 표시할 이미지 단위 평가 결과.</summary>
    public class ImageAssessmentResult
    {
        public string EntryId;                    // ResultIdText 용
        public string CapturedPart;               // ResultTitleText 용
        public Texture2D Thumbnail;               // ResultObjectImage 용 (분석 대상 이미지)
        public BridgeChecklistItem ChecklistItem; // 해석된 체크리스트 항목
        public bool ChecklistItemResolved;        // 촬영부재 문자열을 해석하지 못했으면 false
        public int ComponentIndex;                // 사용자가 입력한 부재 번호(교각7 → 7). 번호를 적지 않았으면 0
        public char StateGrade;                   // 'a'~'e'
        public string DisplayGrade;               // ResultLevel 용 ("A"~"E")
        public float Confidence;                  // ResultConfidence 용 (0~1, 등급을 결정한 결함의 신뢰도)
        public string DefectSummary;              // ResultCrackText 용 ("콘크리트 균열 외 2건" 등)
        public ChecklistEvaluation Evaluation;    // 근거 문자열 등 상세 정보
    }

    /// <summary>이미지 단위 결과 + 교량 단위 종합 등급을 한 번에 담는 리포트.</summary>
    public class BridgeAssessmentReport
    {
        public List<ImageAssessmentResult> PerImage = new List<ImageAssessmentResult>();
        public BridgeAssessmentResult Bridge;
        public List<string> UnresolvedCapturedParts = new List<string>(); // 체크리스트 항목으로 해석 못 한 촬영부재 입력들
    }

    /// <summary>
    /// 등록된 이미지들의 AI 추론 결과를 받아 "이미지별 예상 등급"과 "교량 종합 안전등급"을 함께 산출한다.
    ///
    /// 이미지 단위와 교량 단위를 나눠 계산하는 이유:
    ///   - AnalyzeResultObject는 이미지 1장당 1개씩 생성되므로 이미지별 등급이 필요하다.
    ///   - 반면 매뉴얼의 안전등급은 "체크리스트 항목"당 1회 평가하는 구조라, 같은 부재를 찍은
    ///     사진이 여러 장이면 그 결함들을 합쳐서 항목 1개로 평가해야 산식이 맞는다.
    /// </summary>
    public static class BridgeAssessmentCoordinator
    {
        public static BridgeAssessmentReport Assess(IReadOnlyList<ImageAnalysisInput> inputs)
        {
            var report = new BridgeAssessmentReport();
            if (inputs == null || inputs.Count == 0)
            {
                report.Bridge = SafetyGradeEvaluator.EvaluateBridge(new List<ChecklistEvaluation>());
                return report;
            }

            // 체크리스트 항목별로 결함을 모으기 위한 버킷 (같은 부재를 찍은 사진이 여러 장일 수 있음)
            var defectsByItem = new Dictionary<BridgeChecklistItem, List<DetectedDefect>>();

            foreach (var input in inputs)
            {
                var defects = input.Defects ?? new List<DetectedDefect>();

                // 부재 번호까지 함께 해석한다. 번호는 등급 산정에는 쓰이지 않고
                // 3D 뷰어에서 해당 번호의 부재로 카메라를 옮길 때 사용한다.
                bool resolved = SafetyGradeEvaluator.TryParseCapturedPart(input.CapturedPart, out var item, out int componentIndex);

                // 이미지 단위 평가 (AnalyzeResultObject 표시용)
                // 해석 실패 시에도 화면에는 결과를 보여줘야 하므로, 등급 산정에는 항목 종류가
                // 영향을 주지 않는다는 점을 이용해 임시 항목으로 평가만 수행한다.
                var evaluation = SafetyGradeEvaluator.EvaluateChecklistItem(
                    resolved ? item : BridgeChecklistItem.Girder,
                    input.EntryId,
                    defects);

                // 등급을 결정한 결함(심각도 최대)을 기준으로 신뢰도·결함명을 함께 뽑는다.
                // 신뢰도가 가장 높은 결함과 심각도가 가장 높은 결함은 서로 다를 수 있는데,
                // 화면에 표시되는 "예상 등급"과 "예측 신뢰도"가 각각 다른 결함을 가리키면
                // 사용자가 두 값을 연결해서 읽을 수 없으므로 기준을 하나로 통일한다.
                DetectedDefect gradeDriver = evaluation.defects.Count > 0
                    ? evaluation.defects.OrderByDescending(SafetyGradeEvaluator.EvaluateDefectSeverity).First()
                    : null;

                report.PerImage.Add(new ImageAssessmentResult
                {
                    EntryId = input.EntryId,
                    CapturedPart = input.CapturedPart,
                    Thumbnail = input.Thumbnail,
                    ChecklistItem = item,
                    ChecklistItemResolved = resolved,
                    ComponentIndex = componentIndex,
                    StateGrade = evaluation.stateGrade,
                    DisplayGrade = StateGradeToDisplayGrade(evaluation.stateGrade),
                    Confidence = gradeDriver?.confidence ?? 0f,
                    DefectSummary = BuildDefectSummary(gradeDriver, evaluation.defects.Count),
                    Evaluation = evaluation,
                });

                // 교량 단위 집계용 누적
                if (!resolved)
                {
                    // 어느 부재인지 모르는 사진을 임의 항목(특히 주요시설)에 넣으면 가중치가 왜곡되므로
                    // 종합 등급 산정에서는 제외하고, 대신 해석 실패 사실을 리포트에 남긴다.
                    report.UnresolvedCapturedParts.Add(input.CapturedPart);
                    continue;
                }

                if (!defectsByItem.TryGetValue(item, out var bucket))
                {
                    bucket = new List<DetectedDefect>();
                    defectsByItem[item] = bucket;
                }
                bucket.AddRange(defects);
            }

            // 교량 단위 종합 평가
            var evaluations = defectsByItem
                .Select(pair => SafetyGradeEvaluator.EvaluateChecklistItem(
                    pair.Key,
                    SafetyGradeEvaluator.GetChecklistItemName(pair.Key),
                    pair.Value))
                .ToList();

            report.Bridge = SafetyGradeEvaluator.EvaluateBridge(evaluations);
            return report;
        }

        /// <summary>
        /// ResultCrackText에 표시할 결함 요약 문자열을 만든다.
        /// 등급을 결정한 결함 이름을 앞세우고, 그 외 결함이 더 있으면 건수만 덧붙인다.
        /// </summary>
        static string BuildDefectSummary(DetectedDefect gradeDriver, int totalCount)
        {
            if (gradeDriver == null) return "미검출";

            string name = SafetyGradeEvaluator.GetDefectName(gradeDriver.type);
            return totalCount > 1 ? $"{name} 외 {totalCount - 1}건" : name;
        }

        /// <summary>
        /// 체크리스트 항목의 소문자 상태등급(a~e)을 화면 표시용 대문자 등급(A~E)으로 변환한다.
        /// 매뉴얼상 항목별 상태등급(a~e)과 시설물 종합 안전등급(A~E)은 별개 개념이지만,
        /// 이미지 1장은 항목 1개에 대응하므로 사용자에게는 같은 척도로 보여준다.
        /// </summary>
        public static string StateGradeToDisplayGrade(char stateGrade)
        {
            switch (char.ToLower(stateGrade))
            {
                case 'a': return "A";
                case 'b': return "B";
                case 'c': return "C";
                case 'd': return "D";
                case 'e': return "E";
                default: return "-";
            }
        }
    }
}
