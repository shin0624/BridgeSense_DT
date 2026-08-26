using System;
using System.IO;
using BridgeSenseDT.Report;
using BridgeSenseDT.Session;
using SFB;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// "보고서 출력" 버튼을 누르면 뜨는 포맷 선택 팝업.
    ///
    /// 두 포맷은 담는 내용이 달라서 사용자가 목적에 맞게 골라야 한다.
    /// 어떤 차이가 있는지 팝업에서 함께 알려준다.
    /// </summary>
    public class ReportExportPopupController : MonoBehaviour
    {
        [SerializeField] private Button htmlButton;
        [SerializeField] private Button csvButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button closeButton;

        [Tooltip("포맷별 특성을 안내할 문구. 비워두면 표시하지 않는다")]
        [SerializeField] private TMP_Text htmlDescriptionText;
        [SerializeField] private TMP_Text csvDescriptionText;

        [Tooltip("저장 결과를 알릴 문구. 비워두면 로그만 남는다")]
        [SerializeField] private TMP_Text statusText;

        private const string HtmlDescription =
            "브라우저 창에서 확인 가능한 보고서입니다.\n" +
            "그래프, 표, 이미지, 수치 데이터를 포함하며,\n" +
            "브라우저 내 인쇄 → PDF 저장이 가능합니다.";

        private const string CsvDescription =
            "수치 데이터 중심의 보고서입니다.\n" +
            "스프레드시트 등을 통한 후속작업에 용이합니다.";

        private void OnEnable()
        {
            htmlButton.onClick.AddListener(ExportHtml);
            csvButton.onClick.AddListener(ExportCsv);

            if (cancelButton != null)
                cancelButton.onClick.AddListener(Close);

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (htmlDescriptionText != null)
                htmlDescriptionText.text = HtmlDescription;

            if (csvDescriptionText != null)
                csvDescriptionText.text = CsvDescription;

            if (statusText != null)
                statusText.text = string.Empty;
        }

        private void OnDisable()
        {
            htmlButton.onClick.RemoveListener(ExportHtml);
            csvButton.onClick.RemoveListener(ExportCsv);

            if (cancelButton != null)
                cancelButton.onClick.RemoveListener(Close);

            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
        }

        private void ExportHtml()
        {
            Export("html", "HTML 보고서", (data, path) => HtmlReportWriter.Write(data, path));
        }

        private void ExportCsv()
        {
            Export("csv", "CSV 리포트", (data, path) => CsvReportWriter.Write(data, path));
        }

        private void Export(string extension, string formatName, Action<ReportData, string> writer)
        {
            var manager = AnalysisSessionManager.Instance;

            if (manager?.LastReport == null)
            {
                Report("먼저 AI 분석을 실행해 주세요.", isError: true);
                return;
            }

            var extensions = new[] { new ExtensionFilter(formatName, extension) };

            string path = StandaloneFileBrowser.SaveFilePanel(
                $"BridgeSense DT_{formatName} 저장",
                AnalysisSaveSerializer.GetDefaultSaveDirectory(),
                BuildDefaultFileName(manager),
                extensions);

            if (string.IsNullOrEmpty(path))
                return; // 사용자가 취소함

            // 다이얼로그가 확장자를 붙여주지 않는 경우가 있어 직접 보정한다.
            if (!path.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase))
                path += "." + extension;

            try
            {
                ReportData data = ReportDataCollector.Collect();
                writer(data, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"보고서를 저장하지 못했습니다: {e}");
                Report("보고서를 저장하지 못했습니다. " + e.Message, isError: true);
                return;
            }

            Debug.Log($"{formatName}를 저장했습니다: {path}");
            Report($"{formatName}를 저장했습니다.\n{Path.GetFileName(path)}", isError: false);

            Close();
        }

        private string BuildDefaultFileName(AnalysisSessionManager manager)
        {
            string bridgeName = manager.CurrentSession != null
                                && !string.IsNullOrWhiteSpace(manager.CurrentSession.BridgeName)
                ? manager.CurrentSession.BridgeName
                : "분석";

            return $"{bridgeName}_안전분석리포트_{DateTime.Now:yyyyMMdd}";
        }

        private void Report(string message, bool isError)
        {
            if (statusText != null)
                statusText.text = message;

            if (isError)
                Debug.LogWarning(message);
        }

        private void Close()
        {
            MainDashboardManager.Instance.ClosePopupPanel(gameObject);
        }
    }
}
