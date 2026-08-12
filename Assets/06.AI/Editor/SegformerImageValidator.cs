using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

// SegformerModel을 학습에 쓰이지 않은 06.AI/TestImages/ 실제 이미지로 돌려서
// 픽셀 분할 결과가 말이 되는지 확인하는 1회성 검증 도구.
// 픽셀 단위 정답 마스크가 없으므로 IoU 계산은 못하고, 대신 "폴더별 기대 클래스 픽셀 비율이
// Normal(배경) 폴더보다 뚜렷하게 높은가"로 파이프라인이 의미 있는 신호를 내는지만 확인한다.
// ai/CLAUDE.md 기준 SegFormer eval_mean_iou는 0.47로 RT-DETR보다 훨씬 높게 나온 모델이라
// RT-DETR 검증 때보다 더 뚜렷한 신호를 기대할 수 있다.
public static class SegformerImageValidator
{
    private const string ResultPath =
        @"C:\Users\qjatn\AppData\Local\Temp\claude\c--GitHubClone-BridgeSense-DT\e25cff99-f5e7-4ca4-a9cc-deced9809f31\scratchpad\segformer_image_validation_result.txt";

    private const int NumClasses = 10; // 배경/정상(0) + 결함 9종(1~9)

    // 폴더명 -> model_io_spec.md 4절 기준 기대 SegFormer 클래스 id(RT-DETR과 달리 +1 shift됨)
    // Normal은 결함이 없어야 하므로 기대 클래스를 배경(0)으로 둔다.
    private static readonly Dictionary<string, int> FolderToExpectedClassId = new()
    {
        { "Concrete crack", 1 },
        { "Efflorescence", 2 },
        { "Water Leak", 3 },
        { "Exposed rebar", 5 },
        { "Asphalt crack", 8 },
        { "Normal", 0 },
    };

    [MenuItem("BridgeSense/AI/Run SegFormer Image Validation")]
    public static void Run()
    {
        var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/06.AI/models/segformer.onnx"); // 검증용 도구라 에디터 전용 AssetDatabase 로드로 충분
        var model = new SegformerModel(modelAsset); // 이미지 전체를 순회하는 동안 워커 1개만 재사용

        var report = new StringBuilder();

        foreach (var (folderName, expectedClassId) in FolderToExpectedClassId) // TestImages 하위 6개 카테고리 폴더를 순회
        {
            var folderPath = $"Assets/06.AI/TestImages/{folderName}";
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath }); // 해당 폴더의 Texture2D로 임포트된 이미지만 검색

            report.AppendLine($"=== {folderName} (기대 SegFormer 클래스 id: {expectedClassId}) ===");

            var expectedRatios = new List<float>();     // 폴더별 "기대 클래스" 픽셀 비율을 모아서 평균 낼 리스트
            var nonBackgroundRatios = new List<float>(); // 폴더별 "배경이 아닌 픽셀" 비율을 모아서 평균 낼 리스트

            foreach (var guid in guids) // 폴더 안 이미지 10장을 순회
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                var segResult = model.Run(texture); // 512x512 픽셀별 클래스 인덱스 맵 추론

                var counts = new int[NumClasses]; // 클래스별 픽셀 개수를 셀 히스토그램
                foreach (var classId in segResult.ClassMap) // 262144개 픽셀을 전부 순회하며 카운트
                    counts[classId]++;

                int totalPixels = segResult.ClassMap.Length; // 512*512
                var proportions = counts.Select(c => c / (float)totalPixels).ToArray(); // 클래스별 비율(0~1)로 변환

                float expectedRatio = proportions[expectedClassId]; // 이 폴더가 기대하는 클래스의 픽셀 비율
                float nonBackgroundRatio = 1f - proportions[0]; // 배경(0)이 아닌 모든 결함 픽셀의 합 비율

                expectedRatios.Add(expectedRatio);
                nonBackgroundRatios.Add(nonBackgroundRatio);

                var top3 = proportions
                    .Select((ratio, classId) => (classId, ratio)) // (클래스id, 비율) 쌍으로 변환
                    .OrderByDescending(x => x.ratio) // 비율 내림차순 정렬
                    .Take(3) // 상위 3개 클래스만
                    .Select(x => $"cls{x.classId}:{x.ratio:P1}"); // "cls번호:퍼센트" 형태로 요약

                report.AppendLine(
                    $"{Path.GetFileName(path)} | expected(cls{expectedClassId}) ratio={expectedRatio:P2} | nonBackground={nonBackgroundRatio:P2} | top3=[{string.Join(", ", top3)}]");
            }

            report.AppendLine(
                $"-- {folderName} 요약: 기대 클래스 평균 비율={expectedRatios.Average():P2}, 배경 아닌 픽셀 평균 비율={nonBackgroundRatios.Average():P2}");
            report.AppendLine();
        }

        model.Dispose(); // 검증 끝났으니 워커·텐서 메모리 해제

        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)!);
        File.WriteAllText(ResultPath, report.ToString());

        Debug.Log(report.ToString());
    }
}
