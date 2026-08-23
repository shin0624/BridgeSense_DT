using BridgeSenseDT.Session;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 3의 툴바 버튼이 붙기 전에 세션 매니저를 검증하기 위한 임시 개발용 메뉴.
/// Play 모드에서만 의미가 있으며, Editor 폴더라 빌드에는 포함되지 않는다.
/// 툴바 UI가 완성되면 삭제해도 된다.
/// </summary>
public static class AnalysisSessionDebugMenu
{
    [MenuItem("BridgeSense/Session/Save %#s", priority = 100)]
    public static void Save()
    {
        if (!TryGetManager(out var manager)) return;
        Debug.Log(manager.Save() ? "저장 완료" : "저장이 취소되었거나 실패했습니다");
    }

    [MenuItem("BridgeSense/Session/Save As", priority = 101)]
    public static void SaveAs()
    {
        if (!TryGetManager(out var manager)) return;
        Debug.Log(manager.SaveAs() ? "다른 이름으로 저장 완료" : "저장이 취소되었거나 실패했습니다");
    }

    [MenuItem("BridgeSense/Session/Load", priority = 102)]
    public static void Load()
    {
        if (!TryGetManager(out var manager)) return;
        Debug.Log(manager.LoadWithDialog() ? "불러오기 완료" : "불러오기가 취소되었거나 실패했습니다");
    }

    [MenuItem("BridgeSense/Session/New Session", priority = 103)]
    public static void NewSession()
    {
        if (!TryGetManager(out var manager)) return;
        manager.NewSession();
        Debug.Log("새 분석 세션을 시작했습니다");
    }

    [MenuItem("BridgeSense/Session/Print State", priority = 104)]
    public static void PrintState()
    {
        if (!TryGetManager(out var manager)) return;

        var session = manager.CurrentSession;
        Debug.Log(
            $"파일: {manager.CurrentFileName}\n" +
            $"변경됨(IsDirty): {manager.IsDirty}\n" +
            $"상태: {session.State}\n" +
            $"교량: {session.BridgeName} / {session.Location}\n" +
            $"항목 수: {session.Entries.Count}, 다음 순번 시드: {session.GetNextEntryIdSeed()}\n" +
            $"등급 스냅샷: {session.Snapshot?.Grade ?? "(없음)"}");
    }

    private static bool TryGetManager(out AnalysisSessionManager manager)
    {
        manager = AnalysisSessionManager.Instance;

        if (manager == null)
        {
            Debug.LogWarning("AnalysisSessionManager를 찾을 수 없습니다. Play 모드에서 실행했는지, 씬에 매니저가 있는지 확인해 주세요.");
            return false;
        }

        return true;
    }
}
