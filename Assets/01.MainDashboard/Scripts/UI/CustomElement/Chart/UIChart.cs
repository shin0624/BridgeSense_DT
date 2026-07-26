using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using BridgeSenseDT.UI; // GradeColorMap 참조용

namespace BridgeSenseDT.UI.Charts
{
    /// <summary>
    /// 빈 RectTransform(모달 안 그래프 자리)에 이 컴포넌트만 붙이면 된다.
    /// 인스펙터에서 그래프 형태(Bar/Donut), 데이터(라벨·값·색상 리스트), 스타일을 전부 지정할 수 있고,
    /// [ExecuteAlways]라서 Play 중이 아니어도 씬에 배치하는 즉시 결과가 보인다.
    ///
    /// 새 그래프 형태 추가 방법: IChartRenderer를 구현한 클래스를 만들고
    ///   UIChart.RegisterRenderer(ChartType.새타입, new 새렌더러());
    /// 를 아무 초기화 코드에서 한 번 호출하면 된다. UIChart 자체는 수정할 필요 없음.
    ///
    /// 사용 예 (교량 데이터 연동):
    ///   var points = bridgeSpec.pierGrades.Select(g => new ChartDataPoint{ label=g.Key, value=g.Value, color=GradeColorMap.GetColor(g.Key) }).ToList();
    ///   chart.SetData(points);
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class UIChart : MonoBehaviour
    {
        [Header("그래프 형태")]
        [SerializeField] ChartType chartType = ChartType.Bar;

        [Header("데이터 (요소 갯수·라벨·값·색상 직접 지정)")]
        [SerializeField]
        List<ChartDataPoint> data = new List<ChartDataPoint>
        {
            new ChartDataPoint { label = "A(우수)", value = 21, color = new Color(0.24f, 0.14f, 0.08f) },
            new ChartDataPoint { label = "B(양호)", value = 1,  color = new Color(0.42f, 0.24f, 0.10f) },
            new ChartDataPoint { label = "C(주의)", value = 2,  color = new Color(0.82f, 0.42f, 0.10f) },
            new ChartDataPoint { label = "D(미흡)", value = 1,  color = new Color(1.00f, 0.55f, 0.24f) },
            new ChartDataPoint { label = "E(불량)", value = 1,  color = new Color(0.91f, 0.10f, 0.05f) },
        };

        [Header("스타일 (막대/도넛 공통 + 각 전용 옵션)")]
        [SerializeField] ChartStyle style = new ChartStyle();

        RectTransform content;

        static readonly Dictionary<ChartType, IChartRenderer> renderers = new Dictionary<ChartType, IChartRenderer>
        {
            { ChartType.Bar, new BarChartRenderer() },
            { ChartType.Donut, new DonutChartRenderer() },
        };

#if UNITY_EDITOR
        bool rebuildScheduled;
#endif

        void Awake() => Rebuild();

        void OnEnable()
        {
            if (content == null) Rebuild();
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

        /// <summary>런타임(또는 에디터)에서 데이터를 통째로 갈아끼우고 다시 그린다.</summary>
        public void SetData(List<ChartDataPoint> newData)
        {
            data = newData;
            Rebuild();
        }

        /// <summary>그래프 형태를 코드에서 바꾸고 싶을 때 사용.</summary>
        public void SetChartType(ChartType type)
        {
            chartType = type;
            Rebuild();
        }

        /// <summary>새 그래프 타입을 프레임워크에 등록하는 확장 지점. UIChart 자체는 수정하지 않아도 된다.</summary>
        public static void RegisterRenderer(ChartType type, IChartRenderer renderer)
        {
            renderers[type] = renderer;
        }

        public void Rebuild()
        {
            var root = GetComponent<RectTransform>();

            var old = transform.Find("ChartContent");
            if (old != null)
                EditorRebuildUtility.SafeDestroy(old.gameObject);

            var contentGO = new GameObject("ChartContent", typeof(RectTransform));
            contentGO.transform.SetParent(root, false);
            content = contentGO.GetComponent<RectTransform>();
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            if (data == null || data.Count == 0) return;

            if (renderers.TryGetValue(chartType, out var renderer))
                renderer.Render(content, data, style);
            else
                Debug.LogWarning($"UIChart: '{chartType}' 타입에 등록된 렌더러가 없습니다. RegisterRenderer로 먼저 등록하세요.");
        }

        [ContextMenu("라벨 첫 글자를 등급으로 보고 GradeColorMap 색상 자동 지정")]
        void ApplyGradeColors()
        {
            foreach (var dp in data)
            {
                string key = string.IsNullOrEmpty(dp.label) ? "" : dp.label.Substring(0, 1);
                dp.color = GradeColorMap.GetColor(key);
            }
            Rebuild();
        }
    }
}