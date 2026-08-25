using System.Collections.Generic;
using BridgeSenseDT.Assessment;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 사진 위에 결함 위치를 사각형으로 덧그리는 컴포넌트.
    ///
    /// 좌표는 0~1로 정규화된 값을 받는다. 원본 이미지 해상도를 몰라도 그릴 수 있고,
    /// SegFormer 마스크에서 뽑은 사각형과 RT-DETR bbox를 같은 방식으로 다룰 수 있다.
    ///
    /// 사각형은 프리팹 없이 네 변을 Image로 만들어 붙인다.
    /// 테두리용 스프라이트를 따로 준비하지 않아도 되고, 두께를 코드로 조절할 수 있다.
    /// </summary>
    public class DetectionBoxOverlay : MonoBehaviour
    {
        [SerializeField] private RectTransform container; // 사각형이 놓일 영역. 비워두면 자기 자신

        [SerializeField] private Color boxColor = new Color(0.898f, 0.153f, 0.153f, 1f);
        [SerializeField] private float thickness = 2f;

        [Header("점선")]
        [Tooltip("끄면 실선으로 그린다")]
        [SerializeField] private bool dashed = true;

        [Tooltip("점선 한 칸의 길이(픽셀)")]
        [SerializeField] private float dashLength = 6f;

        [Tooltip("점선 사이 빈 구간의 길이(픽셀)")]
        [SerializeField] private float gapLength = 4f;

        [Tooltip("사각형이 보이지 않을 때 원인을 좁히기 위한 로그. 확인이 끝나면 끈다")]
        [SerializeField] private bool logDiagnostics = true;

        private readonly List<GameObject> createdBoxes = new List<GameObject>();

        // 점선 무늬는 한 번 만들어 모든 사각형이 함께 쓴다. 사각형마다 만들면 텍스처가 낭비된다.
        private Sprite horizontalDash;
        private Sprite verticalDash;
        private readonly List<Texture2D> dashTextures = new List<Texture2D>();

        private RectTransform Container
        {
            get
            {
                if (container == null)
                    container = GetComponent<RectTransform>();
                return container;
            }
        }

        /// <summary>
        /// 결함 위치를 사각형으로 표시한다.
        /// </summary>
        /// <param name="boxes">0~1로 정규화된 좌표(좌상단 원점) 목록</param>
        public void Show(IReadOnlyList<DefectBox> boxes)
        {
            Clear();

            if (boxes == null)
                return;

            // RectTransform이 없으면 사각형을 붙일 자리가 없다.
            // 이 경우 조용히 아무것도 그리지 않게 되어 원인을 찾기 어려우므로 명시적으로 알린다.
            if (Container == null)
            {
                Debug.LogWarning(
                    $"{name}에 RectTransform이 없어 검출 사각형을 그릴 수 없습니다. " +
                    "Canvas 아래의 UI 오브젝트에 붙였는지 확인해 주세요.", this);
                return;
            }

            foreach (var box in boxes)
                CreateBox(box);

            if (logDiagnostics)
            {
                var rect = Container.rect;
                Debug.Log(
                    $"검출 사각형: 요청 {boxes.Count}건 중 {createdBoxes.Count}개 생성, " +
                    $"부모 '{Container.name}' 크기 {rect.width:F0}x{rect.height:F0}", this);
            }
        }

        public void Clear()
        {
            foreach (var box in createdBoxes)
            {
                if (box != null)
                    Destroy(box);
            }

            createdBoxes.Clear();
        }

        private void CreateBox(DefectBox box)
        {
            // 받은 좌표는 이미지 관례대로 좌상단이 원점이고 y가 아래로 증가한다.
            // UI 앵커는 좌하단이 원점이고 y가 위로 증가하므로 y를 뒤집어야 한다.
            float xMin = Mathf.Clamp01(box.xMin);
            float xMax = Mathf.Clamp01(box.xMax);
            float yMin = Mathf.Clamp01(1f - box.yMax);
            float yMax = Mathf.Clamp01(1f - box.yMin);

            if (xMax - xMin <= 0f || yMax - yMin <= 0f)
                return; // 넓이가 없는 사각형은 그리지 않는다

            var boxObject = new GameObject("DetectionBox", typeof(RectTransform));
            var rect = boxObject.GetComponent<RectTransform>();

            rect.SetParent(Container, false);
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // 네 변을 각각 얇은 Image로 만들어 테두리만 있는 사각형을 만든다.
            CreateEdge(rect, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, thickness), true);
            CreateEdge(rect, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, thickness), true);
            CreateEdge(rect, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(thickness, 0f), false);
            CreateEdge(rect, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(thickness, 0f), false);

            createdBoxes.Add(boxObject);
        }

        private void CreateEdge(
            RectTransform parent, string edgeName, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, bool horizontal)
        {
            var edge = new GameObject(edgeName, typeof(RectTransform), typeof(Image));
            var rect = edge.GetComponent<RectTransform>();

            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;      // 늘어나는 축은 0, 두께 축만 값을 준다
            rect.anchoredPosition = Vector2.zero;

            var image = edge.GetComponent<Image>();
            image.color = boxColor;
            image.raycastTarget = false; // 사진 위에 덧그린 장식이라 클릭을 가로채면 안 된다

            if (dashed)
            {
                // 스프라이트를 늘리지 않고 반복해서 채우면 변 길이와 무관하게 점선 간격이 일정해진다.
                image.sprite = GetDashSprite(horizontal);
                image.type = Image.Type.Tiled;
            }
        }

        /// <summary>
        /// 점선 무늬 스프라이트를 만든다. 칠해진 구간과 빈 구간이 번갈아 나오는 1픽셀 폭 텍스처다.
        ///
        /// 점선용 이미지 에셋을 따로 두지 않고 코드로 만드는 이유는
        /// 점 길이와 간격을 인스펙터에서 바로 조절할 수 있게 하기 위해서다.
        ///
        /// 가로변과 세로변은 무늬가 뻗는 방향이 달라 각각 따로 만든다.
        /// 가로용 텍스처를 세로변에 쓰면 무늬가 옆으로 반복되어 실선처럼 보인다.
        /// </summary>
        private Sprite GetDashSprite(bool horizontal)
        {
            ref Sprite cached = ref horizontal ? ref horizontalDash : ref verticalDash;
            if (cached != null)
                return cached;

            int dash = Mathf.Max(1, Mathf.RoundToInt(dashLength));
            int gap = Mathf.Max(1, Mathf.RoundToInt(gapLength));
            int period = dash + gap;

            var texture = new Texture2D(
                horizontal ? period : 1,
                horizontal ? 1 : period,
                TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,   // 무늬가 흐려지지 않도록 보간하지 않는다
                wrapMode = TextureWrapMode.Repeat,
            };

            var pixels = new Color32[period];
            for (int i = 0; i < period; i++)
                pixels[i] = i < dash ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);

            texture.SetPixels32(pixels);
            texture.Apply();

            cached = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 1f); // 텍스처 1픽셀을 UI 1픽셀로 대응시켜 인스펙터 값이 그대로 반영되게 한다

            dashTextures.Add(texture);
            return cached;
        }

        private void OnDestroy()
        {
            Clear();

            // 코드로 만든 텍스처는 참조가 끊겨도 자동으로 정리되지 않으므로 직접 파괴한다.
            foreach (var texture in dashTextures)
            {
                if (texture != null)
                    Destroy(texture);
            }

            dashTextures.Clear();
        }
    }
}
