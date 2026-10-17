#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Aprender.Editor
{
    /// <summary>
    /// Editor-only QA tour. Drives the production flow, moves the real actor, and
    /// captures rendered Game View frames with UI Toolkit. No players are fabricated.
    /// </summary>
    [InitializeOnLoad]
    public static class ShowcaseCapture
    {
        const string Key = "Aprender.Showcase.";
        const int CaptureWidth = 1280, CaptureHeight = 800;
        const string CaptureSizeName = "Ilha do Saber QA temporario";
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly object LogLock = new object();
        static readonly List<LogEntry> Logs = new List<LogEntry>();
        static Manifest manifest;
        static GameBootstrap game;
        static WorldController world;
        static EditorWindow gameView;
        static double phaseStarted;
        static int phase;
        static string pendingCapture;
        static Vector3 movementStart;
        static bool finishing;
        static volatile bool captureLogs;

        [Serializable] public sealed class Shot
        {
            public string name, path, room, capturedAtUtc;
            public Vector3 position;
            public Vector2 movementInput;
            public int frame, width, height;
            public bool pngVerified;
        }

        [Serializable] public sealed class LogEntry
        {
            public string type, message, stack;
        }

        [Serializable] public sealed class Manifest
        {
            public string startedAtUtc, completedAtUtc, unityVersion, status, failure;
            public string outputDirectory;
            public string captureMethod = "Unity ScreenCapture.CaptureScreenshot; actual Game View, including runtime UI";
            public string sessionMode = "practice; no fabricated classmates";
            public string qaProfile = "Lia / dog";
            public bool progressRestored;
            public bool gameViewSelectionRestored;
            public int targetWidth = CaptureWidth, targetHeight = CaptureHeight;
            public int originalGameViewSizeIndex;
            public string originalGameViewSizeGroup, restoredGameViewSizeGroup;
            public bool firstAnswerCorrect;
            public string firstAnswerExplanation;
            public int answeredQuestions;
            public int errors, warnings;
            public Vector3 movementStart, movementEnd;
            public float movementDistance;
            public List<Shot> screenshots = new List<Shot>();
            public List<LogEntry> logs = new List<LogEntry>();
        }

        static ShowcaseCapture()
        {
            EditorApplication.playModeStateChanged += PlayStateChanged;
            EditorApplication.update += Tick;
            Application.logMessageReceivedThreaded += ReceiveLog;
            if (SessionState.GetBool(Key + "active", false))
            {
                captureLogs = true;
                var json = SessionState.GetString(Key + "manifest", "");
                if (!string.IsNullOrEmpty(json)) manifest = JsonUtility.FromJson<Manifest>(json);
                phase = SessionState.GetInt(Key + "phase", 0);
                phaseStarted = EditorApplication.timeSinceStartup;
            }
        }

        [MenuItem("Ilha do Saber/Capturas/Capturar demonstração completa")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Saia do Play e aguarde a compilação antes de iniciar as capturas.");
            if (SessionState.GetBool(Key + "active", false))
                throw new InvalidOperationException("Já existe uma demonstração em andamento.");
            // The standard Unity prompt protects unsaved user scenes. Cancellation is respected.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "screenshots",
                "demo-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
            Directory.CreateDirectory(directory);
            manifest = new Manifest
            {
                startedAtUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                status = "preparing",
                outputDirectory = directory
            };
            lock (LogLock) Logs.Clear();
            finishing = false;
            SnapshotProgress();
            SessionState.SetBool(Key + "active", true);
            captureLogs = true;
            SessionState.SetBool(Key + "restored", false);
            SessionState.SetBool(Key + "resolutionSaved", false);
            SessionState.SetInt(Key + "addedResolutionIndex", -1);
            SessionState.SetString(Key + "scene", SceneManager.GetActiveScene().path ?? "");
            Persist();
            try
            {
                LearningProjectSetup.PrepareProject();
                AvatarAssetBuilder.EnsureAssets();
                GreenEnvironmentBuilder.Rebuild();
                EditorSceneManager.OpenScene(LearningProjectSetup.ScenePath);
                FocusGameView();
                SessionState.SetBool(Key + "wasMaximized", gameView.maximized);
                gameView.maximized = true;
                ConfigureCaptureResolution();
                manifest.status = "starting-play";
                SetPhase(0);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        [MenuItem("Ilha do Saber/Capturas/Capturar demonstração completa", true)]
        static bool CanBegin() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            !EditorApplication.isCompiling && !SessionState.GetBool(Key + "active", false);

        static void PlayStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                manifest.status = "capturing";
                SetPhase(0);
                FocusGameView();
                ConfigureCaptureResolution();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && !finishing)
                Finish(false, "A demonstração foi interrompida antes de terminar.");
            else if (state == PlayModeStateChange.EnteredEditMode)
                Cleanup();
        }

        static void Tick()
        {
            // Explicit file trigger for QA environments without native window automation.
            // Presence is the entire protocol: no file content is read or executed.
            if (!SessionState.GetBool(Key + "active", false) &&
                !EditorApplication.isPlayingOrWillChangePlaymode &&
                !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                var request = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ilha-capture.request"));
                if (File.Exists(request))
                {
                    File.Delete(request);
                    try { Begin(); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
            }
            if (!SessionState.GetBool(Key + "active", false) || finishing ||
                !EditorApplication.isPlaying || EditorApplication.isPaused || manifest == null) return;
            try
            {
                if (EditorApplication.timeSinceStartup - phaseStarted > 35)
                    throw new TimeoutException("A etapa " + phase + " não terminou em 35 segundos.");
                if (game == null) game = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
                if (game == null) return;
                if (world == null) world = game.GetComponent<WorldController>();
                if (world == null) return;
                var document = game.GetComponent<UIDocument>();
                var root = document == null ? null : document.rootVisualElement;
                if (root == null || root.resolvedStyle.width < 300) return;
                EditorApplication.QueuePlayerLoopUpdate();

                switch (phase)
                {
                    case 0:
                        if (root.Q<TextField>() == null || Elapsed < 1.5) return;
                        // Deterministic QA profile; SetValueWithoutNotify avoids synthesizing user events.
                        SetField("enteredName", "Lia");
                        SetField("selectedAvatar", "dog");
                        root.Q<TextField>().SetValueWithoutNotify("Lia");
                        Invoke("ShowLogin");
                        SetPhase(1);
                        break;
                    case 1:
                        if (Elapsed < .8) return;
                        Capture("01-entrada-e-personagens");
                        SetPhase(2);
                        break;
                    case 2:
                        if (!CaptureReady()) return;
                        Invoke("StartPractice");
                        SetPhase(3);
                        break;
                    case 3:
                        if (!GetField<GameSession>("session").IsJoined || Elapsed < 1.5) return;
                        movementStart = world.LocalPosition;
                        manifest.movementStart = movementStart;
                        world.TouchMove = Vector2.up;
                        SetPhase(4);
                        break;
                    case 4:
                        if (Elapsed < .9) return;
                        manifest.movementEnd = world.LocalPosition;
                        manifest.movementDistance = Vector3.Distance(movementStart, world.LocalPosition);
                        if (manifest.movementDistance < .2f)
                            throw new InvalidOperationException("O personagem não se deslocou durante o comando real de movimento.");
                        Capture("02-personagem-caminhando");
                        SetPhase(5);
                        break;
                    case 5:
                        if (!CaptureReady()) return;
                        world.TouchMove = Vector2.zero;
                        SetPhase(6);
                        break;
                    case 6:
                        if (Elapsed < .8) return;
                        Capture("03-praca-verde-e-interface");
                        SetPhase(7);
                        break;
                    case 7:
                        if (!CaptureReady()) return;
                        Invoke("EnterRoom", "matematica");
                        SetPhase(8);
                        break;
                    case 8:
                        if (world.RoomId != "matematica" || Elapsed < 1.2) return;
                        Capture("04-ilha-de-matematica");
                        SetPhase(9);
                        break;
                    case 9:
                        if (!CaptureReady()) return;
                        Invoke("ShowQuizList");
                        SetPhase(10);
                        break;
                    case 10:
                        if (root.Q(className: "quiz-list") == null || Elapsed < .8) return;
                        Capture("05-lista-de-desafios");
                        SetPhase(11);
                        break;
                    case 11:
                        if (!CaptureReady()) return;
                        var quiz = GetField<GameSession>("session").Catalog.FindQuiz("mat-somas");
                        if (quiz == null) throw new InvalidOperationException("Quiz mat-somas não encontrado no catálogo real.");
                        Invoke("BeginQuiz", quiz);
                        SetPhase(12);
                        break;
                    case 12:
                        if (root.Q(className: "question-panel") == null || Elapsed < 1) return;
                        Capture("06-quiz-de-matematica");
                        SetPhase(13);
                        break;
                    case 13:
                        if (!CaptureReady()) return;
                        var firstQuestion = GetField<QuizDto>("quiz").questions[0];
                        SetField("selectedOption", firstQuestion.correctOptionId);
                        Invoke("SubmitAnswer", firstQuestion, root.Q(className: "options"));
                        SetPhase(14);
                        break;
                    case 14:
                        if (!GetField<bool>("answered") || root.Q(className: "success") == null || Elapsed < 1) return;
                        var result = GetField<AnswerResult>("lastAnswer");
                        manifest.firstAnswerCorrect = result.correct;
                        manifest.firstAnswerExplanation = result.explanation;
                        manifest.answeredQuestions = result.answered;
                        Capture("07-resposta-e-explicacao");
                        SetPhase(15);
                        break;
                    case 15:
                        if (!CaptureReady()) return;
                        Finish(true, "");
                        break;
                }
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        static double Elapsed => EditorApplication.timeSinceStartup - phaseStarted;

        static void SetPhase(int value)
        {
            phase = value;
            phaseStarted = EditorApplication.timeSinceStartup;
            SessionState.SetInt(Key + "phase", value);
            Persist();
        }

        static void Capture(string name)
        {
            FocusGameView();
            pendingCapture = Path.Combine(manifest.outputDirectory, name + ".png");
            manifest.screenshots.Add(new Shot
            {
                name = name, path = pendingCapture, room = world.RoomId,
                position = world.LocalPosition, movementInput = world.TouchMove,
                capturedAtUtc = DateTime.UtcNow.ToString("o"), frame = Time.frameCount,
                width = Screen.width, height = Screen.height
            });
            ScreenCapture.CaptureScreenshot(pendingCapture, 1);
            Persist();
        }

        static bool CaptureReady()
        {
            if (Elapsed < .6 || string.IsNullOrEmpty(pendingCapture) || !File.Exists(pendingCapture)) return false;
            var bytes = File.ReadAllBytes(pendingCapture);
            if (bytes.Length < 100 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71)
                return false;
            var shot = manifest.screenshots[manifest.screenshots.Count - 1];
            shot.width = ReadPngInt(bytes, 16);
            shot.height = ReadPngInt(bytes, 20);
            if (shot.width != CaptureWidth || shot.height != CaptureHeight)
                throw new InvalidOperationException("A captura precisa ter exatamente " + CaptureWidth + "x" + CaptureHeight +
                    " pixels, mas o PNG real possui " + shot.width + "x" + shot.height + ".");
            shot.pngVerified = true;
            pendingCapture = null;
            Persist();
            return true;
        }

        static int ReadPngInt(byte[] value, int index) =>
            (value[index] << 24) | (value[index + 1] << 16) | (value[index + 2] << 8) | value[index + 3];

        static void FocusGameView()
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (type == null) throw new InvalidOperationException("A janela Game View não está disponível neste Editor.");
            if (gameView == null) gameView = EditorWindow.GetWindow(type, false, "Game", true);
            gameView.Show();
            gameView.Focus();
            gameView.Repaint();
        }

        // Internal Editor API, isolated to QA. Based on Unity's own GameViewSizes,
        // GameViewSizeGroup and GameView.SizeSelectionCallback implementations:
        // https://github.com/Unity-Technologies/UnityCsReference/tree/master/Editor/Mono/GameView
        static object GetGameViewSizeGroup(out Type sizeType, out Type sizeEnumType,
            out int groupIndex, int requestedGroup = -1)
        {
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
            sizeEnumType = assembly.GetType("UnityEditor.GameViewSizeType", true);
            var current = sizesType.GetProperty("currentGroupType").GetValue(sizes);
            groupIndex = requestedGroup < 0 ? Convert.ToInt32(current) : requestedGroup;
            return sizesType.GetMethod("GetGroup").Invoke(sizes,
                new[] { Enum.ToObject(current.GetType(), groupIndex) });
        }

        static void ConfigureCaptureResolution()
        {
            FocusGameView();
            var selection = gameView.GetType().GetProperty("selectedSizeIndex");
            var group = GetGameViewSizeGroup(out var sizeType, out var sizeEnumType, out var groupIndex);
            if (!SessionState.GetBool(Key + "resolutionSaved", false))
            {
                var original = (int)selection.GetValue(gameView);
                SessionState.SetInt(Key + "originalResolutionIndex", original);
                SessionState.SetInt(Key + "originalResolutionGroup", groupIndex);
                SessionState.SetBool(Key + "resolutionSaved", true);
                manifest.originalGameViewSizeIndex = original;
                manifest.originalGameViewSizeGroup = ((GameViewSizeGroupType)groupIndex).ToString();
            }
            var groupType = group.GetType();
            var count = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
            var getSize = groupType.GetMethod("GetGameViewSize");
            var selected = -1;
            for (var index = 0; index < count; index++)
            {
                var size = getSize.Invoke(group, new object[] { index });
                if (sizeType.GetProperty("sizeType").GetValue(size).ToString() == "FixedResolution" &&
                    (int)sizeType.GetProperty("width").GetValue(size) == CaptureWidth &&
                    (int)sizeType.GetProperty("height").GetValue(size) == CaptureHeight)
                {
                    selected = index;
                    break;
                }
            }
            if (selected < 0)
            {
                var constructor = sizeType.GetConstructor(new[] { sizeEnumType, typeof(int), typeof(int), typeof(string) });
                var size = constructor.Invoke(new[] { Enum.Parse(sizeEnumType, "FixedResolution"),
                    (object)CaptureWidth, CaptureHeight, CaptureSizeName });
                groupType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                selected = count;
                SessionState.SetInt(Key + "addedResolutionIndex", selected);
            }
            // Callback also updates the render target; changing only the property can
            // leave the previous render target active until a later Editor GUI event.
            gameView.GetType().GetMethod("SizeSelectionCallback").Invoke(gameView, new object[] { selected, null });
            gameView.Repaint();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        static void RestoreGameViewSelection()
        {
            if (!SessionState.GetBool(Key + "resolutionSaved", false)) return;
            FocusGameView();
            var original = SessionState.GetInt(Key + "originalResolutionIndex", 0);
            var originalGroup = SessionState.GetInt(Key + "originalResolutionGroup", 0);
            GetGameViewSizeGroup(out _, out _, out var currentGroup);
            var group = GetGameViewSizeGroup(out var sizeType, out _, out _, originalGroup);
            var groupType = group.GetType();
            var count = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
            if (original < 0 || original >= count)
                throw new InvalidOperationException("A resolução original da Game View foi removida durante a captura.");
            // Reading the public property first validates/initializes Unity's per-group
            // selection array. Restore the saved group even if the active target changed.
            var selection = gameView.GetType().GetProperty("selectedSizeIndex");
            selection.GetValue(gameView);
            var selectionsField = gameView.GetType().GetField("m_SelectedSizes", BindingFlags.Instance | BindingFlags.NonPublic);
            var selections = (int[])selectionsField.GetValue(gameView);
            if (originalGroup < 0 || originalGroup >= selections.Length)
                throw new InvalidOperationException("O grupo original da Game View não está disponível.");
            if (currentGroup == originalGroup)
                gameView.GetType().GetMethod("SizeSelectionCallback").Invoke(gameView, new object[] { original, null });
            else
                selections[originalGroup] = original;
            var added = SessionState.GetInt(Key + "addedResolutionIndex", -1);
            if (added >= 0)
            {
                if (added < count)
                {
                    var size = groupType.GetMethod("GetGameViewSize").Invoke(group, new object[] { added });
                    // Never remove a user-defined resolution, even if the list changed.
                    if ((string)sizeType.GetProperty("baseText").GetValue(size) == CaptureSizeName)
                        groupType.GetMethod("RemoveCustomSize").Invoke(group, new object[] { added });
                }
            }
            selection.GetValue(gameView);
            selections = (int[])selectionsField.GetValue(gameView);
            manifest.gameViewSelectionRestored = selections[originalGroup] == original;
            manifest.restoredGameViewSizeGroup = ((GameViewSizeGroupType)originalGroup).ToString();
            SessionState.SetBool(Key + "resolutionSaved", false);
            gameView.Repaint();
        }

        static T GetField<T>(string name) => (T)typeof(GameBootstrap).GetField(name, PrivateInstance).GetValue(game);
        static void SetField(string name, object value) => typeof(GameBootstrap).GetField(name, PrivateInstance).SetValue(game, value);
        static void Invoke(string name, params object[] values) => typeof(GameBootstrap).GetMethod(name, PrivateInstance).Invoke(game, values);

        static void SnapshotProgress()
        {
            var store = ProgressStore.Instance;
            SessionState.SetString(Key + "profileName", store.ProfileName);
            SessionState.SetString(Key + "profileAvatar", store.ProfileAvatarId);
            var path = Path.Combine(Application.persistentDataPath, "aprender-progress-v1.json");
            SessionState.SetString(Key + "progressPath", path);
            foreach (var suffix in new[] { "", ".backup" })
            {
                var exists = File.Exists(path + suffix);
                SessionState.SetBool(Key + "exists" + suffix, exists);
                SessionState.SetString(Key + "bytes" + suffix, exists ? Convert.ToBase64String(File.ReadAllBytes(path + suffix)) : "");
            }
        }

        static void RestoreProgress()
        {
            if (SessionState.GetBool(Key + "restored", false)) return;
            ProgressStore.Instance.SaveProfile(SessionState.GetString(Key + "profileName", ""),
                SessionState.GetString(Key + "profileAvatar", ""));
            var path = SessionState.GetString(Key + "progressPath", "");
            if (string.IsNullOrEmpty(path)) return;
            foreach (var suffix in new[] { "", ".backup" })
            {
                if (SessionState.GetBool(Key + "exists" + suffix, false))
                    File.WriteAllBytes(path + suffix, Convert.FromBase64String(SessionState.GetString(Key + "bytes" + suffix, "")));
                else if (File.Exists(path + suffix)) File.Delete(path + suffix);
            }
            // Re-read the original disk snapshot after removing the QA-only attempt.
            typeof(ProgressStore).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            SessionState.SetBool(Key + "restored", true);
            manifest.progressRestored = true;
        }

        static void ReceiveLog(string message, string stack, LogType type)
        {
            // This callback can run off the main thread: never call Unity Editor APIs here.
            if (!captureLogs || type == LogType.Log) return;
            lock (LogLock) Logs.Add(new LogEntry { type = type.ToString(), message = message, stack = stack });
        }

        static void Finish(bool success, string failure)
        {
            if (finishing) return;
            finishing = true;
            if (world != null) world.TouchMove = Vector2.zero;
            // Completion is published only after leaving Play and restoring Editor state.
            // A file observer must not see "completed" while restoration is still pending.
            manifest.status = success ? "restoring" : "failed";
            manifest.failure = failure;
            try { RestoreProgress(); }
            catch (Exception exception)
            {
                manifest.status = "failed";
                manifest.failure += "\nFalha ao restaurar o progresso: " + exception;
            }
            Persist();
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else Cleanup();
        }

        static void Cleanup()
        {
            if (manifest == null) return;
            try
            {
                RestoreProgress();
                RestoreGameViewSelection();
                if (gameView != null) gameView.maximized = SessionState.GetBool(Key + "wasMaximized", false);
                var previousScene = SessionState.GetString(Key + "scene", "");
                if (!string.IsNullOrEmpty(previousScene) && File.Exists(previousScene) &&
                    SceneManager.GetActiveScene().path != previousScene)
                    EditorSceneManager.OpenScene(previousScene);
                if (manifest.status == "restoring")
                {
                    if (!manifest.progressRestored || !manifest.gameViewSelectionRestored)
                        throw new InvalidOperationException("A restauração do perfil e da Game View não foi confirmada.");
                    manifest.status = "completed";
                }
            }
            catch (Exception exception)
            {
                manifest.status = "failed";
                manifest.failure += "\nFalha ao restaurar o Editor: " + exception;
            }
            finally
            {
                manifest.completedAtUtc = DateTime.UtcNow.ToString("o");
                Persist();
                SessionState.SetBool(Key + "active", false);
                captureLogs = false;
                Debug.Log("Demonstração: " + manifest.status + ". Evidências: " +
                    Path.Combine(manifest.outputDirectory, "manifest.json"));
                finishing = false;
            }
        }

        static void Persist()
        {
            if (manifest == null) return;
            lock (LogLock)
            {
                foreach (var entry in Logs)
                {
                    manifest.logs.Add(entry);
                    if (entry.type == LogType.Warning.ToString()) manifest.warnings++;
                    else manifest.errors++;
                }
                Logs.Clear();
            }
            var json = JsonUtility.ToJson(manifest, true);
            SessionState.SetString(Key + "manifest", json);
            File.WriteAllText(Path.Combine(manifest.outputDirectory, "manifest.json"), json);
        }
    }
}
#endif
