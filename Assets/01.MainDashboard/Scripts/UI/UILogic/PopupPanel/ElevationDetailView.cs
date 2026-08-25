using System.Collections.Generic;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.Session;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 입면도 팝업 우측의 부재 상세 패널.
    /// 입면도에서 고른 경간·교각의 촬영 사진과 AI 검출 결과를 보여준다.
    ///
    /// 사진 위의 사각형은 RT-DETR이 내놓은 bbox다(DetectionBoxOverlay 참고).
    /// </summary>
    public class ElevationDetailView : MonoBehaviour
    {
        [SerializeField] private GameObject contentRoot;   // 선택된 부재가 있을 때만 보여줄 묶음
        [SerializeField] private GameObject emptyRoot;     // 아직 아무것도 고르지 않았을 때 보여줄 안내

        [Header("부재 정보")]
        [SerializeField] private TMP_Text gradeBadgeText;      // 등급 문자(A~E)
        [SerializeField] private Image gradeBadgeBackground;   // 등급 색이 칠해질 배지 배경
        [SerializeField] private TMP_Text partNameText;        // "교각 7"
        [SerializeField] private TMP_Text partCategoryText;    // "교각·교대"
        [SerializeField] private TMP_Text descriptionText;     // 검출 근거 문장

        [Header("사진")]
        [SerializeField] private RawImage photoImage;
        [SerializeField] private DetectionBoxOverlay boxOverlay;

        [Tooltip("검출 사각형이 표시되지 않을 때 어디서 끊기는지 확인하기 위한 로그. 확인이 끝나면 끈다")]
        [SerializeField] private bool logDiagnostics = true;

        private void OnEnable()
        {
            Clear(); // 팝업을 다시 열면 이전 선택은 지운 상태에서 시작한다
        }

        /// <summary>고른 부재의 분석 결과를 표시한다.</summary>
        public void Show(ImageAssessmentResult result, string partLabel)
        {
            if (result == null)
            {
                Clear();
                return;
            }

            SetActive(contentRoot, true);
            SetActive(emptyRoot, false);

            SetText(gradeBadgeText, result.DisplayGrade);
            if (gradeBadgeBackground != null)
                gradeBadgeBackground.color = GradeColorMap.GetColor(result.DisplayGrade);

            SetText(partNameText, partLabel);
            SetText(partCategoryText, SafetyGradeEvaluator.GetChecklistItemName(result.ChecklistItem));
            SetText(descriptionText, BuildDescription(result));

            ShowPhoto(result);
        }

        public void Clear()
        {
            SetActive(contentRoot, false);
            SetActive(emptyRoot, true);

            if (boxOverlay != null)
                boxOverlay.Clear();

            if (photoImage != null)
                photoImage.texture = null;
        }

        private void ShowPhoto(ImageAssessmentResult result)
        {
            if (photoImage != null)
                photoImage.texture = result.Thumbnail;

            if (boxOverlay == null)
            {
                Log($"[{result.EntryId}] Box Overlay가 연결되지 않았습니다.");
                return;
            }

            if (result.Thumbnail == null)
            {
                Log($"[{result.EntryId}] 사진 텍스처가 없습니다. 원본이 이미 파괴됐을 수 있습니다.");
                boxOverlay.Clear();
                return;
            }

            // 검출 좌표는 세션 데이터에 들어있다. 화면에 표시되는 결과에는 등급 요약만 담기 때문이다.
            var session = AnalysisSessionManager.Instance != null
                ? AnalysisSessionManager.Instance.CurrentSession
                : null;

            var entry = session?.FindEntry(result.EntryId);
            if (entry == null)
            {
                Log($"[{result.EntryId}] 세션에서 해당 항목을 찾지 못했습니다.");
                boxOverlay.Clear();
                return;
            }

            var boxes = CollectBoxes(entry, result.Thumbnail.width, result.Thumbnail.height, out string source);
            Log($"[{result.EntryId}] 사각형 {boxes.Count}건({source}), 사진 {result.Thumbnail.width}x{result.Thumbnail.height}");

            boxOverlay.Show(boxes);
        }

        /// <summary>
        /// 사진 위에 그릴 사각형을 모은다.
        ///
        /// RT-DETR 검출이 있으면 그쪽을 쓴다. 객체 검출기라 결함 하나를 하나의 사각형으로 잡아준다.
        /// 다만 실측에서 RT-DETR은 대부분의 이미지에서 임계값을 넘지 못했으므로,
        /// 없을 때는 SegFormer 마스크에서 뽑아둔 사각형으로 대신한다.
        /// 두 출처를 함께 그리면 같은 결함에 사각형이 겹쳐 그려져 오작동처럼 보인다.
        /// </summary>
        private static List<DefectBox> CollectBoxes(AnalysisEntry entry, int width, int height, out string source)
        {
            var boxes = new List<DefectBox>();

            if (entry.Detections != null && entry.Detections.Count > 0 && width > 0 && height > 0)
            {
                source = "RT-DETR";

                foreach (var detection in entry.Detections)
                {
                    boxes.Add(new DefectBox
                    {
                        xMin = detection.X1 / width,
                        yMin = detection.Y1 / height,
                        xMax = detection.X2 / width,
                        yMax = detection.Y2 / height,
                    });
                }

                return boxes;
            }

            source = "SegFormer 마스크";

            if (entry.Defects != null)
            {
                foreach (var defect in entry.Defects)
                {
                    if (defect.boxes != null)
                        boxes.AddRange(defect.boxes);
                }
            }

            return boxes;
        }

        private void Log(string message)
        {
            if (logDiagnostics)
                Debug.Log("입면도 상세: " + message, this);
        }

        /// <summary>
        /// 검출 근거 문장을 만든다.
        /// 등급 판정 시 이미 만들어둔 근거 문장이 있으므로 그것을 쓰고, 없으면 결함 요약으로 대신한다.
        /// </summary>
        private static string BuildDescription(ImageAssessmentResult result)
        {
            string rationale = result.Evaluation?.rationale;
            return string.IsNullOrWhiteSpace(rationale) ? result.DefectSummary : rationale;
        }

        private static void SetActive(GameObject target, bool value)
        {
            if (target != null)
                target.SetActive(value);
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }
    }
}
