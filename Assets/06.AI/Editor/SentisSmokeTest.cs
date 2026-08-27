using System;
using System.IO;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

// 정의된 입출력 텐서 계약대로 rtdetr.onnx가
// Sentis(Inference Engine)에서 실제로 로드·실행되는지만 확인하는 1회성 스모크 테스트.
public static class SentisSmokeTest
{
    private const string ResultPath =
        @"C:\Users\qjatn\AppData\Local\Temp\claude\c--GitHubClone-BridgeSense-DT\e25cff99-f5e7-4ca4-a9cc-deced9809f31\scratchpad\sentis_smoke_test_result.txt";

    [MenuItem("BridgeSense/AI/Run Sentis Smoke Test")]
    public static void Run()
    {
        var report = new StringBuilder();
        bool allPassed = true;

        allPassed &= TestModel(
            report, "RT-DETR", "Assets/06.AI/models/rtdetr.onnx",
            new TensorShape(1, 3, 640, 640),
            new (string name, TensorShape shape)[]
            {
                ("logits", new TensorShape(1, 300, 9)),
                ("pred_boxes", new TensorShape(1, 300, 4)),
            });

        report.AppendLine();
        report.AppendLine(allPassed ? "OVERALL: PASS" : "OVERALL: FAIL");

        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)!);
        File.WriteAllText(ResultPath, report.ToString());

        Debug.Log(report.ToString());

        if (Application.isBatchMode)
            EditorApplication.Exit(allPassed ? 0 : 1);
    }

    private static bool TestModel(
        StringBuilder report, string label, string assetPath,
        TensorShape inputShape, (string name, TensorShape shape)[] expectedOutputs)
    {
        report.AppendLine($"--- {label} ({assetPath}) ---");

        var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(assetPath);
        if (modelAsset == null)
        {
            report.AppendLine($"FAIL: ModelAsset not found at {assetPath}");
            return false;
        }

        Model model;
        try
        {
            model = ModelLoader.Load(modelAsset);
        }
        catch (Exception e)
        {
            report.AppendLine($"FAIL: ModelLoader.Load threw: {e}");
            return false;
        }
        report.AppendLine($"Loaded OK. inputs={model.inputs.Count}, outputs={model.outputs.Count}");

        Worker worker = null;
        Tensor<float> input = null;
        bool passed = true;
        try
        {
            worker = new Worker(model, BackendType.GPUCompute);
            input = new Tensor<float>(inputShape, clearOnInit: true);

            worker.Schedule(input);

            foreach (var (name, expectedShape) in expectedOutputs)
            {
                var output = worker.PeekOutput(name);
                if (output == null)
                {
                    report.AppendLine($"FAIL: output '{name}' not found");
                    passed = false;
                    continue;
                }

                bool shapeMatch = output.shape.Equals(expectedShape);
                report.AppendLine(
                    $"{(shapeMatch ? "PASS" : "FAIL")}: output '{name}' shape={output.shape} expected={expectedShape}");
                if (!shapeMatch)
                    passed = false;
            }
        }
        catch (Exception e)
        {
            report.AppendLine($"FAIL: inference threw: {e}");
            passed = false;
        }
        finally
        {
            input?.Dispose();
            worker?.Dispose();
        }

        return passed;
    }
}
