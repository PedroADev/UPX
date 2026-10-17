#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Aprender.Editor
{
    /// <summary>
    /// Imports the supplied CC0 Cube Pets into stable Resources paths. Generated
    /// prefabs reference the source meshes, retain UVs, and share their colormap.
    /// </summary>
    [InitializeOnLoad]
    public static class AvatarAssetBuilder
    {
        private const string Source = "Assets/kenney_cube-pets_1.0";
        private const string Root = "Assets/Aprender/Resources";
        private const string Generated = "Assets/Aprender/Generated";
        private static bool building;

        static AvatarAssetBuilder()
        {
            EditorApplication.delayCall += EnsureWhenReady;
        }

        private static void EnsureWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureWhenReady;
                return;
            }
            if (!EditorApplication.isPlayingOrWillChangePlaymode) EnsureAssets();
        }

        [MenuItem("Aprender/Recriar personagens e retratos")]
        public static void RebuildAssets() => Generate(true);

        public static void EnsureAssets() => Generate(false);

        private static void Generate(bool force)
        {
            if (building) return;
            building = true;
            try
            {
                Directory.CreateDirectory(Root + "/Avatars");
                Directory.CreateDirectory(Root + "/AvatarPreviews");
                Directory.CreateDirectory(Generated);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var palette = EnsurePalette();
                if (palette == null) return;
                var changed = false;
                foreach (var avatar in AvatarCatalog.Items)
                {
                    var sourcePath = Source + "/Models/FBX format/animal-" + avatar.id + ".fbx";
                    var prefabPath = Root + "/Avatars/" + avatar.id + ".prefab";
                    var previewPath = Root + "/AvatarPreviews/" + avatar.id + ".png";
                    if (force || AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                    {
                        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                        if (source == null)
                        {
                            Debug.LogWarning("Aprender: modelo não encontrado: " + sourcePath);
                            continue;
                        }
                        CreatePrefab(avatar, source, sourcePath, prefabPath, palette);
                        changed = true;
                    }

                    var preview = AssetDatabase.LoadAssetAtPath<Texture2D>(previewPath);
                    if (force || preview == null || preview.width < 512 || preview.height < 512)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        if (prefab != null)
                        {
                            CreatePreview(prefab, previewPath);
                            changed = true;
                        }
                    }
                }
                if (changed)
                {
                    AssetDatabase.SaveAssets();
                    Debug.Log("Aprender: personagens Kenney e retratos preparados. Menu Aprender permite recriá-los.");
                }
            }
            finally { building = false; }
        }

        private static void CreatePreview(GameObject prefab, string path)
        {
            // Render the actual avatar, rather than enlarging the package's 64 px thumbnails.
            // A preview scene isolates the studio lights and never changes the open game scene.
            const int size = 512;
            var studio = new PreviewRenderUtility();
            Texture2D portrait = null;
            RenderTexture downsampled = null;
            var previousTarget = RenderTexture.active;
            var previousSrgbWrite = GL.sRGBWrite;
            var previewOpen = false;
            try
            {
                var model = Object.Instantiate(prefab);
                model.hideFlags = HideFlags.HideAndDontSave;
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                studio.AddSingleGO(model);
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (var animation in model.GetComponentsInChildren<Animation>(true)) animation.enabled = false;
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);

                var camera = studio.camera;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.allowHDR = false;
                camera.allowMSAA = true;
                camera.orthographic = true;
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 100f;
                camera.transform.position = bounds.center + new Vector3(3.8f, 2.4f, 5.5f).normalized * 8f;
                camera.transform.LookAt(bounds.center);
                var horizontal = ProjectExtent(bounds.extents, camera.transform.right);
                var vertical = ProjectExtent(bounds.extents, camera.transform.up);
                camera.orthographicSize = Mathf.Max(horizontal, vertical) * 1.12f;
                studio.ambientColor = new Color(.58f, .60f, .66f);
                studio.lights[0].color = new Color(1f, .96f, .88f);
                studio.lights[0].intensity = 1.05f;
                studio.lights[0].transform.rotation = Quaternion.Euler(32f, -30f, 0f);
                studio.lights[1].color = new Color(.78f, .87f, 1f);
                studio.lights[1].intensity = .65f;
                studio.lights[1].transform.rotation = Quaternion.Euler(20f, 150f, 0f);

                // Supersampling keeps the low-poly silhouette smooth at both card and hero sizes.
                studio.BeginPreview(new Rect(0f, 0f, size * 2, size * 2), GUIStyle.none);
                previewOpen = true;
                studio.Render(true, false);
                var rendered = studio.EndPreview();
                previewOpen = false;
                downsampled = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(rendered, downsampled);
                RenderTexture.active = downsampled;
                portrait = new Texture2D(size, size, TextureFormat.RGBA32, false);
                portrait.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                portrait.Apply(false, false);
                File.WriteAllBytes(path, portrait.EncodeToPNG());
            }
            finally
            {
                if (previewOpen) studio.EndPreview();
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgbWrite;
                if (downsampled != null) RenderTexture.ReleaseTemporary(downsampled);
                if (portrait != null) Object.DestroyImmediate(portrait);
                studio.Cleanup();
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = size;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        private static float ProjectExtent(Vector3 extents, Vector3 direction) =>
            Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.y) * extents.y + Mathf.Abs(direction.z) * extents.z;

        private static Material EnsurePalette()
        {
            const string texturePath = Source + "/Models/FBX format/Textures/colormap.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                Debug.LogWarning("Aprender: aguardando importação da textura original dos personagens.");
                return null;
            }
            var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (textureImporter != null && (textureImporter.filterMode != FilterMode.Point || textureImporter.mipmapEnabled))
            {
                textureImporter.filterMode = FilterMode.Point;
                textureImporter.mipmapEnabled = false;
                textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                textureImporter.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            }
            const string materialPath = Generated + "/KenneyPetPalette.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) throw new BuildFailedException("Shader dos personagens não encontrado. Aguarde a importação do Universal Render Pipeline.");
            if (material == null)
            {
                material = new Material(shader) { name = "Kenney Cube Pets · colormap" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = shader;
            material.color = Color.white;
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .08f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreatePrefab(AvatarDefinition avatar, GameObject source, string sourcePath, string path, Material palette)
        {
            var root = new GameObject(avatar.name);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                model.name = "Modelo Kenney · " + avatar.id;
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new BuildFailedException("Modelo sem malha: " + sourcePath);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    renderer.sharedMaterials = Enumerable.Repeat(palette, Mathf.Max(1, renderer.sharedMaterials.Length)).ToArray();
                    renderer.receiveShadows = true;
                }
                var scale = 1.15f / Mathf.Max(.001f, bounds.size.y);
                model.transform.localScale *= scale;
                model.transform.localPosition = new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, -bounds.center.z * scale);
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                ConfigureAnimation(model, sourcePath, avatar.id);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void ConfigureAnimation(GameObject model, string sourcePath, string id)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__") && clip.length > .05f).ToArray();
            if (clips.Length == 0) return; // AvatarMotion supplies movement for unrigged models.
            var clip = clips.FirstOrDefault(item => item.name.ToLowerInvariant().Contains("idle")) ?? clips[0];
            if (clip.legacy)
            {
                var animation = model.GetComponent<Animation>();
                if (animation == null) animation = model.AddComponent<Animation>();
                animation.AddClip(clip, clip.name);
                animation.clip = clip;
                animation.wrapMode = WrapMode.Loop;
                animation.playAutomatically = true;
            }
            else
            {
                var controllerPath = Generated + "/" + id + ".controller";
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null)
                {
                    controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                    controller.AddMotion(clip);
                }
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
            }
        }
    }

    public sealed class AvatarBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPreprocessBuild(BuildReport report) => AvatarAssetBuilder.EnsureAssets();
    }
}
#endif
