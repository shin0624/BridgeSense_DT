using System.Collections;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.Session;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    // inferenceCheckPopup 자신에게 붙는 전용 컨트롤러 - "이 팝업 안에서 일어나는 확인 로직"만 담당한다.
    // 팝업을 여닫는 건 PopupPanelController/MainDashboardManager의 몫이고,
    // 분석 결과를 어디에 어떻게 그릴지는 AnalysisSessionManager와 AnalysisResultListView의 몫이다.
    // 이 스크립트는 "네"를 눌렀을 때 추론을 돌려 세션에 결과를 채워 넣는 것까지만 책임진다.
    public class InferenceCheckPopupController : MonoBehaviour
    {
        [SerializeField] private Button inferenceCheckYesButton; // 이 팝업의 "네" 버튼
        [SerializeField] private BridgeImageRegistrationController bridgeImageRegistrationController; // 화면에 떠 있는 등록 항목들을 조회하기 위한 참조

        [Tooltip("분석이 진행되는 동안 대신 띄울 로딩 팝업. 비워두면 로딩 화면 없이 바로 분석한다")]
        [SerializeField] private GameObject inferenceLoadingPopup;
        [SerializeField] private InferenceLoadingPopupController inferenceLoadingPopupController;

        private bool isRunning; // 추론이 도는 동안 "네"를 다시 눌러 중복 실행되는 것을 막는다

        //이 팝업은 열고 닫힐 때마다 SetActive가 토글된다.
        private void OnEnable()
        {
            inferenceCheckYesButton.onClick.AddListener(OnYesClicked); // "네" 클릭 시 OnYesClicked 호출
        }

        private void OnDisable()
        {
            inferenceCheckYesButton.onClick.RemoveListener(OnYesClicked); // OnEnable과 짝을 맞춰 제거
        }

        private void OnYesClicked() // "네" 클릭 시 등록된 이미지 전체를 순회하며 AI 추론을 수행하고 결과를 세션에 기록
        {
            if (isRunning)
                return; // 로딩 중에 버튼이 다시 눌리는 경우를 막는다(팝업 전환 애니메이션 중 더블클릭 등)

            // 이 코루틴 자신이 이 컴포넌트(확인 팝업)에서 도는데, 코루틴 안에서 확인 팝업을 곧바로
            // SetActive(false)한다. Unity는 코루틴을 소유한 컴포넌트가 비활성화되면 그 코루틴을
            // 그 자리에서 즉시 멈춰버리므로, 여기서 시작하면 첫 yield에서 영영 멈춘 채로 남는다.
            // 그래서 분석 중에도 계속 활성 상태인 MainDashboardManager가 대신 코루틴을 돌리게 한다.
            MainDashboardManager.Instance.StartCoroutine(RunAnalysis());
        }

        /// <summary>
        /// 등록된 이미지를 한 장씩 분석하며 로딩 팝업의 진행률을 갱신한다.
        ///
        /// AiInferenceManager.AnalyzeImage 자체는 동기 호출이라 진행률을 세분화해서 받을 수는 없다.
        /// 그래서 "이미지 몇 장 중 몇 번째"를 진행률로 삼는다 - 이미지 한 장을 끝낼 때마다
        /// 한 프레임을 넘겨줘야 그 사이에 로딩 화면이 실제로 그려진다. 동기 foreach로 전부 돌리면
        /// 전체가 한 프레임 안에 끝나버려서 로딩 팝업이 뜬 모습을 볼 새도 없이 사라진다.
        /// </summary>
        private IEnumerator RunAnalysis()
        {
            isRunning = true;

            var entries = bridgeImageRegistrationController.GetRegisteredEntries();
            int total = entries.Length;

            OpenLoadingPopup();
            MainDashboardManager.Instance.ClosePopupPanel(gameObject); // 확인 팝업은 로딩 팝업으로 교체

            yield return null; // 로딩 팝업의 첫 화면(0%)이 그려지도록 한 프레임 넘긴다

            var session = AnalysisSessionManager.Instance.CurrentSession;
            int completed = 0;

            foreach (var view in entries) // 화면에 떠 있는 InputImageObject 전체를 순회
            {
                var entry = session.FindEntry(view.EntryId); // 화면 항목에 대응하는 세션 데이터를 찾는다
                if (entry != null)
                {
                    BridgeAnalysisResult analysis = AiInferenceManager.Instance.AnalyzeImage(view.Thumbnail); // 이미지 한 장당 RT-DETR+SegFormer 추론 1회 실행

                    entry.Detections = analysis.Detections;            // bbox 원본은 향후 오버레이 시각화를 위해 보관
                    entry.Defects = DefectExtractor.Extract(analysis); // 등급 산정 입력으로 쓸 결함 목록으로 축약
                    entry.Analyzed = true;
                }

                completed++;
                ReportProgress(total == 0 ? 1f : (float)completed / total);

                yield return null; // 진행률 갱신을 화면에 반영할 시간을 준다
            }

            // 등급 산정, 결과 카드 렌더링, 화면 상태 전환은 세션 매니저가 처리한다.
            // 저장본을 불러오는 경로도 같은 메서드를 거치므로 두 경로의 결과가 어긋나지 않는다.
            AnalysisSessionManager.Instance.NotifyAnalysisCompleted();

            CloseLoadingPopup();
            isRunning = false;
        }

        private void OpenLoadingPopup()
        {
            if (inferenceLoadingPopup == null)
                return;

            inferenceLoadingPopupController?.ResetProgress();
            MainDashboardManager.Instance.OpenPopupPanel(inferenceLoadingPopup);
        }

        private void ReportProgress(float progress)
        {
            if (inferenceLoadingPopupController != null)
                inferenceLoadingPopupController.Report(progress);
        }

        private void CloseLoadingPopup()
        {
            if (inferenceLoadingPopup != null)
                MainDashboardManager.Instance.ClosePopupPanel(inferenceLoadingPopup);
        }
    }
}
