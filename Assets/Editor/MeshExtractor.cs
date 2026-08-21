using System.IO;
using UnityEditor;
using UnityEngine;

namespace Climb.Core.EditorTools
{
    /// <summary>
    /// 一键从 FBX / 模型资源中提取独立 Mesh 资产：
    /// 在 Project 窗口选中 FBX（或直接选中其子 Mesh）→ 右键
    /// 「Mesh Extractor → Extract Mesh to Asset」，会在同目录生成纯 Mesh 的 .asset。
    /// 生成的 mesh 与 FBX 完全解耦，可拖给任意 Mesh 字段使用。
    /// </summary>
    public static class MeshExtractor
    {
        private const string MenuPath = "Assets/Mesh Extractor/Extract Mesh to Asset";

        [MenuItem(MenuPath, false, 60)]
        private static void ExtractSelected()
        {
            // 情况 1：直接选中了 FBX 的子 Mesh 资源
            var mesh = Selection.activeObject as Mesh;
            if (mesh != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrEmpty(assetPath)) return;
                ExtractMesh(mesh, Path.GetDirectoryName(assetPath), mesh.name);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[MeshExtractor] 已提取 {mesh.name} 到 {assetPath} 同目录。");
                return;
            }

            // 情况 2：选中了 FBX 模型资源（GameObject）
            var model = Selection.activeObject as GameObject;
            if (model == null)
            {
                Debug.LogWarning("[MeshExtractor] 请先在 Project 窗口选中一个 FBX / 模型资源。");
                return;
            }

            string modelPath = AssetDatabase.GetAssetPath(model);
            if (string.IsNullOrEmpty(modelPath)) return;

            // 开启 Read/Write 并重新导入，确保 mesh 数据可读
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var meshFilters = loaded != null
                ? loaded.GetComponentsInChildren<MeshFilter>(true)
                : new MeshFilter[0];

            if (meshFilters.Length == 0)
            {
                Debug.LogWarning($"[MeshExtractor] {Path.GetFileName(modelPath)} 里没有找到 MeshFilter / Mesh。");
                return;
            }

            string dir = Path.GetDirectoryName(modelPath);
            string modelBase = Path.GetFileNameWithoutExtension(modelPath);
            int count = 0;
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null) continue;
                string meshName = string.IsNullOrEmpty(mf.gameObject.name)
                    ? modelBase
                    : mf.gameObject.name;
                ExtractMesh(mf.sharedMesh, dir, meshName);
                count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[MeshExtractor] 已从 {modelBase} 提取 {count} 个 mesh 到 {dir}。");
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateExtract()
        {
            return Selection.activeObject is GameObject || Selection.activeObject is Mesh;
        }

        private static void ExtractMesh(Mesh src, string dir, string baseName)
        {
            Mesh copy = DuplicateMesh(src);
            string outPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(dir, baseName + ".asset"));
            AssetDatabase.CreateAsset(copy, outPath);
        }

        /// <summary>复制一份独立 mesh（保留顶点/法线/UV/颜色/切线/多 submesh 索引格式）。</summary>
        private static Mesh DuplicateMesh(Mesh src)
        {
            var mesh = new Mesh { name = src.name };
            mesh.indexFormat = src.indexFormat;
            mesh.vertices = src.vertices;
            mesh.normals = src.normals;
            mesh.tangents = src.tangents;
            mesh.colors = src.colors;
            mesh.uv = src.uv;
            mesh.uv2 = src.uv2;
            mesh.uv3 = src.uv3;
            mesh.uv4 = src.uv4;
            mesh.bounds = src.bounds;

            mesh.subMeshCount = src.subMeshCount;
            for (int i = 0; i < src.subMeshCount; i++)
                mesh.SetTriangles(src.GetTriangles(i), i);

            return mesh;
        }
    }
}
