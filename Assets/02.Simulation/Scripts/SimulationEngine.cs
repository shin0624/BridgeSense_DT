using System.Collections.Generic;
using System.Linq;
using BridgeSenseDT.Assessment;
using UnityEngine;

namespace BridgeSenseDT.Simulation
{
    /// <summary>시뮬레이션 1회 실행에 필요한 입력값 묶음.</summary>
    public struct SimulationInput
    {
        public float Years;                  // 기준 시점으로부터 경과 연수
        public EnvironmentPreset Environment; // 환경 프리셋
        public float CoverDepthMm;            // 피복두께(mm). 0 이하면 AgingModel.DefaultCoverDepthMm 사용
    }

    /// <summary>
    /// 노후화 계수(f_age)를 기존 결함 목록(또는 빈 상태)에 적용해 가상의 BridgeAssessmentReport를 만든다.
    ///
    /// 핵심 설계: 결함 유형·부재를 새로 정의하지 않고, SafetyGradeEvaluator.EvaluateChecklistItem/
    /// EvaluateBridge를 그대로 재사용한다. 그래서 결과 리포트는 실제 AI 분석 리포트와 완전히 같은
    /// 타입이라 BridgeGradeVisualizer.Apply()에 그대로 넘겨 기존 3D 시각화 파이프라인을 재사용할 수 있다.
    /// </summary>
    public static class SimulationEngine
    {
        // FromDesignSpec 모드에서 상태 계수가 이 값 밑으로 떨어지면 콘크리트 균열을 가상으로 주입한다.
        // 콘크리트 균열은 9개 결함 유형 중 가장 흔하고 심각도 가중치가 중간(0.70)이라 "전형적인 노후화"를
        // 대표하기에 적합하다. 실제 결함 종류를 예측하는 것이 아니라 등급 추이를 보여주기 위한 장치다.
        private const float DesignSpecOnsetConditionFactor = 0.85f;

        /// <summary>
        /// AI 실측 결과 기준 시뮬레이션. 기존 결함들의 심각도를 f_age(악화 배율)로 밀어올린다.
        /// f_age = 1 / conditionFactor : 상태 계수가 낮을수록(더 노후화될수록) 결함이 더 심하게 반영된다.
        /// </summary>
        public static BridgeAssessmentReport SimulateFromAnalysis(BridgeAssessmentReport baseline, SimulationInput input)
        {
            float conditionFactor = ComputeConditionFactor(input);
            float ageFactor = ComputeAgeFactor(conditionFactor);

            if (baseline?.Bridge?.evaluations == null || baseline.Bridge.evaluations.Count == 0)
                return SimulateFromDesignSpec(input); // 실측 결과가 비어 있으면 설계기준 모드로 대체

            var evaluations = new List<ChecklistEvaluation>();

            foreach (var srcEval in baseline.Bridge.evaluations)
            {
                if (srcEval.NotApplicable)
                {
                    evaluations.Add(srcEval);
                    continue;
                }

                var agedDefects = srcEval.defects.Select(d => AgeDefect(d, ageFactor)).ToList();

                // 실측 결함이 전혀 없던 항목도 노후화가 진행되면 새 결함이 생길 수 있다.
                // 상태 계수가 충분히 낮아지면(주입 임계값 이하) 대표 결함(콘크리트 균열)을 약하게 주입한다.
                if (agedDefects.Count == 0 && conditionFactor < DesignSpecOnsetConditionFactor)
                {
                    agedDefects.Add(BuildInjectedDefect(conditionFactor));
                }

                evaluations.Add(SafetyGradeEvaluator.EvaluateChecklistItem(srcEval.item, srcEval.componentId, agedDefects));
            }

            var report = new BridgeAssessmentReport
            {
                Bridge = SafetyGradeEvaluator.EvaluateBridge(evaluations),
                PerImage = baseline.PerImage, // 이미지별 카드는 시뮬레이션 대상이 아니므로 원본 그대로 참고용 보존
            };
            return report;
        }

        /// <summary>
        /// 준공년도 기준 시뮬레이션. 결함이 전혀 없는 상태(A등급)에서 출발해,
        /// 노후화 계수가 임계값 밑으로 떨어지면 대표 결함을 주입해 등급 하락을 보여준다.
        /// BridgeSpec.year만 있으면 항상 실행 가능하다(실측 결과 불필요).
        /// </summary>
        public static BridgeAssessmentReport SimulateFromDesignSpec(SimulationInput input)
        {
            float conditionFactor = ComputeConditionFactor(input);

            var evaluations = new List<ChecklistEvaluation>();

            // 주요시설 항목 전체에 동일한 가상 결함을 반영한다.
            // 실제 부재별 편차(교각과 거더가 다르게 낡는 등)까지 반영하려면 부재별 개별 계수가 필요하지만,
            // 이 모드는 "제원만으로도 대략적인 추이를 보여준다"가 목적이므로 균일하게 적용한다.
            foreach (BridgeChecklistItem item in System.Enum.GetValues(typeof(BridgeChecklistItem)))
            {
                var defects = conditionFactor < DesignSpecOnsetConditionFactor
                    ? new List<DetectedDefect> { BuildInjectedDefect(conditionFactor) }
                    : new List<DetectedDefect>();

                evaluations.Add(SafetyGradeEvaluator.EvaluateChecklistItem(
                    item, SafetyGradeEvaluator.GetChecklistItemName(item), defects));
            }

            return new BridgeAssessmentReport
            {
                Bridge = SafetyGradeEvaluator.EvaluateBridge(evaluations),
            };
        }

        /// <summary>"N년 뒤 targetGrade 등급에 도달"의 N을 역산한다. 이미 그 등급 이하이면 0을 반환한다.</summary>
        public static float YearsUntilGrade(
            string targetGrade, EnvironmentPreset preset, float coverDepthMm,
            System.Func<SimulationInput, string> evaluateGradeAt)
        {
            // ConditionFactor가 단조 감소하고 그에 따라 등급도 단조 악화(또는 유지)되므로 이분 탐색으로 충분히 정확하다.
            // 닫힌 형태 역산은 AgingModel.YearsUntilConditionFactor가 제공하지만, 등급은 결함 주입 임계값과
            // SafetyGradeEvaluator의 비선형 계산을 거치므로 여기서는 안전하게 탐색으로 구한다.
            int targetRank = SafetyGradeEvaluator.GradeToRank(targetGrade);

            float lo = 0f, hi = 1f;
            const float maxYears = 200f;

            // 상한을 targetGrade 도달 시점 이상으로 늘려간다.
            while (hi < maxYears)
            {
                var input = new SimulationInput { Years = hi, Environment = preset, CoverDepthMm = coverDepthMm };
                if (SafetyGradeEvaluator.GradeToRank(evaluateGradeAt(input)) <= targetRank)
                    break;
                hi *= 2f;
            }

            if (hi >= maxYears)
                return float.PositiveInfinity; // 200년 안에 도달하지 못함

            for (int i = 0; i < 40; i++) // 이분 탐색 40회면 연 단위 이하로 충분히 수렴
            {
                float mid = (lo + hi) * 0.5f;
                var input = new SimulationInput { Years = mid, Environment = preset, CoverDepthMm = coverDepthMm };
                if (SafetyGradeEvaluator.GradeToRank(evaluateGradeAt(input)) <= targetRank)
                    hi = mid;
                else
                    lo = mid;
            }

            return hi;
        }

        private static float ComputeConditionFactor(SimulationInput input)
        {
            float cover = input.CoverDepthMm > 0f ? input.CoverDepthMm : AgingModel.DefaultCoverDepthMm;
            return AgingModel.ConditionFactor(input.Years, input.Environment, cover);
        }

        /// <summary>상태 계수(1=건전)를 결함 악화 배율로 뒤집는다. 계수가 낮을수록(노후화될수록) 배율이 커진다.</summary>
        private static float ComputeAgeFactor(float conditionFactor)
        {
            float safe = Mathf.Max(0.05f, conditionFactor); // 0 나눗셈 방지 겸 완전 소멸 방지
            return 1f / safe;
        }

        private static DetectedDefect AgeDefect(DetectedDefect source, float ageFactor)
        {
            return new DetectedDefect
            {
                type = source.type,
                confidence = Mathf.Clamp01(source.confidence * Mathf.Min(ageFactor, 1.5f)), // 신뢰도는 완만하게만 보정
                maskAreaRatio = Mathf.Clamp01(source.maskAreaRatio * ageFactor),
                estimatedWidthMm = source.estimatedWidthMm >= 0f ? source.estimatedWidthMm * ageFactor : -1f,
                isStructurallyCritical = source.isStructurallyCritical,
                boxes = source.boxes,
            };
        }

        /// <summary>노후화만으로 새로 생겨나는 전형적 결함(콘크리트 균열)을 상태 계수에 비례한 강도로 만든다.</summary>
        private static DetectedDefect BuildInjectedDefect(float conditionFactor)
        {
            float severity = Mathf.Clamp01(1f - conditionFactor); // 상태가 나쁠수록 결함을 강하게
            return new DetectedDefect
            {
                type = DefectType.ConcreteCrack,
                confidence = Mathf.Clamp01(0.3f + severity * 0.5f),
                maskAreaRatio = Mathf.Clamp01(severity * 0.4f),
                estimatedWidthMm = -1f,
                isStructurallyCritical = false,
            };
        }
    }
}
