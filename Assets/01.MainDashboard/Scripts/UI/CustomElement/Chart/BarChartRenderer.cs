using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BridgeSenseDT.UI.Charts
{
    public class BarChartRenderer : IChartRenderer
    {
        public void Render(RectTransform container, List<ChartDataPoint> data, ChartStyle style)
        {
            if (data == null || data.Count == 0) return;

            float total = ChartUtils.Total(data);
            float maxValue = style.maxValueOverride > 0f ? style.maxValueOverride : Mathf.Max(0.0001f, MaxOf(data));

            var vlg = container.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = style.barRowSpacing;

            foreach (var dp in data)
                CreateRow(container, dp, total, maxValue, style);
        }

        float MaxOf(List<ChartDataPoint> data)
        {
            float m = 0f;
            foreach (var d in data) m = Mathf.Max(m, d.value);
            return m;
        }

        void CreateRow(Transform parent, ChartDataPoint dp, float total, float maxValue, ChartStyle style)
        {
            var rowGO = new GameObject($"Row_{dp.label}", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGO.transform.SetParent(parent, false);

            var rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.preferredHeight = style.barThickness;
            rowLE.flexibleHeight = 0;

            var hlg = rowGO.GetComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 8f;

            // 라벨 (고정폭)
            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelLE = labelGO.AddComponent<LayoutElement>();
            labelLE.preferredWidth = style.labelColumnWidth;
            labelLE.flexibleWidth = 0;

            var labelTmp = labelGO.AddComponent<TextMeshProUGUI>();
            labelTmp.text = dp.label;
            labelTmp.color = style.labelColor;
            labelTmp.fontSize = style.labelFontSize;
            labelTmp.alignment = TextAlignmentOptions.MidlineRight;
            if (style.font != null) labelTmp.font = style.font;

            // 트랙 (남은 폭 전부 차지)
            var trackGO = new GameObject("Track", typeof(RectTransform), typeof(Image));
            trackGO.transform.SetParent(rowGO.transform, false);
            var trackLE = trackGO.AddComponent<LayoutElement>();
            trackLE.flexibleWidth = 1;
            trackGO.GetComponent<Image>().color = style.barTrackColor;

            float fraction = Mathf.Clamp01(dp.value / maxValue);

            // 채움 바
            var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGO.transform.SetParent(trackGO.transform, false);
            var fillRT = fillGO.GetComponent<RectTransform>();
            fillRT.anchorMin = new Vector2(0f, 0f);
            fillRT.anchorMax = new Vector2(fraction, 1f);
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            fillGO.GetComponent<Image>().color = dp.color;

            // 값 텍스트 (바 끝에 배치. fill이 꽉 찬 경우 trackGO 바깥으로 넘치므로,
            // 그때는 trackGO의 RectTransform 오른쪽 끝을 기준으로 안쪽에 배치한다)
            var valueGO = new GameObject("Value", typeof(RectTransform));
            valueGO.transform.SetParent(trackGO.transform, false);
            var valueRT = valueGO.GetComponent<RectTransform>();
            valueRT.sizeDelta = new Vector2(90f, style.barThickness);

            var valueTmp = valueGO.AddComponent<TextMeshProUGUI>();
            valueTmp.text = ChartUtils.FormatValue(dp, total, style);
            valueTmp.color = style.valueColor;
            valueTmp.fontSize = style.valueFontSize;
            if (style.font != null) valueTmp.font = style.font;

            bool fillReachesTrackEnd = fraction >= 1f;
            if (fillReachesTrackEnd)
            {
                // trackGO RectTransform의 오른쪽 끝에 안쪽으로 배치해 트랙 밖으로 넘치지 않게 한다.
                valueRT.anchorMin = new Vector2(1f, 0.5f);
                valueRT.anchorMax = new Vector2(1f, 0.5f);
                valueRT.pivot = new Vector2(1f, 0.5f);
                valueRT.anchoredPosition = new Vector2(-6f, 0f);
                valueTmp.alignment = TextAlignmentOptions.MidlineRight;
            }
            else
            {
                valueRT.anchorMin = new Vector2(fraction, 0.5f);
                valueRT.anchorMax = new Vector2(fraction, 0.5f);
                valueRT.pivot = new Vector2(0f, 0.5f);
                valueRT.anchoredPosition = new Vector2(6f, 0f);
                valueTmp.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }
    }
}