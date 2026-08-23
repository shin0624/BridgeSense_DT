using System.Collections.Generic;
using BridgeSenseDT.Assessment;

namespace BridgeSenseDT.Session
{
    /// <summary>
    /// 등록된 이미지 1장과 그 분석 산출물. UI(InputImageObject)가 아니라 이쪽이 데이터의 원본이며,
    /// InputImageObject는 이 값을 화면에 비추는 역할만 한다.
    /// </summary>
    public class AnalysisEntry
    {
        public string EntryId;       // 등록 순번 문자열
        public string CapturedPart;  // 사용자가 입력한 촬영 부재
        public byte[] ImageBytes;    // 업로드된 원본 파일 바이트. 재인코딩 없이 저장·복원한다
        public string ImageFileName; // 원본 파일명(표시용)

        public bool Analyzed;        // 등록만 하고 아직 분석하지 않은 항목과 구분하기 위한 플래그

        // RT-DETR 원본 검출. 등급 산정에는 쓰이지 않지만 향후 bbox 오버레이 시각화에 필요해 함께 보관한다.
        public List<RtdetrDetection> Detections = new List<RtdetrDetection>();

        // 등급 산정의 실제 입력값. SegFormer 마스크(512x512)는 여기서 클래스별 면적률로 이미 축약돼 있어
        // 원본 마스크를 저장하지 않아도 등급을 그대로 재계산할 수 있다.
        public List<DetectedDefect> Defects = new List<DetectedDefect>();
    }

    /// <summary>
    /// 저장 시점의 등급 산정 결과 사본. 표시에는 저장된 Defects로 다시 계산한 값을 쓰고,
    /// 이 값은 "저장 당시에는 어떤 등급이었는가"를 확인하기 위한 참고용으로만 둔다.
    /// 등급 규칙(SafetyGradeEvaluator의 임계값)이 바뀌면 두 값이 달라질 수 있다.
    /// </summary>
    public class AssessmentSnapshot
    {
        public string Grade;
        public float TotalScore;
        public float MajorScore;
        public float GeneralScore;
        public float AncillaryScore;
    }

    /// <summary>
    /// 한 번의 분석 세션 전체를 담는 단일 진실 공급원.
    /// 지금까지 교량정보는 입력 필드에, 등록 목록은 자식 GameObject에, 카운터는 컨트롤러 필드에
    /// 흩어져 있어서 저장과 초기화 때마다 여러 곳을 훑어야 했다. 이 클래스가 그 역할을 모두 가져간다.
    /// </summary>
    public class AnalysisSession
    {
        public string BridgeName = "";
        public string Location = "";
        public List<AnalysisEntry> Entries = new List<AnalysisEntry>();
        public AnalysisSessionState State = AnalysisSessionState.Editing;
        public AssessmentSnapshot Snapshot; // 아직 분석 전이면 null

        public bool HasEntries => Entries.Count > 0;

        /// <summary>
        /// 다음에 부여할 등록 순번. 저장본을 불러온 뒤 이 값으로 카운터를 되돌리지 않으면
        /// 기존 항목과 같은 순번이 다시 발급되어 결과 카드 매칭이 어긋난다.
        /// 삭제된 순번은 재사용하지 않으므로 "가장 큰 값 + 1"로 계산한다.
        /// </summary>
        public int GetNextEntryIdSeed()
        {
            int maxId = 0;
            foreach (var entry in Entries)
            {
                if (int.TryParse(entry.EntryId, out int parsed) && parsed > maxId)
                    maxId = parsed;
            }
            return maxId;
        }

        public AnalysisEntry FindEntry(string entryId) // 결과 카드와 항목을 순번으로 연결할 때 사용
        {
            foreach (var entry in Entries)
            {
                if (entry.EntryId == entryId)
                    return entry;
            }
            return null;
        }
    }
}
