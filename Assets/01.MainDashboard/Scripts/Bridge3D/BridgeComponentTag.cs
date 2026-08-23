using System;
using System.Collections.Generic;
using BridgeSenseDT.Assessment;
using UnityEngine;

namespace BridgeSenseDT.Bridge3D
{
    /// <summary>
    /// 렌더러의 서브메시 하나가 어떤 체크리스트 부재에 해당하는지를 나타내는 연결 정보.
    /// </summary>
    [Serializable]
    public struct SubmeshBinding
    {
        public int submeshIndex;
        public BridgeChecklistItem item;
    }

    /// <summary>
    /// 3D 교량 모델의 부재를 안전등급 체크리스트 항목과 연결하는 컴포넌트.
    ///
    /// GameObject 단위가 아니라 서브메시 단위로 연결하는 이유:
    /// Deck_GirderSpan_N 하나가 거더·슬래브·난간을 서브메시로 함께 들고 있어서
    /// (머티리얼이 Mat_거더 / Mat_슬래브 / Mat_난간으로 나뉘어 있다),
    /// "난간만 등급 색으로 칠하기"를 하려면 렌더러가 아니라 서브메시를 지목해야 한다.
    ///
    /// 색상은 renderer.material로 인스턴스를 만들지 않고 MaterialPropertyBlock으로 적용한다.
    /// 인스턴스를 만들면 배칭이 깨지고 부재 수만큼 머티리얼이 복제된다.
    /// </summary>
    public class BridgeComponentTag : MonoBehaviour
    {
        // URP Lit의 기본 색상 프로퍼티. 다른 셰이더를 쓰면 인스펙터에서 바꿀 수 있게 노출해둔다.
        [SerializeField] private string colorPropertyName = "_BaseColor";

        [SerializeField] private List<SubmeshBinding> bindings = new List<SubmeshBinding>();

        // 이 부재가 속한 구조 단위의 번호(Pier_Pier_7 → 7, Deck_GirderSpan_13 → 13).
        // 안전점검 실무에서 도면·보고서상 부재에 부여하는 고유 번호에 대응하며,
        // 사용자가 "교각7"처럼 번호를 붙여 입력했을 때 그 부재를 찾는 데 쓴다. 0이면 번호 없음.
        [SerializeField] private int componentIndex;

        public int ComponentIndex => componentIndex;

        private Renderer cachedRenderer;
        private MaterialPropertyBlock propertyBlock;
        private int colorPropertyId;
        private Color[] originalColors; // 등급 표시를 해제할 때 되돌릴 원래 색

        public IReadOnlyList<SubmeshBinding> Bindings => bindings;

        public Renderer TargetRenderer
        {
            get
            {
                if (cachedRenderer == null)
                    cachedRenderer = GetComponent<Renderer>();
                return cachedRenderer;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (propertyBlock != null)
                return;

            propertyBlock = new MaterialPropertyBlock();
            colorPropertyId = Shader.PropertyToID(colorPropertyName);

            var renderer = TargetRenderer;
            if (renderer == null)
                return;

            // 원래 색을 기억해둔다. 등급 표시를 지울 때 이 값으로 되돌린다.
            var materials = renderer.sharedMaterials;
            originalColors = new Color[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                originalColors[i] = materials[i] != null && materials[i].HasProperty(colorPropertyId)
                    ? materials[i].GetColor(colorPropertyId)
                    : Color.white;
            }
        }

        /// <summary>이 부재가 해당 체크리스트 항목을 포함하는지 확인한다.</summary>
        public bool Contains(BridgeChecklistItem item)
        {
            foreach (var binding in bindings)
            {
                if (binding.item == item)
                    return true;
            }
            return false;
        }

        /// <summary>해당 체크리스트 항목에 대응하는 서브메시 인덱스들을 results에 채운다.</summary>
        public void CollectSubmeshes(BridgeChecklistItem item, List<int> results)
        {
            foreach (var binding in bindings)
            {
                if (binding.item == item)
                    results.Add(binding.submeshIndex);
            }
        }

        /// <summary>지정한 서브메시의 색을 바꾼다. 매 프레임 호출해도 되도록 할당이 없다.</summary>
        public void SetSubmeshColor(int submeshIndex, Color color)
        {
            EnsureInitialized();

            var renderer = TargetRenderer;
            if (renderer == null || submeshIndex < 0 || submeshIndex >= renderer.sharedMaterials.Length)
                return;

            renderer.GetPropertyBlock(propertyBlock, submeshIndex);
            propertyBlock.SetColor(colorPropertyId, color);
            renderer.SetPropertyBlock(propertyBlock, submeshIndex);
        }

        /// <summary>지정한 서브메시를 원래 색으로 되돌린다.</summary>
        public void ResetSubmeshColor(int submeshIndex)
        {
            EnsureInitialized();

            if (originalColors == null || submeshIndex < 0 || submeshIndex >= originalColors.Length)
                return;

            SetSubmeshColor(submeshIndex, originalColors[submeshIndex]);
        }

        /// <summary>이 부재의 모든 서브메시를 원래 색으로 되돌린다.</summary>
        public void ResetAllColors()
        {
            EnsureInitialized();

            if (originalColors == null)
                return;

            for (int i = 0; i < originalColors.Length; i++)
                SetSubmeshColor(i, originalColors[i]);
        }

        /// <summary>카메라 포커스에 쓸 월드 공간 경계.</summary>
        public bool TryGetWorldBounds(out Bounds bounds)
        {
            var renderer = TargetRenderer;
            if (renderer == null)
            {
                bounds = default;
                return false;
            }

            bounds = renderer.bounds;
            return true;
        }

#if UNITY_EDITOR
        /// <summary>에디터 자동 태깅 도구에서만 사용한다.</summary>
        public void EditorSetBindings(List<SubmeshBinding> value, int index)
        {
            bindings = value;
            componentIndex = index;
        }
#endif
    }
}
