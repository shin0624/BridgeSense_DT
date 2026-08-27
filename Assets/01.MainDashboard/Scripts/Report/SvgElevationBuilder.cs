using System.Text;
using BridgeSenseDT.UI;

namespace BridgeSenseDT.Report
{
    /// <summary>
    /// 부재별 안전등급 입면도를 SVG로 그린다.
    ///
    /// 화면의 입면도(UIElevationDiagram)와 같은 데이터를 쓰지만 그리는 방식은 다르다.
    /// 화면 쪽은 uGUI 오브젝트를 만들어야 하고, 보고서 쪽은 문자열이어야 하기 때문이다.
    /// 등급별 색은 GradeColorMap을 함께 쓰므로 두 그림의 색이 어긋나지 않는다.
    /// </summary>
    public static class SvgElevationBuilder
    {
        public static string Build(ComponentGradeMap gradeMap, int width = 900, int height = 190)
        {
            if (gradeMap == null || gradeMap.SpanIndices.Count == 0)
                return "<p style=\"font-size:11.5px;color:#666\">입면도를 그릴 부재 정보가 없습니다.</p>";

            var sb = new StringBuilder();
            sb.Append($"<svg viewBox=\"0 0 {width} {height}\" width=\"100%\" xmlns=\"http://www.w3.org/2000/svg\">");

            int margin = 20;
            int usableWidth = width - margin * 2;

            int spanCount = gradeMap.SpanIndices.Count;
            float spanWidth = usableWidth / (float)spanCount;

            const int deckY = 30;
            const int deckHeight = 18;
            const int pierTop = deckY + deckHeight;
            const int pierBottom = 132;
            const int pierWidth = 13;

            // 상부(경간) 스트립
            for (int i = 0; i < spanCount; i++)
            {
                int index = gradeMap.SpanIndices[i];
                string color = GradeToHex(gradeMap.SpanGrades, index);
                float x = margin + i * spanWidth;

                sb.Append($"<rect x=\"{SvgChartBuilder.Num(x + 1)}\" y=\"{deckY}\" " +
                          $"width=\"{SvgChartBuilder.Num(spanWidth - 2)}\" height=\"{deckHeight}\" " +
                          $"fill=\"{color}\" rx=\"2\"/>");
            }

            // 하부(교각)는 경간과 경간 사이 경계에 세운다.
            int pierCount = gradeMap.PierIndices.Count;
            for (int i = 0; i < pierCount; i++)
            {
                int index = gradeMap.PierIndices[i];
                string color = GradeToHex(gradeMap.PierGrades, index);

                // 경간 사이 경계는 경간 수보다 하나 적다. 교각이 더 많으면 남는 것은 그리지 않는다.
                if (i + 1 >= spanCount + 1)
                    break;

                float x = margin + (i + 1) * spanWidth - pierWidth / 2f;

                sb.Append($"<rect x=\"{SvgChartBuilder.Num(x)}\" y=\"{pierTop}\" " +
                          $"width=\"{pierWidth}\" height=\"{pierBottom - pierTop}\" fill=\"{color}\" rx=\"2\"/>");

                sb.Append($"<text x=\"{SvgChartBuilder.Num(x + pierWidth / 2f)}\" y=\"{pierBottom + 16}\" " +
                          $"font-size=\"10.5\" fill=\"#000\" text-anchor=\"middle\">P{index}</text>");
            }

            sb.Append(BuildLegend(width, height));
            sb.Append("</svg>");

            return sb.ToString();
        }

        /// <summary>등급 색이 무엇을 뜻하는지 그림 안에 함께 남긴다.</summary>
        private static string BuildLegend(int width, int height)
        {
            string[] grades = { "A", "B", "C", "D", "E" };
            var sb = new StringBuilder();

            int y = height - 18;
            int x = 20;
            const int boxSize = 11;

            foreach (string grade in grades)
            {
                string color = SvgChartBuilder.ToHex(GradeColorMap.GetColor(grade));

                sb.Append($"<rect x=\"{x}\" y=\"{y - boxSize + 2}\" width=\"{boxSize}\" height=\"{boxSize}\" fill=\"{color}\"/>");
                sb.Append($"<text x=\"{x + boxSize + 4}\" y=\"{y}\" font-size=\"10.5\" fill=\"#000\">" +
                          $"{grade}({GradeColorMap.GetLabel(grade)})</text>");

                x += 72;
            }

            string unknown = SvgChartBuilder.ToHex(GradeColorMap.alert_Unknown);
            sb.Append($"<rect x=\"{x}\" y=\"{y - boxSize + 2}\" width=\"{boxSize}\" height=\"{boxSize}\" fill=\"{unknown}\"/>");
            sb.Append($"<text x=\"{x + boxSize + 4}\" y=\"{y}\" font-size=\"10.5\" fill=\"#000\">미평가</text>");

            return sb.ToString();
        }

        /// <summary>등급이 없는 부재는 촬영되지 않은 것이므로 회색으로 남긴다.</summary>
        private static string GradeToHex(System.Collections.Generic.Dictionary<int, string> grades, int index)
        {
            return grades.TryGetValue(index, out string grade)
                ? SvgChartBuilder.ToHex(GradeColorMap.GetColor(grade))
                : SvgChartBuilder.ToHex(GradeColorMap.alert_Unknown);
        }
    }
}
