using System.Collections.Generic;
using System.IO;
using System.Text;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.Session;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 저장 스키마가 왕복(직렬화 후 역직렬화)에서 값을 잃지 않는지 UI 없이 확인하는 1회성 도구.
/// 세션 매니저와 버튼을 붙이기 전에 이 단계를 통과시켜두면,
/// 이후 저장/불러오기가 이상할 때 직렬화 문제인지 UI 배선 문제인지 바로 갈라낼 수 있다.
/// </summary>
public static class AnalysisSaveRoundTripTest
{
    private const string ResultPath =
        @"C:\Users\qjatn\AppData\Local\Temp\claude\c--GitHubClone-BridgeSense-DT\e25cff99-f5e7-4ca4-a9cc-deced9809f31\scratchpad\analysis_save_roundtrip_result.txt";

    [MenuItem("BridgeSense/Session/Run Save RoundTrip Test")]
    public static void Run()
    {
        var report = new StringBuilder();
        bool allPassed = true;

        AnalysisSession original = BuildDummySession();

        string json;
        AnalysisSession restored;
        try
        {
            json = AnalysisSaveSerializer.Serialize(original);
            restored = AnalysisSaveSerializer.Deserialize(json);
        }
        catch (System.Exception e)
        {
            report.AppendLine($"FAIL: 직렬화/역직렬화 중 예외 발생: {e}");
            WriteReport(report.ToString(), false);
            return;
        }

        report.AppendLine($"직렬화 결과 크기: {json.Length:N0} 문자");
        report.AppendLine();

        allPassed &= Check(report, "교량명", original.BridgeName, restored.BridgeName);
        allPassed &= Check(report, "소재지", original.Location, restored.Location);
        allPassed &= Check(report, "항목 수", original.Entries.Count, restored.Entries.Count);

        // 분석된 항목이 있으므로 상태는 Analyzed로 복원돼야 한다(상태는 저장하지 않고 Analyzed 플래그에서 파생시킨다).
        allPassed &= Check(report, "세션 상태", AnalysisSessionState.Analyzed, restored.State);

        // 순번 시드가 복원되지 않으면 불러온 뒤 등록할 때 순번이 충돌한다.
        allPassed &= Check(report, "다음 순번 시드", original.GetNextEntryIdSeed(), restored.GetNextEntryIdSeed());

        for (int i = 0; i < original.Entries.Count && i < restored.Entries.Count; i++)
        {
            var a = original.Entries[i];
            var b = restored.Entries[i];
            string tag = $"항목[{i}]";

            allPassed &= Check(report, $"{tag} 순번", a.EntryId, b.EntryId);
            allPassed &= Check(report, $"{tag} 촬영부재", a.CapturedPart, b.CapturedPart);
            allPassed &= Check(report, $"{tag} 파일명", a.ImageFileName, b.ImageFileName);
            allPassed &= Check(report, $"{tag} 분석여부", a.Analyzed, b.Analyzed);
            allPassed &= CheckBytes(report, $"{tag} 이미지 바이트", a.ImageBytes, b.ImageBytes);
            allPassed &= Check(report, $"{tag} 검출 수", a.Detections.Count, b.Detections.Count);
            allPassed &= Check(report, $"{tag} 결함 수", a.Defects.Count, b.Defects.Count);

            if (a.Detections.Count == b.Detections.Count && a.Detections.Count > 0)
            {
                allPassed &= Check(report, $"{tag} 검출[0] 클래스", a.Detections[0].ClassId, b.Detections[0].ClassId);
                allPassed &= Check(report, $"{tag} 검출[0] score", a.Detections[0].Score, b.Detections[0].Score);
                allPassed &= Check(report, $"{tag} 검출[0] x2", a.Detections[0].X2, b.Detections[0].X2);
            }

            if (a.Defects.Count == b.Defects.Count && a.Defects.Count > 0)
            {
                allPassed &= Check(report, $"{tag} 결함[0] 유형", a.Defects[0].type, b.Defects[0].type);
                allPassed &= Check(report, $"{tag} 결함[0] 신뢰도", a.Defects[0].confidence, b.Defects[0].confidence);
                allPassed &= Check(report, $"{tag} 결함[0] 중대손상", a.Defects[0].isStructurallyCritical, b.Defects[0].isStructurallyCritical);
            }
        }

        if (restored.Snapshot == null)
        {
            report.AppendLine("FAIL: 등급 스냅샷이 복원되지 않음");
            allPassed = false;
        }
        else
        {
            allPassed &= Check(report, "스냅샷 등급", original.Snapshot.Grade, restored.Snapshot.Grade);
            allPassed &= Check(report, "스냅샷 종합점수", original.Snapshot.TotalScore, restored.Snapshot.TotalScore);
        }

        // 실제 파일 입출력까지 확인한다(경로 생성, 인코딩 문제를 여기서 잡는다).
        try
        {
            string tempPath = Path.Combine(
                AnalysisSaveSerializer.GetDefaultSaveDirectory(),
                "__roundtrip_test" + AnalysisSaveSerializer.FileExtensionWithDot);

            AnalysisSaveSerializer.SaveToFile(original, tempPath);
            var fromFile = AnalysisSaveSerializer.LoadFromFile(tempPath);

            allPassed &= Check(report, "파일 왕복 교량명", original.BridgeName, fromFile.BridgeName);
            allPassed &= Check(report, "파일 왕복 항목 수", original.Entries.Count, fromFile.Entries.Count);
            report.AppendLine($"파일 크기: {new FileInfo(tempPath).Length:N0} 바이트 ({tempPath})");

            File.Delete(tempPath); // 검증용 파일은 남기지 않는다
        }
        catch (System.Exception e)
        {
            report.AppendLine($"FAIL: 파일 입출력 중 예외 발생: {e}");
            allPassed = false;
        }

        WriteReport(report.ToString(), allPassed);
    }

    private static AnalysisSession BuildDummySession()
    {
        var session = new AnalysisSession
        {
            BridgeName = "극락교",
            Location = "광주광역시 서구 마륵동", // 한글이 파일 인코딩을 타지 않는지도 함께 확인
            Snapshot = new AssessmentSnapshot
            {
                Grade = "D",
                TotalScore = 4.25f,
                MajorScore = 3.5f,
                GeneralScore = 6f,
                AncillaryScore = 8f,
            },
        };

        session.Entries.Add(new AnalysisEntry
        {
            EntryId = "1",
            CapturedPart = "거더",
            ImageFileName = "co143UN3P03_601616.jpg",
            ImageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 }, // 실제 디코딩은 하지 않으므로 임의 바이트로 충분
            Analyzed = true,
            Detections = new List<RtdetrDetection>
            {
                new RtdetrDetection { ClassId = 0, Score = 0.27f, X1 = 10f, Y1 = 20f, X2 = 130.5f, Y2 = 240.25f },
            },
            Defects = new List<DetectedDefect>
            {
                new DetectedDefect
                {
                    type = DefectType.ConcreteCrack,
                    confidence = 0.27f,
                    estimatedWidthMm = -1f,
                    isStructurallyCritical = false,
                },
            },
        });

        session.Entries.Add(new AnalysisEntry
        {
            EntryId = "7", // 연속하지 않는 순번을 넣어 시드 계산이 "가장 큰 값 + 1"인지 확인
            CapturedPart = "교각",
            ImageFileName = "ex142UN3P02_500687.jpg",
            ImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            Analyzed = true,
            Defects = new List<DetectedDefect>
            {
                new DetectedDefect
                {
                    type = DefectType.ExposedRebar,
                    confidence = 0.42f,
                    estimatedWidthMm = -1f,
                    isStructurallyCritical = true,
                },
            },
        });

        return session;
    }

    private static bool Check<T>(StringBuilder report, string label, T expected, T actual)
    {
        bool passed = EqualityComparer<T>.Default.Equals(expected, actual);
        report.AppendLine($"{(passed ? "PASS" : "FAIL")}: {label} | 원본={expected} 복원={actual}");
        return passed;
    }

    private static bool CheckBytes(StringBuilder report, string label, byte[] expected, byte[] actual)
    {
        bool passed = expected != null && actual != null && expected.Length == actual.Length;
        if (passed)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i]) { passed = false; break; }
            }
        }
        report.AppendLine($"{(passed ? "PASS" : "FAIL")}: {label} | 원본 {expected?.Length ?? -1}바이트, 복원 {actual?.Length ?? -1}바이트");
        return passed;
    }

    private static void WriteReport(string body, bool allPassed)
    {
        string full = body + System.Environment.NewLine + (allPassed ? "OVERALL: PASS" : "OVERALL: FAIL");

        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
        File.WriteAllText(ResultPath, full);

        Debug.Log(full);
    }
}
