using UnityEngine;

namespace Climb.Core.Softbody
{
    /// <summary>
    /// 程序化软管渲染器（Catmull-Rom 一条平滑曲线版）：
    /// 多锚点模式（anchorPoints，如 根部→关节→末端）用一条 C1 连续样条贯穿所有物理锚点，
    /// 关节处圆滑过渡、无拼接断痕；弯曲时整条四肢平滑变形。
    /// 软体表现：锚点滞后缓冲（SmoothDamp）+ 待机摆动 + 颤振 + Squash&Stretch。
    /// 兼容旧的双锚点模式（start/end 二次贝塞尔）。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [ExecuteAlways]
    public sealed class SoftLimbRenderer : MonoBehaviour
    {
        [Header("双锚点模式（二次贝塞尔，兼容旧用法）")]
        public Transform startPoint;
        public Transform endPoint;
        [Tooltip("中间弯曲控制点（可选，多锚点模式下不使用）")] public Transform elbowPoint;

        [Header("多锚点模式（Catmull-Rom 一条平滑曲线）")]
        [Tooltip("按顺序：根部→关节→末端…，用一条 C1 连续样条贯穿所有锚点，关节处平滑无断痕")]
        public Transform[] anchorPoints;

        [Header("软管形态")]
        [Range(0.05f, 2f)] public float width = 0.71f;
        [Range(4, 64)] public int segments = 55;

        [Header("软体表现")]
        [Range(0f, 8f)] public float lagFactor = 4f;         // 中段滞后弯曲强度
        [Range(0f, 200f)] public float lagSmooth = 100f;     // 锚点滞后速度（越小越肉/越滞后）
        [Range(0f, 1f)] public float squashFactor = 0f;      // 拉伸形变强度（0=关闭）
        [Range(0f, 0.5f)] public float idleWobbleAmp = 0f;   // 待机摆动幅度（0=关闭）
        [Range(0.1f, 5f)] public float idleWobbleFreq = 0.1f;
        [Range(0f, 1f)] public float shakeGain = 0f;         // 颤振强度（外部驱动）

        [Header("渲染顺序")]
        [Tooltip("渲染层级名（保持 Default，需为已有 Sorting Layer）")] public string sortingLayerName = "Default";
        [Tooltip("同层级内渲染顺序：越大越靠前（类似 SpriteRenderer 的 Order in Layer）")] public int sortingOrder = 0;

        [Header("分段配色")]
        [Tooltip("启用后沿长度方向分段着色（需给 segmentColors 填 2 个以上颜色）")]
        public bool useSegmentColors = false;
        [Tooltip("每段颜色：按顺序沿软管长度分布，段间自动过渡")]
        public Color[] segmentColors =
        {
            new Color(0.93f, 0.72f, 0.52f, 1f),
            new Color(0.40f, 0.87f, 1f, 1f),
        };
        [Tooltip("段间过渡宽度（0=硬切，越大过渡越柔和）")]
        [Range(0f, 0.5f)] public float segmentBlend = 0.08f;

        private Mesh _mesh;
        private Material _segmentMaterial;         // 分段配色时的独立材质实例（不污染共享材质）
        private Material _originalSharedMaterial;  // 进入分段配色前记录的共享材质
        private float _time;
        private float _naturalLength = 1f;
        private bool _hasInit;

        // 多锚点模式状态
        private Vector3[] _smoothAnchors;
        private Vector3[] _anchorVels;
        private Vector3[] _samples;
        private bool _multi;

        // 双锚点模式状态
        private Vector3 _smoothElbow;
        private Vector3 _lastMid;

        public void SetShake(float amount) => shakeGain = Mathf.Clamp01(amount);

        /// <summary>把渲染顺序写入 MeshRenderer（URP 2D Renderer 下与 SpriteRenderer 同排序体系）。</summary>
        private void ApplySorting()
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) return;
            if (!string.IsNullOrEmpty(sortingLayerName))
                mr.sortingLayerName = sortingLayerName;
            mr.sortingOrder = sortingOrder;
        }

        /// <summary>启用分段配色时创建独立材质+分段纹理，关闭时还原共享材质。</summary>
        private void ApplySegmentColors()
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) return;

            if (useSegmentColors && segmentColors != null && segmentColors.Length > 0)
            {
                if (_segmentMaterial == null)
                {
                    _segmentMaterial = new Material(_originalSharedMaterial != null ? _originalSharedMaterial : CreateMaterial())
                    {
                        name = "SoftLimb_Segment"
                    };
                    EnsureTransparentQueue(_segmentMaterial); // 复制材质会继承不透明队列，需强制进入透明队列
                }
                Texture2D tex = CreateTubeTexture(segmentColors, segmentBlend);
                if (_segmentMaterial.HasProperty("_BaseMap")) _segmentMaterial.SetTexture("_BaseMap", tex);
                if (_segmentMaterial.HasProperty("_MainTex")) _segmentMaterial.mainTexture = tex;
                mr.material = _segmentMaterial; // 实例化，不污染共享材质
            }
            else if (_segmentMaterial != null)
            {
                mr.sharedMaterial = _originalSharedMaterial;
                DestroyResource(_segmentMaterial);
                _segmentMaterial = null;
            }
        }

        private static void DestroyResource(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 编辑器里修改字段即时生效（与 SpriteRenderer 相同的直觉）
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) EnsureTransparentQueue(mr.sharedMaterial);
            ApplySorting();
            ApplySegmentColors();
        }
#endif

        private void Awake()
        {
            _mesh = new Mesh { name = "SoftLimb_Mesh" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = GetComponent<MeshRenderer>();
            if (mr.sharedMaterial == null)
                mr.sharedMaterial = CreateMaterial();
            _originalSharedMaterial = mr.sharedMaterial; // 在确保有材质之后记录
            EnsureTransparentQueue(mr.sharedMaterial);   // 关键：进入透明队列才能参与 2D sorting
            ApplySorting();
            ApplySegmentColors();
            InitMode();
        }

        private void InitMode()
        {
            _multi = anchorPoints != null && anchorPoints.Length >= 2;
            if (_multi)
            {
                _smoothAnchors = new Vector3[anchorPoints.Length];
                _anchorVels = new Vector3[anchorPoints.Length];
                for (int i = 0; i < anchorPoints.Length; i++)
                    if (anchorPoints[i] != null)
                        // 世界坐标 → 本物体本地坐标：mesh 顶点必须用本地坐标，
                        // 否则 MeshRenderer 所在物体不在世界原点时渲染会整体错位
                        _smoothAnchors[i] = transform.InverseTransformPoint(anchorPoints[i].position);
                int n = Mathf.Max(segments, 4);
                if (_samples == null || _samples.Length != n + 1)
                    _samples = new Vector3[n + 1];
            }
            else if (startPoint != null && endPoint != null)
            {
                Vector3 sp = transform.InverseTransformPoint(startPoint.position);
                Vector3 ep = transform.InverseTransformPoint(endPoint.position);
                _smoothElbow = (sp + ep) * 0.5f;
                _lastMid = _smoothElbow;
            }
        }

        private void Start()
        {
            if (!_hasInit) { InitMode(); _hasInit = true; }
        }

        private void LateUpdate()
        {
            if (!_hasInit) { InitMode(); _hasInit = true; }
            if (_mesh == null) return;
            _time += Time.deltaTime;

            if (_multi && anchorPoints != null && anchorPoints.Length >= 2)
                BuildCatmullRomStrip(useLag: true);
            else if (startPoint != null && endPoint != null)
                BuildBezierStrip();
        }

        /// <summary>多锚点模式：一条 Catmull-Rom 样条贯穿所有锚点。</summary>
        private void BuildCatmullRomStrip(bool useLag)
        {
            int n = anchorPoints.Length;
            if (_smoothAnchors == null || _smoothAnchors.Length != n) InitMode();

            // 1. 锚点滞后缓冲：首尾锚点始终实时（根部固定、末端必须跟鼠标），
            //    中间锚点（关节）滞后 → 软体弯曲感，且不会出现"末端圆点与曲线末端分离"
            for (int i = 0; i < n; i++)
            {
                if (anchorPoints[i] == null) return;
                // 世界坐标 → 本物体本地坐标，避免 MeshRenderer 不在世界原点时整体错位
                Vector3 target = transform.InverseTransformPoint(anchorPoints[i].position);
                if (!useLag || i == 0 || i == n - 1)
                {
                    _smoothAnchors[i] = target;
                }
                else
                {
                    float smooth = Mathf.Max(lagSmooth, 0.1f);
                    _smoothAnchors[i] = Vector3.SmoothDamp(
                        _smoothAnchors[i], target, ref _anchorVels[i], 1f / smooth);
                }
            }

            // 2. 中间锚点叠加待机摆动 + 颤振（由样条平滑传播为整条四肢的软体波动）
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 ax = (_smoothAnchors[i + 1] - _smoothAnchors[i - 1]).normalized;
                if (ax.sqrMagnitude < 0.0001f) ax = Vector3.right;
                Vector3 perp = new Vector3(-ax.y, ax.x, 0f);

                float wob = Mathf.Sin(_time * idleWobbleFreq + i * 1.7f) * idleWobbleAmp;
                float wob2 = Mathf.Sin(_time * idleWobbleFreq * 0.61f + i * 2.3f) * idleWobbleAmp * 0.6f;
                _smoothAnchors[i] += perp * (wob + wob2);

                if (shakeGain > 0.001f)
                {
                    float j = (Mathf.PerlinNoise(_time * 40f, i * 3.7f) - 0.5f) * 2f * shakeGain * 0.15f;
                    _smoothAnchors[i] += perp * j;
                }
            }

            // 3. Catmull-Rom 采样（C1 连续，段间共享切线 → 关节处平滑过渡）
            //    关键：每段都采样到终点 t=1（CatmullRom(p0,p1,p2,p3,1)=p2），
            //    即曲线精确经过每个锚点；非首段从 s=1 起跳过起点（=前段终点，避免重复）。
            //    旧实现 t 从 1/(N+1) 取到 N/(N+1)，永远取不到 t=1，
            //    导致所有中间锚点（关节）都被曲线“绕过”、与锚点脱节 → 视觉错位。
            int segs = Mathf.Max(segments, 4);
            int curveCount = n - 1;
            int perSeg = Mathf.Max(segs / curveCount, 1); // 每段采样数（含终点）
            int total = 1 + curveCount * perSeg;
            if (_samples == null || _samples.Length != total)
                _samples = new Vector3[total];

            Vector3 pre = _smoothAnchors[0] + (_smoothAnchors[0] - _smoothAnchors[1]);
            Vector3 post = _smoothAnchors[n - 1] + (_smoothAnchors[n - 1] - _smoothAnchors[n - 2]);

            int idx = 0;
            _samples[idx++] = _smoothAnchors[0];   // 根部（首点）
            for (int i = 0; i < curveCount; i++)
            {
                Vector3 p0 = (i == 0) ? pre : _smoothAnchors[i - 1];
                Vector3 p1 = _smoothAnchors[i];
                Vector3 p2 = _smoothAnchors[i + 1];
                Vector3 p3 = (i + 2 < n) ? _smoothAnchors[i + 2] : post;

                for (int s = 1; s <= perSeg; s++)
                {
                    float t = (float)s / perSeg;
                    _samples[idx++] = CatmullRom(p0, p1, p2, p3, t);
                }
            }
            int count = idx;

            // 4. 自然长度（首次）与 Squash & Stretch
            float totalLen = 0f;
            for (int i = 1; i < count; i++)
                totalLen += Vector3.Distance(_samples[i - 1], _samples[i]);
            if (_naturalLength <= 0.01f) _naturalLength = totalLen;
            if (totalLen < 0.001f) totalLen = _naturalLength;

            float wScale = 1f + squashFactor * (1f - totalLen / _naturalLength);
            wScale = Mathf.Clamp(wScale, 0.5f, 1.6f);

            BuildStrip(_samples, count, width * wScale);
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>把采样点列表构建为条带网格（本物体本地坐标）。</summary>
        private void BuildStrip(Vector3[] pts, int count, float w)
        {
            int vCount = count * 2;
            var verts = new Vector3[vCount];
            var uvs = new Vector2[vCount];
            var normals = new Vector3[vCount];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                Vector3 pos = pts[i];
                Vector3 tangent;
                if (i == 0)
                    tangent = (pts[1] - pts[0]).normalized;
                else if (i == count - 1)
                    tangent = (pts[i] - pts[i - 1]).normalized;
                else
                    tangent = (pts[i + 1] - pts[i - 1]).normalized;
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.right;

                Vector3 n = new Vector3(-tangent.y, tangent.x, 0f);
                if (n.sqrMagnitude < 0.0001f) n = Vector3.up;

                verts[i * 2] = pos + n * (w * 0.5f);
                verts[i * 2 + 1] = pos - n * (w * 0.5f);
                uvs[i * 2] = new Vector2(t, 1f);
                uvs[i * 2 + 1] = new Vector2(t, 0f);
                normals[i * 2] = n;
                normals[i * 2 + 1] = n;
            }

            var tris = new int[(count - 1) * 6];
            for (int i = 0; i < count - 1; i++)
            {
                int b = i * 2;
                int idx = i * 6;
                tris[idx] = b;
                tris[idx + 1] = b + 2;
                tris[idx + 2] = b + 1;
                tris[idx + 3] = b + 2;
                tris[idx + 4] = b + 3;
                tris[idx + 5] = b + 1;
            }

            _mesh.Clear();
            _mesh.vertices = verts;
            _mesh.uv = uvs;
            _mesh.normals = normals;
            _mesh.triangles = tris;
            _mesh.RecalculateBounds();
        }

        /// <summary>双锚点兼容模式：二次贝塞尔 + 虚拟中点滞后。</summary>
        private void BuildBezierStrip()
        {
            // 世界坐标 → 本物体本地坐标：mesh 顶点必须与 MeshRenderer 同一坐标系，
            // 否则 MeshRenderer 所在物体不在世界原点时渲染会整体错位（线段与锚点脱节）
            Vector3 p0 = transform.InverseTransformPoint(startPoint.position);
            Vector3 p2 = transform.InverseTransformPoint(endPoint.position);
            Vector3 mid = (p0 + p2) * 0.5f;

            Vector3 targetElbow = mid;
            if (elbowPoint != null)
            {
                targetElbow = transform.InverseTransformPoint(elbowPoint.position);
            }
            else
            {
                Vector3 axis = p2 - p0;
                float len = axis.magnitude;
                if (len > 0.001f)
                {
                    Vector3 ax = axis / len;
                    float dt = Mathf.Max(Time.deltaTime, 0.0001f);
                    Vector3 velMid = (mid - _lastMid) / dt;
                    Vector3 lateral = velMid - Vector3.Dot(velMid, ax) * ax;
                    targetElbow = mid + lateral * (lagFactor * 0.12f);
                }
            }

            float smooth = Mathf.Max(lagSmooth, 0.1f);
            _smoothElbow = Vector3.Lerp(_smoothElbow, targetElbow, 1f - Mathf.Exp(-smooth * Time.deltaTime));

            float dist = Vector3.Distance(p0, p2);
            float wScale = 1f + squashFactor * (1f - dist / _naturalLength);
            wScale = Mathf.Clamp(wScale, 0.5f, 1.6f);

            int n = Mathf.Max(segments, 4);
            if (_samples == null || _samples.Length != n + 1)
                _samples = new Vector3[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                float u = 1f - t;
                _samples[i] = u * u * p0 + 2f * u * t * _smoothElbow + t * t * p2;
            }

            BuildStrip(_samples, n + 1, width * wScale);
            _lastMid = mid;
        }

        private Material CreateMaterial()
        {
            Shader shader = FindUsableShader();
            var mat = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
            if (shader == null)
                Debug.LogError("[SoftLimbRenderer] 找不到可用 Shader，软管将不可见", this);

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", CreateTubeTexture());
            if (mat.HasProperty("_MainTex")) mat.mainTexture = CreateTubeTexture();
            mat.renderQueue = 3000;
            return mat;
        }

        private static Shader FindUsableShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) return shader;

            var sr = Object.FindFirstObjectByType<SpriteRenderer>();
            if (sr != null && sr.sharedMaterial != null && sr.sharedMaterial.shader != null)
                return sr.sharedMaterial.shader;

            shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            if (shader != null) return shader;

            return Shader.Find("Sprites/Default");
        }

        private Texture2D CreateTubeTexture()
        {
            const int w = 32, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                    tex.SetPixel(x, y, Color.white); // 无描边：纯白基础色，颜色交给 segmentColors 或材质
            }
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        /// <summary>分段配色版：纹理长度方向按 segmentColors 分段着色，段间可柔和过渡。</summary>
        private Texture2D CreateTubeTexture(Color[] segColors, float blend)
        {
            const int w = 32, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int x = 0; x < w; x++)
            {
                float f = (float)x / (w - 1);
                Color body = SampleSegmentColor(segColors, f, blend);
                for (int y = 0; y < h; y++)
                    tex.SetPixel(x, y, body); // 无描边：整个宽度同色
            }
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        private static Color SampleSegmentColor(Color[] colors, float t, float blend)
        {
            int count = colors.Length;
            if (count <= 0) return Color.white;
            if (count == 1) return colors[0];

            // t∈[0,1] → 段坐标 [0, count]，段 i 均匀占据 [i, i+1)
            float pos = Mathf.Clamp01(t) * count;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(pos), 0, count - 1);
            int iPrev = Mathf.Clamp(i0 - 1, 0, count - 1);
            float local = pos - i0; // 0..1（段内位置）

            if (blend <= 0.001f)
                return colors[i0];

            // 段开头（刚跨过边界）：与上一段颜色柔和过渡，段内保持纯色
            return local < blend
                ? Color.Lerp(colors[iPrev], colors[i0], Mathf.InverseLerp(0f, blend, local))
                : colors[i0];
        }

        private void OnDestroy()
        {
            DestroyResource(_segmentMaterial);
            _segmentMaterial = null;
            if (_mesh == null) return;
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
            _mesh = null;
        }
    }
}
