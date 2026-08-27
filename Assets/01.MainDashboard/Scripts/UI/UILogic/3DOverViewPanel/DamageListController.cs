using System;
using System.Collections.Generic;
using BridgeSenseDT.Assessment;
using BridgeSenseDT.Session;
using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// "검출된 손상 목록"을 채우고, 행 선택을 다른 화면 요소로 이어주는 컨트롤러.
    ///
    /// 목록의 행 하나는 분석 결과 카드(AnalyzeResultObject) 하나와 같은 대상이다.
    /// 즉 이미지 1장 = 결과 카드 1개 = 손상 목록 1행이 되도록 EntryId로 짝을 맞춘다.
    ///
    /// 표는 UITable이 아니라 VerticalLayoutGroup + 행 프리팹으로 구성한다.
    /// UITable은 글꼴 크기와 열 너비를 코드가 정해버려서 화면에서 다듬기가 어려웠다.
    /// </summary>
    public class DamageListController : MonoBehaviour
    {
        [SerializeField] private Transform rowContainer;        // VerticalLayoutGroup이 붙은 행 컨테이너
        [SerializeField] private GameObject damageRowPrefab;    // DamageRowView가 붙은 행 프리팹
        [SerializeField] private AlertPanelView alertPanel;     // 행 선택 시 등급 경고를 띄울 패널

        private readonly List<DamageRowView> rows = new List<DamageRowView>();
        private DamageRowView selectedRow;

        /// <summary>손상 목록에서 행이 선택됐을 때 발생한다. 카메라 이동·부재 강조가 이 이벤트를 구독한다.</summary>
        public event Action<ImageAssessmentResult> DamageSelected;

        private void OnEnable()
        {
            if (AnalysisSessionManager.Instance == null)
                return;

            AnalysisSessionManager.Instance.ReportChanged += Populate;

            // 이 패널이 꺼져 있는 동안 분석이 끝났을 수 있으므로, 구독과 별개로 현재 결과를 한 번 반영한다.
            // 실제 사용 순서가 "입력 패널에서 분석 → 3D 패널로 전환"이라 이 경로가 기본 동작이다.
            Populate(AnalysisSessionManager.Instance.LastReport);
        }

        private void OnDisable()
        {
            if (AnalysisSessionManager.Instance != null)
                AnalysisSessionManager.Instance.ReportChanged -= Populate;
        }

        /// <summary>분석 결과로 목록을 다시 채운다.</summary>
        public void Populate(BridgeAssessmentReport report)
        {
            ClearRows();
            alertPanel.Clear();

            if (report == null)
                return;

            foreach (var result in report.PerImage)
            {
                GameObject instance = Instantiate(damageRowPrefab, rowContainer);
                var row = instance.GetComponent<DamageRowView>();

                row.Initialize(result, HandleRowClicked);
                rows.Add(row);
            }
        }

        private void ClearRows()
        {
            rows.Clear();
            selectedRow = null;

            // 부모에서 먼저 떼어낸 뒤 파괴한다.
            // Destroy는 프레임 끝에 실행되므로, 떼어내지 않으면 곧바로 새 행을 생성했을 때
            // 한 프레임 동안 옛 행과 새 행이 함께 레이아웃에 잡혀 목록이 튄다.
            for (int i = rowContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = rowContainer.GetChild(i);
                child.SetParent(null);
                Destroy(child.gameObject);
            }
        }

        private void HandleRowClicked(DamageRowView row)
        {
            if (selectedRow != null)
                selectedRow.SetSelected(false);

            selectedRow = row;
            row.SetSelected(true);

            alertPanel.Show(row.Result.DisplayGrade);
            DamageSelected?.Invoke(row.Result); // 카메라 포커스·부재 강조는 이 이벤트를 구독해서 처리한다
        }
    }
}
