using BridgeSenseDT.Assessment;
using BridgeSenseDT.Bridge3D;
using UnityEngine;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 손상 목록에서 행을 고르면 3D 카메라가 해당 부재로 이동하도록 연결하는 중개자.
    ///
    /// 손상 목록(UI)이 카메라(3D)를 직접 참조하지 않도록 이 스크립트를 사이에 둔다.
    /// 목록은 "무엇이 선택됐다"만 알리고, 그 선택으로 무엇을 할지는 이쪽에서 정한다.
    /// 나중에 부재 강조나 상세 정보 갱신을 덧붙일 때도 목록 코드를 건드리지 않아도 된다.
    /// </summary>
    public class DamageFocusBinder : MonoBehaviour
    {
        [SerializeField] private DamageListController damageList;
        [SerializeField] private BridgeViewerCameraController cameraController;

        private void OnEnable()
        {
            if (damageList != null)
                damageList.DamageSelected += HandleDamageSelected;
        }

        private void OnDisable()
        {
            if (damageList != null)
                damageList.DamageSelected -= HandleDamageSelected;
        }

        private void HandleDamageSelected(ImageAssessmentResult result)
        {
            if (cameraController == null)
                return;

            // 촬영 부재를 체크리스트 항목으로 해석하지 못한 경우에는 이동할 대상이 없다.
            // 사용자가 오타를 냈거나 모델에 없는 부재를 입력한 상황이므로 조용히 넘어가지 않고 알린다.
            if (!result.ChecklistItemResolved)
            {
                Debug.LogWarning($"'{result.CapturedPart}'을(를) 3D 부재로 해석하지 못해 카메라를 이동하지 않았습니다.");
                return;
            }

            // 사용자가 "교각7"처럼 번호를 붙여 입력했으면 그 번호의 부재로,
            // 번호 없이 "교각"만 입력했으면 대표 부재로 이동한다.
            cameraController.FocusOn(result.ChecklistItem, result.ComponentIndex);
        }
    }
}
