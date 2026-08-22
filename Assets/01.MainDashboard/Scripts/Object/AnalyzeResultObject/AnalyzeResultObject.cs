using BridgeSenseDT.Assessment;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AnalyzeResultObject : MonoBehaviour
{
    // AnalyzeResultObject는 AnalysisResultArea(HorizontalLayoutGroup)에 표시되는 프리팹으로,
    // 이미지 1장에 대한 AI 분석 결과 1건을 나타낸다. 등록된 InputImageObject 수만큼 생성된다.
    // InputImageObject와 마찬가지로 받은 데이터를 표시만 하고, 계산은 Assessment 쪽에서 끝내온다.

    [SerializeField] private RawImage resultObjectImage;    // 분석 대상 이미지 표시용
    [SerializeField] private TMP_Text resultTitleText;      // "분석 결과 : {촬영부재}"
    [SerializeField] private TMP_Text resultIdText;         // "ID : {id}"
    [SerializeField] private TMP_Text resultCrackText;      // "검출 결함 : {결함명 외 N건}"
    [SerializeField] private TMP_Text resultConfidenceText; // "예측 신뢰도 : {confidence}%"
    [SerializeField] private TMP_Text resultLevelText;      // "예상 등급 : {A~E}"

    public void Initialize(ImageAssessmentResult result)
    {
        resultObjectImage.texture = result.Thumbnail; // 분석 대상 이미지 반영
        resultTitleText.text = $"분석 결과 : {result.CapturedPart}";
        resultIdText.text = $"ID : {result.EntryId}";
        resultCrackText.text = $"검출 결함 : {result.DefectSummary}";
        resultConfidenceText.text = $"AI 예측 신뢰도 : {result.Confidence * 100f:F1}%";
        resultLevelText.text = $"AI 예측 등급 : {result.DisplayGrade}";
    }
}
