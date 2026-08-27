using System;
using System.Collections.Generic;
using System.Text;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.Bridge3D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 3D 교량 모델의 머티리얼 이름을 보고 부재 태그(BridgeComponentTag)를 자동으로 붙이는 에디터 도구.
///
/// 부재가 수십 개라 손으로 붙이면 누락이 생기기 쉬우므로 자동화한다.
/// 판별 기준은 머티리얼 이름이며, 에디터 에셋 이름은 영문 규칙을 따른다.
/// (사용자가 UI에 입력하는 촬영 부재는 한글이지만, 그쪽은 SafetyGradeEvaluator가 따로 해석한다.
///  두 경로는 다루는 대상이 달라서 각자의 매핑을 가진다.)
/// </summary>
public static class BridgeComponentAutoTagger
{
    /// <summary>
    /// 머티리얼 이름에 포함된 영문 키워드와 체크리스트 항목의 대응표.
    /// 위에서부터 순서대로 검사하므로, 더 구체적인 키워드를 앞에 둔다.
    /// 모델에 새 부재가 추가되면 여기에 한 줄 추가하면 된다.
    /// </summary>
    private static readonly (string keyword, BridgeChecklistItem item)[] MaterialKeywordMap =
    {
        ("Girder",         BridgeChecklistItem.Girder),
        ("Slab",           BridgeChecklistItem.Slab),
        ("Deck",           BridgeChecklistItem.Slab),
        ("Parapet",        BridgeChecklistItem.Parapet),
        ("Railing",        BridgeChecklistItem.Parapet),
        ("Bearing",        BridgeChecklistItem.Bearing),
        ("Pier",           BridgeChecklistItem.PierAbutment),
        ("Abutment",       BridgeChecklistItem.PierAbutment),
        ("Foundation",     BridgeChecklistItem.Foundation),
        ("Cable",          BridgeChecklistItem.CableAnchorage),
        ("Anchorage",      BridgeChecklistItem.CableAnchorage),
        ("Pavement",       BridgeChecklistItem.Pavement),
        ("ExpansionJoint", BridgeChecklistItem.ExpansionJoint),
        ("CrossBeam",      BridgeChecklistItem.CrossBeam),
        ("Drainage",       BridgeChecklistItem.Drainage),
    };

    /// <summary>구조부재가 아니라서 태깅 대상이 아닌 머티리얼. 경고 없이 조용히 건너뛴다.</summary>
    private static readonly string[] IgnoredKeywords = { "Label", "Placeholder", "Skybox", "Ground" };

    [MenuItem("BridgeSense/3D/선택한 오브젝트 하위 부재 자동 태깅")]
    public static void TagSelected()
    {
        Transform root = Selection.activeTransform;
        if (root == null)
        {
            EditorUtility.DisplayDialog("부재 자동 태깅", "교량 모델의 최상위 오브젝트를 먼저 선택해 주세요.", "확인");
            return;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var report = new StringBuilder();
        var itemCounts = new Dictionary<BridgeChecklistItem, int>();
        var unmatchedNames = new HashSet<string>();
        var nonEnglishNames = new HashSet<string>();

        int taggedRenderers = 0;
        int indexedCount = 0;

        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            var bindings = new List<SubmeshBinding>();

            for (int submeshIndex = 0; submeshIndex < materials.Length; submeshIndex++)
            {
                var material = materials[submeshIndex];
                if (material == null)
                    continue;

                if (IsIgnored(material.name))
                    continue;

                if (!TryResolveItem(material.name, out BridgeChecklistItem item, out bool matchedByKorean))
                {
                    unmatchedNames.Add(material.name);
                    continue;
                }

                if (matchedByKorean)
                    nonEnglishNames.Add(material.name);

                bindings.Add(new SubmeshBinding { submeshIndex = submeshIndex, item = item });

                itemCounts.TryGetValue(item, out int count);
                itemCounts[item] = count + 1;
            }

            if (bindings.Count == 0)
                continue; // 이 렌더러에는 구조부재가 없다

            var tag = renderer.GetComponent<BridgeComponentTag>();
            if (tag == null)
                tag = Undo.AddComponent<BridgeComponentTag>(renderer.gameObject);
            else
                Undo.RecordObject(tag, "부재 태그 갱신");

            int componentIndex = ExtractComponentIndex(renderer.transform, root);
            tag.EditorSetBindings(bindings, componentIndex);

            if (componentIndex > 0)
                indexedCount++;

            EditorUtility.SetDirty(tag);
            taggedRenderers++;
        }

        report.AppendLine($"태깅된 렌더러: {taggedRenderers} / 전체 {renderers.Length}");
        report.AppendLine($"부재 번호가 부여된 렌더러: {indexedCount}개 (번호 없음: {taggedRenderers - indexedCount}개)");
        report.AppendLine();
        report.AppendLine("부재 종류별 서브메시 수");

        foreach (var pair in itemCounts)
            report.AppendLine($"  {SafetyGradeEvaluator.GetChecklistItemName(pair.Key)}: {pair.Value}");

        if (itemCounts.Count == 0)
            report.AppendLine("  (없음) 머티리얼 이름이 영문 부재명 규칙에 맞는지 확인해 주세요.");

        if (nonEnglishNames.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("한글 머티리얼명으로 인식됨. 영문으로 교체 필요:");
            foreach (var name in nonEnglishNames)
                report.AppendLine($"  {name}");
        }

        if (unmatchedNames.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("부재로 판별하지 못한 머티리얼:");
            foreach (var name in unmatchedNames)
                report.AppendLine($"  {name}");
        }

        Debug.Log(report.ToString());

        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
    }

    /// <summary>
    /// 부재가 속한 구조 단위의 번호를 부모 체인에서 찾는다.
    ///
    /// 실제 메시는 Pier_Pier_7 / Deck_GirderSpan_13 같은 오브젝트의 자식으로 들어있고,
    /// 번호는 그 조상 이름에 붙어 있다. 조상 중 번호를 가진 것 가운데
    /// 가장 바깥쪽(교량 루트에 가까운) 것을 쓴다.
    /// 안쪽을 쓰면 한 경간 안의 개별 거더 번호가 잡혀서, 사용자가 말하는 "교각7·7경간"과 어긋난다.
    /// </summary>
    private static int ExtractComponentIndex(Transform target, Transform root)
    {
        int found = 0;

        for (Transform current = target; current != null; current = current.parent)
        {
            int index = SafetyGradeEvaluator.ExtractComponentIndex(TrailingNumberOf(current.name));
            if (index > 0)
                found = index; // 위로 올라갈수록 덮어써서 최종적으로 가장 바깥쪽 값이 남는다

            if (current == root)
                break;
        }

        return found;
    }

    /// <summary>이름 끝에 붙은 숫자만 돌려준다. Deck_GirderSpan_13 → "13", Girder → ""</summary>
    private static string TrailingNumberOf(string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)$");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// 머티리얼 이름에서 부재 종류를 판별한다.
    /// 영문 규칙이 원칙이지만, 아직 교체되지 않은 한글 머티리얼도 인식은 하되 별도로 보고한다.
    /// 조용히 건너뛰면 "부재가 하나도 안 잡힘"이라는 결과만 남아 원인을 찾기 어렵기 때문이다.
    /// </summary>
    private static bool TryResolveItem(string materialName, out BridgeChecklistItem item, out bool matchedByKorean)
    {
        matchedByKorean = false;

        foreach (var (keyword, mapped) in MaterialKeywordMap)
        {
            if (materialName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                item = mapped;
                return true;
            }
        }

        // 한글 머티리얼이 남아있는 경우를 대비한 보조 경로.
        // 사용자 입력 해석에 쓰는 파서를 그대로 활용한다.
        if (SafetyGradeEvaluator.TryParseChecklistItem(materialName, out item))
        {
            matchedByKorean = true;
            return true;
        }

        item = default;
        return false;
    }

    private static bool IsIgnored(string materialName)
    {
        foreach (var keyword in IgnoredKeywords)
        {
            if (materialName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        // 아직 교체되지 않은 한글 라벨 머티리얼도 함께 걸러낸다.
        return materialName.Contains("라벨");
    }

    /// <summary>
    /// 태깅 결과를 눈으로 확인하기 위한 점검 도구.
    /// 어떤 부재가 몇 개 잡혔는지, 빠진 부재 종류는 없는지 보여준다.
    /// </summary>
    [MenuItem("BridgeSense/3D/부재 태깅 상태 점검")]
    public static void InspectTags()
    {
        var tags = UnityEngine.Object.FindObjectsByType<BridgeComponentTag>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        var report = new StringBuilder();
        var itemCounts = new Dictionary<BridgeChecklistItem, int>();

        foreach (var tag in tags)
        {
            foreach (var binding in tag.Bindings)
            {
                itemCounts.TryGetValue(binding.item, out int count);
                itemCounts[binding.item] = count + 1;
            }
        }

        report.AppendLine($"씬 내 부재 태그: {tags.Length}개");
        report.AppendLine();
        report.AppendLine("부재 종류별 서브메시 수");

        foreach (var pair in itemCounts)
            report.AppendLine($"  {SafetyGradeEvaluator.GetChecklistItemName(pair.Key)}: {pair.Value}");

        Debug.Log(report.ToString());
    }
}
