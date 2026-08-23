using BridgeSenseDT.Assessment;
using UnityEngine;

/// <summary>
/// AnalysisResultArea(HorizontalLayoutGroup)에 붙어서 분석 결과 카드들을 그리는 뷰.
///
/// 이 렌더링은 원래 InferenceCheckPopupController 안에 있었지만,
/// 저장본을 불러올 때는 확인 팝업을 거치지 않으므로 재사용할 수 없었다.
/// "결과를 그리는 일"을 팝업에서 떼어내 이쪽으로 옮겨서
/// 방금 분석한 경우와 불러온 경우가 같은 코드를 쓰게 한다.
/// </summary>
public class AnalysisResultListView : MonoBehaviour
{
    [SerializeField] private Transform resultContainer;            // 카드가 생성될 부모. 비워두면 자기 자신을 사용
    [SerializeField] private GameObject analyzeResultObjectPrefab; // 결과 1건을 표시할 AnalyzeResultObject 프리팹

    private Transform Container => resultContainer != null ? resultContainer : transform;

    public void Render(BridgeAssessmentReport report)
    {
        Clear(); // 이전 결과가 남아있으면 먼저 비운다(재분석·재로드 시 중복 방지)

        if (report == null)
            return;

        foreach (var imageResult in report.PerImage) // 등록된 이미지 수만큼 결과 카드 생성
        {
            GameObject prefabInstance = Instantiate(analyzeResultObjectPrefab, Container);
            prefabInstance.GetComponent<AnalyzeResultObject>().Initialize(imageResult);
        }

        if (report.UnresolvedCapturedParts.Count > 0) // 촬영부재를 체크리스트 항목으로 해석하지 못한 입력이 있으면 경고
        {
            Debug.LogWarning(
                "촬영 부재를 인식하지 못해 종합 안전등급 산정에서 제외된 항목이 있습니다: " +
                string.Join(", ", report.UnresolvedCapturedParts));
        }
    }

    public void Clear()
    {
        var container = Container;

        // 부모에서 먼저 떼어낸 뒤 파괴한다.
        // Destroy는 프레임 끝에 실행되므로, 떼어내지 않으면 곧바로 새 카드를 생성했을 때
        // 한 프레임 동안 옛 카드와 새 카드가 함께 레이아웃에 잡혀 화면이 튄다.
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Transform child = container.GetChild(i);
            child.SetParent(null);
            Destroy(child.gameObject);
        }
    }
}
