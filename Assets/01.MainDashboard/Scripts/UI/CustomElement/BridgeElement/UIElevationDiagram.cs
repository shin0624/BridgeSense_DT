using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BridgeSenseDT.UI
{
    [Serializable]
    public class ElevationSpanData
    {
        public string id = "GirderSpan_1";
        public float lengthMeters = 30f;
        public string grade = "A";
    }

    [Serializable]
    public class ElevationPierData
    {
        public string id = "Pier_1";
        public string label = "P1";
        public string grade = "A";
    }

    /// <summary>
    /// "부재별 안전등급 입면도"를 uGUI로 그린다. 경간(거더구간)은 실제 길이(m) 비율에 맞춰
    /// 정규화 앵커(0~1)로 배치되므로 패널 크기가 바뀌어도 비율이 깨지지 않는다.
    /// 교각은 경간과 경간 사이 경계에 자동으로 배치되고 P1, P2... 라벨이 붙는다.
    ///
    /// 데이터는 국토교통부 현황조서 기반 BridgeSpec에서 그대로 채워 넣으면 된다.
    /// (spans.Count 개의 경간이 있으면 piers는 spans.Count-1 개가 자동으로 그 사이에 들어간다.)
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class UIElevationDiagram : MonoBehaviour
    {
        [Header("데이터")]
        [SerializeField] List<ElevationSpanData> spans = new List<ElevationSpanData>();
        [SerializeField] List<ElevationPierData> piers = new List<ElevationPierData>();

        [Header("레이아웃 비율 (패널 크기에 상대적인 0~1 값)")]
        [SerializeField] float marginXRatio = 0.03f;
        [SerializeField] float deckBottomYRatio = 0.55f;   // 데크 스트립의 아래쪽 경계
        [SerializeField] float deckHeightRatio = 0.08f;    // 데크 스트립 두께
        [SerializeField] float pierBottomYRatio = 0.15f;   // 교각 다리가 뻗는 아래쪽 끝
        [SerializeField] float pierWidthPixels = 8f;
        [SerializeField] float labelHeightPixels = 20f;

        [Header("호버 효과 (등급 색상을 흰색 쪽으로 밝게 보정해서 테두리로 표시)")]
        [SerializeField, Range(0f, 1f)] float hoverHighlightBoost = 0.35f;

        [Header("선택 효과 (등급 색상의 채도를 selectedSaturationBoost만큼, selectedSaturationSteps번 중첩 적용)")]
        [SerializeField, Range(0f, 1f)] float selectedSaturationBoost = 0.35f;
        [SerializeField, Range(1, 5)] int selectedSaturationSteps = 2;

        [Header("선택 시 테두리 그라데이션 회전 효과 (시계 방향)")]
        [SerializeField] float borderGradientSpeed = 0.5f;                 // 초당 회전 수 (바퀴/초)
        [SerializeField, Range(0.1f, 0.5f)] float borderGradientWidth = 0.35f; // 하이라이트가 퍼지는 범위(0~0.5)

        [Header("4방향 테두리 두께")]
        [SerializeField] float borderThickness = 2f;

        [Header("라벨(P1, P2...)")]
        [SerializeField] TMP_FontAsset font;
        [SerializeField] int labelFontSize = 12;
        [SerializeField] Color labelColor = new Color(0.969f, 0.945f, 0.902f, 1f);

        public float BorderThickness => borderThickness;
        public float HoverHighlightBoost => hoverHighlightBoost;
        public float SelectedSaturationBoost => selectedSaturationBoost;
        public int SelectedSaturationSteps => selectedSaturationSteps;
        public float BorderGradientSpeed => borderGradientSpeed;
        public float BorderGradientWidth => borderGradientWidth;

        /// <summary>경간 또는 교각을 클릭했을 때 해당 id를 전달하는 이벤트.</summary>
        public event Action<string> OnSegmentSelected;

        readonly Dictionary<string, UIElevationSegment> segments = new Dictionary<string, UIElevationSegment>();
        string selectedId;
        RectTransform root;

#if UNITY_EDITOR
        bool rebuildScheduled;
#endif

        void Awake() => Rebuild();

        void OnEnable()
        {
            if (segments.Count == 0) Rebuild();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (rebuildScheduled) return;
            rebuildScheduled = true;
            EditorApplication.delayCall += () =>
            {
                rebuildScheduled = false;
                if (this == null) return;
                Rebuild();
            };
        }
#endif

        void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        /// <summary>외부(국토교통부 스펙 JSON 등)에서 경간·교각 데이터를 통째로 다시 지정한다.</summary>
        public void SetData(List<ElevationSpanData> newSpans, List<ElevationPierData> newPiers = null)
        {
            spans = newSpans;
            piers = newPiers;
            Rebuild();
        }

        public void Rebuild()
        {
            ClearChildren();
            segments.Clear();
            selectedId = null;

            if (spans == null || spans.Count == 0) return;

            root = GetComponent<RectTransform>();
            float totalLength = spans.Sum(s => s.lengthMeters);
            if (totalLength <= 0f) return;

            float marginL = marginXRatio;
            float marginR = 1f - marginXRatio;
            float usable = marginR - marginL;

            var boundaries = new float[spans.Count + 1];
            boundaries[0] = marginL;
            float cumulative = 0f;
            for (int i = 0; i < spans.Count; i++)
            {
                cumulative += spans[i].lengthMeters;
                boundaries[i + 1] = marginL + usable * (cumulative / totalLength);
            }

            // ── 경간(거더구간) 스트립 ──
            for (int i = 0; i < spans.Count; i++)
            {
                var seg = CreateDeckSegment(spans[i].id, boundaries[i], boundaries[i + 1], GradeColorMap.GetColor(spans[i].grade));
                segments[spans[i].id] = seg;
            }

            // ── 교각: 경간과 경간 사이 경계마다 자동 배치 ──
            int pierCount = spans.Count - 1;
            for (int i = 0; i < pierCount; i++)
            {
                ElevationPierData data = (piers != null && i < piers.Count)
                    ? piers[i]
                    : new ElevationPierData { id = $"Pier_{i + 1}", label = $"P{i + 1}", grade = "A" };

                float xRatio = boundaries[i + 1];
                var seg = CreatePierSegment(data.id, xRatio, GradeColorMap.GetColor(data.grade));
                segments[data.id] = seg;
                CreatePierLabel(data.label, xRatio);
            }
        }

        UIElevationSegment CreateDeckSegment(string id, float xMinRatio, float xMaxRatio, Color color)
        {
            var go = new GameObject($"Deck_{id}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMinRatio, deckBottomYRatio);
            rt.anchorMax = new Vector2(xMaxRatio, deckBottomYRatio + deckHeightRatio);
            rt.offsetMin = new Vector2(1f, 0f); // 경간 사이 살짝 간격
            rt.offsetMax = new Vector2(-1f, 0f);

            var seg = go.AddComponent<UIElevationSegment>();
            seg.Init(this, id, color);
            return seg;
        }

        UIElevationSegment CreatePierSegment(string id, float xRatio, Color color)
        {
            var go = new GameObject($"Pier_{id}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xRatio, pierBottomYRatio);
            rt.anchorMax = new Vector2(xRatio, deckBottomYRatio); // 위쪽 끝 = 데크 밑면에 닿음
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(pierWidthPixels, 0f);      // x만 고정폭, y는 앵커 간격에 맞춰 자동 스트레치
            rt.anchoredPosition = Vector2.zero;

            var seg = go.AddComponent<UIElevationSegment>();
            seg.Init(this, id, color);
            return seg;
        }

        void CreatePierLabel(string label, float xRatio)
        {
            var go = new GameObject($"Label_{label}", typeof(RectTransform));
            go.transform.SetParent(root, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xRatio, pierBottomYRatio);
            rt.anchorMax = new Vector2(xRatio, pierBottomYRatio);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(60f, labelHeightPixels);
            rt.anchoredPosition = new Vector2(0f, -4f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = labelFontSize;
            tmp.color = labelColor;
            tmp.alignment = TextAlignmentOptions.Center;
            if (font != null) tmp.font = font;
            tmp.rectTransform.localScale = new Vector3(0.175f, 1f, 1f);
        }

        /// <summary>경간 또는 교각을 프로그램적으로 선택 상태로 만든다 (예: 3D 뷰 클릭과 동기화).</summary>
        public void SelectSegment(string id)
        {
            if (selectedId == id) return;
            if (selectedId != null && segments.TryGetValue(selectedId, out var prev)) prev.SetSelected(false);
            if (segments.TryGetValue(id, out var next)) next.SetSelected(true);
            selectedId = id;
            OnSegmentSelected?.Invoke(id);
        }

        /// <summary>AI 분석 결과가 갱신되었을 때 특정 부재의 등급 색상만 바꾼다 (전체 리빌드 불필요).</summary>
        public void UpdateGrade(string id, string grade)
        {
            if (segments.TryGetValue(id, out var seg))
                seg.SetGradeColor(GradeColorMap.GetColor(grade));
        }

        [ContextMenu("샘플 데이터로 채우기 (극락교 13경간)")]
        void FillSampleData()
        {
            spans = Enumerable.Range(1, 13)
                .Select(i => new ElevationSpanData { id = $"GirderSpan_{i}", lengthMeters = 30f, grade = "A" })
                .ToList();
            piers = Enumerable.Range(1, 12)
                .Select(i => new ElevationPierData { id = $"Pier_{i}", label = $"P{i}", grade = "A" })
                .ToList();
            piers[5].grade = "D"; // P6 예시
            Rebuild();
        }
    }
}