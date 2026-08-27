using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BridgeSenseDT.Assessment
{
    /// <summary>
    /// 결함 유형. RT-DETR 클래스 id(0~8)와 정수값이 정확히 일치하도록 선언
    /// (convert_to_coco.py의 클래스 순서 기준).
    /// </summary>
    public enum DefectType
    {
        ConcreteCrack = 0,  // 콘크리트_균열 (co)
        Efflorescence = 1,  // 백태 (ef)
        Leakage = 2,        // 누수 (le)
        Spalling = 3,       // 박락 (sp)
        ExposedRebar = 4,   // 철근_노출 (ex)
        SteelCorrosion = 5, // 강재_부식 (st)
        PaintPeeling = 6,   // 도장_박리 (pa)
        AsphaltCrack = 7,   // 아스팔트_균열 (as)
        Subsidence = 8,     // 함몰 (po)
    }

    /// <summary>
    /// 시설영역 구분. 「제3종시설물 안전등급 평가 매뉴얼」(2023.12) 표 2-3 가중치 기준.
    /// 주요시설 60 / 일반시설 20 / 부대시설 20.
    /// </summary>
    public enum FacilityArea
    {
        Major = 0,    // 주요시설 (가중치 60)
        General = 1,  // 일반시설 (가중치 20)
        Ancillary = 2 // 부대시설 (가중치 20)
    }

    /// <summary>
    /// 교량·육교 체크리스트 항목. 매뉴얼 표 2-4 및 표 3-2 기준.
    /// 각 항목이 어느 시설영역에 속하는지가 가중치 계산의 핵심이다.
    /// </summary>
    public enum BridgeChecklistItem
    {
        // 주요시설 (표 3-2)
        Girder,          // 거더(RC 및 PSC/강재 등)의 균열 및 손상 상태
        Slab,            // 바닥판(RC 및 PSC/강재 등)의 균열 및 손상 상태
        CableAnchorage,  // 케이블 부재, 정착부(정착구) 및 주변 손상 상태
        Bearing,         // 교량받침(교좌장치) 및 주변 손상 상태
        PierAbutment,    // 교각(주탑 포함) 및 교대(날개벽 포함)의 균열 및 손상 상태
        Foundation,      // 기초의 세굴·침하 및 손상 상태

        // 일반시설 (표 2-4)
        Pavement,        // 교면포장, 바포장면
        ExpansionJoint,  // 신축이음
        CrossBeam,       // 가로보, 세로보
        Drainage,        // 배수시설
        Parapet,         // 난간(방호벽, 방호울타리) 및 연석

        // 부대시설 (표 2-4)
        RetainingWall,   // 옹벽, 축대, 석축
        Slope,           // 비탈면
        SafetyFacility,  // 안전 및 기타시설(방음벽 등)
        InspectionPath,  // 점검로 등
    }

    /// <summary>RT-DETR 추론 결과를 평가용으로 환산한 결함 1건.</summary>
    public class DetectedDefect
    {
        public DefectType type;
        public float confidence;             // 검출 신뢰도 (0~1)
        public float estimatedWidthMm = -1f; // 스케일 정보 있을 때만. -1이면 미상
        public bool isStructurallyCritical;  // 단면손실 직결 결함(철근노출·강재부식) 여부

        // 결함이 사진의 어디에 있는지 가리키는 사각형들(정규화 좌표, RT-DETR 검출 박스).
        public List<DefectBox> boxes = new List<DefectBox>();
    }

    /// <summary>체크리스트 항목 1개의 평가 결과.</summary>
    public class ChecklistEvaluation
    {
        public BridgeChecklistItem item;
        public string componentId;           // 3D 부재에 매핑되는 ID (예: "Pier_6")
        public char stateGrade;              // 'a'~'e', 또는 '-' (해당없음)
        public int score;                    // 10/8/5/2/0, 해당없음이면 -1
        public List<DetectedDefect> defects = new List<DetectedDefect>();
        public string rationale;
        public bool NotApplicable => stateGrade == '-';
    }

    public class BridgeAssessmentResult
    {
        public float majorScore;       // X: 주요시설 상태점수
        public float generalScore;     // Y: 일반시설 상태점수
        public float ancillaryScore;   // Z: 부대시설 상태점수
        public float totalScore;       // 종합 상태점수 (0~10)
        public string grade;           // "A"~"E"
        public bool forcedDowngrade;   // 중대 결함으로 강제 하향되었는지
        public string rationale;
        public List<ChecklistEvaluation> evaluations = new List<ChecklistEvaluation>();
    }

    /// <summary>
    /// 「제3종시설물 안전등급 평가 매뉴얼」(국토안전관리원, 2023.12)의 산식을 구현한다.
    ///
    /// 산정 절차
    ///   1. 체크리스트 항목별로 a~e 상태 판정 → 표 2-5 점수 부여 (a=10, b=8, c=5, d=2, e=0)
    ///   2. 시설영역별 상태점수 = 해당 영역 항목 점수의 평균 ('해당없음'은 분모에서 제외)
    ///   3. 종합 상태점수 = (X×60 + Y×20 + Z×20) / 100   ← 표 2-3 가중치
    ///      (특정 영역이 통째로 없으면 그 가중치를 나머지 영역에 비율대로 배분)
    ///   4. 표 2-2 범위로 등급 결정 (A≥9, B≥7, C≥5, D≥3, E&lt;3)
    ///   5. 중대 손상 존재 시 D 또는 E로 강제 조정
    ///
    /// ※ 이 산출값의 성격
    /// 매뉴얼의 a~e 판정은 책임기술자가 육안조사로 수행한다. 이 엔진은 그 판정을 AI 추론
    /// 결과로 근사한 **보조 지표(Estimated Grade)**이며 법정 안전등급을 대체하지 않는다.
    /// 특히 균열폭(mm)은 사진만으로는 실측 불가하므로(촬영거리·GSD 정보 없음),
    /// estimatedWidthMm가 주어진 경우에만 폭 기준을 적용하고 아니면 면적률로 대체 판정한다.
    /// </summary>
    public static class SafetyGradeEvaluator
    {
        // 표 2-5: 체크리스트 평가항목의 점수 기준
        public static int StateToScore(char state)
        {
            switch (char.ToLower(state))
            {
                case 'a': return 10;
                case 'b': return 8;
                case 'c': return 5;
                case 'd': return 2;
                case 'e': return 0;
                default: return -1; // 해당없음
            }
        }

        // 표 2-3: 시설영역별 가중치
        public const float WeightMajor = 60f;
        public const float WeightGeneral = 20f;
        public const float WeightAncillary = 20f;

        /// <summary>표 2-4 기준: 체크리스트 항목 → 시설영역 매핑.</summary>
        public static FacilityArea GetFacilityArea(BridgeChecklistItem item)
        {
            switch (item)
            {
                case BridgeChecklistItem.Girder:
                case BridgeChecklistItem.Slab:
                case BridgeChecklistItem.CableAnchorage:
                case BridgeChecklistItem.Bearing:
                case BridgeChecklistItem.PierAbutment:
                case BridgeChecklistItem.Foundation:
                    return FacilityArea.Major;

                case BridgeChecklistItem.Pavement:
                case BridgeChecklistItem.ExpansionJoint:
                case BridgeChecklistItem.CrossBeam:
                case BridgeChecklistItem.Drainage:
                case BridgeChecklistItem.Parapet:
                    return FacilityArea.General;

                default:
                    return FacilityArea.Ancillary;
            }
        }

        /// <summary>
        /// 결함으로 인정할 최소 신뢰도.
        /// 원안 pseudo code는 0.5였지만, 실제 파인튜닝된 rtdetr.onnx의 score 분포를
        /// 학습에 쓰이지 않은 실사진 60장(Assets/06.AI/TestImages)으로 실측한 결과
        /// 0.5를 넘는 검출이 단 1건도 없었다(대부분 0.10~0.27).
        /// 0.5를 유지하면 모든 항목이 '결함 없음 → a(10점)'으로 판정되어 항상 A등급이 나온다.
        /// AiInferenceManager.rtdetrScoreThreshold와 같은 수준으로 맞춰둔다.
        /// ※ 모델을 재학습해 score 분포가 올라가면 이 값도 함께 올릴 것.
        /// </summary>
        public const float MinConfidence = 0.1f;

        // 결함 유형별 기본 위험도. 단면손실로 직결되는 철근노출·강재부식이 가장 위험하고,
        // 표면 열화(백태·도장박리)나 비구조부재 손상(아스팔트 균열)은 낮게 잡는다.
        static readonly Dictionary<DefectType, float> DefectSeverityWeight = new Dictionary<DefectType, float>
        {
            { DefectType.ExposedRebar,   1.00f }, // 철근 노출 = 피복 상실, 부식 급진행
            { DefectType.SteelCorrosion, 1.00f }, // 강재 부식 = 단면 감소
            { DefectType.Spalling,       0.90f }, // 박락 = 철근 노출 전 단계
            { DefectType.Subsidence,     0.85f }, // 함몰
            { DefectType.ConcreteCrack,  0.70f }, // 콘크리트 균열(구조체)
            { DefectType.AsphaltCrack,   0.55f }, // 아스팔트 균열(포장층, 구조체 아님)
            { DefectType.Leakage,        0.50f }, // 누수
            { DefectType.PaintPeeling,   0.40f }, // 도장 박리 = 방식기능 저하, 부식 전조
            { DefectType.Efflorescence,  0.35f }, // 백태
        };

        /// <summary>
        /// 해당 결함이 구조적 중대 손상(단면손실 직결)인지 판정한다.
        /// AI 모델은 "절단균열인가"를 알려주지 않으므로 결함 유형에서 파생시킨다.
        /// </summary>
        public static bool IsStructurallyCriticalType(DefectType type)
        {
            return type == DefectType.ExposedRebar || type == DefectType.SteelCorrosion;
        }

        /// <summary>
        /// 1단계: AI 추론 결과로부터 체크리스트 항목의 a~e 상태를 판정한다.
        /// (매뉴얼의 책임기술자 육안 판정에 해당하는 부분을 AI로 근사)
        /// </summary>
        public static ChecklistEvaluation EvaluateChecklistItem(
            BridgeChecklistItem item, string componentId, List<DetectedDefect> defects)
        {
            var valid = defects?.Where(d => d.confidence >= MinConfidence).ToList()
                        ?? new List<DetectedDefect>();

            var result = new ChecklistEvaluation
            {
                item = item,
                componentId = componentId,
                defects = valid,
            };

            if (valid.Count == 0)
            {
                result.stateGrade = 'a';
                result.score = 10;
                result.rationale = "검출된 결함 없음 → 안전상 문제가 없는 상태";
                return result;
            }

            // 중대 손상이 있으면 즉시 d 이하로 판정 (매뉴얼 2.2 유의사항)
            if (valid.Any(d => d.isStructurallyCritical))
            {
                // 면적 정보가 없으므로 검출 신뢰도가 매우 높은 경우를 "뚜렷한 손상"으로 본다.
                bool severe = valid.Any(d => d.isStructurallyCritical && d.confidence > 0.5f);
                result.stateGrade = severe ? 'e' : 'd';
                result.score = StateToScore(result.stateGrade);
                result.rationale = "구조안전성에 영향을 미치는 중대 손상 검출 → 긴급 조치 검토 필요";
                return result;
            }

            float severity = valid.Max(EvaluateDefectSeverity);

            // 심각도(0~1) → a~e 상태 판정
            if (severity < 0.10f) result.stateGrade = 'a';
            else if (severity < 0.30f) result.stateGrade = 'b';
            else if (severity < 0.55f) result.stateGrade = 'c';
            else if (severity < 0.80f) result.stateGrade = 'd';
            else result.stateGrade = 'e';

            result.score = StateToScore(result.stateGrade);

            var worst = valid.OrderByDescending(EvaluateDefectSeverity).First();
            result.rationale = BuildRationale(worst, valid.Count);
            return result;
        }

        /// <summary>결함 1건의 심각도(0~1).</summary>
        public static float EvaluateDefectSeverity(DetectedDefect d)
        {
            float baseWeight = DefectSeverityWeight.TryGetValue(d.type, out var w) ? w : 0.5f;

            bool isCrack = d.type == DefectType.ConcreteCrack || d.type == DefectType.AsphaltCrack;

            float magnitude;
            if (d.estimatedWidthMm >= 0f && isCrack)
            {
                // 균열폭 기준 (0.1mm 미만은 헤어크랙 수준)
                if (d.estimatedWidthMm < 0.1f) magnitude = 0.15f;
                else if (d.estimatedWidthMm < 0.2f) magnitude = 0.35f;
                else if (d.estimatedWidthMm < 0.3f) magnitude = 0.60f;
                else if (d.estimatedWidthMm < 0.5f) magnitude = 0.80f;
                else magnitude = 1.00f;
            }
            else
            {
                // 스케일·면적 정보가 없으므로 검출 신뢰도 자체를 크기 대체 지표로도 함께 쓴다.
                // confidence를 magnitude에도 반영하고 아래서 다시 곱하므로 제곱 형태가 되어,
                // 신뢰도가 낮은 검출은 심각도가 더 완만하게 낮아진다.
                magnitude = Mathf.Clamp01(d.confidence);
            }

            return Mathf.Clamp01(baseWeight * magnitude * d.confidence);
        }

        /// <summary>
        /// 2~5단계: 체크리스트 전체를 종합해 교량 안전등급을 산정한다.
        /// 매뉴얼 2.2.3의 산식(가중치 배분 포함)을 그대로 따른다.
        /// </summary>
        public static BridgeAssessmentResult EvaluateBridge(List<ChecklistEvaluation> evaluations)
        {
            var result = new BridgeAssessmentResult { evaluations = evaluations };

            if (evaluations == null || evaluations.Count == 0)
            {
                result.totalScore = 10f;
                result.grade = "A";
                result.rationale = "평가된 항목 없음";
                return result;
            }

            // 영역별 평균 ('해당없음' 제외)
            float? x = AreaAverage(evaluations, FacilityArea.Major);
            float? y = AreaAverage(evaluations, FacilityArea.General);
            float? z = AreaAverage(evaluations, FacilityArea.Ancillary);

            result.majorScore = x ?? 0f;
            result.generalScore = y ?? 0f;
            result.ancillaryScore = z ?? 0f;

            // 존재하는 영역들만 모아 가중치를 정규화(= 없는 영역의 가중치를 비율대로 배분)
            float wSum = 0f, scoreSum = 0f;
            if (x.HasValue) { wSum += WeightMajor; scoreSum += x.Value * WeightMajor; }
            if (y.HasValue) { wSum += WeightGeneral; scoreSum += y.Value * WeightGeneral; }
            if (z.HasValue) { wSum += WeightAncillary; scoreSum += z.Value * WeightAncillary; }

            result.totalScore = wSum > 0f ? scoreSum / wSum : 10f;
            result.grade = ScoreToGrade(result.totalScore);

            // 매뉴얼 2.2 유의사항: 중대 손상 시 D 또는 E로 강제 조정
            var criticalItems = evaluations
                .Where(e => !e.NotApplicable
                            && GetFacilityArea(e.item) == FacilityArea.Major
                            && (e.stateGrade == 'd' || e.stateGrade == 'e'))
                .ToList();

            if (criticalItems.Count > 0)
            {
                string forced = criticalItems.Any(e => e.stateGrade == 'e') ? "E" : "D";
                if (GradeToRank(result.grade) > GradeToRank(forced))
                {
                    result.grade = forced;
                    result.forcedDowngrade = true;
                }

                result.rationale =
                    $"주요시설 {criticalItems.Count}개 항목에서 {forced}등급 상당 결함 검출 " +
                    $"({string.Join(", ", criticalItems.Take(3).Select(e => e.componentId ?? GetChecklistItemName(e.item)))}" +
                    $"{(criticalItems.Count > 3 ? " 외" : "")}) → 긴급 보수·보강 및 사용제한 검토 필요";
            }
            else
            {
                result.rationale =
                    $"종합 상태점수 {result.totalScore:F2}점 " +
                    $"(주요 {result.majorScore:F1} / 일반 {result.generalScore:F1} / 부대 {result.ancillaryScore:F1})";
            }

            return result;
        }

        static float? AreaAverage(List<ChecklistEvaluation> evaluations, FacilityArea area)
        {
            var items = evaluations
                .Where(e => GetFacilityArea(e.item) == area && !e.NotApplicable)
                .ToList();
            if (items.Count == 0) return null; // 해당 영역 자체가 없음 → 가중치 배분 대상
            return (float)items.Sum(e => e.score) / items.Count;
        }

        /// <summary>표 2-2: 종합 상태점수 → 안전등급.</summary>
        public static string ScoreToGrade(float total)
        {
            if (total >= 9f) return "A";
            if (total >= 7f) return "B";
            if (total >= 5f) return "C";
            if (total >= 3f) return "D";
            return "E";
        }

        /// <summary>
        /// 등급을 크기 비교가 가능한 값으로 바꾼다. A가 가장 크고 E가 가장 작다.
        /// 한 부재에 여러 판정이 겹칠 때 더 나쁜 쪽을 고르는 데 쓴다.
        /// </summary>
        public static int GradeToRank(string grade)
        {
            switch (grade) { case "A": return 5; case "B": return 4; case "C": return 3; case "D": return 2; default: return 1; }
        }

        static string BuildRationale(DetectedDefect worst, int totalCount)
        {
            string typeName = GetDefectName(worst.type);
            string scale = worst.estimatedWidthMm >= 0f
                ? $"폭 약 {worst.estimatedWidthMm:F2}mm, "
                : "";
            string suffix = totalCount > 1 ? $" 외 결함 {totalCount - 1}건" : "";
            return $"{typeName} 검출 ({scale}신뢰도 {worst.confidence * 100f:F0}%){suffix}";
        }

        public static string GetDefectName(DefectType type)
        {
            switch (type)
            {
                case DefectType.ConcreteCrack: return "콘크리트 균열";
                case DefectType.Efflorescence: return "백태";
                case DefectType.Leakage: return "누수";
                case DefectType.Spalling: return "박락";
                case DefectType.ExposedRebar: return "철근 노출";
                case DefectType.SteelCorrosion: return "강재 부식";
                case DefectType.PaintPeeling: return "도장 박리";
                case DefectType.AsphaltCrack: return "아스팔트 균열";
                case DefectType.Subsidence: return "함몰";
                default: return "기타";
            }
        }

        /// <summary>
        /// 촬영 부재 입력을 부재 종류와 부재 번호로 함께 해석한다.
        ///
        /// 안전점검 실무에서는 지침에 따라 도면·보고서상 모든 부재에 고유 번호를 부여하므로
        /// 사용자가 "교각7", "교각 7", "교각 7번"처럼 번호를 붙여 입력할 수 있다.
        /// 번호를 적지 않으면 componentIndex가 0이 되고, 그때는 종류만으로 처리한다.
        /// </summary>
        public static bool TryParseCapturedPart(string capturedPart, out BridgeChecklistItem item, out int componentIndex)
        {
            componentIndex = 0;

            if (!TryParseChecklistItem(capturedPart, out item))
                return false;

            componentIndex = ExtractComponentIndex(capturedPart);
            return true;
        }

        /// <summary>문자열에서 처음 나타나는 정수를 부재 번호로 뽑는다. 없으면 0.</summary>
        public static int ExtractComponentIndex(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var match = System.Text.RegularExpressions.Regex.Match(text, @"\d+");
            return match.Success && int.TryParse(match.Value, out int parsed) ? parsed : 0;
        }

        public static string GetChecklistItemName(BridgeChecklistItem item)
        {
            switch (item)
            {
                case BridgeChecklistItem.Girder: return "거더(주형)";
                case BridgeChecklistItem.Slab: return "바닥판(슬래브)";
                case BridgeChecklistItem.CableAnchorage: return "케이블·정착부";
                case BridgeChecklistItem.Bearing: return "교량받침";
                case BridgeChecklistItem.PierAbutment: return "교각·교대";
                case BridgeChecklistItem.Foundation: return "기초";
                case BridgeChecklistItem.Pavement: return "교면포장";
                case BridgeChecklistItem.ExpansionJoint: return "신축이음";
                case BridgeChecklistItem.CrossBeam: return "가로보·세로보";
                case BridgeChecklistItem.Drainage: return "배수시설";
                case BridgeChecklistItem.Parapet: return "난간·방호벽";
                case BridgeChecklistItem.RetainingWall: return "옹벽·축대";
                case BridgeChecklistItem.Slope: return "비탈면";
                case BridgeChecklistItem.SafetyFacility: return "안전 및 기타시설";
                default: return "점검로";
            }
        }

        /// <summary>
        /// 사용자가 "촬영 부재"에 자유 입력한 문자열을 체크리스트 항목으로 해석한다.
        /// 자유 입력이라 확정적으로 매칭할 수 없으므로, 못 알아들으면 false를 반환한다.
        /// ※ 장기적으로는 InputAndAnalyzePanel의 촬영부재 입력을 TMP_Dropdown으로 바꿔서
        ///    이 추측 단계 자체를 없애는 편이 정확하다.
        /// </summary>
        public static bool TryParseChecklistItem(string capturedPart, out BridgeChecklistItem item)
        {
            item = BridgeChecklistItem.Girder;
            if (string.IsNullOrWhiteSpace(capturedPart)) return false;

            string s = capturedPart.Replace(" ", "");

            if (s.Contains("거더") || s.Contains("주형")) { item = BridgeChecklistItem.Girder; return true; }
            if (s.Contains("바닥판") || s.Contains("슬래브")) { item = BridgeChecklistItem.Slab; return true; }
            if (s.Contains("케이블") || s.Contains("정착")) { item = BridgeChecklistItem.CableAnchorage; return true; }
            if (s.Contains("받침") || s.Contains("교좌")) { item = BridgeChecklistItem.Bearing; return true; }
            if (s.Contains("교각") || s.Contains("교대") || s.Contains("주탑")) { item = BridgeChecklistItem.PierAbutment; return true; }
            if (s.Contains("기초")) { item = BridgeChecklistItem.Foundation; return true; }
            if (s.Contains("포장") || s.Contains("교면")) { item = BridgeChecklistItem.Pavement; return true; }
            if (s.Contains("신축이음")) { item = BridgeChecklistItem.ExpansionJoint; return true; }
            if (s.Contains("가로보") || s.Contains("세로보")) { item = BridgeChecklistItem.CrossBeam; return true; }
            if (s.Contains("배수")) { item = BridgeChecklistItem.Drainage; return true; }
            if (s.Contains("난간") || s.Contains("방호") || s.Contains("연석")) { item = BridgeChecklistItem.Parapet; return true; }
            if (s.Contains("옹벽") || s.Contains("축대") || s.Contains("석축")) { item = BridgeChecklistItem.RetainingWall; return true; }
            if (s.Contains("비탈") || s.Contains("사면")) { item = BridgeChecklistItem.Slope; return true; }
            if (s.Contains("방음") || s.Contains("안전")) { item = BridgeChecklistItem.SafetyFacility; return true; }
            if (s.Contains("점검로")) { item = BridgeChecklistItem.InspectionPath; return true; }

            return false;
        }
    }
}
