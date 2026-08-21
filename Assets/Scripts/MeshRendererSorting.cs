using UnityEngine;

namespace Climb.Core
{
    /// <summary>
    /// 通用 MeshRenderer 绘制顺序控制：
    /// 设置 Sorting Layer / Order in Layer（与 SpriteRenderer 同一 2D 排序队列），
    /// 并可选强制材质进入透明队列（renderQueue=3000）——
    /// 否则在 URP 2D 下 MeshRenderer 走深度排序，Sorting Order 不会生效。
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    [ExecuteAlways]
    public sealed class MeshRendererSorting : MonoBehaviour
    {
        [Header("渲染层级")]
        [Tooltip("渲染层级名（保持 Default，需为已有 Sorting Layer）")]
        public string sortingLayerName = "Default";
        [Tooltip("同层级内渲染顺序：越大越靠前（类似 SpriteRenderer 的 Order in Layer）")]
        public int sortingOrder = 0;

        [Header("透明队列")]
        [Tooltip("强制材质 renderQueue=3000（透明队列）：URP 2D 下不透明材质不参与 SpriteRenderer 的排序")]
        public bool forceTransparentQueue = true;

        private void OnEnable()
        {
            Apply();
        }

        /// <summary>应用 / 刷新排序设置（运行中替换了材质后也调用它）。</summary>
        public void Apply()
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) return;

            if (!string.IsNullOrEmpty(sortingLayerName))
                mr.sortingLayerName = sortingLayerName;
            mr.sortingOrder = sortingOrder;

            if (forceTransparentQueue)
            {
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (mats[i].renderQueue != 3000)
                        mats[i].renderQueue = 3000;
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 编辑器里改字段即时生效
            Apply();
        }
#endif
    }
}
