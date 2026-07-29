using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BridgeSenseDT.UI.Charts
{
    /// <summary>
    /// 슬라이스는 Unity Image의 Radial360 필드 방식(원형 스프라이트 + fillAmount)으로 그리고,
    /// 각 슬라이스를 누적 각도만큼 회전시켜 이어붙인다. donutThickness > 0이면 가운데에
    /// 구멍 색상 원을 하나 더 얹어서 도넛처럼 보이게 한다(0이면 꽉 찬 파이 차트).
    /// </summary>
    public class DonutChartRenderer : IChartRenderer
    {
        public void Render(RectTransform container, List<ChartDataPoint> data, ChartStyle style)
        {
            if (data == null || data.Count == 0) return;
            float total = ChartUtils.Total(data);
            if (total <= 0f) return;

            var ringGO = new GameObject("Ring", typeof(RectTransform));
            ringGO.transform.SetParent(container, false);
            var ringRT = ringGO.GetComponent<RectTransform>();
            ringRT.anchorMin = new Vector2(0.5f, 0.5f);
            ringRT.anchorMax = new Vector2(0.5f, 0.5f);
            ringRT.pivot = new Vector2(0.5f, 0.5f);

            float diameter = Mathf.Min(container.rect.width, container.rect.height) * 0.85f;
            if (diameter <= 1f) diameter = 200f; // 레이아웃 계산 전이면 임시 기본값
            ringRT.sizeDelta = new Vector2(diameter, diameter);
            ringRT.anchoredPosition = Vector2.zero;

            var sprite = ChartUtils.GetCircleSprite();
            float cumulative = 0f;

            foreach (var dp in data)
            {
                float fraction = dp.value / total;
                if (fraction <= 0f) continue;

                var sliceGO = new GameObject($"Slice_{dp.label}", typeof(RectTransform), typeof(Image));
                sliceGO.transform.SetParent(ringGO.transform, false);
                var sliceRT = sliceGO.GetComponent<RectTransform>();
                sliceRT.anchorMin = Vector2.zero;
                sliceRT.anchorMax = Vector2.one;
                sliceRT.offsetMin = Vector2.zero;
                sliceRT.offsetMax = Vector2.zero;

                float startAngle = style.donutStartAngleDeg + cumulative * 360f;
                sliceRT.localEulerAngles = new Vector3(0f, 0f, style.donutClockwise ? -startAngle : startAngle);

                var img = sliceGO.GetComponent<Image>();
                img.sprite = sprite;
                img.color = dp.color;
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Radial360;
                img.fillOrigin = (int)Image.Origin360.Top;
                img.fillClockwise = style.donutClockwise;
                img.fillAmount = Mathf.Clamp01(fraction);

                CreateLabel(container, ringRT, dp, total, style, cumulative + fraction / 2f, diameter);

                cumulative += fraction;
            }

            if (style.donutThickness > 0f)
            {
                var holeGO = new GameObject("Hole", typeof(RectTransform), typeof(Image));
                holeGO.transform.SetParent(ringGO.transform, false);
                var holeRT = holeGO.GetComponent<RectTransform>();
                holeRT.anchorMin = new Vector2(0.5f, 0.5f);
                holeRT.anchorMax = new Vector2(0.5f, 0.5f);
                holeRT.pivot = new Vector2(0.5f, 0.5f);
                float holeDiameter = diameter * style.donutThickness;
                holeRT.sizeDelta = new Vector2(holeDiameter, holeDiameter);
                holeRT.anchoredPosition = Vector2.zero;

                var holeImg = holeGO.GetComponent<Image>();
                holeImg.sprite = sprite;
                holeImg.color = style.donutHoleColor;
            }
        }

        void CreateLabel(RectTransform container, RectTransform ringRT, ChartDataPoint dp, float total,
                          ChartStyle style, float midFraction, float diameter)
        {
            float angleFromTop = midFraction * 360f * (style.donutClockwise ? 1f : -1f) + style.donutStartAngleDeg;
            float rad = angleFromTop * Mathf.Deg2Rad;
            float dx = Mathf.Sin(rad);
            float dy = Mathf.Cos(rad);

            float labelRadius = (diameter / 2f) * style.donutLabelRadiusRatio;

            var labelGO = new GameObject($"Label_{dp.label}", typeof(RectTransform));
            labelGO.transform.SetParent(container, false);
            var rt = labelGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(90f, 36f);
            rt.anchoredPosition = ringRT.anchoredPosition + new Vector2(dx, dy) * labelRadius;

            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = $"{dp.label}\n{ChartUtils.FormatValue(dp, total, style)}";
            tmp.color = style.labelColor;
            tmp.fontSize = style.labelFontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            if (style.font != null) tmp.font = style.font;
        }
    }
}