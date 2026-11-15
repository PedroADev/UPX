using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Aprender
{
    /// <summary>The complete child-facing flow. World and session services remain independent of the UI.</summary>
    public sealed partial class GameBootstrap : MonoBehaviour
    {
        GameSession session;
        WorldController world;
        UIDocument document;
        VisualElement root, screen, overlay, friends, friendList, feedback;
        Label status, roomLabel, starsLabel, interactionLabel;
        Button interactionButton, confirmButton, soundButton;
        TextField nameField, codeField, serverField;
        string selectedAvatar = "dog", currentQuizId = "";
        string enteredName = "", enteredCode = "", enteredServer = "http://localhost:8787";
        QuizDto quiz;
        AnswerResult lastAnswer;
        int questionIndex;
        string selectedOption;
        bool busy, answered, muted, modalOpen;
        float presenceClock;
        AudioSource audioSource;
        AudioClip positiveTone, gentleTone;

        void Start()
        {
            Application.targetFrameRate = 60;
            session = gameObject.AddComponent<GameSession>();
            world = gameObject.AddComponent<WorldController>();
            world.PortalEntered += EnterRoom;
            world.QuizRequested += ShowQuizList;
            session.PlayersChanged += RefreshFriends;
            session.ConnectionLost += ShowConnectionLost;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            positiveTone = MakeTone("Descoberta", 660, 880);
            gentleTone = MakeTone("Aprendizado", 392, 523);
            muted = PlayerPrefs.GetInt("aprender.muted", 0) == 1;
            selectedAvatar = ProgressStore.Instance.ProfileAvatarId;
            if (!AvatarCatalog.Items.Any(a => a.id == selectedAvatar)) selectedAvatar = "dog";
            enteredName = ProgressStore.Instance.ProfileName ?? "";
            enteredServer = PlayerPrefs.GetString("aprender.server", enteredServer);
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("AprenderPanel");
            if (document.panelSettings == null)
            {
                Debug.LogError("Painel não encontrado. Use Ilha do Saber > Preparar projeto no Editor.");
                return;
            }
            InitializeMobileUI();
            root = document.rootVisualElement;
            root.AddToClassList("app");
            var css = Resources.Load<StyleSheet>("Aprender");
            if (css != null) root.styleSheets.Add(css);
            InitializeControllerUI();
            root.RegisterCallback<GeometryChangedEvent>(_ => root.EnableInClassList("compact", root.resolvedStyle.width < 1100));
            StartCoroutine(Initialize());
        }

        IEnumerator Initialize()
        {
            yield return session.LoadCatalog();
            ShowLogin();
        }

        void Update()
        {
            UpdateControllerUI();
            UpdateMobileUI();
            if (session == null || world == null || !session.IsJoined) return;
            presenceClock += Time.unscaledDeltaTime;
            if (presenceClock > 0.2f)
            {
                presenceClock = 0;
                session.SetPresence(world.RoomId, world.LocalPosition, world.LocalYaw, currentQuizId);
            }
            if (interactionLabel != null)
            {
                interactionLabel.text = world.NearbyActionLabel;
                interactionButton.style.display = world.CanInteract && !modalOpen ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (!ControllerConsumedEscape && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (modalOpen) CloseModal();
                else ShowHelp();
            }
        }

        void ResetUI()
        {
            ControllerUIReset();
            ResetMobileUI();
            root.Clear();
            screen = Box(root, "screen");
            ConfigureMobileScreen();
            overlay = null;
            friends = null;
            status = null;
            interactionLabel = null;
            modalOpen = false;
        }

        void ShowLogin()
        {
            ResetUI();
            busy = false;
            world.InputEnabled = false;
            var header = Box(screen, "login-header row");
            Brand(header);
            Text(header, "Matemática · Português · Ciências", "header-subjects");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            ConfigureMobileScrollView(scroll);
            scroll.AddToClassList("login-scroll");
            screen.Add(scroll);
            var columns = Box(scroll.contentContainer, "login-columns row");
            var hero = Box(columns, "hero");
            Text(hero, "Ilha do Saber", "hero-title");
            Text(hero, "Explore a praça e encontre os desafios de cada matéria.", "hero-copy");
            var art = Box(hero, "hero-art");
            Box(art, "pet-ground");
            var featuredPet = PetImage(art, selectedAvatar, "hero-pet");
            Text(hero, "Caminhe pelos portais. Escolha um desafio. Jogue no seu ritmo.", "hero-note");

            var card = Box(columns, "login-card card");
            Text(card, "Vamos jogar", "title");
            Text(card, "Seu nome", "field-title");
            nameField = new TextField { value = enteredName, maxLength = 20, name = "player-name" };
            ConfigureMobileTextField(nameField, TouchScreenKeyboardType.Default);
            nameField.AddToClassList("text-input");
            nameField.tooltip = "Use um nome ou apelido com 2 a 20 caracteres.";
            nameField.RegisterValueChangedCallback(e => enteredName = e.newValue);
            card.Add(nameField);
            Text(card, "Seu animal", "field-title");
            var pets = Box(card, "pet-grid row");
            foreach (var avatar in AvatarCatalog.Items)
            {
                var id = avatar.id;
                var button = new Button(() =>
                {
                    selectedAvatar = id;
                    foreach (var child in pets.Children()) child.EnableInClassList("selected", child.name == id);
                    featuredPet.image = Resources.Load<Texture2D>("AvatarPreviews/" + id);
                }) { name = id, tooltip = "Escolher " + avatar.name };
                button.AddToClassList("pet-choice");
                button.EnableInClassList("selected", selectedAvatar == id);
                PetImage(button, id, "pet-thumb");
                Text(button, avatar.name, "pet-name");
                pets.Add(button);
            }
            status = Text(card, "", "status");
            Btn(card, "Jogar sozinho", StartPractice, "primary practice-button");
            Text(card, "Funciona sem conexão com a internet.", "hint");
            var classroom = new Foldout { text = "Entrar na turma", value = false, name = "classroom-options" };
            classroom.AddToClassList("classroom");
            card.Add(classroom);
            Text(classroom.contentContainer, "Código da turma", "field-title");
            var codeRow = Box(classroom.contentContainer, "row code-row");
            codeField = new TextField { value = enteredCode, maxLength = 8, name = "classroom-code" };
            ConfigureMobileTextField(codeField, TouchScreenKeyboardType.ASCIICapable);
            codeField.AddToClassList("text-input");
            codeField.AddToClassList("code-input");
            codeField.RegisterValueChangedCallback(e => enteredCode = e.newValue.ToUpperInvariant());
            codeRow.Add(codeField);
            Btn(codeRow, "Entrar", () => Join(false), "secondary join-button");
            Text(classroom.contentContainer, "Peça o código ao professor.", "hint");
            var teacher = new Foldout { text = "Configuração da turma", value = false };
            teacher.AddToClassList("teacher");
            classroom.Add(teacher);
            Text(teacher.contentContainer, "Endereço do servidor", "field-title");
            serverField = new TextField { value = enteredServer, maxLength = 200, name = "classroom-server" };
            ConfigureMobileTextField(serverField, TouchScreenKeyboardType.URL);
            serverField.AddToClassList("text-input");
            serverField.AddToClassList("server-input");
            serverField.RegisterValueChangedCallback(e => enteredServer = e.newValue);
            teacher.Add(serverField);
            Btn(teacher.contentContainer, "Criar turma", () => Join(true), "secondary");
            Text(teacher.contentContainer, "Use o mesmo servidor nos aparelhos da turma.", "hint wrap");
            if (!IsMobileUI && !GameControls.Connected) nameField.Focus();
        }

        bool ValidateName()
        {
            enteredName = GameTextInputRules.NormalizePlayerName(enteredName);
            if (!GameTextInputRules.IsValidPlayerName(enteredName))
            {
                status.text = "Escolha um nome ou apelido com 2 a 20 caracteres.";
                nameField.Focus();
                return false;
            }
            return true;
        }

        void StartPractice()
        {
            if (busy || !ValidateName()) return;
            if (session.Catalog == null || session.Catalog.subjects == null || session.Catalog.subjects.Length == 0)
            {
                status.text = "Os desafios não carregaram. Peça ajuda para abrir o projeto novamente.";
                return;
            }
            session.JoinPractice(enteredName, selectedAvatar);
            if (!session.IsJoined) { status.text = session.Error; return; }
            BeginWorld();
        }

        void Join(bool create)
        {
            if (busy || !ValidateName()) return;
            if (!create && string.IsNullOrWhiteSpace(enteredCode))
            {
                status.text = "Falta o código da turma. Seu professor pode ajudar.";
                codeField.Focus();
                return;
            }
            if (!ValidateMobileServerAddress()) return;
            busy = true;
            status.text = create ? "Abrindo sua turma…" : "Encontrando sua turma…";
            screen.SetEnabled(false);
            StartCoroutine(session.Join(enteredServer.Trim(), enteredCode.Trim().ToUpperInvariant(), enteredName, selectedAvatar, create, success =>
            {
                busy = false;
                screen.SetEnabled(true);
                if (success)
                {
                    PlayerPrefs.SetString("aprender.server", enteredServer.Trim());
                    BeginWorld();
                }
                else status.text = session.Error;
            }));
        }

        void BeginWorld()
        {
            ProgressStore.Instance.SaveProfile(enteredName, selectedAvatar);
            world.Initialize(session.PlayerId, session.PlayerName, session.AvatarId);
            currentQuizId = "";
            world.SetRoom("hub");
            ShowHUD();
        }

        void ShowHUD()
        {
            ResetUI();
            screen.AddToClassList("game-screen");
            screen.pickingMode = PickingMode.Ignore;
            var top = Box(screen, "hud-top row");
            Brand(top);
            Box(top, "vertical-divider");
            roomLabel = Text(top, RoomTitle(world.RoomId), "location-title");
            Box(top, "spacer");
            Text(top, session.IsOnline ? "Turma " + session.SessionCode : "Prática", "session-pill");
            starsLabel = Text(top, "★ " + ProgressStore.Instance.TotalStars, "stars-pill");
            soundButton = Btn(top, muted ? "Som: não" : "Som: sim", ToggleSound, "quiet small-button");
            Btn(top, "Ajuda", ShowHelp, "quiet small-button");
            Btn(top, "Sair", ShowLeave, "quiet small-button");

            var guide = Box(screen, "world-guide");
            if (world.RoomId == "hub")
            {
                var destinations = new Foldout { text = "Ir para uma ilha", value = false };
                guide.Add(destinations);
                foreach (var subject in session.Catalog.subjects)
                {
                    var id = subject.id;
                    var button = Btn(destinations.contentContainer, subject.title, () => EnterRoom(id), "subject-link");
                    button.style.borderLeftColor = SubjectColor(id);
                }
            }
            else Btn(guide, "Escolher desafio", ShowQuizList, "secondary");

            if (session.IsOnline)
            {
                var classmates = new Foldout { text = "Colegas", value = false };
                classmates.AddToClassList("friends-card");
                screen.Add(classmates);
                friends = Box(classmates.contentContainer, "friends-content");
                RefreshFriends();
            }
            var bottom = Box(screen, "hud-bottom row");
            if (world.RoomId != "hub") Btn(bottom, "Voltar à praça", () => EnterRoom("hub"), "secondary");
            else Text(bottom, "WASD / setas: caminhar · E: interagir", "key-hint");
            Box(bottom, "spacer");
            interactionButton = new Button(() => world.Interact());
            interactionButton.AddToClassList("primary");
            interactionButton.AddToClassList("interaction");
            interactionLabel = Text(interactionButton, "", "body");
            bottom.Add(interactionButton);
            interactionButton.style.display = DisplayStyle.None;
            Box(bottom, "spacer");
            Btn(bottom, "Acenar", () => world.Wave(), "secondary");
            if (!CreateMobileControls(screen))
            {
                var dpad = Box(screen, "dpad");
                Direction(dpad, "↑", new Vector2(0, 1), "up");
                Direction(dpad, "←", new Vector2(-1, 0), "left");
                Direction(dpad, "↓", new Vector2(0, -1), "down");
                Direction(dpad, "→", new Vector2(1, 0), "right");
            }
            world.InputEnabled = true;
            world.ApplyPlayers(session.Players);
        }

        void Direction(VisualElement parent, string title, Vector2 direction, string css)
        {
            var b = Btn(parent, title, () => { }, "direction " + css);
            b.RegisterCallback<PointerDownEvent>(e => { world.TouchMove = direction; b.CapturePointer(e.pointerId); e.StopPropagation(); });
            b.RegisterCallback<PointerUpEvent>(e => { world.TouchMove = Vector2.zero; b.ReleasePointer(e.pointerId); });
            b.RegisterCallback<PointerCaptureOutEvent>(_ => world.TouchMove = Vector2.zero);
        }

        void RefreshFriends()
        {
            if (world != null && session.IsJoined) world.ApplyPlayers(session.Players);
            if (friends == null) return;
            friends.Clear();
            var inRoom = session.Players.Where(p => p.roomId == world.RoomId && p.id != session.PlayerId).ToArray();
            Text(friends, "Nesta ilha", "eyebrow");
            Text(friends, "Colegas · " + (inRoom.Length + 1), "guide-title");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            ConfigureMobileScrollView(scroll);
            scroll.AddToClassList("friend-scroll");
            friends.Add(scroll);
            friendList = scroll.contentContainer;
            FriendRow(session.PlayerName, session.AvatarId, "Você", true);
            foreach (var p in inRoom) FriendRow(p.name, p.avatarId, string.IsNullOrEmpty(p.quizId) ? "Explorando" : "Em um desafio", false);
            if (inRoom.Length == 0)
                Text(friendList, session.IsOnline ? "Seus amigos aparecem aqui\nquando entram nesta ilha." : "Você está praticando.\nEntre em uma turma para\naprender com os amigos.", "hint wrap friend-empty");
        }

        void FriendRow(string playerName, string avatar, string activity, bool self)
        {
            var row = Box(friendList, "friend-row row");
            PetImage(row, avatar, "friend-avatar");
            var info = Box(row, "friend-info");
            Text(info, playerName, "friend-name");
            Text(info, activity, "hint");
            Text(row, "●", self ? "online-dot self" : "online-dot");
        }

        void EnterRoom(string roomId)
        {
            if (modalOpen || busy) return;
            currentQuizId = "";
            world.SetRoom(roomId);
            ShowHUD();
            PlayTone(true);
        }

        VisualElement OpenModal(string css = "")
        {
            if (overlay != null) overlay.RemoveFromHierarchy();
            modalOpen = true;
            SetMobileControlsEnabled(false);
            world.InputEnabled = false;
            world.TouchMove = Vector2.zero;
            overlay = Box(screen, "overlay");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            ConfigureMobileScrollView(scroll);
            scroll.AddToClassList("modal-scroll");
            overlay.Add(scroll);
            return Box(scroll.contentContainer, "modal card " + css);
        }

        void CloseModal()
        {
            if (busy) return;
            if (overlay != null) overlay.RemoveFromHierarchy();
            overlay = null;
            modalOpen = false;
            currentQuizId = "";
            quiz = null;
            world.InputEnabled = true;
            world.TouchMove = Vector2.zero;
            RefreshFriends();
            if (starsLabel != null) starsLabel.text = "★  " + ProgressStore.Instance.TotalStars;
        }

        void ShowQuizList()
        {
            if (busy || (modalOpen && quiz != null)) return;
            var subject = session.Catalog.subjects.FirstOrDefault(s => s.id == world.RoomId);
            if (subject == null) return;
            var panel = OpenModal("quiz-list");
            var heading = Box(panel, "row");
            Text(heading, SubjectSymbol(subject.id) + "   ILHA DE " + subject.title.ToUpperInvariant(), "eyebrow");
            Box(heading, "spacer");
            Btn(heading, "Fechar  ×", CloseModal, "quiet");
            Text(panel, "Escolha um desafio", "title");
            Text(panel, "Você pode repetir os desafios quando quiser.", "muted body");
            foreach (var item in subject.quizzes)
            {
                var q = item;
                var row = Box(panel, "quiz-choice row");
                var info = Box(row, "quiz-info");
                Text(info, q.title, "guide-title");
                Text(info, q.description, "muted small wrap");
                Text(info, q.questions.Length + " perguntas", "hint");
                Btn(row, "Começar", () => BeginQuiz(q), "primary");
            }
            status = Text(panel, "", "status");
        }

        void BeginQuiz(QuizDto selected)
        {
            if (busy) return;
            busy = true;
            if (status != null) status.text = "Preparando seu desafio…";
            StartCoroutine(session.BeginQuiz(selected.id, attempt =>
            {
                busy = false;
                if (attempt == null) { if (status != null) status.text = session.Error; return; }
                quiz = selected;
                currentQuizId = selected.id;
                questionIndex = attempt.questionIndex;
                ShowQuestion();
            }));
        }

        void ShowQuestion()
        {
            answered = false;
            selectedOption = null;
            var question = quiz.questions[questionIndex];
            var panel = OpenModal("question-panel");
            var heading = Box(panel, "row");
            Text(heading, quiz.title.ToUpperInvariant(), "eyebrow");
            Box(heading, "spacer");
            Btn(heading, "Voltar à ilha  ×", CloseModal, "quiet");
            var progress = Box(panel, "progress-row row");
            for (var i = 0; i < quiz.questions.Length; i++)
            {
                var dot = Box(progress, "progress-step");
                dot.EnableInClassList("done", i < questionIndex);
                dot.EnableInClassList("current", i == questionIndex);
            }
            Text(panel, "PERGUNTA " + (questionIndex + 1) + " DE " + quiz.questions.Length, "eyebrow question-count");
            Text(panel, question.text, "question-text");
            Text(panel, "Toque na resposta que você escolheu.", "muted body");
            var options = Box(panel, "options row");
            for (var i = 0; i < question.options.Length; i++)
            {
                var option = question.options[i];
                var button = new Button { name = option.id };
                button.AddToClassList("answer-choice");
                button.AddToClassList("row");
                Text(button, ((char)('A' + i)).ToString(), "answer-letter");
                Text(button, option.text, "answer-text");
                button.clicked += () =>
                {
                    if (answered || busy) return;
                    selectedOption = option.id;
                    foreach (var child in options.Children()) child.EnableInClassList("selected", child.name == selectedOption);
                    confirmButton.SetEnabled(true);
                };
                options.Add(button);
            }
            feedback = Box(panel, "feedback");
            status = Text(panel, "", "status");
            var actions = Box(panel, "row question-actions");
            Text(actions, "Escolha e confira sua resposta.", "hint");
            Box(actions, "spacer");
            confirmButton = Btn(actions, "Conferir resposta", () => SubmitAnswer(question, options), "primary");
            confirmButton.SetEnabled(false);
        }

        void SubmitAnswer(QuestionDto question, VisualElement options)
        {
            if (busy || answered || string.IsNullOrEmpty(selectedOption)) return;
            busy = true;
            // A timeout can hide an accepted server response. Keep the same
            // selection for an idempotent retry instead of sending a new answer.
            options.SetEnabled(false);
            confirmButton.SetEnabled(false);
            status.text = "Conferindo…";
            StartCoroutine(session.Answer(question.id, selectedOption, result =>
            {
                busy = false;
                if (result == null)
                {
                    status.text = session.Error;
                    confirmButton.SetEnabled(true);
                    return;
                }
                answered = true;
                lastAnswer = result;
                status.text = "";
                foreach (var child in options.Children())
                {
                    child.EnableInClassList("selected", false);
                    child.EnableInClassList("correct", child.name == result.correctOptionId);
                    child.EnableInClassList("try-again", child.name == selectedOption && !result.correct);
                }
                feedback.AddToClassList(result.correct ? "success" : "learning");
                Text(feedback, result.correct ? "Resposta certa!" : "Veja a explicação", "guide-title");
                Text(feedback, result.explanation, "body wrap");
                PlayTone(result.correct);
                confirmButton.text = result.completed ? "Ver resultado" : "Próxima pergunta";
                confirmButton.SetEnabled(true);
                // Replace the action instead of adding another callback to the same button.
                var next = Btn(confirmButton.parent, confirmButton.text, () =>
                {
                    if (lastAnswer.completed) ShowResults();
                    else { questionIndex = lastAnswer.answered; ShowQuestion(); }
                }, "primary");
                confirmButton.RemoveFromHierarchy();
                confirmButton = next;
            }));
        }

        void ShowResults()
        {
            var panel = OpenModal("results-panel");
            var score = lastAnswer.CorrectCount;
            var total = lastAnswer.total;
            var stars = lastAnswer.stars;
            Text(panel, "Desafio concluído", "eyebrow");
            Text(panel, new string('★', stars) + new string('☆', 3 - stars), "result-stars");
            Text(panel, "Seu resultado", "title");
            Text(panel, "Você acertou " + score + " de " + total + " perguntas.", "body");
            Text(panel, score == total ? "Você acertou todas. Escolha outro desafio para continuar." : "Você pode repetir para praticar as perguntas que errou.", "muted body wrap");
            var recap = Box(panel, "result-recap");
            Text(recap, quiz.title, "guide-title");
            Text(recap, string.IsNullOrEmpty(ProgressStore.Instance.LastSaveError) ? "Suas estrelas ficam guardadas neste dispositivo." : ProgressStore.Instance.LastSaveError, "hint");
            Btn(panel, "Voltar à ilha", CloseModal, "primary");
            var replay = quiz;
            Btn(panel, "Repetir desafio", () => BeginQuiz(replay), "quiet");
            PlayTone(true);
        }

        void ShowHelp()
        {
            if (busy) return;
            var panel = OpenModal("help-panel");
            Text(panel, "Como jogar", "eyebrow");
            Text(panel, "Controles", "title");
            var controls = GameControls.Connected
                ? "Analógico esquerdo: caminhar\n" + GameControls.ConfirmLabel + ": interagir / confirmar\n" + GameControls.CancelLabel + ": voltar\n" + GameControls.WaveLabel + ": acenar\n" + GameControls.MenuLabel + ": ajuda e pausa\n" + GameControls.HUDLabel + ": navegar pelos botões da tela\n\nNos campos de texto, confirme para abrir o teclado na tela."
                : IsMobileUI ? "Arraste o controle no canto esquerdo para caminhar.\nAproxime-se de um portal ou livro e toque no botão de ação.\nUse os botões da tela para voltar à praça, acenar ou abrir a ajuda."
                : "WASD ou setas: caminhar\nE: entrar no portal ou abrir o livro\nEspaço: acenar\nEsc: ajuda / voltar\n\nVocê também pode usar os botões na tela.";
            Text(panel, controls + "\n\nEm cada ilha, escolha um desafio e confira suas respostas. Os colegas da turma aparecem na mesma ilha.", "body wrap help-copy");
            Btn(panel, "Voltar ao jogo", CloseModal, "primary");
        }

        void ShowLeave()
        {
            var panel = OpenModal("help-panel");
            Text(panel, "Pausa", "eyebrow");
            Text(panel, "Sair do jogo?", "title");
            Text(panel, string.IsNullOrEmpty(ProgressStore.Instance.LastSaveError) ? "Suas estrelas já estão guardadas aqui." : ProgressStore.Instance.LastSaveError, "body muted");
            Btn(panel, "Continuar na ilha", CloseModal, "primary");
            Btn(panel, "Voltar à entrada", Leave, "quiet");
        }

        void ShowConnectionLost(string message)
        {
            busy = false;
            if (root == null) return;
            var panel = OpenModal("help-panel");
            Text(panel, "Conexão interrompida", "eyebrow");
            Text(panel, "Não foi possível continuar na turma", "title");
            Text(panel, message, "body wrap muted");
            Btn(panel, "Voltar à entrada", Leave, "primary");
        }

        void Leave()
        {
            if (busy) return;
            busy = true;
            world.InputEnabled = false;
            StartCoroutine(session.Leave(() =>
            {
                world.Shutdown();
                currentQuizId = "";
                quiz = null;
                busy = false;
                ShowLogin();
            }));
        }

        void ToggleSound()
        {
            muted = !muted;
            PlayerPrefs.SetInt("aprender.muted", muted ? 1 : 0);
            PlayerPrefs.Save();
            soundButton.text = muted ? "Som: não" : "Som: sim";
        }

        void PlayTone(bool correct)
        {
            if (!muted && audioSource != null) audioSource.PlayOneShot(correct ? positiveTone : gentleTone, 0.18f);
        }

        static AudioClip MakeTone(string clipName, float first, float second)
        {
            const int rate = 22050;
            var samples = new float[rate / 3];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = (float)i / rate;
                var frequency = t < 0.14f ? first : second;
                var envelope = Mathf.Sin(Mathf.PI * i / samples.Length) * Mathf.Exp(-t * 4);
                samples[i] = Mathf.Sin(t * frequency * Mathf.PI * 2) * envelope * 0.45f;
            }
            var clip = AudioClip.Create(clipName, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static void Brand(VisualElement parent)
        {
            var brand = Box(parent, "brand row");
            Text(brand, "IS", "brand-icon");
            Text(brand, "Ilha do Saber", "brand-name");
        }

        static Image PetImage(VisualElement parent, string id, string classes)
        {
            var image = new Image { image = Resources.Load<Texture2D>("AvatarPreviews/" + id), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            foreach (var css in classes.Split(' ')) if (css.Length > 0) image.AddToClassList(css);
            parent.Add(image);
            return image;
        }

        static VisualElement Box(VisualElement parent, string classes)
        {
            var element = new VisualElement();
            foreach (var css in classes.Split(' ')) if (css.Length > 0) element.AddToClassList(css);
            parent.Add(element);
            return element;
        }

        static Label Text(VisualElement parent, string value, string classes)
        {
            var label = new Label(value) { enableRichText = false };
            foreach (var css in classes.Split(' ')) if (css.Length > 0) label.AddToClassList(css);
            parent.Add(label);
            return label;
        }

        static Button Btn(VisualElement parent, string title, Action action, string classes)
        {
            var button = new Button(action) { text = title };
            foreach (var css in classes.Split(' ')) if (css.Length > 0) button.AddToClassList(css);
            parent.Add(button);
            return button;
        }

        static string RoomTitle(string room) => room == "hub" ? "Praça" : "Ilha de " + (room == "matematica" ? "Matemática" : room == "portugues" ? "Português" : "Ciências");
        static string SubjectSymbol(string id) => id == "matematica" ? "+" : id == "portugues" ? "Aa" : "✿";
        static Color SubjectColor(string id) => id == "matematica" ? new Color(0.53f, 0.40f, 0.84f) : id == "portugues" ? new Color(0.90f, 0.55f, 0.31f) : new Color(0.26f, 0.64f, 0.51f);

        void OnApplicationFocus(bool focused) => SetMobileApplicationFocus(focused);

        void OnApplicationPause(bool paused) => SetMobileApplicationPaused(paused);

        void OnDestroy()
        {
            DisposeControllerUI();
            DisposeMobileUI();
            if (positiveTone != null) Destroy(positiveTone);
            if (gentleTone != null) Destroy(gentleTone);
        }
    }
}
