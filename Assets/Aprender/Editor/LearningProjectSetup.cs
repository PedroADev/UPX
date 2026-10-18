using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Aprender.Editor
{
    [InitializeOnLoad]
    public static class LearningProjectSetup
    {
        public const string ScenePath = "Assets/Aprender/Scenes/IlhaDoSaber.unity";
        const string ResourcesPath = "Assets/Aprender/Resources";
        const string PipelinePath = ResourcesPath + "/IlhaPipeline.asset";
        static Process ownedServer;

        static LearningProjectSetup()
        {
            EditorApplication.delayCall += PrepareIfMissing;
            EditorApplication.quitting += StopServer;
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
        }

        static void PrepareIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += PrepareIfMissing;
                return;
            }
            if (!File.Exists(PipelinePath)) PrepareProject();
            else EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        [MenuItem("Ilha do Saber/Preparar projeto")]
        public static void PrepareProject()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Saia do modo Play antes de preparar o projeto.");
            Directory.CreateDirectory(ResourcesPath);
            AssetDatabase.Refresh();
            var rendererPath = ResourcesPath + "/IlhaRenderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "IlhaRenderer";
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "IlhaPipeline";
                pipeline.msaaSampleCount = 4;
                pipeline.shadowDistance = 35;
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
                var levels = quality.FindProperty("m_QualitySettings");
                if (levels != null && i < levels.arraySize)
                {
                    levels.GetArrayElementAtIndex(i).FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipeline;
                    quality.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            QualitySettings.renderPipeline = pipeline;
            var panelPath = ResourcesPath + "/AprenderPanel.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, panelPath);
            }
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1280, 800);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Aprender/Resources/UnityDefaultRuntimeTheme.tss");
            EditorUtility.SetDirty(panel);
            IncludeRuntimeShaders();
            AvatarAssetBuilder.EnsureAssets();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            PlayerSettings.companyName = "Ilha do Saber";
            PlayerSettings.productName = "Ilha do Saber";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            AssetDatabase.SaveAssets();
            Debug.Log("Ilha do Saber pronta. Abra a cena pelo menu e pressione Play. O modo prática funciona sem servidor.");
        }

        static void IncludeRuntimeShaders()
        {
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Sprites/Default" })
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                var present = false;
                for (var i = 0; i < shaders.arraySize; i++) present |= shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader;
                if (present) continue;
                shaders.InsertArrayElementAtIndex(shaders.arraySize);
                shaders.GetArrayElementAtIndex(shaders.arraySize - 1).objectReferenceValue = shader;
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Ilha do Saber/Abrir jogo")]
        public static void OpenGame()
        {
            PrepareProject();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Ilha do Saber/Servidor/Iniciar servidor local")]
        public static void StartServer()
        {
            if (ownedServer != null && !ownedServer.HasExited) { Debug.Log("O servidor já está ligado em http://localhost:8787."); return; }
            var backend = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "backend"));
            try
            {
                var start = new ProcessStartInfo("node", "server.mjs")
                {
                    WorkingDirectory = backend, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardError = true, RedirectStandardOutput = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                ownedServer = Process.Start(start);
                // Drain both streams so a long classroom session cannot fill the pipe.
                ownedServer.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.Log("[Turma] " + e.Data); };
                ownedServer.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.LogWarning("[Turma] " + e.Data); };
                ownedServer.BeginOutputReadLine();
                ownedServer.BeginErrorReadLine();
                Debug.Log("Servidor iniciando em http://localhost:8787. Em outros computadores, use o IP deste computador. Recompilar scripts ou fechar o Editor encerra este servidor.");
            }
            catch (Exception e) { Debug.LogError("Não foi possível iniciar o servidor. Instale Node.js 20+ ou execute node backend/server.mjs. " + e.Message); }
        }

        [MenuItem("Ilha do Saber/Servidor/Parar servidor local")]
        public static void StopServer()
        {
            if (ownedServer == null) return;
            try { if (!ownedServer.HasExited) ownedServer.Kill(); }
            catch (InvalidOperationException) { }
            finally { ownedServer.Dispose(); ownedServer = null; }
        }

        [MenuItem("Ilha do Saber/Gerar executável Windows")]
        public static void BuildWindows()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Saia do modo Play antes de gerar o executável.");
            PrepareProject();
            Directory.CreateDirectory("Builds/IlhaDoSaber");
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Builds/IlhaDoSaber/IlhaDoSaber.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("A geração do jogo falhou. Veja o Console.");
            Debug.Log("Executável criado em Builds/IlhaDoSaber/IlhaDoSaber.exe.");
        }

        [MenuItem("Ilha do Saber/Preparar projeto", true)]
        [MenuItem("Ilha do Saber/Abrir jogo", true)]
        [MenuItem("Ilha do Saber/Gerar executável Windows", true)]
        static bool CanPrepareOrBuild() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
    }

    public sealed class LearningBuildPreparation : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (EditorBuildSettings.scenes.Any(s => s.enabled && s.path == LearningProjectSetup.ScenePath)) LearningProjectSetup.PrepareProject();
        }
    }
}
