using BridgeSenseDT.Bridge3D;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// 3D 뷰어가 그려지는 RawImage 위에서 마우스 입력을 받아 카메라 조작으로 넘긴다.
    ///
    /// 3D 화면이 RenderTexture로 그려져 UI 안에 들어와 있으므로 Input을 직접 읽으면 안 된다.
    /// 그러면 뷰어 밖을 조작해도 카메라가 움직이고, 팝업이 위에 떠 있어도 반응해버린다.
    /// EventSystem을 통해 이 RawImage 위에서 일어난 입력만 받도록 한다.
    ///
    /// 회전·이동은 화면상 이동량(delta)만 있으면 되므로 RenderTexture 좌표 변환이 필요 없다.
    /// (부재를 직접 클릭해 고르는 기능을 넣게 되면 그때는 UV 변환이 필요해진다.)
    /// </summary>
    public class BridgeViewerInputHandler : MonoBehaviour, IDragHandler, IScrollHandler
    {
        [SerializeField] private BridgeViewerCameraController cameraController;

        [Tooltip("왼쪽 버튼 드래그로 회전 대신 이동하고 싶을 때 켠다")]
        [SerializeField] private bool invertDragRoles = false;

        public void OnDrag(PointerEventData eventData)
        {
            if (cameraController == null)
                return;

            bool isLeftButton = eventData.button == PointerEventData.InputButton.Left;
            bool shouldOrbit = invertDragRoles ? !isLeftButton : isLeftButton;

            if (shouldOrbit)
                cameraController.Orbit(eventData.delta);
            else
                cameraController.Pan(eventData.delta);
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (cameraController == null)
                return;

            cameraController.Zoom(eventData.scrollDelta.y);
        }
    }
}
