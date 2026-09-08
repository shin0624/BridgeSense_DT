using UnityEngine;

namespace BridgeSenseDT.Simulation
{
    /// <summary>
    /// 환경 조건에 따른 탄산화 진행 속도 프리셋. k값(mm/√year)은 공학적으로 통용되는 크기 순서를 따른 근사치.
    ///
    /// 탄산화는 콘크리트 기공 안의 수분을 매개로 CO2가 침투하는 반응이라 "습도"와 단순 비례하지 않는다.
    /// 상시 건조(기공이 말라 CO2가 자유롭게 드나들지만 반응에 필요한 수분이 부족)와
    /// 상시 포화(기공이 물로 막혀 CO2 확산 자체가 차단)는 둘 다 오히려 느리고,
    /// 그 사이를 오가는 습윤·건조 반복이 가장 빠르다(젖을 때 이온 침투, 마를 때 CO2 재침투 통로가 열림).
    /// 그래서 Dry와 WetDryCycle을 "습도의 두 끝"이 아니라 서로 다른 반응 조건으로 분리해뒀다.
    /// </summary>
    public enum EnvironmentPreset
    {
        Inland,       // 내륙 일반 환경(보통 습도, 강우 노출은 있으나 반복 사이클만큼 가혹하지 않음)
        Dry,          // 상시 건조 환경(실내·저습도 지역 등). 수분 부족으로 반응 자체가 정체되어 가장 느림
        WetDryCycle,  // 습윤·건조 반복(교면 누수, 배수 불량 구간 등). 네 프리셋 중 순수 탄산화 속도가 가장 빠름
        CoastalSalt,  // 해안 염해 환경. 탄산화에 염화물 침투까지 가세해 종합 위험도가 가장 높음
    }

    /// <summary>
    /// 콘크리트 탄산화 확산 모델(Fick 법칙 근사, depth ∝ √t)을 이용해
    /// "경과 연수 → 부재 상태 계수(0~1)"를 계산하고, 그 역함수도 제공한다.
    ///
    /// 실제 정밀 열화 예측에는 피복두께 실측, 염화물 함유량 등 많은 변수가 필요하지만,
    /// 이 시뮬레이터는 "안전등급이 대략 어떻게 변해가는지"를 보여주는 체험용 도구이므로
    /// 널리 쓰이는 단순화된 형태(depth(t) = k * sqrt(t))만 사용한다.
    /// </summary>
    public static class AgingModel
    {
        /// <summary>
        /// 환경별 탄산화 속도계수 k(mm/√year). 값이 클수록 빨리 진행된다.
        /// 순서: Dry(2.5) &lt; Inland(4.0) &lt; WetDryCycle(6.0) &lt; CoastalSalt(8.5).
        /// CoastalSalt가 WetDryCycle보다 높은 이유는 순수 탄산화 속도가 아니라 염화물 복합작용까지
        /// 반영한 종합 위험도이기 때문 — 이 시뮬레이터는 탄산화 단일 모델만 쓰므로 염해의 가중을
        /// k값에 얹어 근사한다(정밀 염화물 확산 모델은 별도로 필요).
        /// </summary>
        public static float GetDiffusionCoefficient(EnvironmentPreset preset)
        {
            switch (preset)
            {
                case EnvironmentPreset.Dry: return 2.5f;
                case EnvironmentPreset.WetDryCycle: return 6.0f;
                case EnvironmentPreset.CoastalSalt: return 8.5f;
                default: return 4.0f; // Inland
            }
        }

        /// <summary>표준 피복두께(mm). 실측값이 없을 때 근사 계산에 쓴다.</summary>
        public const float DefaultCoverDepthMm = 40f;

        /// <summary>상태 계수가 감쇠하는 곡률. p가 클수록 초반엔 완만하다가 후반에 급격히 나빠진다.</summary>
        public const float DefaultDecayCurvature = 1.6f;

        /// <summary>경과 연수(t)에서의 탄산화 깊이(mm). depth(t) = k * sqrt(t)</summary>
        public static float CarbonationDepthMm(float years, EnvironmentPreset preset)
        {
            float k = GetDiffusionCoefficient(preset);
            return k * Mathf.Sqrt(Mathf.Max(0f, years));
        }

        /// <summary>
        /// 경과 연수(t)에서의 부재 상태 계수(0~1, 1이 건전 상태).
        /// depth가 피복두께에 도달하면 0에 가까워진다(철근 도달 = 부식 시작점).
        /// </summary>
        public static float ConditionFactor(
            float years, EnvironmentPreset preset,
            float coverDepthMm = DefaultCoverDepthMm, float curvature = DefaultDecayCurvature)
        {
            float depth = CarbonationDepthMm(years, preset);
            float ratio = Mathf.Clamp01(depth / Mathf.Max(1f, coverDepthMm));
            return 1f - Mathf.Pow(ratio, curvature);
        }

        /// <summary>
        /// ConditionFactor의 역함수: 상태 계수가 targetFactor 이하로 떨어지는 최초 경과 연수를 구한다.
        /// ConditionFactor가 years에 대해 단조 감소하므로 닫힌 형태로 바로 계산할 수 있다.
        /// 이미 그 상태를 넘어섰으면(targetFactor가 현재보다 높으면) 0을 반환한다.
        /// </summary>
        public static float YearsUntilConditionFactor(
            float targetFactor, EnvironmentPreset preset,
            float coverDepthMm = DefaultCoverDepthMm, float curvature = DefaultDecayCurvature)
        {
            targetFactor = Mathf.Clamp01(targetFactor);

            // 1 - (depth/cover)^p = target  =>  depth = cover * (1-target)^(1/p)
            float ratio = Mathf.Pow(1f - targetFactor, 1f / curvature);
            float targetDepth = coverDepthMm * ratio;

            float k = GetDiffusionCoefficient(preset);
            if (k <= 0f) return float.PositiveInfinity; // 확산이 없으면 영원히 도달하지 않음

            // depth = k * sqrt(t)  =>  t = (depth/k)^2
            return Mathf.Pow(targetDepth / k, 2f);
        }

        public static string GetPresetName(EnvironmentPreset preset)
        {
            switch (preset)
            {
                case EnvironmentPreset.Dry: return "상시 건조";
                case EnvironmentPreset.WetDryCycle: return "습윤·건조 반복(누수·배수불량)";
                case EnvironmentPreset.CoastalSalt: return "해안 염해";
                default: return "내륙 일반";
            }
        }
    }
}
