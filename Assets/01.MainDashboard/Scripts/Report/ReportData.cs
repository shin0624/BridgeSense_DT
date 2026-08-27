using System.Collections.Generic;

namespace BridgeSenseDT.Report
{
    /// <summary>보고서 한 부에 담기는 모든 값. CSV와 HTML이 이 하나를 함께 쓴다.</summary>
    public class ReportData
    {
        public string GeneratedAt;      // 작성일자
        public string EvaluationBasis;  // 평가기준 문구
        public string Disclaimer;       // 유의사항 문구

        public FacilityInfo Facility = new FacilityInfo();
        public ResultSummary Summary = new ResultSummary();

        public List<DefectRow> Defects = new List<DefectRow>();
        public List<ComponentRow> Components = new List<ComponentRow>();
        public List<GradeDistributionRow> Distribution = new List<GradeDistributionRow>();
        public List<FacilityAreaRow> AreaScores = new List<FacilityAreaRow>();
        public List<ImagePair> Images = new List<ImagePair>();

        public Conclusion Verdict = new Conclusion();

        // 5번 섹션(입면도)을 그리는 데 필요한 부재 단위 등급.
        // 화면의 입면도 팝업과 같은 산출 결과를 그대로 넘겨 두 그림이 어긋나지 않게 한다.
        public UI.ComponentGradeMap GradeMap;
    }

    /// <summary>1. 대상 시설물. 현황조서에서 찾은 값이며 없으면 빈 문자열로 둔다.</summary>
    public class FacilityInfo
    {
        public string Name;
        public string Location;
        public string Route;
        public string CompletionYear;
        public string Superstructure;
        public string Substructure;
        public string Length;
        public string Width;
        public string UsableWidth;
        public string SpanCount;
        public string MaxSpan;
        public string DesignLoad;
        public string Agency;
    }

    /// <summary>2. 분석 결과 요약.</summary>
    public class ResultSummary
    {
        public string Grade;            // 종합 안전등급 A~E
        public float TotalScore;        // 종합 상태점수
        public float MajorScore;
        public float GeneralScore;
        public float AncillaryScore;
        public bool HasMajor;           // 해당 영역에 평가된 항목이 있는지
        public bool HasGeneral;
        public bool HasAncillary;
        public int ImageCount;          // 분석 사진 수
        public int DefectCount;         // 검출 결함 수
        public bool ForcedDowngrade;    // 중대 손상으로 강제 하향되었는지
    }

    /// <summary>3. 결함별 검출 결과 한 줄.</summary>
    public class DefectRow
    {
        public int No;
        public string ComponentId;    // 부재 식별자(Pier_7 등)
        public string ComponentName;  // 사람이 읽는 부재명(교각 7)
        public string ChecklistItem;  // 점검 항목명
        public string FacilityArea;   // 시설영역
        public string DefectType;     // 결함 유형
        public float AreaRatioPercent;
        public float ConfidencePercent;
        public char StateGrade;       // a~e
        public int Score;
        public string Grade;          // A~E
    }

    /// <summary>4. 부재별 종합 등급 한 줄.</summary>
    public class ComponentRow
    {
        public string ComponentId;
        public string ComponentName;
        public string ChecklistItem;
        public char StateGrade;
        public int Score;
        public string Grade;
        public int DefectCount;
    }

    /// <summary>5. 안전 등급 분포 한 줄.</summary>
    public class GradeDistributionRow
    {
        public string Grade;
        public string GradeLabel;
        public int Count;
        public float Percent;
    }

    /// <summary>6. 시설영역별 상태점수 한 줄.</summary>
    public class FacilityAreaRow
    {
        public string AreaName;
        public float Weight;
        public int ItemCount;
        public float Score;
        public bool Applicable; // 해당 영역에 평가된 항목이 하나도 없으면 false
    }

    /// <summary>3번 섹션의 이미지 한 쌍. 원본과 검출 결과를 좌우로 대비시킨다.</summary>
    public class ImagePair
    {
        public string EntryId;
        public string CapturedPart;
        public string Grade;
        public string DefectSummary;
        public byte[] ImageBytes;   // 원본 파일 바이트. base64로 심는다
        public string ImageMimeType;
        public int PixelWidth;
        public int PixelHeight;
        public List<BoxRect> Boxes = new List<BoxRect>(); // 0~1 정규화, 좌상단 원점
    }

    /// <summary>검출 위치 사각형. 정규화 좌표라 이미지 크기와 무관하게 그릴 수 있다.</summary>
    public struct BoxRect
    {
        public float XMin;
        public float YMin;
        public float XMax;
        public float YMax;
    }

    /// <summary>8. AI 총평.</summary>
    public class Conclusion
    {
        public string Judgement;   // 종합판정
        public string Rationale;   // 주요근거
        public string Action;      // 권고조치
        public string Observation; // 추가관찰
    }
}
