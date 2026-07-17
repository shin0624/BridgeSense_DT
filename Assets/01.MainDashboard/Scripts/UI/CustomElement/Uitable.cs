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
    /// <summary>
    /// 빈 RectTransform(패널)에 이 컴포넌트만 붙이면 헤더 행 + 스크롤 가능한 데이터 행 영역을
    /// 자동으로 구성한다. [ExecuteAlways]가 붙어 있어 Play 중이 아니어도 씬에 배치하는 즉시
    /// 에디터에서 결과가 보인다.
    ///
    /// - 에디터(Edit Mode)에서는 editorPreviewRowCount 만큼 더미 행을 보여준다.
    /// - Play 모드로 진입하면 더미 행은 자동으로 지워지고, AddRow()로 채운 실제 데이터만 표시된다.
    ///
    /// 사용 예:
    ///   table.SetColumns(new[]{"ID","위치","손상유형","예상등급","심각도"});
    ///   int rowId = table.AddRow(new[]{"1001","교각 7","미세균열","B","낮음"});
    ///   table.OnRowSelected += (id) => Debug.Log($"선택된 행: {id}");
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class UITable : MonoBehaviour
    {
        [Header("컬럼 설정 (인스펙터에서 조정)")]
        [SerializeField] List<string> columnHeaders = new List<string> { "ID", "위치", "손상유형", "예상등급", "심각도" };
        [SerializeField] List<float> columnFlexWidths = new List<float> { 1f, 1.2f, 1.4f, 1f, 1f };

        [Header("행 크기")]
        [SerializeField] float headerHeight = 36f;
        [SerializeField] float rowHeight = 32f;
        [SerializeField] int fontSize = 14;
        [SerializeField] TMP_FontAsset font;

        [Header("기본 색상")]
        [SerializeField] Color headerBackgroundColor = new Color(0.153f, 0.129f, 0.102f, 1f);
        [SerializeField] Color headerTextColor = new Color(0.769f, 0.718f, 0.639f, 1f);
        [SerializeField] Color rowColorA = new Color(0.110f, 0.090f, 0.071f, 1f);
        [SerializeField] Color rowColorB = new Color(0.149f, 0.125f, 0.098f, 1f);
        [SerializeField] Color rowTextColor = new Color(0.969f, 0.945f, 0.902f, 1f);

        [Header("호버 효과 (색상 직접 지정 가능)")]
        [SerializeField] bool useOutlineForHover = false;
        [SerializeField] Color hoverColor = new Color(1f, 0.549f, 0.239f, 0.18f);

        [Header("선택 효과 (색상 직접 지정 가능)")]
        [SerializeField] bool useOutlineForSelection = true;
        [SerializeField] Color selectedColor = new Color(1f, 0.549f, 0.239f, 0.55f);

        [Header("4방향 테두리 두께 (useOutlineForHover/Selection이 true일 때 사용)")]
        [SerializeField] float borderThickness = 2f;

        [Header("에디터 미리보기 (Edit Mode 전용, Play 시작하면 자동으로 지워짐)")]
        [SerializeField] int editorPreviewRowCount = 3;

        public float BorderThickness => borderThickness;
        public Color HoverColor => hoverColor;
        public Color SelectedColor => selectedColor;
        public bool UseOutlineForHover => useOutlineForHover;
        public bool UseOutlineForSelection => useOutlineForSelection;

        /// <summary>행이 클릭(선택)될 때마다 rowId를 전달하는 이벤트. 3D 부재 하이라이트 연동 등에 사용.</summary>
        public event Action<int> OnRowSelected;

        RectTransform content;
        ScrollRect scrollRect;

        readonly Dictionary<int, UITableRow> rows = new Dictionary<int, UITableRow>();
        int nextRowId = 0;
        int selectedRowId = -1;

#if UNITY_EDITOR
        bool rebuildScheduled;
#endif

        void Awake()
        {
            if (transform.Find("Header") == null)
                BuildHierarchy();

            if (Application.isPlaying)
            {
                // 에디터에서 만들어둔 미리보기 행은 실제 데이터로 교체되어야 하므로 비운다.
                ClearRows();
            }
            else
            {
                RefreshEditorPreviewRows();
            }
        }

        void OnEnable()
        {
            if (!Application.isPlaying && transform.Find("Header") == null)
                BuildHierarchy();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying) return;
            if (rebuildScheduled) return;
            rebuildScheduled = true;
            EditorApplication.delayCall += () =>
            {
                rebuildScheduled = false;
                if (this == null) return; // 그 사이 오브젝트가 삭제됐을 수 있음
                RebuildForEditorPreview();
            };
        }
#endif

        /// <summary>Header/ScrollView를 통째로 지우고 다시 만든 뒤 미리보기 행을 채운다 (Edit Mode 전용).</summary>
        void RebuildForEditorPreview()
        {
            if (this == null || Application.isPlaying) return;
            DestroyChildIfExists("Header");
            DestroyChildIfExists("ScrollView");
            rows.Clear();
            nextRowId = 0;
            selectedRowId = -1;
            BuildHierarchy();
            RefreshEditorPreviewRows();
        }

        void RefreshEditorPreviewRows()
        {
            ClearRows();
            for (int i = 0; i < editorPreviewRowCount; i++)
            {
                var vals = new string[columnHeaders.Count];
                for (int c = 0; c < vals.Length; c++)
                    vals[c] = c == 0 ? (i + 1).ToString() : "-";
                AddRow(vals);
            }
        }

        void DestroyChildIfExists(string name)
        {
            var t = transform.Find(name);
            if (t != null) SafeDestroy(t.gameObject);
        }

        /// <summary>Play 모드면 Destroy, Edit 모드면 DestroyImmediate로 안전하게 제거.</summary>
        void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        void BuildHierarchy()
        {
            var root = GetComponent<RectTransform>();

            var rootLayout = GetComponent<VerticalLayoutGroup>();
            if (rootLayout == null) rootLayout = gameObject.AddComponent<VerticalLayoutGroup>();
            rootLayout.childControlHeight = true;
            rootLayout.childControlWidth = true;
            rootLayout.childForceExpandHeight = false;
            rootLayout.childForceExpandWidth = true;

            // ── 헤더 행 ──
            var headerRow = CreateRowContainer(root, "Header", headerBackgroundColor);
            var headerLE = headerRow.gameObject.AddComponent<LayoutElement>();
            headerLE.preferredHeight = headerHeight;
            headerLE.flexibleHeight = 0;
            PopulateCells(headerRow, columnHeaders.ToArray(), headerTextColor, bold: true);

            // ── 스크롤 영역 ──
            var scrollGO = new GameObject("ScrollView", typeof(RectTransform));
            scrollGO.transform.SetParent(root, false);
            var scrollLE = scrollGO.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1;

            scrollRect = scrollGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 20f;

            var viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            var viewportRT = viewportGO.GetComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            content = contentGO.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            var contentLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;

            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRT;
            scrollRect.content = content;
        }

        RectTransform CreateRowContainer(Transform parent, string name, Color bgColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = bgColor;

            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.padding = new RectOffset(8, 8, 4, 4);
            h.spacing = 4;
            return go.GetComponent<RectTransform>();
        }

        void PopulateCells(RectTransform rowRT, string[] values, Color textColor, bool bold)
        {
            for (int i = 0; i < columnHeaders.Count; i++)
            {
                var cellGO = new GameObject($"Cell_{i}", typeof(RectTransform));
                cellGO.transform.SetParent(rowRT, false);

                var le = cellGO.AddComponent<LayoutElement>();
                le.flexibleWidth = i < columnFlexWidths.Count ? columnFlexWidths[i] : 1f;

                var tmp = cellGO.AddComponent<TextMeshProUGUI>();
                tmp.text = i < values.Length ? values[i] : "";
                tmp.color = textColor;
                tmp.fontSize = fontSize;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                if (font != null) tmp.font = font;
                if (bold) tmp.fontStyle = FontStyles.Bold;
            }
        }

        /// <summary>런타임(혹은 에디터)에 컬럼 구성을 다시 지정하고 싶을 때 사용 (기존 행은 전부 제거됨).</summary>
        public void SetColumns(string[] headers, float[] weights = null)
        {
            columnHeaders = headers.ToList();
            if (weights != null) columnFlexWidths = weights.ToList();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RebuildForEditorPreview();
                return;
            }
#endif
            ClearRows();
        }

        /// <summary>데이터 행 1개를 추가하고 고유 rowId를 반환한다. Play 중 몇 번이든 호출 가능.</summary>
        public int AddRow(string[] cellValues)
        {
            int rowId = nextRowId++;
            bool alt = rows.Count % 2 == 1;
            Color rowColor = alt ? rowColorB : rowColorA;

            var rowRT = CreateRowContainer(content, $"Row_{rowId}", rowColor);
            var le = rowRT.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = rowHeight;
            le.flexibleHeight = 0;

            PopulateCells(rowRT, cellValues, rowTextColor, bold: false);

            var tableRow = rowRT.gameObject.AddComponent<UITableRow>();
            var cellTexts = rowRT.GetComponentsInChildren<TextMeshProUGUI>();
            tableRow.Init(this, rowId, cellTexts, rowColor);

            rows[rowId] = tableRow;
            return rowId;
        }

        /// <summary>rowId에 해당하는 행을 제거한다.</summary>
        public void RemoveRow(int rowId)
        {
            if (!rows.TryGetValue(rowId, out var row)) return;
            SafeDestroy(row.gameObject);
            rows.Remove(rowId);
            if (selectedRowId == rowId) selectedRowId = -1;
        }

        /// <summary>모든 데이터 행을 제거한다 (헤더는 유지).</summary>
        public void ClearRows()
        {
            foreach (var row in rows.Values)
                if (row != null) SafeDestroy(row.gameObject);
            rows.Clear();
            selectedRowId = -1;
            nextRowId = 0;
        }

        /// <summary>
        /// 특정 행을 선택 상태로 만든다. 사용자가 행을 클릭했을 때 UITableRow가 내부적으로 호출하지만,
        /// 3D 뷰에서 부재를 클릭했을 때 외부에서 직접 호출해 테이블과 3D 뷰를 서로 동기화할 수도 있다.
        /// </summary>
        public void SelectRow(int rowId)
        {
            if (selectedRowId == rowId) return;
            if (rows.TryGetValue(selectedRowId, out var prev)) prev.SetSelected(false);
            if (rows.TryGetValue(rowId, out var next)) next.SetSelected(true);
            selectedRowId = rowId;
            OnRowSelected?.Invoke(rowId);
        }

        [ContextMenu("테스트 행 추가")]
        void AddTestRowFromEditor()
        {
            var dummy = columnHeaders.Select((h, i) => i == 0 ? (rows.Count + 1).ToString() : "-").ToArray();
            AddRow(dummy);
        }
    }
}
