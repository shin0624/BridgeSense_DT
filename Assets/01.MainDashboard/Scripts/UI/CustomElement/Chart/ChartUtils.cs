using System.Collections.Generic;
using UnityEngine;

namespace BridgeSenseDT.UI.Charts
{
    public static class ChartUtils
    {
        public static float Total(List<ChartDataPoint> data)
        {
            float sum = 0f;
            foreach (var d in data) sum += Mathf.Max(0f, d.value);
            return sum;
        }

        public static string FormatValue(ChartDataPoint dp, float total, ChartStyle style)
        {
            float pct = total > 0f ? dp.value / total * 100f : 0f;
            switch (style.displayFormat)
            {
                case ValueDisplayFormat.None:
                    return "";
                case ValueDisplayFormat.Value:
                    return dp.value.ToString(style.valueNumberFormat);
                case ValueDisplayFormat.Percentage:
                    return pct.ToString("F1") + "%";
                case ValueDisplayFormat.ValueAndPercentage:
                    return $"{dp.value.ToString(style.valueNumberFormat)} ({pct:F1}%)";
                case ValueDisplayFormat.Custom:
                    return string.Format(style.customFormat, dp.value, pct, dp.label);
                default:
                    return dp.value.ToString();
            }
        }

        static Sprite _circleSprite;

        /// <summary>도넛/파이 슬라이스용 원형 스프라이트를 1회만 생성해 캐싱한다. 외부 에셋 불필요.</summary>
        public static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float alpha = Mathf.Clamp01(radius - d + 1f); // 가장자리 1px 안티앨리어싱
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();

            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _circleSprite;
        }
    }
}