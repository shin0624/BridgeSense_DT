using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// "AI 기반 분석 결과" 영역에 등급별 경고 패널 프리팹을 띄우는 뷰.
    ///
    /// 등급마다 색·아이콘·문구가 다른 프리팹이 미리 만들어져 있으므로,
    /// 이 스크립트는 등급 문자열을 받아 해당 프리팹으로 교체하기만 한다.
    /// 프리팹 안의 내용을 코드로 수정하지 않는 이유는, 디자인 변경이 프리팹 수정만으로 끝나게 하기 위해서다.
    /// </summary>
    public class AlertPanelView : MonoBehaviour
    {
        [SerializeField] private Transform container; // 프리팹이 생성될 자리. 비워두면 자기 자신을 사용

        [SerializeField] private GameObject alertPanelGood;         // A 우수
        [SerializeField] private GameObject alertPanelSemiGood;     // B 양호
        [SerializeField] private GameObject alertPanelNormal;       // C 보통
        [SerializeField] private GameObject alertPanelInsufficient; // D 미흡
        [SerializeField] private GameObject alertPanelDanger;       // E 불량

        private Transform Container => container != null ? container : transform;

        private string currentGrade;         // 같은 등급이면 다시 만들지 않기 위해 기억해둔다
        private GameObject currentInstance;

        /// <summary>등급에 해당하는 경고 패널을 표시한다.</summary>
        public void Show(string grade)
        {
            if (currentInstance != null && currentGrade == grade)
                return; // 이미 같은 등급이 떠 있으면 그대로 둔다

            Clear();

            GameObject prefab = GetPrefab(grade);
            if (prefab == null)
            {
                Debug.LogWarning($"등급 '{grade}'에 해당하는 경고 패널 프리팹이 연결되지 않았습니다.");
                return;
            }

            currentInstance = Instantiate(prefab, Container);
            currentGrade = grade;
        }

        public void Clear()
        {
            if (currentInstance != null)
                Destroy(currentInstance);

            currentInstance = null;
            currentGrade = null;
        }

        private GameObject GetPrefab(string grade)
        {
            switch (grade)
            {
                case "A": return alertPanelGood;
                case "B": return alertPanelSemiGood;
                case "C": return alertPanelNormal;
                case "D": return alertPanelInsufficient;
                case "E": return alertPanelDanger;
                default: return null;
            }
        }
    }
}
