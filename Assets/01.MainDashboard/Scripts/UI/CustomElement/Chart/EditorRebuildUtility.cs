using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BridgeSenseDT.UI
{
    /// <summary>
    /// [ExecuteAlways] 컴포넌트들(UITable, UIElevationDiagram, UIChart 등)이 에디터에서
    /// 자식 오브젝트를 DestroyImmediate로 지울 때 공통으로 쓰는 안전장치.
    ///
    /// 지금 하이어라키/인스펙터에서 선택돼 있는 오브젝트가 파괴 대상(혹은 그 자식)이면
    /// 먼저 선택을 살아남는 부모로 옮긴다. 이걸 안 하면 Unity 에디터가 이미 파괴된
    /// 오브젝트를 계속 그리려다 SerializedObjectNotCreatableException /
    /// MissingReferenceException을 던진다.
    /// </summary>
    public static class EditorRebuildUtility
    {
        /// <summary>parent의 자식을 전부 안전하게 파괴한다 (parent 자신은 유지).</summary>
        public static void SafeDestroyChildren(Transform parent)
        {
#if UNITY_EDITOR
            DeselectIfChildOf(parent);
#endif
            for (int i = parent.childCount - 1; i >= 0; i--)
                SafeDestroy(parent.GetChild(i).gameObject);
        }

        /// <summary>단일 오브젝트(또는 컴포넌트)를 안전하게 파괴한다.</summary>
        public static void SafeDestroy(Object obj)
        {
            if (obj == null) return;

#if UNITY_EDITOR
            // Play 진입 순간의 Awake()에서 Rebuild가 실행될 때도 Application.isPlaying은
            // 이미 true이므로, 선택 이동은 재생 여부와 무관하게 항상 먼저 해줘야 한다.
            // (그렇지 않으면 Play 전에 선택해 둔 자식 오브젝트가 파괴되면서
            // 인스펙터가 이미 파괴된 오브젝트를 그리려다 예외를 던진다.)
            if (obj is GameObject go)
                DeselectIfChildOf(go.transform, includeSelf: true);

            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(obj);
                return;
            }
#endif
            Object.Destroy(obj);
        }

#if UNITY_EDITOR
        static void DeselectIfChildOf(Transform root, bool includeSelf = false)
        {
            var selected = Selection.gameObjects;
            if (selected == null || selected.Length == 0) return;

            foreach (var sel in selected)
            {
                if (sel == null) continue;
                bool isSelf = sel.transform == root;
                if (isSelf && !includeSelf) continue; // SafeDestroyChildren 호출 시 root 자신은 대상 아님

                if (isSelf || sel.transform.IsChildOf(root))
                {
                    // 살아남는 오브젝트(부모, 없으면 null)로 선택을 옮겨서
                    // 인스펙터가 곧 파괴될 오브젝트를 붙잡고 있지 않게 한다.
                    Selection.activeGameObject = root.parent != null ? root.parent.gameObject : null;
                    return;
                }
            }
        }
#endif
    }
}