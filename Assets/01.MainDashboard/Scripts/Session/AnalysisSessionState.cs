namespace BridgeSenseDT.Session
{
    /// <summary>
    /// 한 번의 분석 세션이 가질 수 있는 화면 상태.
    /// InputAndAnalyzePanel 안에서 ImageUploadPanel과 AnalysisResultArea 중
    /// 무엇이 보이는지를 결정하며, MainDashboardPanelState와 같은 방식으로
    /// "둘 다 켜지거나 둘 다 꺼진" 상태가 코드상 존재할 수 없게 만든다.
    /// </summary>
    public enum AnalysisSessionState
    {
        Editing,  // 이미지 업로드·등록 중. ImageUploadPanel 활성, 결과 영역 비어있음
        Analyzed, // 분석 완료. ImageUploadPanel 비활성, 결과 카드 표시
    }
}
