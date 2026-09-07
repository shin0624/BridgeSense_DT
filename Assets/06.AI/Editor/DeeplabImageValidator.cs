using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

// DeeplabModel을 학습에 쓰이지 않은 06.AI/TestImages/ 실제 이미지로 돌려서
// 파이프라인(전처리→추론→argmax 디코딩)이 말이 되는 결과를 내는지 확인하는 1회성 검증 도구.
// RT-DETR 검증(RtdetrImageValidator)과 달리 이쪽은 픽셀별 클래스 분포를 본다 -
// "정상 이미지에서는 결함 클래스 픽셀이 거의 없어야 하고, 결함 이미지에서는
// 해당 결함 클래스가 유의미한 면적을 차지해야 한다"가 기대 동작이다.
public static class DeeplabImageValidator
{
    private const string ResultPath =
        @"C:\Users\qjatn\AppData\Local\Temp\claude\c--GitHubClone-BridgeSense-DT\e25cff99-f5e7-4ca4-a9cc-deced9809f31\scratchpad\deeplab_image_validation_result.txt";

    private const int NumClasses = 10; // 배경(0) + 결함 9종(1~9)

    // 폴더명 -> 기대 클래스 id (분할 모델 기준, 배경 0 + 결함 1~9. RT-DETR 기준 id + 1)
    // RtdetrImageValidator.FolderToExpectedClassId와 동일한 폴더를 쓰되 +1 shift.
    private static readonly Dictionary<string, int> FolderToExpectedClassId = new()
    {
        { "Concrete crack", 1 },
        { "Efflorescence", 2 },
        { "Water Leak", 3 },
        { "Exposed rebar", 5 },
        { "Asphalt crack", 8 },
        { "Normal", 0 },
    };

    [MenuItem("BridgeSense/AI/Run DeepLabV3+ Image Validation")]
    public static void Run()
    {
        var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/06.AI/models/deeplabv3plus.onnx"); // 검증용 도구라 에디터 전용 AssetDatabase 로드로 충분(런타임 코드는 인스펙터 참조 사용)
        if (modelAsset == null)
        {
            Debug.LogError("Assets/06.AI/models/deeplabv3plus.onnx 를 찾지 못했습니다.");
            return;
        }

        var model = new DeeplabModel(modelAsset); // 이미지 전체를 순회하는 동안 워커 1개만 재사용
        var report = new StringBuilder();
        bool anyFolderFound = false;

        foreach (var (folderName, expectedClassId) in FolderToExpectedClassId) // TestImages 하위 카테고리 폴더를 순회
        {
            var folderPath = $"Assets/06.AI/TestImages/{folderName}";
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                report.AppendLine($"=== {folderName}: 폴더 없음({folderPath}), 건너뜀 ===");
                report.AppendLine();
                continue;
            }

            anyFolderFound = true;
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath }); // 해당 폴더의 Texture2D로 임포트된 이미지만 검색

            report.AppendLine($"=== {folderName} (기대 클래스 id: {expectedClassId}) ===");

            var expectedAreaRatios = new List<float>(); // 폴더별로 "기대 클래스"의 면적률을 모아서 평균 낼 리스트

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                var result = model.Run(texture);
                var counts = new int[NumClasses];
                foreach (var classId in result.ClassMap)
                {
                    if (classId >= 0 && classId < NumClasses)
                        counts[classId]++;
                }

                int totalPixels = result.ClassMap.Length;
                float expectedRatio = counts[expectedClassId] / (float)totalPixels;
                expectedAreaRatios.Add(expectedRatio);

                string distSummary = string.Join(", ",
                    Enumerable.Range(0, NumClasses)
                        .Where(c => counts[c] > 0)
                        .Select(c => $"cls{c}:{counts[c] / (float)totalPixels:P1}"));
                report.AppendLine($"{Path.GetFileName(path)} | 기대 클래스 면적률={expectedRatio:P2} | 분포=[{distSummary}]");
            }

            var avgExpected = expectedAreaRatios.Count > 0 ? expectedAreaRatios.Average() : 0f;
            report.AppendLine($"-- {folderName} 요약: 이미지 {guids.Length}장, 기대 클래스 평균 면적률={avgExpected:P2}");
            report.AppendLine();
        }

        model.Dispose(); // 검증 끝났으니 워커·텐서 메모리 해제

        if (!anyFolderFound)
        {
            report.AppendLine("경고: Assets/06.AI/TestImages/ 아래 어떤 폴더도 찾지 못했습니다. " +
                               "테스트 이미지 없이는 출력 shape/로드 여부만 SentisSmokeTest로 확인 가능합니다.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)!);
        File.WriteAllText(ResultPath, report.ToString());

        Debug.Log(report.ToString());
    }
}
