using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

// RtdetrModel을 학습에 쓰이지 않은 06.AI/TestImages/ 실제 이미지로 돌려서
// 파이프라인(전처리→추론→디코딩)이 말이 되는 결과를 내는지 확인하는 1회성 검증 도구.
// eval_map이 0.047~0.056로 낮게 나온 모델이라(ai/CLAUDE.md), "정확히 맞추는가"가 아니라
// "정상 이미지보다 결함 이미지에서 관련 클래스 score가 유의미하게 높게 나오는가"를 본다.
public static class RtdetrImageValidator
{
    private const string ResultPath =
        @"C:\Users\qjatn\AppData\Local\Temp\claude\c--GitHubClone-BridgeSense-DT\e25cff99-f5e7-4ca4-a9cc-deced9809f31\scratchpad\rtdetr_image_validation_result.txt";

    // 폴더명 -> model_io_spec.md 4절 기준 기대 클래스 id (정상은 결함 클래스가 없으므로 -1)
    private static readonly Dictionary<string, int> FolderToExpectedClassId = new()
    {
        { "Concrete crack", 0 },
        { "Efflorescence", 1 },
        { "Water Leak", 2 },
        { "Exposed rebar", 4 },
        { "Asphalt crack", 7 },
        { "Normal", -1 },
    };

    [MenuItem("BridgeSense/AI/Run RTDETR Image Validation")]
    public static void Run()
    {
        var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/06.AI/models/rtdetr.onnx"); // 검증용 도구라 에디터 전용 AssetDatabase 로드로 충분(런타임 코드는 인스펙터 참조 사용)
        var model = new RtdetrModel(modelAsset); // 이미지 전체를 순회하는 동안 워커 1개만 재사용

        var report = new StringBuilder();

        foreach (var (folderName, expectedClassId) in FolderToExpectedClassId) // TestImages 하위 6개 카테고리 폴더를 순회
        {
            var folderPath = $"Assets/06.AI/TestImages/{folderName}";
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath }); // 해당 폴더의 Texture2D로 임포트된 이미지만 검색

            report.AppendLine($"=== {folderName} (기대 클래스 id: {expectedClassId}) ===");

            var topScoresForExpectedClass = new List<float>(); // 폴더별로 "기대 클래스"의 top score를 모아서 평균 낼 리스트
            var passCount = 0; // 공식 threshold(0.5)를 통과한 이미지 수

            foreach (var guid in guids) // 폴더 안 이미지 10장을 순회
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                var top5 = model.Run(texture, scoreThreshold: 0f, topK: 5); // threshold를 0으로 둬서 필터링 없이 원본 순위 그대로 top5 확인
                var official = model.Run(texture, scoreThreshold: 0.5f, topK: 100); // model_io_spec.md 권장 기본 threshold(0.5) 기준 실제 검출 수

                if (official.Count > 0)
                    passCount++;

                var expectedTop = expectedClassId >= 0
                    ? top5.FirstOrDefault(d => d.ClassId == expectedClassId) // 기대 클래스가 5위 안에 있으면 그 score를, 없으면 default(score=0)를 기록
                    : default;
                if (expectedClassId >= 0)
                    topScoresForExpectedClass.Add(expectedTop.Score);

                var top5Summary = string.Join(", ", top5.Select(d => $"cls{d.ClassId}:{d.Score:F3}")); // top5를 "cls번호:score" 형태로 요약
                report.AppendLine($"{Path.GetFileName(path)} | official(>=0.5) count={official.Count} | top5=[{top5Summary}]");
            }

            var avgExpected = topScoresForExpectedClass.Count > 0 ? topScoresForExpectedClass.Average() : 0f; // 폴더 전체의 기대 클래스 평균 score
            report.AppendLine($"-- {folderName} 요약: official threshold 통과 이미지 {passCount}/{guids.Length}, 기대 클래스 평균 top score={avgExpected:F4}");
            report.AppendLine();
        }

        model.Dispose(); // 검증 끝났으니 워커·텐서 메모리 해제

        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)!);
        File.WriteAllText(ResultPath, report.ToString());

        Debug.Log(report.ToString());
    }
}
