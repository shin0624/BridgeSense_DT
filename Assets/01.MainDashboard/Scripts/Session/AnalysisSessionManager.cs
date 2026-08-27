using System.Collections.Generic;
using System.IO;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.UI;
using SFB;
using UnityEngine;

namespace BridgeSenseDT.Session
{
    /// <summary>
    /// 한 번의 분석 세션 데이터를 소유하고, 저장·불러오기·새로 시작을 담당하는 매니저.
    /// MainDashboardManager(패널 전환), AiInferenceManager(AI 추론)와 같은 씬 로컬 싱글톤 패턴을 쓰되
    /// 관심사는 셋 다 다르므로 클래스를 분리한다.
    ///
    /// 이 매니저가 생기기 전에는 교량정보가 입력 필드에, 등록 목록이 자식 GameObject에,
    /// 카운터가 컨트롤러 필드에, 리포트가 팝업 컨트롤러에 흩어져 있어서
    /// 저장하려면 네 곳을 훑고 초기화하려면 네 곳을 각각 지워야 했다.
    /// </summary>
    public class AnalysisSessionManager : MonoBehaviour
    {
        public static AnalysisSessionManager Instance { get; private set; }

        // ImageUploadPanel은 InputAndAnalyzePanel 안에서 AnalyzeResultPanel과 같은 자리를 그대로 덮고 있는
        // 오버레이 패널이다. AnalyzeResultPanel(및 그 안의 InputImagePanel)은 항상 켜져 있고,
        // Editing/Analyzed 전환은 오직 이 덮개 하나를 켜고 끄는 것으로 이뤄진다.
        [SerializeField] private GameObject imageUploadPanel;                              // Editing 상태에서만 보이는 업로드 패널
        [SerializeField] private BridgeImageRegistrationController registrationController; // 등록 목록과 입력 필드를 그리는 뷰
        [SerializeField] private AnalysisResultListView resultListView;                    // 결과 카드를 그리는 뷰

        [Header("전환 애니메이션(선택)")]
        [Tooltip("ImageUploadPanel이 사라지고 나타날 때 쓸 페이드. 비워두면 즉시 SetActive로 전환한다")]
        [SerializeField] private PanelCrossfadeTransition panelCrossfade;

        public AnalysisSession CurrentSession { get; private set; }
        public bool IsDirty { get; private set; }          // 마지막 저장 이후 바뀐 내용이 있는지
        public string CurrentFilePath { get; private set; } // 아직 한 번도 저장하지 않았으면 null
        public BridgeAssessmentReport LastReport { get; private set; } // 3D 등급 시각화 등 후속 단계에서 참조

        /// <summary>
        /// 등급 산정 결과가 새로 만들어졌을 때 발생한다.
        /// 3D 뷰어의 손상 목록·등급 색상은 InputAndAnalyzePanel과 다른 패널에 있어
        /// 매니저가 직접 참조를 들고 호출하기보다 이 이벤트를 구독하는 편이 결합이 덜하다.
        ///
        /// 구독하는 쪽이 비활성 상태일 때 발생한 결과는 놓치므로,
        /// 구독과 별개로 활성화 시점에 LastReport를 직접 읽어 한 번 반영해야 한다.
        /// </summary>
        public event System.Action<BridgeAssessmentReport> ReportChanged;

        public bool HasSaveTarget => !string.IsNullOrEmpty(CurrentFilePath);

        public string CurrentFileName =>
            HasSaveTarget ? Path.GetFileNameWithoutExtension(CurrentFilePath) : "분석 상태가 변경될 경우 안내문구가 표시됩니다.";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            CurrentSession = new AnalysisSession(); // 데이터만 먼저 만들어 둔다. 화면 반영은 Start에서
        }

        private void Start()
        {
            // 시작 시에는 항상 업로드 패널이 보이는 상태. 씬 진입 첫 프레임부터 굳이 페이드를
            // 재생할 이유가 없으므로 애니메이션 없이 즉시 반영한다.
            ApplyState(AnalysisSessionState.Editing, animate: false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>내용이 바뀌었음을 기록한다. 등록·삭제·분석 등 세션을 변경하는 모든 지점에서 호출한다.</summary>
        public void MarkDirty()
        {
            IsDirty = true;
        }

        /// <summary>
        /// 아무것도 등록되지 않은 새 세션으로 되돌린다.
        /// 저장 여부 확인은 이 메서드의 책임이 아니다. 호출하는 쪽(툴바)에서 IsDirty를 보고 먼저 확인할 것.
        /// </summary>
        public void NewSession()
        {
            CurrentSession = new AnalysisSession();
            CurrentFilePath = null;
            IsDirty = false;
            LastReport = null;

            registrationController.ClearAll(); // 등록 목록 파괴, 입력 필드 비우고 잠금 해제, 업로더 초기화
            resultListView.Clear();

            ApplyState(AnalysisSessionState.Editing);
        }

        /// <summary>
        /// 현재 저장 경로에 덮어쓴다. 아직 저장한 적이 없으면 다른 이름으로 저장으로 넘어간다.
        /// 일반적인 편집기의 저장 동작과 같다.
        /// </summary>
        public bool Save()
        {
            if (!HasSaveTarget)
                return SaveAs();

            return WriteToFile(CurrentFilePath);
        }

        /// <summary>파일 다이얼로그로 경로를 받아 저장한다. 사용자가 취소하면 false를 반환한다.</summary>
        public bool SaveAs()
        {
            var extensions = new[]
            {
                new ExtensionFilter("BridgeSense 분석 파일", AnalysisSaveSerializer.FileExtension),
            };

            string path = StandaloneFileBrowser.SaveFilePanel(
                "BridgeSense DT_분석 데이터 저장",
                AnalysisSaveSerializer.GetDefaultSaveDirectory(),
                BuildDefaultFileName(),
                extensions);

            if (string.IsNullOrEmpty(path))
                return false; // 사용자가 취소함

            // 다이얼로그가 확장자를 붙여주지 않는 경우가 있어 직접 보정한다.
            if (!path.EndsWith(AnalysisSaveSerializer.FileExtensionWithDot, System.StringComparison.OrdinalIgnoreCase))
                path += AnalysisSaveSerializer.FileExtensionWithDot;

            return WriteToFile(path);
        }

        /// <summary>파일 다이얼로그로 저장본을 골라 불러온다. 사용자가 취소하면 false를 반환한다.</summary>
        public bool LoadWithDialog()
        {
            var extensions = new[]
            {
                new ExtensionFilter("BridgeSense 분석 파일", AnalysisSaveSerializer.FileExtension),
            };

            string[] paths = StandaloneFileBrowser.OpenFilePanel(
                "BridgeSense DT_분석 이력 불러오기",
                AnalysisSaveSerializer.GetDefaultSaveDirectory(),
                extensions,
                false);

            if (paths.Length == 0 || string.IsNullOrEmpty(paths[0]))
                return false; // 사용자가 취소함

            return Load(paths[0]);
        }

        public bool Load(string filePath)
        {
            AnalysisSession loaded;
            try
            {
                loaded = AnalysisSaveSerializer.LoadFromFile(filePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"분석 이력을 불러오지 못했습니다: {e.Message}");
                return false;
            }

            CurrentSession = loaded;
            CurrentFilePath = filePath;
            IsDirty = false;

            registrationController.RebuildFromSession(loaded); // 저장돼 있던 항목들을 다시 그린다

            if (loaded.State == AnalysisSessionState.Analyzed)
            {
                // AI를 다시 돌리지 않는다. 저장된 결함 목록으로 등급만 재계산한다.
                // 이렇게 하면 SafetyGradeEvaluator의 임계값을 개선했을 때 과거 저장본에도 소급 적용된다.
                RefreshResults();
                WarnIfSnapshotDiffers(loaded);
            }
            else
            {
                LastReport = null;
                resultListView.Clear();
            }

            ApplyState(loaded.State);
            return true;
        }

        /// <summary>
        /// 결과 화면에서 편집 상태로 되돌린다.
        /// 등록된 항목과 이미 산정된 결과는 그대로 두고 화면만 바꾸므로,
        /// 이미지를 더 추가한 뒤 다시 분석할 수 있다.
        /// 이 경로가 없으면 한 번 분석한 세션은 전부 버리는 것 말고는 손댈 방법이 없다.
        /// </summary>
        public void ReturnToEditing()
        {
            ApplyState(AnalysisSessionState.Editing);
        }

        /// <summary>
        /// 추론이 끝나 세션의 각 항목에 결과가 채워진 뒤 호출한다.
        /// 등급을 산정해 화면에 반영하고 저장용 스냅샷을 갱신한다.
        /// </summary>
        public void NotifyAnalysisCompleted()
        {
            RefreshResults();
            UpdateSnapshot();
            MarkDirty();
            ApplyState(AnalysisSessionState.Analyzed);
        }

        /// <summary>세션에 담긴 결함 목록으로 등급을 산정하고 결과 카드를 다시 그린다.</summary>
        public void RefreshResults()
        {
            LastReport = BridgeAssessmentCoordinator.Assess(BuildInputsFromSession());
            resultListView.Render(LastReport);
            ReportChanged?.Invoke(LastReport); // 3D 뷰어 쪽 손상 목록·등급 색상 갱신
        }

        /// <summary>
        /// 등급 산정에 넣을 입력을 만든다.
        /// 썸네일은 화면에 이미 떠 있는 InputImageObject가 소유한 텍스처를 그대로 빌려 쓰고,
        /// 결함 목록은 세션 데이터에서 가져온다. 방금 분석한 경우든 불러온 경우든 같은 경로를 탄다.
        /// </summary>
        private List<ImageAnalysisInput> BuildInputsFromSession()
        {
            var inputs = new List<ImageAnalysisInput>();

            foreach (var view in registrationController.GetRegisteredEntries())
            {
                var entry = CurrentSession.FindEntry(view.EntryId);
                if (entry == null)
                    continue; // 세션에 없는 화면 항목은 무시(정상적으로는 발생하지 않음)

                inputs.Add(new ImageAnalysisInput
                {
                    EntryId = entry.EntryId,
                    CapturedPart = entry.CapturedPart,
                    Thumbnail = view.Thumbnail,
                    Defects = entry.Defects,
                });
            }

            return inputs;
        }

        private void UpdateSnapshot()
        {
            if (LastReport?.Bridge == null)
                return;

            CurrentSession.Snapshot = new AssessmentSnapshot
            {
                Grade = LastReport.Bridge.grade,
                TotalScore = LastReport.Bridge.totalScore,
                MajorScore = LastReport.Bridge.majorScore,
                GeneralScore = LastReport.Bridge.generalScore,
                AncillaryScore = LastReport.Bridge.ancillaryScore,
            };
        }

        /// <summary>
        /// 저장 당시 등급과 지금 재계산한 등급이 다르면 알린다.
        /// 등급 규칙(임계값·가중치)이 바뀌었다는 뜻이므로 조용히 넘어가면 혼란스럽다.
        /// </summary>
        private void WarnIfSnapshotDiffers(AnalysisSession loaded)
        {
            if (loaded.Snapshot == null || LastReport?.Bridge == null)
                return;

            if (loaded.Snapshot.Grade != LastReport.Bridge.grade)
            {
                Debug.LogWarning(
                    $"등급 산정 기준이 저장 시점과 달라졌습니다. " +
                    $"저장 당시 {loaded.Snapshot.Grade}등급({loaded.Snapshot.TotalScore:F2}점) → " +
                    $"현재 기준 {LastReport.Bridge.grade}등급({LastReport.Bridge.totalScore:F2}점)");
            }
        }

        private bool WriteToFile(string filePath)
        {
            try
            {
                SyncBridgeInfoFromView(); // 입력 필드에만 있고 세션에 아직 반영되지 않은 값이 있을 수 있다
                AnalysisSaveSerializer.SaveToFile(CurrentSession, filePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"분석 데이터를 저장하지 못했습니다: {e.Message}");
                return false;
            }

            CurrentFilePath = filePath;
            IsDirty = false;
            Debug.Log($"분석 데이터를 저장했습니다: {filePath}");
            return true;
        }

        /// <summary>
        /// 교량명·소재지는 첫 항목을 등록할 때 세션에 기록되지만,
        /// 아직 아무것도 등록하지 않은 상태로 저장하면 입력 필드의 값이 누락된다. 그 경우를 메운다.
        /// </summary>
        private void SyncBridgeInfoFromView()
        {
            if (!CurrentSession.HasEntries)
                registrationController.WriteBridgeInfoTo(CurrentSession);
        }

        private string BuildDefaultFileName()
        {
            string bridgeName = string.IsNullOrWhiteSpace(CurrentSession.BridgeName)
                ? "분석"
                : CurrentSession.BridgeName;

            return $"{bridgeName}_{System.DateTime.Now:yyyyMMdd_HHmm}";
        }

        /// <summary>
        /// ImageUploadPanel은 덮개이므로 켜고 끄는 대상은 그것 하나뿐이다.
        /// Editing으로 갈 땐 나타나고(등록 화면을 다시 덮음), Analyzed로 갈 땐 사라진다(밑에 깔린
        /// AnalyzeResultPanel이 드러남). AnalyzeResultPanel 자체는 항상 활성 상태라 건드리지 않는다.
        /// </summary>
        private void ApplyState(AnalysisSessionState state, bool animate = true)
        {
            CurrentSession.State = state;

            bool editing = state == AnalysisSessionState.Editing;

            if (animate && panelCrossfade != null)
            {
                if (editing)
                    panelCrossfade.Show(from: null, to: imageUploadPanel);   // 페이드인
                else
                    panelCrossfade.Show(from: imageUploadPanel, to: null);   // 페이드아웃
            }
            else
            {
                imageUploadPanel.SetActive(editing);
            }
        }
    }
}
