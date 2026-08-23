using System.Collections.Generic;

namespace BridgeSenseDT.Session
{
    /// <summary>
    /// .bsdt 저장 파일의 최상위 스키마.
    ///
    /// 런타임 모델(AnalysisSession)을 직접 직렬화하지 않고 이 DTO를 거치는 이유는,
    /// 런타임 타입의 필드명이나 구조를 바꿔도 이미 저장된 파일이 깨지지 않게 하기 위해서다.
    /// 저장 포맷은 formatVersion으로만 관리한다.
    ///
    /// 이미지는 byte[]로 두면 Newtonsoft가 자동으로 base64 문자열로 기록하므로
    /// 별도 인코딩 처리 없이 파일 하나에 자기완결적으로 담긴다.
    /// </summary>
    public class AnalysisSaveFile
    {
        public int formatVersion;
        public string savedAtUtc;    // ISO8601
        public string modelVersion;  // 어떤 AI 모델로 만든 결과인지. 모델 교체 후 과거 이력 해석에 필요
        public BridgeInfoDto bridge;
        public List<EntryDto> entries = new List<EntryDto>();
        public AssessmentSnapshotDto assessmentSnapshot; // 분석 전이면 null
    }

    public class BridgeInfoDto
    {
        public string name;
        public string location;
    }

    public class EntryDto
    {
        public string entryId;
        public string capturedPart;
        public string imageFileName;
        public byte[] imageData;   // Newtonsoft가 base64 문자열로 직렬화한다
        public bool analyzed;

        public List<DetectionDto> detections = new List<DetectionDto>();
        public List<DefectDto> defects = new List<DefectDto>();
    }

    /// <summary>RT-DETR 검출 1건. 좌표는 원본 이미지 픽셀 기준(xyxy).</summary>
    public class DetectionDto
    {
        public int classId;
        public float score;
        public float x1;
        public float y1;
        public float x2;
        public float y2;
    }

    /// <summary>
    /// 등급 산정 입력이 되는 결함 1건.
    /// defectType을 int가 아니라 문자열로 저장하는 이유는 저장 파일을 열어봤을 때 읽을 수 있게 하기 위해서다.
    /// 알 수 없는 이름이 들어오면 불러오기 단계에서 해당 결함만 건너뛴다.
    /// </summary>
    public class DefectDto
    {
        public string defectType;
        public float confidence;
        public float maskAreaRatio;
        public float estimatedWidthMm;
        public bool isStructurallyCritical;
    }

    public class AssessmentSnapshotDto
    {
        public string grade;
        public float totalScore;
        public float majorScore;
        public float generalScore;
        public float ancillaryScore;
    }
}
