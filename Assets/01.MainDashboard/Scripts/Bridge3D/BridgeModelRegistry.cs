using System.Collections.Generic;
using BridgeSenseDT.Assessment;
using UnityEngine;

namespace BridgeSenseDT.Bridge3D
{
    /// <summary>
    /// 씬에 배치된 3D 교량 모델의 부재들을 모아두고, 체크리스트 항목으로 조회할 수 있게 하는 목록.
    ///
    /// 촬영 부재에 번호를 받지 않기로 했으므로 조회 단위는 "종류"다.
    /// 예를 들어 거더를 조회하면 Deck_GirderSpan_1~13의 거더 서브메시가 전부 대상이 되고,
    /// 카메라 포커스도 그것들을 모두 감싸는 경계를 기준으로 잡는다.
    /// </summary>
    public class BridgeModelRegistry : MonoBehaviour
    {
        public static BridgeModelRegistry Instance { get; private set; }

        [SerializeField] private Transform bridgeRoot; // 교량 모델의 최상위. 비워두면 자기 자신을 사용

        private readonly List<BridgeComponentTag> tags = new List<BridgeComponentTag>();
        private readonly List<int> submeshBuffer = new List<int>(); // 조회할 때마다 새 리스트를 만들지 않도록 재사용

        public IReadOnlyList<BridgeComponentTag> Tags => tags;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>교량 모델 아래의 부재 태그를 다시 수집한다.</summary>
        public void Rebuild()
        {
            tags.Clear();

            Transform root = bridgeRoot != null ? bridgeRoot : transform;

            // 3D 뷰어 패널이 꺼져 있는 동안에도 조회할 수 있어야 하므로 비활성 오브젝트까지 포함한다.
            root.GetComponentsInChildren(true, tags);
        }

        /// <summary>
        /// 해당 부재 종류의 모든 서브메시를 순회하며 콜백을 호출한다.
        /// 등급 색상 적용과 강조 표시가 이 경로를 공유한다.
        /// </summary>
        public void ForEachSubmesh(BridgeChecklistItem item, System.Action<BridgeComponentTag, int> action)
        {
            foreach (var tag in tags)
            {
                if (tag == null)
                    continue; // 씬 정리 중이면 부재가 먼저 파괴돼 있을 수 있다

                submeshBuffer.Clear();
                tag.CollectSubmeshes(item, submeshBuffer);

                foreach (int submeshIndex in submeshBuffer)
                    action(tag, submeshIndex);
            }
        }

        /// <summary>
        /// 해당 부재 종류에 속하는 모든 부재를 감싸는 월드 경계를 구한다.
        /// 카메라를 그 부재로 이동시킬 때 기준으로 쓴다.
        /// </summary>
        public bool TryGetBounds(BridgeChecklistItem item, out Bounds bounds)
        {
            bool found = false;
            bounds = default;

            foreach (var tag in tags)
            {
                if (!tag.Contains(item))
                    continue;

                if (!tag.TryGetWorldBounds(out Bounds tagBounds))
                    continue;

                if (!found)
                {
                    bounds = tagBounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(tagBounds);
                }
            }

            return found;
        }

        /// <summary>
        /// 해당 부재 종류에 존재하는 부재 번호들을 오름차순으로 모은다.
        ///
        /// 입면도가 경간·교각을 몇 칸 그릴지 정할 때 쓴다.
        /// 현황조서의 경간수를 쓰지 않고 3D 모델에서 세는 이유는,
        /// 그래야 입면도와 3D 뷰어가 항상 같은 구성을 보여주기 때문이다.
        /// </summary>
        public void CollectComponentIndices(BridgeChecklistItem item, SortedSet<int> results)
        {
            foreach (var tag in tags)
            {
                if (tag == null || !tag.Contains(item))
                    continue;

                if (tag.ComponentIndex > 0)
                    results.Add(tag.ComponentIndex);
            }
        }

        /// <summary>
        /// 교량이 가장 길게 뻗은 축을 돌려준다(0=x, 1=y, 2=z).
        /// 입면도에서 경간 길이를 실제 비율대로 그릴 때 이 축을 기준으로 잰다.
        /// </summary>
        public int GetLongitudinalAxis()
        {
            if (!TryGetWholeBounds(out Bounds bounds))
                return 0;

            Vector3 size = bounds.size;

            if (size.x >= size.y && size.x >= size.z)
                return 0;

            return size.z >= size.y ? 2 : 1;
        }

        /// <summary>
        /// 번호가 지정된 부재 하나의 경계를 구한다(교각7 → 7번 교각).
        /// 해당 번호의 부재가 없으면 false를 돌려주고, 호출하는 쪽이 대표 부재로 넘어가게 한다.
        /// </summary>
        public bool TryGetIndexedBounds(BridgeChecklistItem item, int componentIndex, out Bounds bounds)
        {
            bool found = false;
            bounds = default;

            if (componentIndex <= 0)
                return false;

            foreach (var tag in tags)
            {
                if (tag.ComponentIndex != componentIndex || !tag.Contains(item))
                    continue;

                if (!tag.TryGetWorldBounds(out Bounds tagBounds))
                    continue;

                if (!found)
                {
                    bounds = tagBounds;
                    found = true;
                }
                else
                {
                    // 같은 번호의 구조 단위 안에 같은 종류 부재가 여럿 있을 수 있다(한 경간의 거더 9개 등).
                    bounds.Encapsulate(tagBounds);
                }
            }

            return found;
        }

        /// <summary>
        /// 해당 부재 종류를 대표하는 한 덩어리의 경계를 구한다.
        ///
        /// TryGetBounds는 같은 종류 전체를 감싸므로, 거더처럼 13경간에 걸쳐 117개가 흩어져 있는 부재는
        /// 결과가 교량 전체와 같아진다. 그 경계로 카메라를 옮기면 "전체 보기"와 구분되지 않는다.
        /// 그래서 교량 중앙에 가장 가까운 부재 하나를 고르고, 같은 부모(경간·교각 단위) 아래에 있는
        /// 같은 종류 부재만 함께 묶는다. 거더를 고르면 한 경간의 거더들이 화면에 담긴다.
        /// </summary>
        public bool TryGetRepresentativeBounds(BridgeChecklistItem item, out Bounds bounds)
        {
            bounds = default;

            if (!TryGetWholeBounds(out Bounds wholeBounds))
                return false;

            // 교량 중앙에 가장 가까운 부재를 대표로 삼는다. 어느 것을 골라도 등급 색은 같으므로
            // 사용자가 예측하기 쉬운 "가운데"를 기준으로 한다.
            BridgeComponentTag representative = null;
            float nearestDistance = float.MaxValue;

            foreach (var tag in tags)
            {
                if (!tag.Contains(item) || !tag.TryGetWorldBounds(out Bounds tagBounds))
                    continue;

                float sqrDistance = (tagBounds.center - wholeBounds.center).sqrMagnitude;
                if (sqrDistance < nearestDistance)
                {
                    nearestDistance = sqrDistance;
                    representative = tag;
                }
            }

            if (representative == null || !representative.TryGetWorldBounds(out Bounds singleBounds))
                return false;

            // 같은 부모 아래의 같은 종류 부재를 함께 묶는다(한 경간의 거더 9개 등).
            Bounds grouped = singleBounds;
            Transform parent = representative.transform.parent;

            if (parent != null)
            {
                foreach (var tag in tags)
                {
                    if (tag.transform.parent != parent || !tag.Contains(item))
                        continue;

                    if (tag.TryGetWorldBounds(out Bounds siblingBounds))
                        grouped.Encapsulate(siblingBounds);
                }
            }

            // 부재가 부모로 묶여있지 않은 모델이면 위 과정이 결국 전체를 다시 감쌀 수 있다.
            // 그 경우에는 대표 부재 하나만 쓴다.
            float wholeExtent = Mathf.Max(wholeBounds.size.x, wholeBounds.size.y, wholeBounds.size.z);
            float groupedExtent = Mathf.Max(grouped.size.x, grouped.size.y, grouped.size.z);

            bounds = groupedExtent > wholeExtent * 0.5f ? singleBounds : grouped;
            return true;
        }

        /// <summary>교량 전체를 감싸는 경계. 카메라 초기 위치를 잡을 때 쓴다.</summary>
        public bool TryGetWholeBounds(out Bounds bounds)
        {
            bool found = false;
            bounds = default;

            foreach (var tag in tags)
            {
                if (!tag.TryGetWorldBounds(out Bounds tagBounds))
                    continue;

                if (!found)
                {
                    bounds = tagBounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(tagBounds);
                }
            }

            return found;
        }

        /// <summary>모든 부재를 원래 색으로 되돌린다.</summary>
        public void ResetAllColors()
        {
            foreach (var tag in tags)
            {
                if (tag == null)
                    continue; // 씬 정리 중이면 부재가 먼저 파괴돼 있을 수 있다

                tag.ResetAllColors();
            }
        }
    }
}
