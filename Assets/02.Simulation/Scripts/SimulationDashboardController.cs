using BridgeSenseDT.Assessment;
using BridgeSenseDT.Bridge3D;
using BridgeSenseDT.BridgeData;
using BridgeSenseDT.Session;
using BridgeSenseDT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeSenseDT.Simulation
{
    /// <summary>
    /// 시뮬레이션 대시보드 패널의 UI를 총괄한다.
    ///
    /// 데이터 흐름: 슬라이더/토글 조작 → SimulationEngine이 가상 BridgeAssessmentReport 생성
    /// → BridgeGradeVisualizer.Apply()로 3D 색상 반영 (AI 분석 결과를 반영할 때와 동일한 경로 재사용)
    /// → 이 컨트롤러가 등급·점수·역산 카드 UI를 갱신.
    ///
    /// 패널이 켜질 때마다(OnEnable) 현재 세션 기준으로 다시 계산한다 — 다른 패널에서 새로 분석하거나
    /// 세션을 불러온 뒤 시뮬레이션 패널로 돌아왔을 때 이전 결과가 남아있지 않도록 하기 위함이다.
    /// </summary>
    public class SimulationDashboardController : MonoBehaviour
    {
        [Header("기준점 토글")]
        [SerializeField] private Button baselineFromAnalysisButton; // "AI 실측 결과 기준"
        [SerializeField] private Button baselineFromDesignSpecButton; // "준공년도 기준"
        [SerializeField] private GameObject baselineFromAnalysisSelectedMark; // 선택 표시(선택). 비워도 무방
        [SerializeField] private GameObject baselineFromDesignSpecSelectedMark;

        [Header("환경 프리셋")]
        [SerializeField] private Button presetInlandButton;
        [SerializeField] private Button presetDryButton;
        [SerializeField] private Button presetWetDryButton;
        [SerializeField] private Button presetCoastalButton;
        [SerializeField] private TMP_Text presetNameText;

        [Header("경과 연수 슬라이더")]
        [SerializeField] private Slider yearsSlider; // 0~100년 범위로 인스펙터에서 설정
        [SerializeField] private TMP_Text yearsValueText;

        [Header("결과 표시")]
        [SerializeField] private TMP_Text gradeText;         // "A"~"E"
        [SerializeField] private TMP_Text totalScoreText;    // 종합 상태점수
        [SerializeField] private TMP_Text rationaleText;     // 산정 근거 문구
        [SerializeField] private Image gradeBadgeImage;      // 등급 배경색(선택, 비워도 무방)

        [Header("역산 카드 (E)")]
        [SerializeField] private TMP_Dropdown targetGradeDropdown; // "B","C","D","E" 등 목표 등급 선택
        [SerializeField] private TMP_Text yearsUntilGradeText;     // "N년 뒤 D등급 도달 예상"

        [Header("연결")]
        [SerializeField] private BridgeGradeVisualizer gradeVisualizer; // 비워두면 씬에서 자동 검색

        private SimulationBaselineMode baselineMode = SimulationBaselineMode.FromAnalysis;
        private EnvironmentPreset environment = EnvironmentPreset.Inland;
        private float coverDepthMm = AgingModel.DefaultCoverDepthMm;

        private BridgeAssessmentReport analysisBaselineSnapshot; // FromAnalysis 모드 계산에 쓸 기준 리포트(패널이 켜질 때 캡처)

        private void Awake()
        {
            if (gradeVisualizer == null)
                gradeVisualizer = FindFirstObjectByType<BridgeGradeVisualizer>();
        }

        private void OnEnable()
        {
            CaptureAnalysisBaseline();
            BindListenersOnce();
            RefreshBaselineAvailability();
            Recompute();
        }

        private bool listenersBound;

        private void BindListenersOnce()
        {
            if (listenersBound)
                return;
            listenersBound = true;

            if (baselineFromAnalysisButton != null)
                baselineFromAnalysisButton.onClick.AddListener(() => SetBaselineMode(SimulationBaselineMode.FromAnalysis));
            if (baselineFromDesignSpecButton != null)
                baselineFromDesignSpecButton.onClick.AddListener(() => SetBaselineMode(SimulationBaselineMode.FromDesignSpec));

            if (presetInlandButton != null)
                presetInlandButton.onClick.AddListener(() => SetEnvironment(EnvironmentPreset.Inland));
            if (presetDryButton != null)
                presetDryButton.onClick.AddListener(() => SetEnvironment(EnvironmentPreset.Dry));
            if (presetWetDryButton != null)
                presetWetDryButton.onClick.AddListener(() => SetEnvironment(EnvironmentPreset.WetDryCycle));
            if (presetCoastalButton != null)
                presetCoastalButton.onClick.AddListener(() => SetEnvironment(EnvironmentPreset.CoastalSalt));

            if (yearsSlider != null)
                yearsSlider.onValueChanged.AddListener(_ => Recompute());

            if (targetGradeDropdown != null)
                targetGradeDropdown.onValueChanged.AddListener(_ => RefreshYearsUntilGradeCard());
        }

        /// <summary>패널이 켜질 때 AI 실측 리포트를 스냅샷으로 떠 둔다. 슬라이더를 움직여도 원본 리포트는 바뀌지 않는다.</summary>
        private void CaptureAnalysisBaseline()
        {
            analysisBaselineSnapshot = AnalysisSessionManager.Instance != null
                ? AnalysisSessionManager.Instance.LastReport
                : null;
        }

        /// <summary>실측 결과가 없으면 FromAnalysis 버튼을 비활성화하고 자동으로 설계기준 모드로 전환한다.</summary>
        private void RefreshBaselineAvailability()
        {
            bool hasAnalysis = analysisBaselineSnapshot?.Bridge != null;

            if (baselineFromAnalysisButton != null)
                baselineFromAnalysisButton.interactable = hasAnalysis;

            if (!hasAnalysis && baselineMode == SimulationBaselineMode.FromAnalysis)
                baselineMode = SimulationBaselineMode.FromDesignSpec;

            UpdateBaselineSelectedMarks();
        }

        private void SetBaselineMode(SimulationBaselineMode mode)
        {
            if (mode == SimulationBaselineMode.FromAnalysis && analysisBaselineSnapshot?.Bridge == null)
                return; // 실측 결과가 없으면 전환 무시(버튼도 비활성화돼 있지만 이중 방어)

            baselineMode = mode;
            UpdateBaselineSelectedMarks();
            Recompute();
        }

        private void UpdateBaselineSelectedMarks()
        {
            if (baselineFromAnalysisSelectedMark != null)
                baselineFromAnalysisSelectedMark.SetActive(baselineMode == SimulationBaselineMode.FromAnalysis);
            if (baselineFromDesignSpecSelectedMark != null)
                baselineFromDesignSpecSelectedMark.SetActive(baselineMode == SimulationBaselineMode.FromDesignSpec);
        }

        private void SetEnvironment(EnvironmentPreset preset)
        {
            environment = preset;
            if (presetNameText != null)
                presetNameText.text = AgingModel.GetPresetName(preset);
            Recompute();
        }

        /// <summary>현재 슬라이더/토글 상태로 리포트를 다시 계산하고 화면 전체를 갱신한다.</summary>
        private void Recompute()
        {
            float sliderYears = yearsSlider != null ? yearsSlider.value : 0f;
            if (yearsValueText != null)
                yearsValueText.text = $"{sliderYears:F0}년 뒤";

            var input = BuildSimulationInput(sliderYears);
            var report = ComputeReport(input);

            ApplyReportToUi(report);
            gradeVisualizer?.Apply(report);

            RefreshYearsUntilGradeCard();
        }

        /// <summary>
        /// 슬라이더가 가리키는 "지금부터 N년 후"를 실제 경과 연수로 환산한다.
        /// FromDesignSpec 모드에서는 준공년도부터 지금까지 이미 지난 세월을 더해야
        /// 오래된 교량이 슬라이더 0에서도 이미 노후화된 상태로 시작한다.
        /// FromAnalysis 모드는 AI 실측 시점을 t=0으로 보므로 보정하지 않는다.
        /// </summary>
        private SimulationInput BuildSimulationInput(float sliderYears)
        {
            float years = sliderYears;
            if (baselineMode == SimulationBaselineMode.FromDesignSpec)
                years += TryGetElapsedYearsSinceCompletion();

            return new SimulationInput { Years = years, Environment = environment, CoverDepthMm = coverDepthMm };
        }

        private BridgeAssessmentReport ComputeReport(SimulationInput input)
        {
            if (baselineMode == SimulationBaselineMode.FromAnalysis && analysisBaselineSnapshot?.Bridge != null)
                return SimulationEngine.SimulateFromAnalysis(analysisBaselineSnapshot, input);

            return SimulationEngine.SimulateFromDesignSpec(input);
        }

        private void ApplyReportToUi(BridgeAssessmentReport report)
        {
            var bridge = report?.Bridge;
            if (bridge == null)
                return;

            if (gradeText != null)
                gradeText.text = bridge.grade;

            if (totalScoreText != null)
                totalScoreText.text = $"{bridge.totalScore:F2}점";

            if (rationaleText != null)
                rationaleText.text = bridge.rationale;

            if (gradeBadgeImage != null)
                gradeBadgeImage.color = GradeColorMap.GetColor(bridge.grade);
        }

        /// <summary>"N년 뒤 목표등급 도달" 카드를 갱신한다. "N년"은 슬라이더와 같은 척도(지금부터 N년 후)로 맞춘다.</summary>
        private void RefreshYearsUntilGradeCard()
        {
            if (yearsUntilGradeText == null)
                return;

            string targetGrade = ResolveTargetGrade();
            if (string.IsNullOrEmpty(targetGrade))
                return;

            // YearsUntilGrade가 탐색하는 input.Years는 SimulationInput의 "절대 경과 연수"이므로,
            // FromDesignSpec 모드에서 슬라이더 기준(지금부터 N년 후)으로 보여주려면 이미 지난 세월만큼 빼서 맞춘다.
            float elapsedOffset = baselineMode == SimulationBaselineMode.FromDesignSpec
                ? TryGetElapsedYearsSinceCompletion()
                : 0f;

            System.Func<SimulationInput, string> evaluateGradeAt = input => ComputeReport(input).Bridge.grade;

            float absoluteYears = SimulationEngine.YearsUntilGrade(targetGrade, environment, coverDepthMm, evaluateGradeAt);

            if (float.IsPositiveInfinity(absoluteYears))
            {
                yearsUntilGradeText.text = $"현재 조건에서는 200년 내 {targetGrade}등급에 도달하지 않습니다.";
                return;
            }

            float yearsFromNow = Mathf.Max(0f, absoluteYears - elapsedOffset);
            yearsUntilGradeText.text = $"현재 조건 유지 시 약 {yearsFromNow:F0}년 뒤 {targetGrade}등급 도달 예상";
        }

        private string ResolveTargetGrade()
        {
            if (targetGradeDropdown == null || targetGradeDropdown.options.Count == 0)
                return "D"; // 드롭다운을 아직 안 붙였으면 가장 흔히 쓰는 기준(D등급, 정밀안전진단 대상)으로 기본 계산

            return targetGradeDropdown.options[targetGradeDropdown.value].text;
        }

        /// <summary>
        /// 현재 세션 교량의 준공년도부터 지금까지 이미 지난 연수. 조회 실패 시 0(보정 없음).
        /// FromDesignSpec 모드가 "제원만으로" 동작할 수 있는 핵심 — BridgeSpecRepository에서
        /// 준공년도를 찾으면 슬라이더 0년 지점이 "지금 이 순간"이 되도록 이미 지난 세월을 더해준다.
        /// </summary>
        private int TryGetElapsedYearsSinceCompletion()
        {
            var session = AnalysisSessionManager.Instance?.CurrentSession;
            if (session == null)
                return 0;

            BridgeSpec spec = BridgeSpecRepository.Find(session.BridgeName, session.Location);
            if (spec == null || !int.TryParse(spec.year, out int completionYear))
                return 0;

            return System.DateTime.Now.Year - completionYear;
        }
    }
}
