using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// UITable이 런타임에 생성하는 데이터 행 1개에 부착되는 컴포넌트.
    /// 마우스 호버·클릭 선택 시 배경색을 바꾸거나, 4개의 얇은 Image로 만든
    /// 완전한 4방향 테두리를 표시한다. UnityEngine.UI.Outline은 사용하지 않는다
    /// (한쪽 방향 그림자 복제 효과라 실제 테두리로는 부적합).
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class UITableRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public int RowId { get; private set; }
        public TextMeshProUGUI[] Cells { get; private set; }

        Image background;
        UITable owner;
        Color normalColor;
        bool isSelected;

        Image borderTop, borderBottom, borderLeft, borderRight;

        public void Init(UITable owner, int rowId, TextMeshProUGUI[] cells, Color normalColor)
        {
            this.owner = owner;
            RowId = rowId;
            Cells = cells;
            this.normalColor = normalColor;

            background = GetComponent<Image>();
            background.color = normalColor;

            CreateBorders(owner.BorderThickness);
            SetBorderVisible(false, Color.clear);
        }

        // ── 4방향 테두리: 얇은 Image 4장을 각 변에 앵커 스트레치로 붙인다 ──
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
            img.raycastTarget = false; // 보더가 클릭/호버 이벤트를 가로채지 않도록
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

        /// <summary>행 데이터가 갱신될 때(짝수/홀수 배경색 재계산 등) 기본 배경색을 다시 지정.</summary>
        public void SetNormalColor(Color color)
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
            owner.SelectRow(RowId);
        }
    }
}