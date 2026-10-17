using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Aprender
{
    /// <summary>
    /// A green, flat play space. The application owns session/quiz state;
    /// this component owns avatars, movement and physical interaction affordances.
    /// </summary>
    public sealed class WorldController : MonoBehaviour
    {
        public event Action<string> PortalEntered;
        public event Action QuizRequested;

        public string RoomId { get; private set; } = "hub";
        public Vector3 LocalPosition => localActor == null ? Vector3.zero : localActor.position;
        public float LocalYaw => localActor == null ? 0 : localActor.eulerAngles.y;
        public bool InputEnabled { get; set; }
        public Vector2 TouchMove { get; set; }
        public bool CanInteract => InputEnabled && nearestInteraction != null;
        public string NearbyActionLabel => nearestInteraction == null ? "" : nearestInteraction.label;

        private sealed class Interaction
        {
            public Vector3 position;
            public string target;
            public string label;
            public Transform ring;
            public bool quiz;
        }

        private sealed class RemoteAvatar
        {
            public AvatarMotion motion;
            public string avatarId;
            public TextMesh nameLabel;
            public TextMesh statusLabel;
        }

        private readonly Dictionary<string, RemoteAvatar> remotes = new Dictionary<string, RemoteAvatar>();
        private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        private readonly List<Interaction> interactions = new List<Interaction>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly HashSet<string> seenPlayers = new HashSet<string>();
        private readonly List<string> departedPlayers = new List<string>();
        private Transform roomRoot;
        private Transform actorsRoot;
        private Transform localActor;
        private AvatarMotion localMotion;
        private Camera worldCamera;
        private GameObject lightObject;
        private string localId;
        private Interaction nearestInteraction;
        private float interactionCooldown;
        private bool ownsCamera;
        private bool initialized;
        private Font labelFont;
        private Material worldTextMaterial;
        private Vector3 cameraVelocity;
        private const float MoveSpeed = 4.1f;
        private const float ArenaRadius = 10.4f;
        private static readonly Vector3 CameraOffset = new Vector3(0, 13.8f, -15.8f);
        private static readonly Color Ink = new Color32(47, 57, 84, 255);
        private static readonly Color Lilac = new Color32(139, 117, 232, 255);
        private static readonly Color MathColor = new Color32(136, 102, 214, 255);
        private static readonly Color LanguageColor = new Color32(230, 140, 79, 255);
        private static readonly Color ScienceColor = new Color32(66, 163, 130, 255);

        public void Initialize(string playerId, string playerName, string avatarId)
        {
            if (!initialized) Setup();
            localId = playerId;
            if (localActor != null) RemoveActor(localActor);
            var actor = CreateAvatar(avatarId, playerName, true, out _, out _);
            localMotion = actor;
            localActor = actor.transform;
            localActor.position = new Vector3(0, 0, -3.5f);
            localActor.rotation = Quaternion.identity;
            InputEnabled = true;
            interactionCooldown = Time.time + .7f;
            SnapCamera();
        }

        public void SetRoom(string roomId)
        {
            if (!initialized) Setup();
            RoomId = IsKnownRoom(roomId) ? roomId : "hub";
            foreach (var remote in remotes.Values)
                if (remote.motion != null) RemoveActor(remote.motion.transform);
            remotes.Clear();
            BuildRoom();
            if (localActor != null)
            {
                localActor.position = new Vector3(0, 0, -3.5f);
                localActor.rotation = Quaternion.identity;
            }
            nearestInteraction = null;
            TouchMove = Vector2.zero;
            interactionCooldown = Time.time + 1f;
            SnapCamera();
        }

        public void ApplyPlayers(List<PlayerPresence> players)
        {
            if (!initialized || players == null) return;
            seenPlayers.Clear();
            foreach (var player in players)
            {
                if (player == null || string.IsNullOrEmpty(player.id) || player.id == localId || player.roomId != RoomId)
                    continue;
                // Rendering never trusts unbounded positions from another client.
                if (float.IsNaN(player.x) || float.IsInfinity(player.x) || float.IsNaN(player.z) || float.IsInfinity(player.z))
                    continue;
                seenPlayers.Add(player.id);
                if (remotes.TryGetValue(player.id, out var remote) && remote.avatarId != player.avatarId)
                {
                    RemoveActor(remote.motion.transform);
                    remotes.Remove(player.id);
                    remote = null;
                }
                var position = ClampToArena(new Vector3(player.x, 0, player.z));
                if (remote == null)
                {
                    var actor = CreateAvatar(player.avatarId, player.name, false, out var nameLabel, out var statusLabel);
                    actor.transform.position = position;
                    actor.IsRemote = true;
                    remote = new RemoteAvatar { motion = actor, avatarId = player.avatarId, nameLabel = nameLabel, statusLabel = statusLabel };
                    remotes[player.id] = remote;
                }
                remote.motion.TargetPosition = position;
                remote.motion.TargetYaw = float.IsNaN(player.yaw) || float.IsInfinity(player.yaw) ? 0 : player.yaw;
                remote.nameLabel.text = SafeName(player.name);
                remote.statusLabel.text = string.IsNullOrEmpty(player.quizId) ? "" : "Respondendo um quiz";
            }
            departedPlayers.Clear();
            foreach (var pair in remotes)
                if (!seenPlayers.Contains(pair.Key)) departedPlayers.Add(pair.Key);
            foreach (var id in departedPlayers)
            {
                if (remotes[id].motion != null) RemoveActor(remotes[id].motion.transform);
                remotes.Remove(id);
            }
        }

        public void Wave()
        {
            if (localMotion != null) localMotion.Wave();
        }

        public void Interact()
        {
            if (!CanInteract || Time.time < interactionCooldown) return;
            var action = nearestInteraction;
            interactionCooldown = Time.time + 1.1f;
            nearestInteraction = null;
            if (action.quiz) QuizRequested?.Invoke();
            else PortalEntered?.Invoke(action.target);
        }

        public void Shutdown()
        {
            InputEnabled = false;
            TouchMove = Vector2.zero;
            if (localActor != null) RemoveActor(localActor);
            localActor = null;
            localMotion = null;
            localId = null;
            foreach (var remote in remotes.Values)
                if (remote.motion != null) RemoveActor(remote.motion.transform);
            remotes.Clear();
            SetRoom("hub");
        }

        private void Setup()
        {
            initialized = true;
            labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // TextMesh vertex colours need explicit sRGB conversion in a linear URP
            // project; the built-in font material otherwise makes dark text look pale.
            var textShader = Resources.Load<Shader>("WorldText");
            if (textShader == null) textShader = Shader.Find("GUI/Text Shader");
            worldTextMaterial = new Material(textShader) { name = "Aprender · legendas", color = Color.white };
            RefreshFontTexture(labelFont);
            Font.textureRebuilt += RefreshFontTexture;
            actorsRoot = new GameObject("Jogadores").transform;
            actorsRoot.SetParent(transform, false);
            worldCamera = Camera.main;
            if (worldCamera == null)
            {
                var cameraObject = new GameObject("Câmera do Aprender", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                worldCamera = cameraObject.GetComponent<Camera>();
                ownsCamera = true;
            }
            worldCamera.orthographic = true;
            worldCamera.orthographicSize = 8.2f;
            worldCamera.nearClipPlane = .1f;
            worldCamera.farClipPlane = 100f;
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = new Color32(189, 224, 236, 255);
            worldCamera.allowHDR = true;
            lightObject = new GameObject("Luz suave", typeof(Light));
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, .97f, .92f);
            light.intensity = .88f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = .30f;
            lightObject.transform.rotation = Quaternion.Euler(48, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.48f, .52f, .47f);
            RenderSettings.fog = false;
            BuildRoom();
            SnapCamera();
        }

        private void Update()
        {
            if (!initialized) return;
            foreach (var interaction in interactions)
            {
                if (interaction.ring == null) continue;
                var pulse = 1f + Mathf.Sin(Time.time * 1.7f + interaction.position.x) * .025f;
                interaction.ring.localScale = new Vector3(pulse, pulse, pulse);
            }
            if (localActor == null || !InputEnabled)
            {
                nearestInteraction = null;
                return;
            }

            var keyboard = Keyboard.current;
            var input = TouchMove;
            if (keyboard != null)
            {
                input.x += (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                input.y += (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                    - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
                if (keyboard.spaceKey.wasPressedThisFrame) Wave();
            }
            input = Vector2.ClampMagnitude(input, 1);
            if (input.sqrMagnitude > .005f)
            {
                var direction = new Vector3(input.x, 0, input.y);
                localActor.position = ClampToArena(localActor.position + direction * (MoveSpeed * Time.deltaTime));
                localActor.rotation = Quaternion.RotateTowards(localActor.rotation,
                    Quaternion.LookRotation(direction), 620f * Time.deltaTime);
            }

            nearestInteraction = null;
            var nearestDistance = 2.45f;
            foreach (var interaction in interactions)
            {
                var distance = Vector3.Distance(localActor.position, interaction.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestInteraction = interaction;
                }
            }
            if (nearestInteraction != null && Time.time >= interactionCooldown)
            {
                if ((!nearestInteraction.quiz && nearestDistance < .95f)
                    || (keyboard != null && keyboard.eKey.wasPressedThisFrame)) Interact();
            }
        }

        private void LateUpdate()
        {
            if (!initialized || worldCamera == null) return;
            var follow = localActor == null ? Vector3.zero : localActor.position;
            // A little follow keeps the whole welcoming plaza readable while moving.
            var target = new Vector3(follow.x * .25f, 0, 2f + follow.z * .2f);
            worldCamera.transform.position = Vector3.SmoothDamp(worldCamera.transform.position,
                target + CameraOffset, ref cameraVelocity, .22f);
            worldCamera.transform.rotation = Quaternion.LookRotation(-CameraOffset);
            // A portrait screen needs a larger field to preserve the three portals.
            worldCamera.orthographicSize = Mathf.Max(8.2f, 10.5f / Mathf.Max(.65f, worldCamera.aspect));
        }

        private void SnapCamera()
        {
            if (worldCamera == null) return;
            cameraVelocity = Vector3.zero;
            var follow = localActor == null ? Vector3.zero : localActor.position;
            worldCamera.transform.position = new Vector3(follow.x * .25f, 0, 2f + follow.z * .2f) + CameraOffset;
            worldCamera.transform.rotation = Quaternion.LookRotation(-CameraOffset);
        }

        private void BuildRoom()
        {
            if (roomRoot != null)
            {
                ReleaseMeshes(roomRoot);
                Destroy(roomRoot.gameObject);
            }
            interactions.Clear();
            roomRoot = new GameObject("Sala · " + RoomId).transform;
            roomRoot.SetParent(transform, false);
            var color = RoomColor(RoomId);
            BuildGreenEnvironment();

            if (RoomId == "hub")
            {
                Portal("matematica", "MATEMÁTICA", "1  2  3", new Vector3(-5.1f, 0, 3.8f), MathColor);
                Portal("portugues", "PORTUGUÊS", "A  B  C", new Vector3(0, 0, 6.7f), LanguageColor);
                Portal("ciencias", "CIÊNCIAS", "VIDA", new Vector3(5.1f, 0, 3.8f), ScienceColor);
                Label("PRAÇA DAS DESCOBERTAS", new Vector3(0, .04f, -6.7f), .09f,
                    new Color32(49, 101, 66, 255), roomRoot, false, Quaternion.Euler(90, 0, 0));
                Trail(new Vector3(-3.6f, 0, 0), new Vector3(-5.1f, 0, 2), MathColor);
                Trail(new Vector3(0, 0, 1), new Vector3(0, 0, 4.8f), LanguageColor);
                Trail(new Vector3(3.6f, 0, 0), new Vector3(5.1f, 0, 2), ScienceColor);
            }
            else
            {
                Portal("hub", "VOLTAR À PRAÇA", "CASA", new Vector3(-5.1f, 0, -1.5f), Lilac);
                QuizStation(color);
                var title = RoomId == "matematica" ? "ILHA DOS NÚMEROS" : RoomId == "portugues" ? "ILHA DAS PALAVRAS" : "ILHA DAS DESCOBERTAS";
                Label(title, new Vector3(0, .04f, -6.7f), .095f, color, roomRoot, false, Quaternion.Euler(90, 0, 0));
                // Simple learning props make each subject identifiable without a finished scenario.
                var symbols = RoomId == "matematica" ? new[] { "1", "+", "2", "=", "3" }
                    : RoomId == "portugues" ? new[] { "A", "E", "I", "O", "U" } : new[] { "SOL", "ÁGUA", "VIDA" };
                for (var i = 0; i < symbols.Length; i++)
                {
                    var x = (i - (symbols.Length - 1) / 2f) * 2.1f;
                    Primitive("Pedestal", PrimitiveType.Cylinder, new Vector3(x, .18f, 7.2f), new Vector3(1.2f, .18f, 1.2f),
                        Color.Lerp(color, Color.white, .55f));
                    Label(symbols[i], new Vector3(x, 1.13f, 7.2f), symbols.Length == 3 ? .10f : .22f, color, roomRoot, true);
                }
                Trail(new Vector3(0, 0, -1), new Vector3(0, 0, 1.6f), color);
            }
        }

        private void BuildGreenEnvironment()
        {
            // The broad lawn and level paths keep movement and portal approaches readable.
            // Imported decorations are composed once by GreenEnvironmentBuilder and loaded
            // from Resources so the same scene is present in standalone builds.
            Primitive("Base da ilha verde", PrimitiveType.Cylinder, new Vector3(0, -.48f, 0),
                new Vector3(35, .42f, 35), new Color32(82, 120, 63, 255));
            Primitive("Gramado", PrimitiveType.Cylinder, new Vector3(0, -.045f, 0),
                new Vector3(34.7f, .04f, 34.7f), new Color32(121, 173, 96, 255));
            var sand = (Color)new Color32(229, 213, 175, 255);
            Primitive("Praça de areia clara", PrimitiveType.Cylinder, new Vector3(0, .002f, -1),
                new Vector3(7.7f, .009f, 6.4f), sand);
            Path(new Vector3(0, 0, -6), new Vector3(0, 0, 0), 2f, sand);
            if (RoomId == "hub")
            {
                Path(new Vector3(-1.4f, 0, -.4f), new Vector3(-5.1f, 0, 3.8f), 1.7f, sand);
                Path(new Vector3(0, 0, .5f), new Vector3(0, 0, 6.7f), 1.7f, sand);
                Path(new Vector3(1.4f, 0, -.4f), new Vector3(5.1f, 0, 3.8f), 1.7f, sand);
            }
            else
            {
                Path(new Vector3(0, 0, -1.5f), new Vector3(-5.1f, 0, -1.5f), 1.7f, sand);
                Path(new Vector3(0, 0, -1), new Vector3(0, 0, 3.2f), 1.9f, sand);
            }
            var environment = Resources.Load<GameObject>("Environment/GreenPlaza");
            if (environment != null)
            {
                var scenery = Instantiate(environment, roomRoot, false);
                scenery.name = "Jardim · Toon Environments World Creator Pack Lite";
            }
        }

        private void Path(Vector3 from, Vector3 to, float width, Color color)
        {
            var direction = to - from;
            var path = Primitive("Caminho do jardim", PrimitiveType.Cube,
                (from + to) * .5f + Vector3.up * .012f, new Vector3(width, .014f, direction.magnitude), color);
            path.transform.localRotation = Quaternion.LookRotation(direction);
            foreach (var end in new[] { from, to })
                Primitive("Curva do caminho", PrimitiveType.Cylinder, end + Vector3.up * .012f,
                    new Vector3(width, .007f, width), color);
        }

        private void Portal(string target, string title, string symbol, Vector3 position, Color color)
        {
            Primitive("Base do portal", PrimitiveType.Cylinder, position + Vector3.up * .06f,
                new Vector3(3.1f, .06f, 2.2f), Color.Lerp(color, Color.white, .55f));
            var ringRoot = new GameObject("Portal · " + target).transform;
            ringRoot.SetParent(roomRoot, false);
            ringRoot.localPosition = position;
            var ring = Ring("Arco", ringRoot, new Vector3(0, 1.65f, 0), 1.2f, .14f, color, false);
            ring.transform.localScale = new Vector3(1, 1.22f, 1);
            var inner = Primitive("Luz do portal", PrimitiveType.Cylinder, position + Vector3.up * 1.65f,
                new Vector3(2.1f, .018f, 2.58f), Color.Lerp(color, Color.white, .78f));
            inner.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Label(symbol, position + new Vector3(0, 1.9f, -.16f), .13f, color, roomRoot, false);
            Label("ENTRAR", position + new Vector3(0, 1.20f, -.17f), .07f, color, roomRoot, false);
            Label(title, position + new Vector3(0, 3.4f, 0), .085f, Ink, roomRoot, true);
            Ring("Destino", roomRoot, position + new Vector3(0, .015f, -.1f), 1.55f, .035f, color, true);
            interactions.Add(new Interaction
            {
                position = position,
                target = target,
                label = target == "hub" ? "Voltar à praça" : "Entrar em " + RoomName(target),
                ring = ringRoot
            });
        }

        private void QuizStation(Color color)
        {
            var center = new Vector3(0, 0, 3.2f);
            Primitive("Palco dos quizzes", PrimitiveType.Cylinder, center + Vector3.up * .06f,
                new Vector3(4.8f, .06f, 3.4f), Color.Lerp(color, Color.white, .65f));
            var plinth = Primitive("Mesa dos desafios", PrimitiveType.Cube, center + Vector3.up * .55f,
                new Vector3(2.45f, 1f, .8f), color);
            plinth.transform.localRotation = Quaternion.Euler(0, 0, 0);
            var screen = Primitive("Livro de desafios", PrimitiveType.Cube, center + new Vector3(0, 1.23f, -.05f),
                new Vector3(2.65f, .28f, 1.1f), Color.white);
            screen.transform.localRotation = Quaternion.Euler(-12, 0, 0);
            Label("VAMOS APRENDER?", center + new Vector3(0, 2.65f, 0), .105f, Ink, roomRoot, true);
            Label("QUIZZES", center + new Vector3(0, 1.7f, -.15f), .13f, color, roomRoot, true);
            var ring = Ring("Hora de descobrir", roomRoot, center + new Vector3(0, .015f, -1.2f), 1.1f, .055f, color, true);
            interactions.Add(new Interaction { position = center + Vector3.back * 1.2f, label = "Escolher um quiz", quiz = true, ring = ring.transform });
        }

        private void Trail(Vector3 from, Vector3 to, Color color)
        {
            for (var i = 0; i < 5; i++)
                Primitive("Caminho", PrimitiveType.Cylinder, Vector3.Lerp(from, to, i / 4f) + Vector3.up * .005f,
                    new Vector3(.18f, .006f, .18f), Color.Lerp(color, Color.white, .42f));
        }

        private AvatarMotion CreateAvatar(string avatarId, string playerName, bool local, out TextMesh nameLabel, out TextMesh statusLabel)
        {
            var definition = AvatarCatalog.Find(avatarId);
            var actor = new GameObject((local ? "Você · " : "Amigo · ") + SafeName(playerName));
            actor.transform.SetParent(actorsRoot, false);
            var visual = new GameObject("Personagem").transform;
            visual.SetParent(actor.transform, false);
            var prefab = Resources.Load<GameObject>("Avatars/" + definition.id);
            if (prefab != null) Instantiate(prefab, visual, false);
            else BuildFallbackPet(visual, definition.color);
            var shadow = Primitive("Sombra", PrimitiveType.Cylinder, new Vector3(0, .028f, 0), new Vector3(.86f, .005f, .63f),
                new Color32(91, 125, 64, 255), actor.transform);
            shadow.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (local) Ring("Seu personagem", actor.transform, new Vector3(0, .039f, 0), .62f, .045f,
                new Color32(244, 187, 65, 255), true);
            nameLabel = Label(SafeName(playerName) + (local ? " · você" : ""), new Vector3(0, 1.65f, 0), .064f,
                local ? (Color)new Color32(92, 73, 173, 255) : Ink, actor.transform, true);
            statusLabel = Label("", new Vector3(0, 2.0f, 0), .043f, Ink, actor.transform, true);
            var motion = actor.AddComponent<AvatarMotion>();
            motion.Setup(visual);
            return motion;
        }

        private void BuildFallbackPet(Transform parent, Color color)
        {
            // Visible even if the project is opened before source models finish importing.
            Primitive("Corpo", PrimitiveType.Cube, new Vector3(0, .59f, 0), new Vector3(.8f, .72f, .72f), color, parent);
            Primitive("Cabeça", PrimitiveType.Cube, new Vector3(0, .98f, .2f), new Vector3(.72f, .52f, .62f), color, parent);
            for (var i = -1; i <= 1; i += 2)
            {
                Primitive("Orelha", PrimitiveType.Cube, new Vector3(i * .27f, 1.28f, .2f), new Vector3(.19f, .27f, .25f), color, parent);
                Primitive("Olho", PrimitiveType.Cube, new Vector3(i * .18f, 1.01f, .519f), new Vector3(.08f, .09f, .025f), Ink, parent);
                Primitive("Patinha", PrimitiveType.Cube, new Vector3(i * .23f, .12f, .14f), new Vector3(.25f, .24f, .44f), color, parent);
            }
        }

        private GameObject Primitive(string name, PrimitiveType shape, Vector3 position, Vector3 scale, Color color, Transform parent = null)
        {
            var obj = GameObject.CreatePrimitive(shape);
            obj.name = name;
            obj.transform.SetParent(parent == null ? roomRoot : parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            var collider = obj.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return obj;
        }

        private TextMesh Label(string text, Vector3 position, float size, Color color, Transform parent, bool faceCamera, Quaternion? rotation = null)
        {
            var obj = new GameObject("Legenda · " + text, typeof(TextMesh), typeof(WorldLabel));
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localRotation = rotation ?? Quaternion.identity;
            var label = obj.GetComponent<TextMesh>();
            label.text = text;
            label.font = labelFont;
            label.fontSize = 64;
            label.fontStyle = FontStyle.Bold;
            label.characterSize = size;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = color;
            var renderer = obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = worldTextMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            obj.GetComponent<WorldLabel>().FaceCamera = faceCamera;
            return label;
        }

        private void RefreshFontTexture(Font rebuiltFont)
        {
            if (rebuiltFont == labelFont && worldTextMaterial != null)
                worldTextMaterial.mainTexture = labelFont.material.mainTexture;
        }

        private GameObject Ring(string name, Transform parent, Vector3 center, float radius, float width, Color color, bool horizontal)
        {
            var obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = center;
            const int segments = 64;
            const int sides = 6;
            var vertices = new Vector3[(segments + 1) * (sides + 1)];
            var triangles = new int[segments * sides * 6];
            var index = 0;
            for (var i = 0; i <= segments; i++)
            {
                var angle = i / (float)segments * Mathf.PI * 2f;
                for (var j = 0; j <= sides; j++)
                {
                    var cross = j / (float)sides * Mathf.PI * 2f;
                    var r = radius + Mathf.Cos(cross) * width;
                    var v = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, Mathf.Sin(cross) * width);
                    vertices[i * (sides + 1) + j] = horizontal ? new Vector3(v.x, v.z, v.y) : v;
                    if (i == segments || j == sides) continue;
                    var a = i * (sides + 1) + j;
                    var b = a + sides + 1;
                    triangles[index++] = a; triangles[index++] = b; triangles[index++] = a + 1;
                    triangles[index++] = a + 1; triangles[index++] = b; triangles[index++] = b + 1;
                }
            }
            // Swapping axes reverses winding for a horizontal ring.
            if (horizontal)
                for (var i = 0; i < triangles.Length; i += 3)
                { var swap = triangles[i]; triangles[i] = triangles[i + 1]; triangles[i + 1] = swap; }
            var mesh = new Mesh { name = "Arco" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            obj.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
            return obj;
        }

        private Material MaterialFor(Color color)
        {
            if (materials.TryGetValue(color, out var material)) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader) { name = "Aprender · cor", color = color };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .12f);
            materials.Add(color, material);
            return material;
        }

        private static Vector3 ClampToArena(Vector3 position)
        {
            var flat = Vector2.ClampMagnitude(new Vector2(position.x, position.z), ArenaRadius);
            return new Vector3(flat.x, 0, flat.y);
        }

        private static bool IsKnownRoom(string id) => id == "hub" || id == "matematica" || id == "portugues" || id == "ciencias";
        private static Color RoomColor(string id) => id == "matematica" ? MathColor : id == "portugues" ? LanguageColor : id == "ciencias" ? ScienceColor : Lilac;
        private static string RoomName(string id) => id == "matematica" ? "Matemática" : id == "portugues" ? "Português" : id == "ciencias" ? "Ciências" : "Praça";
        private static string SafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Explorador";
            var clean = value.Replace("<", "").Replace(">", "").Replace("\n", " ").Replace("\r", " ");
            return clean.Length > 20 ? clean.Substring(0, 20) : clean;
        }

        private void ReleaseMeshes(Transform root)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh != null && meshes.Remove(mesh)) Destroy(mesh);
            }
        }

        private void RemoveActor(Transform actor)
        {
            ReleaseMeshes(actor);
            Destroy(actor.gameObject);
        }

        private void OnDestroy()
        {
            Font.textureRebuilt -= RefreshFontTexture;
            if (worldTextMaterial != null) Destroy(worldTextMaterial);
            foreach (var material in materials.Values) if (material != null) Destroy(material);
            foreach (var mesh in meshes) if (mesh != null) Destroy(mesh);
            if (ownsCamera && worldCamera != null) Destroy(worldCamera.gameObject);
            if (lightObject != null) Destroy(lightObject);
        }
    }
}
