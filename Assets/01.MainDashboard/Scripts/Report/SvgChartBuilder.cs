using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BridgeSenseDT.UI;
using UnityEngine;

namespace BridgeSenseDT.Report
{
    /// <summary>
    /// 보고서에 넣을 그래프를 SVG 문자열로 만든다.
    ///
    /// 차트 라이브러리를 링크하지 않고 SVG를 직접 쓰는 이유는,
    /// 보고서가 인터넷 연결 없이 어느 브라우저에서나 열려야 하기 때문이다.
    /// 스크립트 태그로 외부 라이브러리를 부르면 오프라인에서 그래프가 사라진다.
    /// 이미지로 굽는 방법도 있지만 확대하면 흐려지고 인쇄 품질이 떨어진다.
    /// </summary>
    public static class SvgChartBuilder
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>등급별 부재 수를 가로 막대로 그린다.</summary>
        public static string BuildBarChart(List<GradeDistributionRow> rows, int width = 420, int rowHeight = 34)
        {
            int height = Mathf.Max(rowHeight * rows.Count + 10, rowHeight);
            int labelWidth = 74;
            int valueWidth = 42;
            int trackWidth = width - labelWidth - valueWidth;

            int maxCount = 1;
            foreach (var row in rows)
                maxCount = Mathf.Max(maxCount, row.Count);

            var sb = new StringBuilder();
            sb.Append($"<svg viewBox=\"0 0 {width} {height}\" width=\"100%\" xmlns=\"http://www.w3.org/2000/svg\">");

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int y = i * rowHeight;
                int barY = y + 7;
                int barHeight = rowHeight - 16;

                float ratio = row.Count / (float)maxCount;
                float barWidth = Mathf.Max(trackWidth * ratio, row.Count > 0 ? 3f : 0f);

                string color = ToHex(GradeColorMap.GetColor(row.Grade));

                sb.Append($"<text x=\"0\" y=\"{y + rowHeight / 2 + 4}\" font-size=\"11.5\" fill=\"#000\">" +
                          $"{Escape(row.Grade)}({Escape(row.GradeLabel)})</text>");

                sb.Append($"<rect x=\"{labelWidth}\" y=\"{barY}\" width=\"{trackWidth}\" height=\"{barHeight}\" " +
                          "fill=\"#00000014\" rx=\"2\"/>");

                if (barWidth > 0f)
                {
                    sb.Append($"<rect x=\"{labelWidth}\" y=\"{barY}\" width=\"{Num(barWidth)}\" height=\"{barHeight}\" " +
                              $"fill=\"{color}\" rx=\"2\"/>");
                }

                sb.Append($"<text x=\"{labelWidth + trackWidth + 8}\" y=\"{y + rowHeight / 2 + 4}\" " +
                          $"font-size=\"11.5\" fill=\"#000\">{row.Count}</text>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// 등급 비율을 도넛으로 그린다.
        ///
        /// viewBox를 size 그대로 두면 라벨이 원 바깥(반지름의 1.18배)까지 뻗어 나갈 때
        /// 그림 경계를 넘어가 잘린다. 라벨이 차지할 여백만큼 viewBox를 실제로 넓혀서
        /// 원과 중심은 그대로 두고 여백만 그림 영역에 포함시킨다.
        /// </summary>
        public static string BuildDonutChart(List<GradeDistributionRow> rows, int size = 260, float holeRatio = 0.55f)
        {
            // 라벨은 가운데 정렬이라 "E 3.8%" 같은 문자열의 절반 폭만큼 계산 지점 밖으로 더 나간다.
            // 그만큼까지 넉넉히 포함해야 좌우 조각의 라벨이 잘리지 않는다.
            const float labelMargin = 46f;

            float radius = size / 2f - 34f; // 라벨을 놓을 자리를 남긴 원 반지름
            float holeRadius = radius * holeRatio;

            float canvasSize = size + labelMargin * 2f;
            float center = canvasSize / 2f;

            int total = 0;
            foreach (var row in rows)
                total += row.Count;

            var sb = new StringBuilder();
            sb.Append($"<svg viewBox=\"0 0 {Num(canvasSize)} {Num(canvasSize)}\" width=\"100%\" " +
                      "xmlns=\"http://www.w3.org/2000/svg\">");

            if (total == 0)
            {
                sb.Append($"<circle cx=\"{Num(center)}\" cy=\"{Num(center)}\" r=\"{Num(radius)}\" fill=\"#eee\"/>");
                sb.Append($"<text x=\"{Num(center)}\" y=\"{Num(center + 4)}\" font-size=\"12\" fill=\"#666\" " +
                          "text-anchor=\"middle\">데이터 없음</text>");
                sb.Append("</svg>");
                return sb.ToString();
            }

            float startAngle = -90f; // 12시 방향에서 시작해야 읽기 자연스럽다

            foreach (var row in rows)
            {
                if (row.Count == 0)
                    continue;

                float sweep = row.Count * 360f / total;
                float endAngle = startAngle + sweep;
                string color = ToHex(GradeColorMap.GetColor(row.Grade));

                sb.Append(BuildArcPath(center, radius, startAngle, endAngle, color));

                // 조각 한가운데 바깥쪽에 비율을 적는다.
                float midAngle = startAngle + sweep / 2f;
                Vector2 labelPoint = PolarToCartesian(center, radius * 1.18f, midAngle);

                sb.Append($"<text x=\"{Num(labelPoint.x)}\" y=\"{Num(labelPoint.y)}\" font-size=\"10.5\" fill=\"#000\" " +
                          $"text-anchor=\"middle\">{Escape(row.Grade)} {Num(row.Percent, "0.0")}%</text>");

                startAngle = endAngle;
            }

            // 가운데를 흰색으로 덮어 도넛 구멍을 만든다.
            sb.Append($"<circle cx=\"{Num(center)}\" cy=\"{Num(center)}\" r=\"{Num(holeRadius)}\" fill=\"#fff\"/>");
            sb.Append("</svg>");

            return sb.ToString();
        }

        /// <summary>
        /// 원의 한 조각을 그리는 경로를 만든다.
        /// 각도가 180도를 넘으면 큰 호 플래그를 켜야 조각이 뒤집히지 않는다.
        /// </summary>
        private static string BuildArcPath(float center, float radius, float startAngle, float endAngle, string color)
        {
            // 한 조각이 원 전체를 차지하면 시작점과 끝점이 같아져 경로가 그려지지 않는다.
            if (endAngle - startAngle >= 359.99f)
                return $"<circle cx=\"{Num(center)}\" cy=\"{Num(center)}\" r=\"{Num(radius)}\" fill=\"{color}\"/>";

            Vector2 start = PolarToCartesian(center, radius, startAngle);
            Vector2 end = PolarToCartesian(center, radius, endAngle);
            int largeArc = endAngle - startAngle > 180f ? 1 : 0;

            return $"<path d=\"M {Num(center)} {Num(center)} L {Num(start.x)} {Num(start.y)} " +
                   $"A {Num(radius)} {Num(radius)} 0 {largeArc} 1 {Num(end.x)} {Num(end.y)} Z\" fill=\"{color}\"/>";
        }

        private static Vector2 PolarToCartesian(float center, float radius, float angleDegrees)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(
                center + radius * Mathf.Cos(radians),
                center + radius * Mathf.Sin(radians));
        }

        public static string ToHex(Color color)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(color);
        }

        public static string Num(float value, string format = "0.##")
        {
            return value.ToString(format, Invariant);
        }

        /// <summary>
        /// SVG와 HTML에 그대로 넣으면 안 되는 문자를 바꾼다.
        /// 사용자가 입력한 부재명에 꺾쇠나 앰퍼샌드가 들어가면 문서 구조가 깨진다.
        /// </summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
