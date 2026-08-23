using System;
using BridgeSenseDT.Assessment;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// "검출된 손상 목록"의 행 하나. 분석 결과 1건(= 이미지 1장 = 결과 카드 1개)을 표시한다.
    ///
    /// UITable을 쓰지 않고 VerticalLayoutGroup + 행 프리팹 방식을 쓰므로,
    /// 셀 텍스트를 프리팹에서 직접 배치하고 이 스크립트는 값만 채운다.
    /// 글꼴 크기·열 너비를 프리팹에서 그대로 조절할 수 있다.
    ///
    /// 클릭은 Button 대신 IPointerClickHandler로 받는다.
    /// 행 전체가 클릭 영역이어야 하는데 Button을 쓰면 자식 텍스트가 클릭을 가로챌 수 있고,
    /// 이 프리팹에는 배경 Image가 이미 있어 레이캐스트 대상이 확보돼 있다.
    /// </summary>
    public class DamageRowView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private TMP_Text idText;       // Cell_0
        [SerializeField] private TMP_Text locationText; // Cell_1 촬영 부재
        [SerializeField] private TMP_Text defectText;   // Cell_2 손상 유형
        [SerializeField] private TMP_Text gradeText;    // Cell_3 예상 등급
        [SerializeField] private TMP_Text severityText; // Cell_4 심각도

        [SerializeField] private Image background;      // 선택 표시에 쓸 배경. 없으면 선택 색이 적용되지 않는다
        [SerializeField] private Color normalColor = new Color(0.110f, 0.090f, 0.071f, 1f);
        [SerializeField] private Color selectedColor = new Color(1f, 0.549f, 0.239f, 0.55f);

        // 등급 문자열을 등급 색으로 물들일지 여부. 디자인상 흰색을 유지하고 싶으면 프리팹에서 끄면 된다.
        [SerializeField] private bool colorGradeText = true;

        public ImageAssessmentResult Result { get; private set; }

        private Action<DamageRowView> onClicked;

        public void Initialize(ImageAssessmentResult result, Action<DamageRowView> onClicked)
        {
            Result = result;
            this.onClicked = onClicked;

            idText.text = result.EntryId;
            locationText.text = result.CapturedPart;
            defectText.text = result.DefectSummary;
            gradeText.text = result.DisplayGrade;
            severityText.text = GradeColorMap.GetSeverityLabel(result.DisplayGrade);

            if (colorGradeText)
                gradeText.color = GradeColorMap.GetColor(result.DisplayGrade);

            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (background != null)
                background.color = selected ? selectedColor : normalColor;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            onClicked?.Invoke(this);
        }
    }
}
