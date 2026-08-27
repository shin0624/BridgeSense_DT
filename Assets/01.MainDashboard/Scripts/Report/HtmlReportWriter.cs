using System;
using System.IO;
using System.Text;
using BridgeSenseDT.UI;

namespace BridgeSenseDT.Report
{
    /// <summary>
    /// 분석 결과를 HTML 보고서로 내보낸다.
    ///
    /// 이미지를 base64로 심고 그래프를 SVG로 직접 그려서 파일 하나 처리.
    /// 별도 이미지 폴더나 인터넷 연결 없이 어느 브라우저에서나 열린다.
    /// @page 규칙을 넣어 브라우저의 인쇄 기능으로 바로 A4 PDF를 만들 수 있다.
    /// </summary>
    public static class HtmlReportWriter
    {
        private const string TitleColor = "#b4541d";

        public static void Write(ReportData data, string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(filePath, Build(data), new UTF8Encoding(true));
        }

        public static string Build(ReportData data)
        {
            var sb = new StringBuilder();

            sb.Append("<!DOCTYPE html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
            sb.Append($"<title>BridgeSense DT 리포트 - {E(data.Facility.Name)}</title>");
            sb.Append("<style>").Append(BuildStyle()).Append("</style></head><body><div class=\"doc\">");

            AppendHeader(sb, data);
            AppendFacility(sb, data);
            AppendSummary(sb, data);
            AppendImages(sb, data);
            AppendDefects(sb, data);
            AppendElevation(sb, data);
            AppendDistribution(sb, data);
            AppendConclusion(sb, data);
            AppendLimits(sb, data);
            AppendFooter(sb, data);

            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        private static string BuildStyle()
        {
            return
                "@page { size: A4; margin: 14mm; }" +
                "* { box-sizing: border-box; margin:0; padding:0; }" +
                "body { font-family:'Noto Sans CJK KR','Malgun Gothic',sans-serif; background:#fff; color:#000;" +
                " font-size:12px; line-height:1.6; padding:32px 40px; }" +
                ".doc { max-width:1000px; margin:0 auto; }" +
                $"h1 {{ font-size:24px; color:{TitleColor}; font-weight:700; letter-spacing:-0.02em; }}" +
                $"h2 {{ font-size:15px; color:{TitleColor}; font-weight:700; margin:0 0 12px;" +
                $" padding-bottom:6px; border-bottom:2px solid {TitleColor}; }}" +
                $"header {{ border-bottom:3px solid {TitleColor}; padding-bottom:14px; margin-bottom:8px;" +
                " display:flex; justify-content:space-between; align-items:flex-end; }" +
                ".sub { font-size:12px; color:#555; margin-top:4px; }" +
                ".meta { text-align:right; font-size:11px; color:#555; line-height:1.8; }" +
                "section { margin-top:26px; page-break-inside:avoid; }" +
                "table { width:100%; border-collapse:collapse; font-size:11.5px; }" +
                "th { background:#f5f2ef; color:#000; font-weight:700; text-align:left;" +
                " padding:8px 10px; border-top:1.5px solid #000; border-bottom:1px solid #999; }" +
                "td { padding:7px 10px; border-bottom:1px solid #e0e0e0; }" +
                "tbody tr:last-child td { border-bottom:1.5px solid #000; }" +
                ".num { text-align:right; } .ctr { text-align:center; }" +
                ".badge { display:inline-block; min-width:20px; padding:2px 7px; border-radius:3px;" +
                " color:#fff; font-weight:700; font-size:11px; }" +
                ".summary-grid { width:100%; border-collapse:separate; border-spacing:10px 0; margin-bottom:6px; }" +
                ".summary-grid td { width:25%; border:none; padding:0; }" +
                ".card { border:1px solid #ddd; border-radius:4px; padding:12px 14px; }" +
                ".card .label { font-size:11px; color:#666; }" +
                ".card .value { font-size:20px; font-weight:700; margin-top:3px; }" +
                ".pair { width:100%; border-collapse:separate; border-spacing:8px 0; margin-bottom:12px; }" +
                ".pair td { width:50%; border:none; padding:0; vertical-align:top; }" +
                ".pair img { width:100%; border:1px solid #ddd; display:block; }" +
                ".shot { position:relative; line-height:0; }" +
                ".shot svg { position:absolute; left:0; top:0; width:100%; height:100%; }" +
                ".cap { font-size:11px; color:#555; margin-bottom:5px; line-height:1.6; }" +
                ".charts { width:100%; border-collapse:separate; border-spacing:14px 0; }" +
                ".charts td { width:50%; border:none; padding:0; vertical-align:top; }" +
                $".note {{ background:#faf7f4; border-left:3px solid {TitleColor}; padding:12px 16px; font-size:11.5px; }}" +
                ".note p + p { margin-top:7px; }" +
                "footer { margin-top:32px; padding-top:12px; border-top:1px solid #ccc;" +
                " font-size:10.5px; color:#777; display:flex; justify-content:space-between; }";
        }

        private static void AppendHeader(StringBuilder sb, ReportData data)
        {
            sb.Append("<header><div><h1>교량 안전 분석 리포트</h1>");
            sb.Append($"<div class=\"sub\">{E(data.Facility.Name)} · {E(data.Facility.Location)}</div></div>");
            sb.Append("<div class=\"meta\">");
            sb.Append($"작성일자 {E(data.GeneratedAt)}<br>평가기준 {E(data.EvaluationBasis)}");
            sb.Append("</div></header>");
        }

        private static void AppendFacility(StringBuilder sb, ReportData data)
        {
            var f = data.Facility;

            sb.Append("<section><h2>1. 대상 시설물 정보</h2><table><tbody>");
            AppendInfoRow(sb, "시설명", f.Name, "소재지", f.Location);
            AppendInfoRow(sb, "준공년도", f.CompletionYear, "상부구조", f.Superstructure);
            AppendInfoRow(sb, "하부구조", f.Substructure, "연장(m)", f.Length);
            AppendInfoRow(sb, "폭원(m)", f.Width, "경간수", f.SpanCount);
            AppendInfoRow(sb, "최대경간장(m)", f.MaxSpan, "설계하중", f.DesignLoad);
            AppendSingleInfoRow(sb, "관리주체", f.Agency);
            sb.Append("</tbody></table></section>");
        }

        private static void AppendInfoRow(StringBuilder sb, string k1, string v1, string k2, string v2)
        {
            sb.Append("<tr>");
            sb.Append($"<th style=\"width:14%\">{E(k1)}</th><td style=\"width:36%\">{Dash(v1)}</td>");
            sb.Append($"<th style=\"width:14%\">{E(k2)}</th><td>{Dash(v2)}</td>");
            sb.Append("</tr>");
        }

        /// <summary>항목 수가 홀수라 짝을 못 맞춘 마지막 행. 빈 칸으로 나머지를 채우지 않고 하나만 넓게 쓴다.</summary>
        private static void AppendSingleInfoRow(StringBuilder sb, string key, string value)
        {
            sb.Append("<tr>");
            sb.Append($"<th style=\"width:14%\">{E(key)}</th><td colspan=\"3\">{Dash(value)}</td>");
            sb.Append("</tr>");
        }

        private static void AppendSummary(StringBuilder sb, ReportData data)
        {
            var s = data.Summary;
            string color = SvgChartBuilder.ToHex(GradeColorMap.GetColor(s.Grade));

            sb.Append("<section><h2>2. 분석 결과 요약</h2>");
            sb.Append("<table class=\"summary-grid\"><tr>");

            AppendCard(sb, "종합 안전등급",
                $"<span class=\"badge\" style=\"background:{color}\">{E(s.Grade)}</span> " +
                $"{E(GradeColorMap.GetLabel(s.Grade))}");
            AppendCard(sb, "종합 상태점수", $"{s.TotalScore:F2}");
            AppendCard(sb, "분석 사진 수", $"{s.ImageCount}장");
            AppendCard(sb, "검출 결함 수", $"{s.DefectCount}건");

            sb.Append("</tr></table>");

            if (s.ForcedDowngrade)
            {
                sb.Append("<div class=\"note\"><p>구조안전성에 영향을 미치는 중대 손상이 검출되어 " +
                          "종합 등급이 강제 하향되었습니다.</p></div>");
            }

            sb.Append("<table style=\"margin-top:12px\"><thead><tr>" +
                      "<th>시설영역</th><th class=\"num\">가중치</th><th class=\"num\">평가항목수</th>" +
                      "<th class=\"num\">상태점수</th><th>비고</th></tr></thead><tbody>");

            foreach (var area in data.AreaScores)
            {
                sb.Append("<tr>");
                sb.Append($"<td>{E(area.AreaName)}</td>");
                sb.Append($"<td class=\"num\">{area.Weight:0}</td>");
                sb.Append($"<td class=\"num\">{area.ItemCount}</td>");
                sb.Append($"<td class=\"num\">{(area.Applicable ? area.Score.ToString("F1") : "-")}</td>");
                sb.Append($"<td>{(area.Applicable ? "" : "해당없음")}</td>");
                sb.Append("</tr>");
            }

            sb.Append("</tbody></table></section>");
        }

        private static void AppendCard(StringBuilder sb, string label, string valueHtml)
        {
            sb.Append("<td><div class=\"card\">");
            sb.Append($"<div class=\"label\">{E(label)}</div>");
            sb.Append($"<div class=\"value\">{valueHtml}</div>");
            sb.Append("</div></td>");
        }

        /// <summary>
        /// 3. 입력 이미지와 AI 검출 결과를 좌우로 나란히 보여준다.
        /// 오른쪽은 같은 이미지 위에 검출 사각형을 SVG로 덧그린다.
        /// 이미지를 두 번 심지 않도록 같은 base64 문자열을 두 곳에서 참조한다.
        /// </summary>
        private static void AppendImages(StringBuilder sb, ReportData data)
        {
            if (data.Images.Count == 0)
                return;

            sb.Append("<section><h2>3. 입력 이미지 및 AI 검출 결과</h2>");

            foreach (var image in data.Images)
            {
                string source = $"data:{image.ImageMimeType};base64,{Convert.ToBase64String(image.ImageBytes)}";
                string color = SvgChartBuilder.ToHex(GradeColorMap.GetColor(image.Grade));

                sb.Append($"<div class=\"cap\"><span class=\"badge\" style=\"background:{color}\">{E(image.Grade)}</span> " +
                          $"ID {E(image.EntryId)} · {E(image.CapturedPart)} · {E(image.DefectSummary)}</div>");

                sb.Append("<table class=\"pair\"><tr>");
                sb.Append($"<td><div class=\"cap\">원본</div><img src=\"{source}\" alt=\"원본\"></td>");
                sb.Append("<td><div class=\"cap\">AI 검출 결과</div><div class=\"shot\">");
                sb.Append($"<img src=\"{source}\" alt=\"검출 결과\">");
                sb.Append(BuildBoxOverlay(image));
                sb.Append("</div></td>");
                sb.Append("</tr></table>");
            }

            sb.Append("</section>");
        }

        /// <summary>
        /// 검출 사각형을 이미지 위에 겹쳐 그린다.
        /// 좌표가 0~1로 정규화돼 있으므로 viewBox를 0~100으로 두면 백분율처럼 다룰 수 있어
        /// 이미지가 어떤 크기로 표시되든 위치가 맞는다.
        /// </summary>
        private static string BuildBoxOverlay(ImagePair image)
        {
            if (image.Boxes.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append("<svg viewBox=\"0 0 100 100\" preserveAspectRatio=\"none\" xmlns=\"http://www.w3.org/2000/svg\">");

            foreach (var box in image.Boxes)
            {
                float x = box.XMin * 100f;
                float y = box.YMin * 100f;
                float w = (box.XMax - box.XMin) * 100f;
                float h = (box.YMax - box.YMin) * 100f;

                if (w <= 0f || h <= 0f)
                    continue;

                sb.Append($"<rect x=\"{SvgChartBuilder.Num(x)}\" y=\"{SvgChartBuilder.Num(y)}\" " +
                          $"width=\"{SvgChartBuilder.Num(w)}\" height=\"{SvgChartBuilder.Num(h)}\" " +
                          "fill=\"none\" stroke=\"#e52727\" stroke-width=\"2.0\" " +
                          "stroke-dasharray=\"2 1.2\" vector-effect=\"non-scaling-stroke\"/>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        private static void AppendDefects(StringBuilder sb, ReportData data)
        {
            sb.Append("<section><h2>4. 결함별 검출 결과</h2><table><thead><tr>" +
                      "<th class=\"ctr\">No</th><th>부재</th><th>점검항목</th><th>시설영역</th><th>결함유형</th>" +
                      "<th class=\"num\">신뢰도(%)</th>" +
                      "<th class=\"ctr\">상태</th><th class=\"num\">점수</th><th class=\"ctr\">등급</th>" +
                      "</tr></thead><tbody>");

            if (data.Defects.Count == 0)
            {
                sb.Append("<tr><td colspan=\"9\" class=\"ctr\">검출된 결함이 없습니다.</td></tr>");
            }
            else
            {
                foreach (var d in data.Defects)
                {
                    string color = SvgChartBuilder.ToHex(GradeColorMap.GetColor(d.Grade));

                    sb.Append("<tr>");
                    sb.Append($"<td class=\"ctr\">{d.No}</td>");
                    sb.Append($"<td>{E(d.ComponentName)}</td>");
                    sb.Append($"<td>{E(d.ChecklistItem)}</td>");
                    sb.Append($"<td>{E(d.FacilityArea)}</td>");
                    sb.Append($"<td>{E(d.DefectType)}</td>");
                    sb.Append($"<td class=\"num\">{d.ConfidencePercent:F0}</td>");
                    sb.Append($"<td class=\"ctr\">{d.StateGrade}</td>");
                    sb.Append($"<td class=\"num\">{d.Score}</td>");
                    sb.Append($"<td class=\"ctr\"><span class=\"badge\" style=\"background:{color}\">{E(d.Grade)}</span></td>");
                    sb.Append("</tr>");
                }
            }

            sb.Append("</tbody></table></section>");
        }

        private static void AppendElevation(StringBuilder sb, ReportData data)
        {
            sb.Append("<section><h2>5. 부재별 안전등급 입면도</h2>");
            sb.Append(SvgElevationBuilder.Build(data.GradeMap));
            sb.Append("</section>");
        }

        private static void AppendDistribution(StringBuilder sb, ReportData data)
        {
            sb.Append("<section><h2>6. 안전 등급 분포</h2>");
            sb.Append("<table class=\"charts\"><tr>");

            sb.Append("<td><div class=\"cap\">등급별 부재 수</div>");
            sb.Append(SvgChartBuilder.BuildBarChart(data.Distribution));
            sb.Append("</td>");

            sb.Append("<td><div class=\"cap\">등급별 비율</div>");
            sb.Append(SvgChartBuilder.BuildDonutChart(data.Distribution));
            sb.Append("</td>");

            sb.Append("</tr></table></section>");
        }

        private static void AppendConclusion(StringBuilder sb, ReportData data)
        {
            var v = data.Verdict;

            sb.Append("<section><h2>7. AI 분석 결과 총평</h2><table><tbody>");
            AppendVerdictRow(sb, "종합판정", v.Judgement);
            AppendVerdictRow(sb, "주요근거", v.Rationale);
            AppendVerdictRow(sb, "권고조치", v.Action);
            AppendVerdictRow(sb, "추가관찰", v.Observation);
            sb.Append("</tbody></table></section>");
        }

        private static void AppendVerdictRow(StringBuilder sb, string key, string value)
        {
            sb.Append($"<tr><th style=\"width:14%\">{E(key)}</th><td>{Dash(value)}</td></tr>");
        }

        private static void AppendLimits(StringBuilder sb, ReportData data)
        {
            sb.Append("<section><h2>8. 분석 한계 및 유의사항</h2><div class=\"note\">");
            sb.Append($"<p>{E(data.Disclaimer)}. 매뉴얼의 상태 판정은 책임기술자의 육안조사로 이루어지며, " +
                      "본 리포트는 그 판정을 AI 추론으로 근사한 보조 지표입니다.</p>");
            sb.Append("<p>균열폭(mm)은 촬영거리와 축척 정보가 없어 실측할 수 없으므로, " +
                      "결함의 크기는 분할 면적률로 대체 판정하였습니다.</p>");
            sb.Append("<p>촬영되지 않은 부재는 평가 대상에서 제외되어 등급이 부여되지 않았습니다. " +
                      "입면도의 회색 구간이 이에 해당하며, 해당 부재는 별도 점검이 필요합니다.</p>");
            sb.Append("<p>실제 법정 안전등급은 관련 기관과 책임기술자의 판정을 따라야 합니다.</p>");
            sb.Append("</div></section>");
        }

        private static void AppendFooter(StringBuilder sb, ReportData data)
        {
            sb.Append("<footer><span>BridgeSense DT</span>");
            sb.Append($"<span>{E(data.Facility.Name)} · {E(data.GeneratedAt)}</span></footer>");
        }

        private static string E(string value)
        {
            return SvgChartBuilder.Escape(value);
        }

        /// <summary>값이 비어 있으면 대시로 표시한다. 빈 칸은 누락인지 값이 없는 것인지 구분되지 않는다.</summary>
        private static string Dash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : E(value);
        }
    }
}
