using System.Collections;
using BridgeSenseDT.BridgeData;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// StartScene에서 무거운 자원을 미리 불러온 뒤 메인 대시보드로 넘어가는 로딩 컨트롤러.
    ///
    /// 미리 불러오는 대상은 두 가지다.
    /// 1. AI 모델 두 개(합쳐 185MB). 대시보드 진입 시점에 읽으면 화면이 수 초 멈춘다.
    ///    AiInferenceManager가 DontDestroyOnLoad라 여기서 읽어두면 그대로 넘어간다.
    /// 2. 교량 제원 자료(8MB). BridgeSpecRepository는 static이라 씬이 바뀌어도 유지된다.
    ///    미리 부르지 않으면 3D 뷰어를 처음 열 때 한 번 멈춘다.
    /// </summary>
    public class StartSceneLoader : MonoBehaviour
    {
        [SerializeField] private string nextSceneName = "MainDashboardScene";

        [Header("표시 요소(선택)")]
        [SerializeField] private Image progressBar;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text progressValue;

        [Header("전환")]
        [Tooltip("준비가 끝나면 나타나는 시작 버튼. 비워두면 준비 즉시 자동으로 넘어간다")]
        [SerializeField] private Button enterButton;

        [Tooltip("페이드아웃할 UI 묶음. 보통 StartScene의 Canvas에 CanvasGroup을 붙여 지정한다")]
        [SerializeField] private CanvasGroup fadeCanvasGroup;

        [SerializeField] private float fadeDuration = 0.6f;

        [Tooltip("준비가 끝났을 때 시작 버튼이 나타나는 시간")]
        [SerializeField] private float enterButtonFadeInDuration = 0.4f;

        [Tooltip("로딩이 너무 빨리 끝나도 이 시간만큼은 화면을 유지한다. 문구가 깜빡이고 사라지는 것을 막는다")]
        [SerializeField] private float minimumDisplaySeconds = 1.2f;

        // 단계별 진행률 구간. AI 모델이 가장 오래 걸리므로 가장 넓게 잡는다.
        private const float ModelPhaseEnd = 0.7f;
        private const float SpecPhaseEnd = 0.85f;

        private float startTime;
        private AsyncOperation sceneLoadOperation;
        private bool enterRequested;

        private IEnumerator Start()
        {
            startTime = Time.realtimeSinceStartup;

            if (enterButton != null)
                enterButton.gameObject.SetActive(false); // 준비가 끝나기 전에는 누를 수 없어야 한다

            Report(0f, "초기화하는 중");
            yield return null;

            yield return LoadAiModels();
            yield return LoadBridgeSpec();
            yield return PrepareNextScene();
            yield return WaitForMinimumDisplay();
            yield return WaitForEnter();
            yield return FadeOutAndEnter();
        }

        private IEnumerator LoadAiModels()
        {
            var manager = AiInferenceManager.Instance;

            if (manager == null)
            {
                // StartScene에 매니저를 두지 않으면 미리 불러오는 의미가 없어진다.
                // 조용히 넘어가면 대시보드에서 갑자기 멈추는 원인을 찾기 어려우므로 알린다.
                Debug.LogWarning(
                    "StartScene에 AiInferenceManager가 없어 AI 모델을 미리 불러오지 못했습니다. " +
                    "대시보드에서 첫 분석 직전에 모델을 읽느라 화면이 멈출 수 있습니다.");

                Report(ModelPhaseEnd, "AI 모델 준비를 건너뜀");
                yield break;
            }

            yield return manager.InitializeRoutine((progress, message) =>
            {
                Report(progress * ModelPhaseEnd, message);
            });
        }

        private IEnumerator LoadBridgeSpec()
        {
            Report(ModelPhaseEnd, "교량 제원 자료를 불러오는 중");
            yield return null; // 위 문구를 먼저 그린 뒤 읽기 시작한다

            BridgeSpecRepository.Preload();

            Report(SpecPhaseEnd, "교량 제원 자료 준비 완료");
            yield return null;
        }

        /// <summary>로딩이 순식간에 끝나도 화면이 깜빡이고 사라지지 않도록 최소 시간을 채운다.</summary>
        private IEnumerator WaitForMinimumDisplay()
        {
            float elapsed = Time.realtimeSinceStartup - startTime;
            float remaining = minimumDisplaySeconds - elapsed;

            if (remaining > 0f)
                yield return new WaitForSecondsRealtime(remaining);
        }

        /// <summary>
        /// 대시보드 씬을 미리 읽어두되 전환은 하지 않는다.
        /// 버튼을 누른 뒤에 읽기 시작하면 페이드가 끝난 자리에서 다시 기다려야 한다.
        /// </summary>
        private IEnumerator PrepareNextScene()
        {
            Report(SpecPhaseEnd, "대시보드를 준비하는 중");

            sceneLoadOperation = SceneManager.LoadSceneAsync(nextSceneName);
            if (sceneLoadOperation == null)
            {
                Debug.LogError(
                    $"'{nextSceneName}' 씬을 불러오지 못했습니다. " +
                    "File > Build Profiles의 씬 목록에 포함돼 있는지 확인해 주세요.");
                yield break;
            }

            sceneLoadOperation.allowSceneActivation = false; // 버튼을 누를 때까지 전환을 미룬다

            // allowSceneActivation이 false인 동안 progress는 0.9에서 멈추고 isDone은 끝내 true가 되지 않는다.
            // 그래서 완료 조건을 isDone이 아니라 0.9 도달로 잡아야 한다.
            while (sceneLoadOperation.progress < 0.9f)
            {
                float sceneProgress = Mathf.Clamp01(sceneLoadOperation.progress / 0.9f);
                Report(Mathf.Lerp(SpecPhaseEnd, 1f, sceneProgress), "대시보드를 준비하는 중");
                yield return null;
            }

            Report(1f, "준비 완료");
        }

        /// <summary>시작 버튼을 눌러줄 때까지 기다린다. 버튼을 지정하지 않았으면 기다리지 않고 넘어간다.</summary>
        private IEnumerator WaitForEnter()
        {
            if (enterButton == null)
                yield break;

            enterButton.gameObject.SetActive(true);
            enterButton.onClick.AddListener(OnEnterClicked);

            FadeInEnterButton();

            while (!enterRequested)
                yield return null;

            enterButton.onClick.RemoveListener(OnEnterClicked);
        }

        /// <summary>
        /// 시작 버튼을 서서히 나타나게 한다.
        ///
        /// 나타나는 동안에는 클릭을 받지 않는다.
        /// 아직 보이지 않는 버튼이 눌리면 사용자가 무엇을 눌렀는지 알 수 없다.
        /// </summary>
        private void FadeInEnterButton()
        {
            CanvasGroup group = GetOrAddCanvasGroup(enterButton.gameObject);

            group.alpha = 0f;
            group.blocksRaycasts = false;

            group.DOFade(1f, enterButtonFadeInDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(enterButton.gameObject)
                .OnComplete(() => group.blocksRaycasts = true);
        }

        /// <summary>
        /// 페이드에는 CanvasGroup이 필요하다. 없으면 만들어 붙인다.
        /// 버튼 배경과 글자를 따로 페이드하면 요소가 늘어날 때마다 코드를 고쳐야 하므로
        /// 묶음 단위로 투명도를 다루는 편이 낫다.
        /// </summary>
        private static CanvasGroup GetOrAddCanvasGroup(GameObject target)
        {
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            return group != null ? group : target.AddComponent<CanvasGroup>();
        }

        private void OnEnterClicked()
        {
            enterRequested = true;
            enterButton.interactable = false; // 페이드가 도는 동안 다시 눌리지 않도록 막는다
        }

        /// <summary>UI를 서서히 지운 뒤 대시보드로 전환한다.</summary>
        private IEnumerator FadeOutAndEnter()
        {
            if (sceneLoadOperation == null)
                yield break; // 씬 준비에 실패한 경우

            if (fadeCanvasGroup != null)
            {
                fadeCanvasGroup.blocksRaycasts = false; // 사라지는 중에 뒤쪽이 눌리지 않도록 한다

                yield return fadeCanvasGroup
                    .DOFade(0f, fadeDuration)
                    .SetEase(Ease.InQuad)
                    .SetLink(gameObject)
                    .WaitForCompletion();
            }

            // 이 시점에는 씬이 이미 다 읽혀 있어 곧바로 전환된다.
            sceneLoadOperation.allowSceneActivation = true;
        }

        /// <summary>
        /// 진행 상황을 화면에 반영한다.
        ///
        /// 내부 진행률은 0~1로 다룬다. Image.fillAmount가 그 범위를 요구하기 때문
        /// 사람이 읽는 백분율은 표시하는 순간에만 100을 곱함
        /// </summary>
        private void Report(float progress, string message)
        {
            if (progressBar != null)
                progressBar.fillAmount = progress;

            if (progressValue != null)
                progressValue.text = Mathf.RoundToInt(progress * 100f) + "%";

            if (statusText != null)
                statusText.text = message;
        }
    }
}
