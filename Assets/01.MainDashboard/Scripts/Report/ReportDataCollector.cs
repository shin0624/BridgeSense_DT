using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.Bridge3D;
using BridgeSenseDT.BridgeData;
using BridgeSenseDT.Session;
using BridgeSenseDT.UI;

namespace BridgeSenseDT.Report
{
    /// <summary>
    /// 흩어져 있는 분석 결과를 보고서 한 부 분량으로 모은다.
    ///
    /// CSV와 HTML이 각자 데이터를 긁어오면 같은 보고서인데 숫자가 다를 수 있다.
    /// 여기서 한 번만 모아 두 포맷이 그 결과를 함께 쓰도록 한다.
    /// 부재 단위 등급은 입면도·등급분포 팝업과 같은 산출기를 쓴다.
    /// </summary>
    public static class ReportDataCollector
    {
        private const string BasisText = "「제3종시설물 안전등급 평가 매뉴얼」(국토안전관리원, 2023.12)";

        private const string DisclaimerText =
            "본 결과는 AI 추론 기반 보조지표이며 법정 안전등급을 대체하지 않음";

        public static ReportData Collect()
        {
            var manager = AnalysisSessionManager.Instance;

            var data = new ReportData
            {
                GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd"),
                EvaluationBasis = BasisText,
                Disclaimer = DisclaimerText,
            };

            if (manager == null)
                return data;

            var session = manager.CurrentSession;
            var report = manager.LastReport;
            var gradeMap = BridgeComponentGradeResolver.Resolve(report, BridgeModelRegistry.Instance);
            data.GradeMap = gradeMap;

            FillFacility(data, session);
            FillSummary(data, report, gradeMap);
            FillDefects(data, report);
            FillComponents(data, report);
            FillDistribution(data, gradeMap);
            FillAreaScores(data, report);
            FillImages(data, session, report);
            FillConclusion(data, report);

            return data;
        }

        private static void FillFacility(ReportData data, AnalysisSession session)
        {
            if (session == null)
                return;

            data.Facility.Name = session.BridgeName;
            data.Facility.Location = session.Location;

            BridgeSpec spec = BridgeSpecRepository.Find(session.BridgeName, session.Location);
            if (spec == null)
                return;

            // 조서에서 찾았으면 주소도 조서 표기로 바꾼다. 사용자 입력보다 정확한 행정구역 표기다.
            data.Facility.Location = spec.GetAddress();
            data.Facility.Route = spec.route;
            data.Facility.CompletionYear = spec.year;
            data.Facility.Superstructure = spec.sup;
            data.Facility.Substructure = spec.sub;
            data.Facility.Length = spec.len;
            data.Facility.Width = spec.width;
            data.Facility.UsableWidth = spec.usableWidth;
            data.Facility.SpanCount = spec.spans;
            data.Facility.MaxSpan = spec.maxSpan;
            data.Facility.DesignLoad = spec.designLoad;
            data.Facility.Agency = spec.agency;
        }

        private static void FillSummary(ReportData data, BridgeAssessmentReport report, ComponentGradeMap gradeMap)
        {
            data.Summary.ImageCount = report?.PerImage?.Count ?? 0;
            data.Summary.DefectCount = CountDefects(report);

            var bridge = report?.Bridge;
            if (bridge == null)
                return;

            data.Summary.Grade = bridge.grade;
            data.Summary.TotalScore = bridge.totalScore;
            data.Summary.MajorScore = bridge.majorScore;
            data.Summary.GeneralScore = bridge.generalScore;
            data.Summary.AncillaryScore = bridge.ancillaryScore;
            data.Summary.ForcedDowngrade = bridge.forcedDowngrade;

            // 영역별 점수는 평가된 항목이 없으면 0이 들어온다.
            // 0점(최악)과 해당없음을 구분하려면 항목 존재 여부를 따로 봐야 한다.
            data.Summary.HasMajor = HasArea(bridge, FacilityArea.Major);
            data.Summary.HasGeneral = HasArea(bridge, FacilityArea.General);
            data.Summary.HasAncillary = HasArea(bridge, FacilityArea.Ancillary);
        }

        private static bool HasArea(BridgeAssessmentResult bridge, FacilityArea area)
        {
            return bridge.evaluations != null
                && bridge.evaluations.Any(e => !e.NotApplicable && SafetyGradeEvaluator.GetFacilityArea(e.item) == area);
        }

        private static int CountDefects(BridgeAssessmentReport report)
        {
            if (report?.PerImage == null)
                return 0;

            int total = 0;
            foreach (var image in report.PerImage)
                total += image.Evaluation?.defects?.Count ?? 0;

            return total;
        }

        private static void FillDefects(ReportData data, BridgeAssessmentReport report)
        {
            if (report?.PerImage == null)
                return;

            int no = 1;

            foreach (var image in report.PerImage)
            {
                var evaluation = image.Evaluation;
                if (evaluation?.defects == null)
                    continue;

                foreach (var defect in evaluation.defects)
                {
                    data.Defects.Add(new DefectRow
                    {
                        No = no++,
                        ComponentId = BuildComponentId(image),
                        ComponentName = BuildComponentName(image),
                        ChecklistItem = SafetyGradeEvaluator.GetChecklistItemName(image.ChecklistItem),
                        FacilityArea = GetAreaName(SafetyGradeEvaluator.GetFacilityArea(image.ChecklistItem)),
                        DefectType = SafetyGradeEvaluator.GetDefectName(defect.type),
                        ConfidencePercent = defect.confidence * 100f,
                        StateGrade = evaluation.stateGrade,
                        Score = evaluation.score,
                        Grade = image.DisplayGrade,
                    });
                }
            }
        }

        /// <summary>
        /// 부재별 종합 등급. 같은 부재를 여러 장 촬영했을 수 있으므로 부재 단위로 묶는다.
        /// 등급이 겹치면 더 나쁜 쪽을 남기는데, 이는 입면도가 칸 색을 정하는 방식과 같다.
        /// </summary>
        private static void FillComponents(ReportData data, BridgeAssessmentReport report)
        {
            if (report?.PerImage == null)
                return;

            var merged = new Dictionary<string, ComponentRow>();

            foreach (var image in report.PerImage)
            {
                string key = BuildComponentId(image);

                if (!merged.TryGetValue(key, out var row))
                {
                    row = new ComponentRow
                    {
                        ComponentId = key,
                        ComponentName = BuildComponentName(image),
                        ChecklistItem = SafetyGradeEvaluator.GetChecklistItemName(image.ChecklistItem),
                        StateGrade = image.StateGrade,
                        Score = image.Evaluation?.score ?? 0,
                        Grade = image.DisplayGrade,
                    };
                    merged[key] = row;
                }
                else if (SafetyGradeEvaluator.GradeToRank(image.DisplayGrade) < SafetyGradeEvaluator.GradeToRank(row.Grade))
                {
                    row.StateGrade = image.StateGrade;
                    row.Score = image.Evaluation?.score ?? 0;
                    row.Grade = image.DisplayGrade;
                }

                row.DefectCount += image.Evaluation?.defects?.Count ?? 0;
            }

            data.Components.AddRange(merged.Values);
        }

        private static void FillDistribution(ReportData data, ComponentGradeMap gradeMap)
        {
            var counts = new Dictionary<string, int>();
            foreach (string grade in new[] { "A", "B", "C", "D", "E" })
                counts[grade] = 0;

            foreach (string grade in gradeMap.EnumerateGrades())
            {
                if (counts.ContainsKey(grade))
                    counts[grade]++;
            }

            int total = gradeMap.GradedComponentCount;

            foreach (var pair in counts)
            {
                data.Distribution.Add(new GradeDistributionRow
                {
                    Grade = pair.Key,
                    GradeLabel = GradeColorMap.GetLabel(pair.Key),
                    Count = pair.Value,
                    Percent = total > 0 ? pair.Value * 100f / total : 0f,
                });
            }
        }

        private static void FillAreaScores(ReportData data, BridgeAssessmentReport report)
        {
            AddAreaRow(data, report, FacilityArea.Major, SafetyGradeEvaluator.WeightMajor);
            AddAreaRow(data, report, FacilityArea.General, SafetyGradeEvaluator.WeightGeneral);
            AddAreaRow(data, report, FacilityArea.Ancillary, SafetyGradeEvaluator.WeightAncillary);
        }

        private static void AddAreaRow(ReportData data, BridgeAssessmentReport report, FacilityArea area, float weight)
        {
            var evaluations = report?.Bridge?.evaluations;

            int itemCount = evaluations?.Count(
                e => !e.NotApplicable && SafetyGradeEvaluator.GetFacilityArea(e.item) == area) ?? 0;

            float score = area switch
            {
                FacilityArea.Major => report?.Bridge?.majorScore ?? 0f,
                FacilityArea.General => report?.Bridge?.generalScore ?? 0f,
                _ => report?.Bridge?.ancillaryScore ?? 0f,
            };

            data.AreaScores.Add(new FacilityAreaRow
            {
                AreaName = GetAreaName(area),
                Weight = weight,
                ItemCount = itemCount,
                Score = score,
                Applicable = itemCount > 0,
            });
        }

        private static void FillImages(ReportData data, AnalysisSession session, BridgeAssessmentReport report)
        {
            if (report?.PerImage == null || session == null)
                return;

            foreach (var image in report.PerImage)
            {
                var entry = session.FindEntry(image.EntryId);
                if (entry?.ImageBytes == null)
                    continue;

                var pair = new ImagePair
                {
                    EntryId = image.EntryId,
                    CapturedPart = image.CapturedPart,
                    Grade = image.DisplayGrade,
                    DefectSummary = image.DefectSummary,
                    ImageBytes = entry.ImageBytes,
                    ImageMimeType = GuessMimeType(entry.ImageFileName),
                    PixelWidth = image.Thumbnail != null ? image.Thumbnail.width : 0,
                    PixelHeight = image.Thumbnail != null ? image.Thumbnail.height : 0,
                };

                CollectBoxes(entry, pair);
                data.Images.Add(pair);
            }
        }

        /// <summary>
        /// 표시할 검출 사각형을 모은다.
        /// entry.Detections(원본 RT-DETR 검출)가 있으면 그것을 우선 쓰고, 없으면(저장 파일이
        /// bbox 원본 없이 결함 목록만 갖고 있는 과거 포맷인 경우 등) entry.Defects[].boxes를 쓴다.
        /// 화면(ElevationDetailView)과 같은 규칙이라 보고서와 화면이 같은 위치를 가리킨다.
        /// </summary>
        private static void CollectBoxes(AnalysisEntry entry, ImagePair pair)
        {
            if (entry.Detections != null && entry.Detections.Count > 0 && pair.PixelWidth > 0 && pair.PixelHeight > 0)
            {
                foreach (var detection in entry.Detections)
                {
                    pair.Boxes.Add(new BoxRect
                    {
                        XMin = detection.X1 / pair.PixelWidth,
                        YMin = detection.Y1 / pair.PixelHeight,
                        XMax = detection.X2 / pair.PixelWidth,
                        YMax = detection.Y2 / pair.PixelHeight,
                    });
                }

                return;
            }

            if (entry.Defects == null)
                return;

            foreach (var defect in entry.Defects)
            {
                if (defect.boxes == null)
                    continue;

                foreach (var box in defect.boxes)
                {
                    pair.Boxes.Add(new BoxRect
                    {
                        XMin = box.xMin,
                        YMin = box.yMin,
                        XMax = box.xMax,
                        YMax = box.yMax,
                    });
                }
            }
        }

        /// <summary>
        /// 총평 문장을 만든다.
        ///
        /// 문장을 규칙으로 조립하는 이유는, 매뉴얼이 요구하는 판단 근거를 사람이 읽을 수 있게
        /// 남기되 없는 사실을 지어내지 않기 위해서다. 등급과 최악 부재, 결함 유형처럼
        /// 실제 산출된 값만 문장에 넣는다.
        /// </summary>
        private static void FillConclusion(ReportData data, BridgeAssessmentReport report)
        {
            var bridge = report?.Bridge;
            if (bridge == null)
            {
                data.Verdict.Judgement = "분석 결과 없음";
                return;
            }

            string grade = bridge.grade;
            data.Verdict.Judgement = $"{grade}등급({GradeColorMap.GetLabel(grade)})";

            ComponentRow worst = data.Components
                .OrderBy(c => SafetyGradeEvaluator.GradeToRank(c.Grade))
                .FirstOrDefault();

            DefectRow worstDefect = data.Defects
                .OrderBy(d => SafetyGradeEvaluator.GradeToRank(d.Grade))
                .ThenByDescending(d => d.ConfidencePercent)
                .FirstOrDefault();

            if (worstDefect != null)
            {
                data.Verdict.Rationale =
                    $"{worstDefect.ComponentName} {worstDefect.DefectType} 신뢰도 {worstDefect.ConfidencePercent:F0}%, " +
                    $"상태 {worstDefect.StateGrade} 판정이 종합 등급을 결정";
            }
            else
            {
                data.Verdict.Rationale = "검출된 결함 없음";
            }

            data.Verdict.Action = BuildAction(grade, worst);
            data.Verdict.Observation = BuildObservation(data);
        }

        private static string BuildAction(string grade, ComponentRow worst)
        {
            string target = worst != null ? worst.ComponentName : "대상 부재";

            switch (grade)
            {
                case "E":
                    return $"{target} 긴급 정밀안전진단 및 사용제한 검토 필요";
                case "D":
                    return $"{target} 정밀점검을 통한 손상 원인 규명 후 보수 우선 실시";
                case "C":
                    return $"{target} 경과 관찰 후 계획 보수 실시";
                case "B":
                    return "정기점검 주기에 따라 상태 관찰";
                default:
                    return "현 상태 유지, 정기점검 주기 준수";
            }
        }

        /// <summary>
        /// 추가 관찰 문구. 중대 손상으로 강제 하향된 경우와
        /// 두 번째로 나쁜 부재가 있는 경우를 우선해서 알린다.
        /// </summary>
        private static string BuildObservation(ReportData data)
        {
            if (data.Summary.ForcedDowngrade)
                return "구조안전성에 영향을 미치는 중대 손상이 검출되어 종합 등급이 강제 하향됨";

            var second = data.Components
                .OrderBy(c => SafetyGradeEvaluator.GradeToRank(c.Grade))
                .Skip(1)
                .FirstOrDefault();

            if (second != null)
                return $"{second.ComponentName} {second.Grade}등급, 진행성 손상 여부 관찰 필요";

            int ungradedNote = data.Summary.ImageCount;
            return ungradedNote > 0
                ? "촬영되지 않은 부재는 평가 대상에서 제외되었으므로 별도 점검 필요"
                : "해당 없음";
        }

        private static string BuildComponentId(ImageAssessmentResult image)
        {
            // 3D 모델의 오브젝트 이름 규칙과 맞춰 두면 보고서와 화면을 대조하기 쉽다.
            bool isSubstructure = Array.IndexOf(
                BridgeComponentGradeResolver.SubstructureItems, image.ChecklistItem) >= 0;

            string prefix = isSubstructure ? "Pier" : "Span";
            return image.ComponentIndex > 0 ? $"{prefix}_{image.ComponentIndex}" : prefix;
        }

        private static string BuildComponentName(ImageAssessmentResult image)
        {
            return string.IsNullOrWhiteSpace(image.CapturedPart)
                ? SafetyGradeEvaluator.GetChecklistItemName(image.ChecklistItem)
                : image.CapturedPart;
        }

        private static string GetAreaName(FacilityArea area)
        {
            switch (area)
            {
                case FacilityArea.Major: return "주요시설";
                case FacilityArea.General: return "일반시설";
                default: return "부대시설";
            }
        }

        private static string GuessMimeType(string fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            return extension == ".png" ? "image/png" : "image/jpeg";
        }
    }
}
