#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Aprender.Editor
{
    /// <summary>Composes the imported Toon Environments pack into a build-ready garden.</summary>
    [InitializeOnLoad]
    public static class GreenEnvironmentBuilder
    {
        public const string PrefabPath = "Assets/Aprender/Resources/Environment/GreenPlaza.prefab";
        private const string Generated = "Assets/Aprender/Generated/Environment";
        private static bool building;

        static GreenEnvironmentBuilder() => QueueRefresh();

        internal static void QueueRefresh() => EditorApplication.delayCall += EnsureWhenReady;

        private static void EnsureWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureWhenReady;
                return;
            }
            if (!EditorApplication.isPlayingOrWillChangePlaymode) EnsureAssets();
        }

        public static void EnsureAssets() => Generate(false);

        [MenuItem("Ilha do Saber/Cenário/Recriar jardim verde")]
        public static void Rebuild() => Generate(true);

        private static void Generate(bool force)
        {
            if (building || (!force && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)) return;
            var sourceRoot = FindPackRoot();
            if (sourceRoot == null)
            {
                if (force) Debug.LogWarning("Ilha do Saber: importe primeiro Toon Environments World Creator Pack Lite pelo Package Manager > My Assets.");
                return; // An unopened Asset Store download is not an imported asset.
            }
            var sources = AssetDatabase.FindAssets("t:Prefab", new[] { sourceRoot })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var trees = Select(sources, "tree", "pine", "oak", "birch");
            if (trees.Length == 0)
            {
                Debug.LogWarning("Ilha do Saber: o pacote foi encontrado, mas seus prefabs de árvores ainda não foram importados.");
                return;
            }

            building = true;
            var root = new GameObject("Jardim verde · Toon Environments World Creator Pack Lite");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                Directory.CreateDirectory(Generated);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var palette = new Dictionary<Material, Material>();
                var used = new HashSet<string>();
                // All tall props stay outside the walking circle and behind the portals.
                var treePositions = new[]
                {
                    new Vector3(-11.6f, 0, 6.2f), new Vector3(-8.8f, 0, 10.0f),
                    new Vector3(-3.8f, 0, 12.0f), new Vector3(3.7f, 0, 12.2f),
                    new Vector3(9.5f, 0, 9.3f), new Vector3(12.6f, 0, 3.4f),
                    new Vector3(-13.1f, 0, -.6f), new Vector3(13.2f, 0, -2.4f)
                };
                for (var i = 0; i < treePositions.Length; i++)
                    Place(trees[i % trees.Length], root.transform, treePositions[i], 4.1f + (i % 3) * .5f,
                        i * 113f + 19f, palette, used);

                var bushes = Select(sources, "bush", "shrub", "hedge");
                var rocks = Select(sources, "rock", "stone", "boulder");
                var grass = Select(sources, "grass", "flower", "fern", "plant");
                var edgePositions = new[]
                {
                    new Vector3(-10.4f, 0, -5.1f), new Vector3(-11.8f, 0, 2.9f),
                    new Vector3(-7.6f, 0, 9.7f), new Vector3(-1.9f, 0, 11.2f),
                    new Vector3(5.9f, 0, 10.7f), new Vector3(11.0f, 0, 5.3f),
                    new Vector3(10.5f, 0, -5.4f), new Vector3(-7.7f, 0, -8.8f),
                    new Vector3(7.8f, 0, -8.6f)
                };
                for (var i = 0; i < edgePositions.Length; i++)
                {
                    var position = edgePositions[i];
                    if (bushes.Length > 0)
                        Place(bushes[i % bushes.Length], root.transform, position, .85f + i % 3 * .12f, i * 71, palette, used);
                    if (rocks.Length > 0 && i % 2 == 0)
                        Place(rocks[i % rocks.Length], root.transform, position + new Vector3(1.0f, 0, .7f), .65f,
                            i * 53, palette, used);
                    if (grass.Length > 0)
                        for (var j = 0; j < 2; j++)
                            Place(grass[(i + j) % grass.Length], root.transform, position + new Vector3(j * .8f - .5f, 0, -1),
                                .50f + j * .15f, i * 37 + j * 43, palette, used, true);
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var sourceDependencies = AssetDatabase.GetDependencies(used.ToArray(), true)
                    .Where(path => path.StartsWith(sourceRoot + "/", StringComparison.Ordinal)
                        && !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.Ordinal);
                File.WriteAllLines(Generated + "/SourceAssets.txt", new[]
                {
                    "Toon Environments World Creator Pack Lite",
                    "https://assetstore.unity.com/packages/3d/environments/landscapes/toon-environments-world-creator-pack-lite-264325",
                    "Original source prefabs used by GreenPlaza.prefab:"
                }.Concat(used.OrderBy(path => path, StringComparer.Ordinal))
                    .Concat(new[] { "", "Original mesh, material and texture dependencies (kept unchanged):" })
                    .Concat(sourceDependencies));
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(Generated + "/SourceAssets.txt");
                Debug.Log("Ilha do Saber: jardim verde criado com " + used.Count + " prefabs originais do pacote Toon Environments.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                building = false;
            }
        }

        private static string FindPackRoot()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/Aprender/", StringComparison.Ordinal)) continue;
                var parts = path.Split('/');
                for (var i = 1; i < parts.Length - 1; i++)
                {
                    var name = parts[i].Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
                    if (name.Contains("toonenvironment") || name.Contains("worldcreator"))
                        return string.Join("/", parts.Take(i + 1));
                }
            }
            return null;
        }

        private static string[] Select(IEnumerable<string> paths, params string[] names) => paths.Where(path =>
        {
            var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return names.Any(name.Contains) && !new[]
            {
                "stump", "chopped", "trunk", "dead", "fallen", "log", "leaf", "leaves", "path", "tile", "terrain", "ground", "wall", "floor"
            }.Any(name.Contains);
        }).ToArray();

        private static void Place(string path, Transform parent, Vector3 position, float targetSize, float yaw,
            IDictionary<Material, Material> palette, ISet<string> used, bool normalizeLargestDimension = false)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) return;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.transform.SetParent(parent, false);
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { Object.DestroyImmediate(instance); return; }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            // Flat flowers have very little height: fitting them by height made their
            // petals several metres wide. Ground details fit inside a small bounding box.
            var sourceSize = normalizeLargestDimension
                ? Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) : bounds.size.y;
            if (sourceSize < .001f) { Object.DestroyImmediate(instance); return; }
            var scale = targetSize / sourceSize;
            instance.transform.localScale *= scale;
            var center = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var rotation = Quaternion.Euler(0, yaw, 0);
            instance.transform.localPosition = position - rotation * center * scale;
            instance.transform.localRotation = rotation * instance.transform.localRotation;
            instance.name = source.name;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(material => CompatibleMaterial(material, palette)).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            used.Add(path);
        }

        private static Material CompatibleMaterial(Material source, IDictionary<Material, Material> palette)
        {
            if (source == null) return null;
            if (palette.TryGetValue(source, out var cached)) return cached;
            if (source.shader != null && source.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
            {
                palette.Add(source, source);
                return source;
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long fileId);
            var outputPath = Generated + "/Material-" + guid + "-" + fileId + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(outputPath);
            if (material == null)
            {
                material = new Material(shader) { name = source.name + " · URP" };
                AssetDatabase.CreateAsset(material, outputPath);
            }
            material.shader = shader;
            var color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            var texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap")
                : source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            material.color = color;
            material.mainTexture = texture;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .05f);
            var cutout = source.IsKeywordEnabled("_ALPHATEST_ON") || source.renderQueue == (int)RenderQueue.AlphaTest;
            if (cutout && material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 1);
                material.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : .5f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            }
            EditorUtility.SetDirty(material);
            palette.Add(source, material);
            return material;
        }
    }

    public sealed class GreenEnvironmentBuildPreparation : IPreprocessBuildWithReport
    {
        public int callbackOrder => -90;
        public void OnPreprocessBuild(BuildReport report) => GreenEnvironmentBuilder.EnsureAssets();
    }

    public sealed class GreenEnvironmentImportWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (importedAssets.Any(path => !path.StartsWith("Assets/Aprender/", StringComparison.Ordinal)
                && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
                GreenEnvironmentBuilder.QueueRefresh();
        }
    }
}
#endif
