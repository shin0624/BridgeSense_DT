using BridgeSenseDT.Assessment;
using DG.Tweening;
using UnityEngine;

namespace BridgeSenseDT.Bridge3D
{
    /// <summary>
    /// 3D 뷰어의 카메라를 궤도 방식으로 조작한다.
    ///
    /// 카메라 위치를 직접 다루지 않고 "바라보는 지점(pivot) + 거리 + 각도"로 관리한다.
    /// 위치를 직접 옮기면 회전과 이동이 서로 얽혀 조작감이 무너지는데,
    /// 이 방식은 각 조작이 서로 다른 값 하나씩만 건드려서 예측 가능하게 움직인다.
    ///
    /// 입력을 직접 읽지 않고 공개 메서드만 노출한다.
    /// 실제 입력은 RenderTexture 위에 있는 RawImage가 받으므로 BridgeViewerInputHandler가 담당한다.
    /// </summary>
    public class BridgeViewerCameraController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;          // 3D 뷰어를 렌더링하는 카메라
        [SerializeField] private BridgeModelRegistry registry; // 비워두면 싱글톤 인스턴스를 사용

        [Header("조작 감도")]
        [SerializeField] private float orbitSpeed = 0.25f;
        [SerializeField] private float panSpeed = 0.0015f;  // 거리에 비례해 보정되므로 작은 값이 기준
        [SerializeField] private float zoomSpeed = 0.12f;   // 거리에 비례해 보정된다

        [Header("제한")]
        [SerializeField] private float minDistance = 0.25f;
        [SerializeField] private float maxDistance = 100.0f;
        [SerializeField] private float minPitch = -10f; // 지면 아래로 과하게 내려가지 않도록
        [SerializeField] private float maxPitch = 80f;

        [Header("부재 포커스")]
        [SerializeField] private float focusDuration = 0.7f;
        [Tooltip("부재를 화면에 담을 때 남길 여백 배수. 1이면 딱 맞고 클수록 멀어진다")]
        [SerializeField] private float focusPadding = 1.35f;

        [Tooltip("켜면 같은 종류 부재 전체를 화면에 담는다. 거더처럼 교량 전체에 흩어진 부재는 전체 보기와 같아진다")]
        [SerializeField] private bool focusOnEntireComponentType = false;

        private Vector3 pivot;   // 카메라가 바라보는 지점
        private float distance;  // pivot으로부터의 거리
        private float yaw;
        private float pitch;

        private Tween focusTween;

        private BridgeModelRegistry Registry => registry != null ? registry : BridgeModelRegistry.Instance;

        private void Start()
        {
            InitializeFromCurrentTransform();
            FocusOnWholeBridge(instant: true); // 시작 시 교량 전체가 보이는 위치에서 출발
        }

        private void OnDestroy()
        {
            focusTween?.Kill();
        }

        /// <summary>씬에 배치해둔 카메라의 현재 자세를 궤도 값으로 환산한다.</summary>
        private void InitializeFromCurrentTransform()
        {
            if (targetCamera == null)
                return;

            Vector3 euler = targetCamera.transform.eulerAngles;
            pitch = NormalizeAngle(euler.x);
            yaw = euler.y;
            distance = Mathf.Clamp(distance <= 0f ? 100f : distance, minDistance, maxDistance);
            pivot = targetCamera.transform.position + targetCamera.transform.forward * distance;
        }

        /// <summary>드래그로 카메라를 궤도 회전시킨다.</summary>
        public void Orbit(Vector2 screenDelta)
        {
            CancelFocus();

            yaw += screenDelta.x * orbitSpeed;
            pitch = Mathf.Clamp(pitch - screenDelta.y * orbitSpeed, minPitch, maxPitch);

            ApplyTransform();
        }

        /// <summary>드래그로 바라보는 지점을 화면과 나란한 방향으로 옮긴다.</summary>
        public void Pan(Vector2 screenDelta)
        {
            if (targetCamera == null)
                return;

            CancelFocus();

            // 멀리서 볼수록 같은 드래그로 더 많이 움직여야 조작감이 일정하다.
            float scale = panSpeed * distance;
            Vector3 move = (-targetCamera.transform.right * screenDelta.x - targetCamera.transform.up * screenDelta.y) * scale;

            pivot += move;
            ApplyTransform();
        }

        /// <summary>휠로 확대·축소한다.</summary>
        public void Zoom(float scrollDelta)
        {
            CancelFocus();

            // 가까울수록 조금씩, 멀수록 크게 움직여야 자연스럽다.
            distance = Mathf.Clamp(distance - scrollDelta * zoomSpeed * distance, minDistance, maxDistance);
            ApplyTransform();
        }

        /// <summary>
        /// 특정 부재가 화면에 담기도록 카메라를 이동시킨다.
        /// componentIndex가 1 이상이면 그 번호의 부재를 우선 찾고(교각7 → 7번 교각),
        /// 번호가 없거나 해당 번호를 찾지 못하면 대표 부재로 이동한다.
        /// </summary>
        public void FocusOn(BridgeChecklistItem item, int componentIndex = 0, bool instant = false)
        {
            var modelRegistry = Registry;
            if (modelRegistry == null)
                return;

            Bounds bounds;
            bool found = modelRegistry.TryGetIndexedBounds(item, componentIndex, out bounds);

            if (!found)
            {
                if (componentIndex > 0)
                {
                    Debug.LogWarning(
                        $"'{SafetyGradeEvaluator.GetChecklistItemName(item)} {componentIndex}'번 부재를 찾지 못해 대표 부재로 이동합니다.");
                }

                // 번호가 없을 때의 기본은 대표 부재 한 덩어리다.
                // 같은 종류 전체를 감싸면 거더·바닥판처럼 교량 전 구간에 걸친 부재는
                // 결과가 교량 전체와 같아져서 어디로 이동했는지 알아볼 수 없다.
                found = focusOnEntireComponentType
                    ? modelRegistry.TryGetBounds(item, out bounds)
                    : modelRegistry.TryGetRepresentativeBounds(item, out bounds);
            }

            if (!found)
            {
                Debug.LogWarning($"3D 모델에서 '{SafetyGradeEvaluator.GetChecklistItemName(item)}' 부재를 찾지 못해 카메라를 이동하지 않았습니다.");
                return;
            }

            FocusOn(bounds, instant);
        }

        /// <summary>교량 전체가 담기도록 카메라를 되돌린다.</summary>
        public void FocusOnWholeBridge(bool instant = false)
        {
            var modelRegistry = Registry;
            if (modelRegistry == null || !modelRegistry.TryGetWholeBounds(out Bounds bounds))
                return;

            FocusOn(bounds, instant);
        }

        /// <summary>주어진 영역이 화면에 담기도록 카메라를 이동시킨다.</summary>
        public void FocusOn(Bounds bounds, bool instant = false)
        {
            if (targetCamera == null)
                return;

            CancelFocus();

            Vector3 targetPivot = bounds.center;
            float targetDistance = Mathf.Clamp(CalculateFitDistance(bounds), minDistance, maxDistance);

            if (instant)
            {
                pivot = targetPivot;
                distance = targetDistance;
                ApplyTransform();
                return;
            }

            Vector3 startPivot = pivot;
            float startDistance = distance;

            focusTween = DOVirtual.Float(0f, 1f, focusDuration, t =>
            {
                pivot = Vector3.Lerp(startPivot, targetPivot, t);
                distance = Mathf.Lerp(startDistance, targetDistance, t);
                ApplyTransform();
            })
            .SetEase(Ease.InOutCubic)
            .SetLink(gameObject);
        }

        /// <summary>
        /// 영역 전체가 화면에 들어오는 거리를 구한다.
        /// 세로 시야각만 쓰면 거더처럼 가로로 긴 부재가 화면 밖으로 잘리므로,
        /// 가로·세로 시야각 중 좁은 쪽을 기준으로 잡는다.
        /// </summary>
        private float CalculateFitDistance(Bounds bounds)
        {
            float radius = bounds.extents.magnitude; // 영역을 감싸는 구의 반지름
            if (radius <= Mathf.Epsilon)
                return minDistance;

            float verticalFov = targetCamera.fieldOfView * Mathf.Deg2Rad;
            float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f) * targetCamera.aspect);
            float narrowerFov = Mathf.Min(verticalFov, horizontalFov);

            return radius / Mathf.Sin(narrowerFov * 0.5f) * focusPadding;
        }

        private void ApplyTransform()
        {
            if (targetCamera == null)
                return;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            targetCamera.transform.rotation = rotation;
            targetCamera.transform.position = pivot - rotation * Vector3.forward * distance;
        }

        /// <summary>포커스 이동 중에 사용자가 조작하면 이동을 멈추고 조작을 우선한다.</summary>
        private void CancelFocus()
        {
            if (focusTween != null)
            {
                focusTween.Kill();
                focusTween = null;
            }
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
