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
            if (selected)
            {
                if (owner.UseOutlineForSelection)
                {
                    background.color = normalColor;
                    SetBorderVisible(true, owner.SelectedColor);
                }
                else
                {
                    SetBorderVisible(false, Color.clear);
                    background.color = owner.SelectedColor;
                }
            }
            else
            {
                SetBorderVisible(false, Color.clear);
                background.color = normalColor;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (isSelected) return;
            if (owner.UseOutlineForHover)
                SetBorderVisible(true, owner.HoverColor);
            else
                background.color = owner.HoverColor;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (isSelected) return;
            SetBorderVisible(false, Color.clear);
            background.color = normalColor;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            owner.SelectSegment(SegmentId);
        }
    }
}