namespace BridgeSenseDT.Simulation
{
    /// <summary>시뮬레이션이 어디서부터 출발할지 결정하는 기준점.</summary>
    public enum SimulationBaselineMode
    {
        /// <summary>AnalysisSessionManager.LastReport(AI 실측 결과)를 시작점으로 사용. 결과가 없으면 사용할 수 없다.</summary>
        FromAnalysis,

        /// <summary>결함이 전혀 없는 준공 시점(A등급)에서 출발. BridgeSpec.year만 있으면 항상 사용 가능하다.</summary>
        FromDesignSpec,
    }
}
