using UnityEngine;
using UnityEngine.Rendering;

namespace Climb.Core
{
    /// <summary>
    /// 多个 MeshRenderer 形状的实时合并器：
    /// 把 sources 里所有 MeshRenderer 的网格（各自本地空间）合并成一个 Mesh，
    /// 实时显示在本物体（目标）的 MeshRenderer 上。
    /// 源物体如何移动 / 旋转 / 变形，合并形状都会跟随。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))] 
    [ExecuteAlways]
    public sealed class MeshCombiner : MonoBehaviour
    {
        [Header("合并来源")]
        [Tooltip("要合并的源 MeshRenderer（不要包含本物体）")]
        public MeshRenderer[] sources;

        [Header("合并选项")]
        [Tooltip("源材质相同时合并为单个 submesh（用一个材质）；关闭则保留多 submesh 并同步材质")]
        public bool mergeSubMeshes = true;
        [Tooltip("每帧实时更新合并结果（关闭则只在 Awake 合并一次，需手动调用 Rebuild()）")]
        public bool updateEveryFrame = true;

        [Header("绘制偏移")]
        [Tooltip("合并网格在目标本地空间中的 X/Y 偏移（绘制时相对目标整体偏移）")]
        public Vector2 offset = Vector2.zero;

        [Header("渲染层级")]
        [Tooltip("渲染层级名（保持 Default，需为已有 Sorting Layer）")]
        public string sortingLayerName = "Default";
        [Tooltip("同层级内渲染顺序：越大越靠前（类似 SpriteRenderer 的 Order in Layer）")]
        public int sortingOrder = 0;

        [Header("重叠处理")]
        [Tooltip("合并 mesh 不透明渲染：重叠区域显示单层颜色、不叠加（关闭则保留源材质的透明混合）")]
        public bool opaqueMerge = true;
        [Tooltip("给不同源分配微小 z 深度分层，避免同平面重叠时的 z-fighting 闪烁")]
        public bool depthLayerPerSource = true;
        [Tooltip("z 分层间距（相邻源之间的深度差）")]
        [Range(0f, 0.01f)] public float depthLayerSpacing = 0.001f;

        private Mesh _combined;
        private CombineInstance[] _items;
        private Material _mergeMaterial;     // opaqueMerge 时的不透明实例材质（单材质）
        private Material[] _mergeMaterials;  // opaqueMerge 时的多材质实例缓存
        private bool _hasInit;

        private void Awake()
        {
            _combined = new Mesh { name = "CombinedMesh_" + name };
            _combined.indexFormat = IndexFormat.UInt32; // 支持大量顶点（>65535）
            _combined.MarkDynamic();                    // 每帧更新更高效
            GetComponent<MeshFilter>().sharedMesh = _combined;
            _hasInit = true;

            ApplySorting();

            if (!updateEveryFrame)
                Rebuild();
        }

        private void LateUpdate()
        {
            if (!_hasInit || !updateEveryFrame) return;
            Rebuild();
        }

        /// <summary>重新合并（源网格变化或需要一次性刷新时调用）。</summary>
        public void Rebuild()
        {
            if (!_hasInit) return;

            // 统计有效源（有 MeshFilter 且 sharedMesh 非空）
            int valid = 0;
            if (sources != null)
                for (int i = 0; i < sources.Length; i++)
                    if (IsValidSource(sources[i]))
                        valid++;

            if (valid == 0)
            {
                _combined.Clear();
                return;
            }

            if (_items == null || _items.Length != valid)
                _items = new CombineInstance[valid];

            int idx = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                var src = sources[i];
                if (!IsValidSource(src)) continue;

                // 绘制偏移 + 每源 z 深度分层（后合并的源 z 更小更靠前 → 覆盖前面的，重叠区域颜色均匀）
                float z = depthLayerPerSource ? -idx * depthLayerSpacing : 0f;
                Matrix4x4 offsetM = Matrix4x4.TRS(new Vector3(offset.x, offset.y, z), Quaternion.identity, Vector3.one);
                _items[idx].mesh = src.GetComponent<MeshFilter>().sharedMesh;
                // 顶点从「源本地 → 源世界 → 目标本地」+ 目标本地偏移
                _items[idx].transform = offsetM * transform.worldToLocalMatrix * src.transform.localToWorldMatrix;
                idx++;
            }

            _combined.Clear();
            _combined.CombineMeshes(_items, mergeSubMeshes, true);

            // 多 submesh 时，同步各源材质到目标
            if (!mergeSubMeshes)
            {
                var mats = new Material[valid];
                int m = 0;
                for (int i = 0; i < sources.Length; i++)
                {
                    var src = sources[i];
                    if (!IsValidSource(src)) continue;
                    mats[m++] = src.sharedMaterial;
                }

                if (opaqueMerge)
                    ApplyOpaqueMaterials(mats);
                else
                    for (int i = 0; i < mats.Length; i++) EnsureTransparentQueue(mats[i]);

                GetComponent<MeshRenderer>().sharedMaterials = mats;
            }
            else
            {
                // 单 submesh：opaqueMerge 时用不透明实例材质（重叠区域不叠加颜色）
                var mr = GetComponent<MeshRenderer>();
                Material mat = opaqueMerge ? GetOpaqueMaterial() : mr.sharedMaterial;
                mr.sharedMaterial = mat;
                EnsureTransparentQueue(mat);
            }
        }

        /// <summary>多材质时把每个材质实例化并强制不透明（缓存实例，避免每帧重建）。</summary>
        private void ApplyOpaqueMaterials(Material[] mats)
        {
            if (_mergeMaterials == null || _mergeMaterials.Length != mats.Length)
            {
                if (_mergeMaterials != null)
                    for (int i = 0; i < _mergeMaterials.Length; i++) DestroyResource(_mergeMaterials[i]);
                _mergeMaterials = new Material[mats.Length];
            }

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (_mergeMaterials[i] == null || _mergeMaterials[i].shader != mats[i].shader)
                {
                    if (_mergeMaterials[i] != null) DestroyResource(_mergeMaterials[i]);
                    _mergeMaterials[i] = new Material(mats[i]) { name = "MeshCombiner_Opaque_Mat" + i };
                    MakeOpaque(_mergeMaterials[i]);
                }
                mats[i] = _mergeMaterials[i];
            }
        }

        /// <summary>创建 / 复用不透明实例材质：重叠区域由深度决定显示单层，颜色不叠加。</summary>
        private Material GetOpaqueMaterial()
        {
            if (_mergeMaterial == null)
            {
                var baseMat = GetComponent<MeshRenderer>().sharedMaterial;
                // 用确定不透明的 URP/Unlit 材质，而不是复制源材质（避免继承透明混合属性）
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                _mergeMaterial = new Material(shader) { name = "MeshCombiner_Opaque" };
                if (baseMat != null)
                {
                    // 只复制纹理与基础色，绝不复制混合/表面属性
                    if (baseMat.HasProperty("_BaseMap") && _mergeMaterial.HasProperty("_BaseMap"))
                        _mergeMaterial.SetTexture("_BaseMap", baseMat.GetTexture("_BaseMap"));
                    if (baseMat.HasProperty("_MainTex") && _mergeMaterial.HasProperty("_MainTex"))
                        _mergeMaterial.SetTexture("_MainTex", baseMat.GetTexture("_MainTex"));
                    if (baseMat.HasProperty("_BaseColor") && _mergeMaterial.HasProperty("_BaseColor"))
                        _mergeMaterial.SetColor("_BaseColor", baseMat.GetColor("_BaseColor"));
                    if (baseMat.HasProperty("_Color") && _mergeMaterial.HasProperty("_Color"))
                        _mergeMaterial.SetColor("_Color", baseMat.GetColor("_Color"));
                }
                MakeOpaque(_mergeMaterial);
            }
            return _mergeMaterial;
        }

        /// <summary>把材质切换为不透明渲染（关闭透明混合、写深度，重叠区域不再叠加）。</summary>
        private static void MakeOpaque(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_SURFACE_TYPE_OPAQUE");
            // 显式强制混合属性：不透明 = 源色直接覆盖，绝不叠加
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", 1f); // BlendMode.One
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", 0f); // BlendMode.Zero
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
        }

        private static void DestroyResource(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        /// <summary>把渲染层级写入 MeshRenderer（URP 2D 下与 SpriteRenderer 同排序体系）。</summary>
        private void ApplySorting()
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) return;
            if (!string.IsNullOrEmpty(sortingLayerName))
                mr.sortingLayerName = sortingLayerName;
            mr.sortingOrder = sortingOrder;
        }

        /// <summary>
        /// 强制材质进入透明队列（renderQueue=3000）。
        /// 不透明材质（默认 2000）的 MeshRenderer 不参与 URP 2D 的透明排序，
        /// 导致 Sorting Order 与 SpriteRenderer 无法混排（遮挡由深度决定）。
        /// </summary>
        private static void EnsureTransparentQueue(Material mat)
        {
            if (mat == null) return;
            if (mat.renderQueue != 3000)
                mat.renderQueue = 3000;
        }

        private static bool IsValidSource(MeshRenderer src)
        {
            if (src == null) return false;
            var mf = src.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null;
        }

        private void OnDestroy()
        {
            if (_mergeMaterial != null)
            {
                DestroyResource(_mergeMaterial);
                _mergeMaterial = null;
            }
            if (_mergeMaterials != null)
            {
                for (int i = 0; i < _mergeMaterials.Length; i++) DestroyResource(_mergeMaterials[i]);
                _mergeMaterials = null;
            }
            if (_combined == null) return;
            DestroyResource(_combined);
            _combined = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 编辑器里改字段/拖入源时兜底刷新（LateUpdate 在 [ExecuteAlways] 下也会跑）
            ApplySorting();
            if (_hasInit) Rebuild();
        }
#endif
    }
}
