using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Climb.Core.EditorTools
{
    /// <summary>
    /// 把 PNG 转换为贴合形状的精细平面 Mesh：
    /// 基于透明通道（alpha）在像素级提取轮廓（边界追踪），再用 Ear Clipping 三角剖分，
    /// 生成带纹理 UV 的独立 Mesh 资产。轮廓贴合到像素级，比 Sprite Tight Mesh 精细。
    /// 使用：在 Project 窗口选中 PNG → 右键「Png To Mesh → Create Mesh Asset」。
    /// </summary>
    public static class PngToMesh
    {
        private const string MenuPath = "Assets/Png To Mesh/Create Mesh Asset";
        private const float AlphaThreshold = 0.5f;

        [MenuItem(MenuPath, false, 61)]
        private static void Create()
        {
            var tex = Selection.activeObject as Texture2D;
            if (tex == null)
            {
                Debug.LogWarning("[PngToMesh] 请先在 Project 窗口选中一张 PNG / Texture2D。");
                return;
            }

            string texPath = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(texPath) ||
                Path.GetExtension(texPath).ToLowerInvariant() != ".png")
            {
                Debug.LogWarning("[PngToMesh] 请选中 PNG 文件。");
                return;
            }

            // 1) 确保纹理可读（读取 alpha 需要）
            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            bool changed = false;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                changed = true;
            }
            if (changed)
            {
                importer.SaveAndReimport();
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            }

            // 2) 读取 alpha 掩码
            Color[] pixels = tex.GetPixels();
            int w = tex.width, h = tex.height;
            bool[] inside = new bool[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                inside[i] = pixels[i].a >= AlphaThreshold;

            // 3) 像素级轮廓提取
            var segments = CollectBoundarySegments(inside, w, h);
            if (segments.Count == 0)
            {
                Debug.LogWarning("[PngToMesh] 没有有效形状（alpha 全透明？）。");
                return;
            }

            var loops = TraceLoops(segments);
            if (loops.Count == 0)
            {
                Debug.LogWarning("[PngToMesh] 无法提取闭合轮廓。");
                return;
            }

            // 4) 分离外壳与洞
            var (outer, holes) = SplitOuterHoles(loops);

            // 5) Ear Clipping 三角剖分外壳（自动统一为逆时针）
            var tris = Triangulate(outer);
            if (tris == null || tris.Count == 0)
            {
                Debug.LogWarning("[PngToMesh] 三角剖分失败（形状过于复杂？）。");
                return;
            }

            // 剔除重心落在洞内的三角形（带洞形状如环形也能正确生成）
            if (holes.Count > 0)
            {
                var kept = new List<int>();
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    var ca = outer[tris[t]];
                    var cb = outer[tris[t + 1]];
                    var cc = outer[tris[t + 2]];
                    Vector2 centroid = new Vector2(
                        (ca.x + cb.x + cc.x) / 3f,
                        (ca.y + cb.y + cc.y) / 3f);
                    bool inHole = false;
                    for (int hi = 0; hi < holes.Count && !inHole; hi++)
                        if (PointInPolygon(holes[hi], centroid)) inHole = true;
                    if (!inHole)
                    {
                        kept.Add(tris[t]);
                        kept.Add(tris[t + 1]);
                        kept.Add(tris[t + 2]);
                    }
                }
                tris = kept;
                if (tris.Count == 0)
                {
                    Debug.LogWarning("[PngToMesh] 三角剖分结果为空（形状全在洞内？）。");
                    return;
                }
            }

            // 6) 构建 mesh（顶点 z=0 平面，UV 映射纹理）
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(texPath) + "_Mesh" };
            var verts = new Vector3[outer.Count];
            var uvs = new Vector2[outer.Count];
            var bounds = new Bounds(new Vector3(outer[0].x, outer[0].y, 0f), Vector3.zero);
            for (int i = 0; i < outer.Count; i++)
            {
                verts[i] = new Vector3(outer[i].x, outer[i].y, 0f);
                uvs[i] = new Vector2((float)outer[i].x / w, (float)outer[i].y / h);
                bounds.Encapsulate(verts[i]);
            }
            Vector3 center = bounds.center;
            for (int i = 0; i < verts.Length; i++)
                verts[i] -= center; // pivot 居中

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // 7) 保存为资产
            string dir = Path.GetDirectoryName(texPath);
            string outPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(dir, mesh.name + ".asset"));
            AssetDatabase.CreateAsset(mesh, outPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[PngToMesh] 已生成精细贴合 mesh: {outPath}（顶点 {mesh.vertexCount}，三角形 {mesh.triangles.Length / 3}）");
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate() => Selection.activeObject is Texture2D;

        // ---------------- 轮廓提取 ----------------

        private struct Segment
        {
            public Vector2Int a, b;
            public Segment(Vector2Int a, Vector2Int b) { this.a = a; this.b = b; }
        }

        /// <summary>收集所有边界线段（像素角点坐标），方向保证外壳逆时针、洞顺时针。</summary>
        private static List<Segment> CollectBoundarySegments(bool[] inside, int w, int h)
        {
            bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && inside[y * w + x];

            var segs = new List<Segment>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!inside[y * w + x]) continue;
                if (!In(x, y + 1)) segs.Add(new Segment(new Vector2Int(x + 1, y + 1), new Vector2Int(x, y + 1))); // 上边
                if (!In(x, y - 1)) segs.Add(new Segment(new Vector2Int(x, y), new Vector2Int(x + 1, y)));         // 下边
                if (!In(x + 1, y)) segs.Add(new Segment(new Vector2Int(x + 1, y), new Vector2Int(x + 1, y + 1))); // 右边
                if (!In(x - 1, y)) segs.Add(new Segment(new Vector2Int(x, y + 1), new Vector2Int(x, y)));         // 左边
            }
            return segs;
        }

        /// <summary>把边界线段追踪成闭合环（优先延续当前方向，减少自交）。</summary>
        private static List<List<Vector2Int>> TraceLoops(List<Segment> segments)
        {
            var map = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < segments.Count; i++)
            {
                if (!map.TryGetValue(segments[i].a, out var list))
                    map[segments[i].a] = list = new List<int>();
                list.Add(i);
            }

            var loops = new List<List<Vector2Int>>();
            var used = new bool[segments.Count];

            for (int start = 0; start < segments.Count; start++)
            {
                if (used[start]) continue;

                var loop = new List<Vector2Int>();
                int idx = start;
                int guard = 0;
                while (idx >= 0)
                {
                    used[idx] = true;
                    loop.Add(segments[idx].a);
                    Vector2Int nextStart = segments[idx].b;

                    if (!map.TryGetValue(nextStart, out var cands))
                        break;

                    // 优先选与当前方向最连续的未用线段，减少自交
                    int next = -1;
                    float bestDot = -2f;
                    Vector2 dir = segments[idx].b - segments[idx].a;
                    for (int c = 0; c < cands.Count; c++)
                    {
                        int cand = cands[c];
                        if (used[cand]) continue;
                        Vector2 cdir = segments[cand].b - segments[cand].a;
                        float d = Vector2.Dot(dir.normalized, cdir.normalized);
                        if (d > bestDot) { bestDot = d; next = cand; }
                    }
                    if (next == -1) break; // 环已闭合
                    idx = next;
                    guard++;
                    if (guard > segments.Count + 8) break;
                }

                if (loop.Count > 2)
                    loops.Add(loop);
            }
            return loops;
        }

        private static float SignedArea(List<Vector2Int> poly)
        {
            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        /// <summary>分离外壳（面积最大）与洞环；洞转成逆时针以便桥接。</summary>
        private static (List<Vector2Int> outer, List<List<Vector2Int>> holes) SplitOuterHoles(List<List<Vector2Int>> loops)
        {
            List<Vector2Int> outer = null;
            float maxArea = 0f;
            foreach (var loop in loops)
            {
                float area = SignedArea(loop);
                if (Mathf.Abs(area) > maxArea)
                {
                    maxArea = Mathf.Abs(area);
                    outer = loop;
                }
            }

            var holes = new List<List<Vector2Int>>();
            foreach (var loop in loops)
                if (loop != outer)
                    holes.Add(loop);

            return (outer, holes);
        }

        /// <summary>点在多边形内判定（射线法）。</summary>
        private static bool PointInPolygon(List<Vector2Int> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) &&
                    p.x < (float)(b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        // ---------------- 三角剖分（Ear Clipping） ----------------

        private static List<int> Triangulate(List<Vector2Int> poly)
        {
            // 统一为逆时针（CCW），ear clipping 依赖该方向
            if (SignedArea(poly) < 0f)
                poly.Reverse();

            var idx = new List<int>();
            for (int i = 0; i < poly.Count; i++) idx.Add(i);
            var tris = new List<int>();

            int guard = 0;
            while (idx.Count > 3)
            {
                bool earFound = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int iPrev = idx[(i - 1 + idx.Count) % idx.Count];
                    int iCur = idx[i];
                    int iNext = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[iPrev], b = poly[iCur], c = poly[iNext];

                    // 逆时针多边形：cross > 0 才是凸点
                    float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                    if (cross <= 0.0001f) continue;

                    // 三角形内不能有其他顶点
                    bool blocked = false;
                    for (int j = 0; j < idx.Count; j++)
                    {
                        if (j == (i - 1 + idx.Count) % idx.Count || j == i || j == (i + 1) % idx.Count) continue;
                        if (PointInTriangle(poly[idx[j]], a, b, c)) { blocked = true; break; }
                    }
                    if (blocked) continue;

                    tris.Add(iPrev); tris.Add(iCur); tris.Add(iNext);
                    idx.RemoveAt(i);
                    earFound = true;
                    break;
                }

                guard++;
                if (!earFound || guard > poly.Count * 2 + 100)
                    return null; // 退化多边形
            }

            if (idx.Count == 3)
                tris.AddRange(new[] { idx[0], idx[1], idx[2] });
            return tris;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
            => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
}
