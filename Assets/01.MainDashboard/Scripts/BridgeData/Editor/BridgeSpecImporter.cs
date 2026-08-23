using System;
using System.Collections.Generic;
using System.IO;
using BridgeSenseDT.BridgeData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 국토교통부 현황조서 JSON에서 화면에 필요한 항목만 추려 StreamingAssets에 넣는 변환 도구.
///
/// 원본을 그대로 빌드에 넣지 않는 이유:
/// 전국 교량 4만여 건이 담긴 원본은 25MB에 달하고, 21개 컬럼 중 실제로 쓰는 것은 일부뿐이다.
/// 필요한 컬럼만 남기면 크기가 크게 줄고 읽는 시간도 짧아진다.
///
/// CSV가 아니라 JSON을 원본으로 삼은 이유:
/// 같은 자료의 CSV는 CP949로 인코딩돼 있는데, 이 코드페이지는 .NET Standard 2.1에
/// 기본 포함되지 않아 빌드 환경에 따라 한글이 깨질 수 있다. JSON은 UTF-8이라 그 위험이 없다.
///
/// 해마다 새 현황조서가 나오면 data 폴더의 원본 파일만 교체하고 이 메뉴를 다시 실행하면 된다.
/// </summary>
public static class BridgeSpecImporter
{
    private const string SourceDirectory = "data";
    private const string OutputPath = "Assets/StreamingAssets/BridgeData/bridges.json";

    // 원본 조서의 컬럼명. 조서 양식이 바뀌면 여기만 고치면 된다.
    private const string ColumnName = "시설명";
    private const string ColumnSido = "시도";
    private const string ColumnSigungu = "시군구";
    private const string ColumnEmd = "읍면동";
    private const string ColumnRi = "리";
    private const string ColumnAgency1 = "기관구분1";
    private const string ColumnAgency2 = "기관구분2";
    private const string ColumnAgency3 = "기관구분3";
    private const string ColumnSuper = "상부구조";
    private const string ColumnSub = "하부구조";
    private const string ColumnYear = "준공년도";

    [MenuItem("BridgeSense/Data/교량 제원 데이터 생성")]
    public static void Import()
    {
        string sourcePath = SelectSourceFile();
        if (string.IsNullOrEmpty(sourcePath))
            return;

        try
        {
            int count = Convert(sourcePath, OutputPath);

            AssetDatabase.Refresh();

            long sizeBytes = new FileInfo(OutputPath).Length;
            string message =
                $"교량 {count:N0}건을 변환했습니다.\n\n" +
                $"원본: {Path.GetFileName(sourcePath)}\n" +
                $"결과: {OutputPath}\n" +
                $"크기: {sizeBytes / 1024f / 1024f:F1} MB";

            Debug.Log(message);
            EditorUtility.DisplayDialog("교량 제원 데이터 생성", message, "확인");
        }
        catch (Exception e)
        {
            Debug.LogError($"교량 제원 데이터를 만들지 못했습니다: {e}");
            EditorUtility.DisplayDialog("교량 제원 데이터 생성", $"변환에 실패했습니다.\n\n{e.Message}", "확인");
        }
    }

    /// <summary>data 폴더의 json을 찾는다. 여러 개면 사용자가 고르게 한다.</summary>
    private static string SelectSourceFile()
    {
        string directory = Path.GetFullPath(SourceDirectory);

        if (!Directory.Exists(directory))
        {
            EditorUtility.DisplayDialog("교량 제원 데이터 생성", $"원본 폴더를 찾지 못했습니다: {directory}", "확인");
            return null;
        }

        string[] candidates = Directory.GetFiles(directory, "*.json");

        if (candidates.Length == 0)
        {
            EditorUtility.DisplayDialog("교량 제원 데이터 생성",
                $"{directory} 안에 현황조서 json 파일이 없습니다.", "확인");
            return null;
        }

        if (candidates.Length == 1)
            return candidates[0];

        // 여러 연도의 조서가 함께 있을 수 있으므로 직접 고르게 한다.
        return EditorUtility.OpenFilePanel("현황조서 원본 선택", directory, "json");
    }

    /// <summary>
    /// 원본을 한 항목씩 흘려 읽으면서 필요한 컬럼만 뽑아 곧바로 결과 파일에 쓴다.
    /// 25MB를 통째로 메모리에 올리지 않기 위해 읽기와 쓰기 모두 스트리밍으로 처리한다.
    /// </summary>
    private static int Convert(string sourcePath, string outputPath)
    {
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        int count = 0;

        using (var sourceStream = new StreamReader(sourcePath))
        using (var reader = new JsonTextReader(sourceStream))
        using (var outputStream = new StreamWriter(outputPath, false, new System.Text.UTF8Encoding(false)))
        using (var writer = new JsonTextWriter(outputStream))
        {
            var serializer = JsonSerializer.CreateDefault();

            writer.WriteStartObject();

            writer.WritePropertyName("sourceName");
            writer.WriteValue(Path.GetFileName(sourcePath));

            writer.WritePropertyName("generatedAt");
            writer.WriteValue(DateTime.Now.ToString("o"));

            writer.WritePropertyName("bridges");
            writer.WriteStartArray();

            while (reader.Read())
            {
                if (reader.TokenType != JsonToken.StartObject)
                    continue;

                JObject row = JObject.Load(reader);
                BridgeSpec spec = BuildSpec(row);

                if (spec == null)
                    continue; // 시설명이 없는 행은 조회할 수 없으므로 버린다

                serializer.Serialize(writer, spec);
                count++;
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return count;
    }

    private static BridgeSpec BuildSpec(JObject row)
    {
        string name = ReadString(row, ColumnName);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return new BridgeSpec
        {
            name = name,
            sido = ReadString(row, ColumnSido),
            sgg = ReadString(row, ColumnSigungu),
            emd = ReadString(row, ColumnEmd),
            ri = ReadString(row, ColumnRi),
            agency = JoinAgency(row),
            sup = ReadString(row, ColumnSuper),
            sub = ReadString(row, ColumnSub),
            year = ReadString(row, ColumnYear),
        };
    }

    /// <summary>기관구분 1~3을 하나의 관리기관 문자열로 합친다. 비어있는 단계는 건너뛴다.</summary>
    private static string JoinAgency(JObject row)
    {
        var parts = new List<string>(3);

        foreach (string column in new[] { ColumnAgency1, ColumnAgency2, ColumnAgency3 })
        {
            string value = ReadString(row, column);
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add(value);
        }

        return string.Join("-", parts);
    }

    /// <summary>숫자 컬럼도 문자열로 받아둔다. 표시용이라 계산에 쓰지 않는다.</summary>
    private static string ReadString(JObject row, string column)
    {
        JToken token = row[column];

        if (token == null || token.Type == JTokenType.Null)
            return string.Empty;

        return token.ToString().Trim();
    }
}
