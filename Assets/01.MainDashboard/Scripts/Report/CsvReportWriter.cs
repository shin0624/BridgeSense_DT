using System.IO;
using System.Text;

namespace BridgeSenseDT.Report
{
    /// <summary>
    /// 분석 결과를 CSV로 내보낸다. 이미지와 차트를 빼고 정형 데이터만 담는다.
    ///
    /// 외부 라이브러리에 의존하지 않는 유일한 표 형식이라 CSV를 택했다.
    /// xlsx와 docx는 겉보기와 달리 ZIP 컨테이너 안에 규격에 맞는 여러 파트를 조립해야 해서
    /// 별도 라이브러리 없이 만들기 어렵다.
    /// </summary>
    public static class CsvReportWriter
    {
        /// <summary>표를 넉넉히 담기 위한 열 수. 짧은 행도 이 폭에 맞춰 쉼표를 채운다.</summary>
        private const int ColumnCount = 11;

        public static void Write(ReportData data, string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Excel은 BOM이 없으면 UTF-8 파일을 시스템 기본 인코딩으로 읽어 한글이 깨진다.
            File.WriteAllText(filePath, Build(data), new UTF8Encoding(true));
        }

        public static string Build(ReportData data)
        {
            var sb = new StringBuilder();

            WriteHeader(sb, data);
            WriteFacility(sb, data);
            WriteSummary(sb, data);
            WriteDefects(sb, data);
            WriteComponents(sb, data);
            WriteDistribution(sb, data);
            WriteAreaScores(sb, data);
            WriteConclusion(sb, data);

            return sb.ToString();
        }

        private static void WriteHeader(StringBuilder sb, ReportData data)
        {
            Row(sb, "BridgeSense DT 교량 안전 분석 리포트");
            Blank(sb);
            Row(sb, "작성일자", data.GeneratedAt);
            Row(sb, "평가기준", data.EvaluationBasis);
            Row(sb, "유의사항", data.Disclaimer);
            Blank(sb);
            Blank(sb);
            Blank(sb);
        }

        private static void WriteFacility(StringBuilder sb, ReportData data)
        {
            var facility = data.Facility;

            Row(sb, "[1. 대상 시설물]");
            Row(sb, "항목", "값");
            Row(sb, "시설명", facility.Name);
            Row(sb, "소재지", facility.Location);
            Row(sb, "준공년도", facility.CompletionYear);
            Row(sb, "상부구조", facility.Superstructure);
            Row(sb, "하부구조", facility.Substructure);
            Row(sb, "연장(m)", facility.Length);
            Row(sb, "폭원(m)", facility.Width);
            Row(sb, "유효폭(m)", facility.UsableWidth);
            Row(sb, "경간수", facility.SpanCount);
            Row(sb, "최대경간장(m)", facility.MaxSpan);
            Row(sb, "설계하중", facility.DesignLoad);
            Row(sb, "관리주체", facility.Agency);
            Blank(sb);
        }

        private static void WriteSummary(StringBuilder sb, ReportData data)
        {
            var summary = data.Summary;

            Row(sb, "[2. 분석 결과 요약]");
            Row(sb, "항목", "값");
            Row(sb, "종합 안전등급", summary.Grade);
            Row(sb, "종합 상태점수", Number(summary.TotalScore));
            Row(sb, "주요시설 상태점수", summary.HasMajor ? Number(summary.MajorScore) : "해당없음");
            Row(sb, "일반시설 상태점수", summary.HasGeneral ? Number(summary.GeneralScore) : "해당없음");
            Row(sb, "부대시설 상태점수", summary.HasAncillary ? Number(summary.AncillaryScore) : "해당없음");
            Row(sb, "분석 사진 수", summary.ImageCount.ToString());
            Row(sb, "검출 결함 수", summary.DefectCount.ToString());
            Row(sb, "강제하향 여부", summary.ForcedDowngrade ? "Y" : "N");
            Blank(sb);
        }

        private static void WriteDefects(StringBuilder sb, ReportData data)
        {
            Row(sb, "[3. 결함별 검출 결과]");
            Row(sb, "No", "부재ID", "부재명", "점검항목", "시설영역", "결함유형",
                "신뢰도(%)", "상태", "점수", "등급");

            foreach (var defect in data.Defects)
            {
                Row(sb,
                    defect.No.ToString(),
                    defect.ComponentId,
                    defect.ComponentName,
                    defect.ChecklistItem,
                    defect.FacilityArea,
                    defect.DefectType,
                    Number(defect.ConfidencePercent, "0"),
                    defect.StateGrade.ToString(),
                    defect.Score.ToString(),
                    defect.Grade);
            }

            Blank(sb);
        }

        private static void WriteComponents(StringBuilder sb, ReportData data)
        {
            Row(sb, "[4. 부재별 종합 등급]");
            Row(sb, "부재ID", "부재명", "점검항목", "상태", "점수", "등급", "검출결함수");

            foreach (var component in data.Components)
            {
                Row(sb,
                    component.ComponentId,
                    component.ComponentName,
                    component.ChecklistItem,
                    component.StateGrade.ToString(),
                    component.Score.ToString(),
                    component.Grade,
                    component.DefectCount.ToString());
            }

            Blank(sb);
        }

        private static void WriteDistribution(StringBuilder sb, ReportData data)
        {
            Row(sb, "[5. 안전 등급 분포]");
            Row(sb, "등급", "등급명", "부재수", "비율(%)");

            foreach (var row in data.Distribution)
                Row(sb, row.Grade, row.GradeLabel, row.Count.ToString(), Number(row.Percent));

            Blank(sb);
        }

        private static void WriteAreaScores(StringBuilder sb, ReportData data)
        {
            Row(sb, "[6. 시설영역별 상태점수]");
            Row(sb, "시설영역", "가중치", "평가항목수", "상태점수", "비고");

            foreach (var area in data.AreaScores)
            {
                Row(sb,
                    area.AreaName,
                    Number(area.Weight, "0"),
                    area.ItemCount.ToString(),
                    area.Applicable ? Number(area.Score) : "",
                    area.Applicable ? "" : "해당없음");
            }

            Blank(sb);
        }

        private static void WriteConclusion(StringBuilder sb, ReportData data)
        {
            Row(sb, "[7. 총평]");
            Row(sb, "구분", "내용");
            Row(sb, "종합판정", data.Verdict.Judgement);
            Row(sb, "주요근거", data.Verdict.Rationale);
            Row(sb, "권고조치", data.Verdict.Action);
            Row(sb, "추가관찰", data.Verdict.Observation);
        }

        private static string Number(float value, string format = "0.#")
        {
            return value.ToString(format);
        }

        private static void Blank(StringBuilder sb)
        {
            Row(sb);
        }

        /// <summary>한 행을 쓴다. 열 수가 모자라면 빈 칸으로 채워 표 폭을 맞춘다.</summary>
        private static void Row(StringBuilder sb, params string[] cells)
        {
            for (int i = 0; i < ColumnCount; i++)
            {
                if (i > 0)
                    sb.Append(',');

                if (i < cells.Length)
                    sb.Append(Escape(cells[i]));
            }

            sb.Append('\n');
        }

        /// <summary>
        /// 쉼표·큰따옴표·줄바꿈이 들어 있으면 큰따옴표로 감싸고 내부 따옴표는 두 번 겹쳐 쓴다.
        /// 검출 근거 문장에 쉼표가 들어가므로(면적률 3.2%, 신뢰도 27%) 이 처리가 없으면 열이 밀린다.
        /// </summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            bool needsQuotes = value.IndexOf(',') >= 0
                || value.IndexOf('"') >= 0
                || value.IndexOf('\n') >= 0
                || value.IndexOf('\r') >= 0;

            if (!needsQuotes)
                return value;

            return '"' + value.Replace("\"", "\"\"") + '"';
        }
    }
}
