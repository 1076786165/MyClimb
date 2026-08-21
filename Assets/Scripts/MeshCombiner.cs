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

        private Mesh _combined;
        private CombineInstance[] _items;
        private bool _hasInit;

        private void Awake()
        {
            _combined = new Mesh { name = "CombinedMesh_" + name };
            _combined.indexFormat = IndexFormat.UInt32;
            _combined.MarkDynamic();
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

        public void Rebuild()
        {
            if (!_hasInit) return;

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

            Matrix4x4 offsetM = Matrix4x4.TRS(new Vector3(offset.x, offset.y, 0f), Quaternion.identity, Vector3.one);

            int idx = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                var src = sources[i];
                if (!IsValidSource(src)) continue;

                _items[idx].mesh = src.GetComponent<MeshFilter>().sharedMesh;
                _items[idx].transform = offsetM * transform.worldToLocalMatrix * src.transform.localToWorldMatrix;
                idx++;
            }

            _combined.Clear();
            _combined.CombineMeshes(_items, mergeSubMeshes, true);

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
                for (int i = 0; i < mats.Length; i++) EnsureTransparentQueue(mats[i]);
                GetComponent<MeshRenderer>().sharedMaterials = mats;
            }
            else
            {
                EnsureTransparentQueue(GetComponent<MeshRenderer>().sharedMaterial);
            }
        }

        private void ApplySorting()
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) return;
            if (!string.IsNullOrEmpty(sortingLayerName))
                mr.sortingLayerName = sortingLayerName;
            mr.sortingOrder = sortingOrder;
        }

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
            if (_combined == null) return;
            if (Application.isPlaying) Destroy(_combined);
            else DestroyImmediate(_combined);
            _combined = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplySorting();
            if (_hasInit) Rebuild();
        }
#endif
    }
}