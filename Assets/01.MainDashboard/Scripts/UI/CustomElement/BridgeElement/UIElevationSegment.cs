using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 입면도에서 경간(거더구간) 스트립 또는 교각 다리 하나에 부착되는 컴포넌트.
    /// UITableRow와 동일하게 UnityEngine.UI.Outline은 쓰지 않고, 4장의 얇은 Image로
    /// 만든 완전한 4방향 테두리로 호버/선택 상태를 표시한다.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class UIElevationSegment : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public string SegmentId { get; private set; }

        Image background;
        UIElevationDiagram owner;
        Color normalColor;
        bool isSelected;

        Image borderTop, borderBottom, borderLeft, borderRight;
        Coroutine borderGradientRoutine;

        public void Init(UIElevationDiagram owner, string segmentId, Color normalColor)
        {
            this.owner = owner;
            SegmentId = segmentId;
            this.normalColor = normalColor;

            background = GetComponent<Image>();
            background.color = normalColor;

            CreateBorders(owner.BorderThickness);
            SetBorderVisible(false, Color.clear);
        }

        void CreateBorders(float thickness)
        {
            borderTop = CreateEdge("Border_Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, thickness));
            borderBottom = CreateEdge("Border_Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, thickness));
            borderLeft = CreateEdge("Border_Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(thickness, 0));
            borderRight = CreateEdge("Border_Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(thickness, 0));
        }

        Image CreateEdge(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = Color.clear;
            return img;
        }

        void SetBorderVisible(bool visible, Color color)
        {
            Color c = visible ? color : Color.clear;
            borderTop.color = c;
            borderBottom.color = c;
            borderLeft.color = c;
            borderRight.color = c;
        }

        /// <summary>AI 분석 결과 갱신 등으로 등급이 바뀌었을 때 기본 색상을 다시 지정.</summary>
        public void SetGradeColor(Color color)
        {
            normalColor = color;
            if (!isSelected) background.color = color;
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;
            background.color = normalColor;

            if (borderGradientRoutine != null)
            {
                StopCoroutine(borderGradientRoutine);
                borderGradientRoutine = null;
            }

            if (selected)
                borderGradientRoutine = StartCoroutine(AnimateSelectedBorderGradient());
            else
                SetBorderVisible(false, Color.clear);
        }

        /// <summary>
        /// 선택된 오브젝트를, 등급 색상을 selectedSaturationSteps단계 채도 부스트한 색이
        /// 시계 방향(상→우→하→좌)으로 회전하며 밝아지는 그라데이션 형태로 테두리와 본체 색상 모두에 표시한다.
        /// </summary>
        IEnumerator AnimateSelectedBorderGradient()
        {
            Color dim = normalColor;
            Color bright = normalColor;
            for (int i = 0; i < owner.SelectedSaturationSteps; i++)
                bright = GradeColorMap.BoostSaturation(bright, owner.SelectedSaturationBoost);

            float phase = 0f;
            while (true)
            {
                phase = Mathf.Repeat(phase + Time.deltaTime * owner.BorderGradientSpeed, 1f);

                float wTop = ComputeEdgeWeight(0.0f, phase);
                float wRight = ComputeEdgeWeight(0.25f, phase);
                float wBottom = ComputeEdgeWeight(0.5f, phase);
                float wLeft = ComputeEdgeWeight(0.75f, phase);

                borderTop.color = Color.Lerp(dim, bright, wTop);
                borderRight.color = Color.Lerp(dim, bright, wRight);
                borderBottom.color = Color.Lerp(dim, bright, wBottom);
                borderLeft.color = Color.Lerp(dim, bright, wLeft);

                // 오브젝트 본체는 가장 가까운 테두리의 가중치를 따라가며 함께 밝아진다 (가시성 강화).
                float objectWeight = Mathf.Max(Mathf.Max(wTop, wRight), Mathf.Max(wBottom, wLeft));
                background.color = Color.Lerp(dim, bright, objectWeight);

                yield return null;
            }
        }

        float ComputeEdgeWeight(float edgePosition, float phase)
        {
            // 두 위치 사이의 원형(0~1 루프) 거리를 0~0.5 범위로 계산한다.
            float distance = Mathf.Abs(Mathf.DeltaAngle(edgePosition * 360f, phase * 360f)) / 360f;
            return Mathf.Clamp01(1f - distance / owner.BorderGradientWidth);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (isSelected) return;
            SetBorderVisible(true, GradeColorMap.Highlight(normalColor, owner.HoverHighlightBoost));
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (isSelected) return;
            SetBorderVisible(false, Color.clear);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            owner.SelectSegment(SegmentId);
        }
    }
}