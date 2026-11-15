using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Aprender
{
    /// <summary>
    /// Coordinates the child-facing client. Practice works entirely on this device;
    /// online presence and grading always come from the session server.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        private const int RequestTimeoutSeconds = 6;
        private const float PollInterval = 0.5f;
        private const float DisconnectAfterSeconds = 12f;

        public Catalog Catalog { get; private set; } = new Catalog();
        public bool IsOnline { get; private set; }
        public bool IsJoined { get; private set; }
        public string PlayerId { get; private set; } = "";
        public string PlayerName { get; private set; } = "";
        public string AvatarId { get; private set; } = "";
        public string SessionCode { get; private set; } = "";
        public string ServerUrl { get; private set; } = "http://localhost:8787";
        public List<PlayerPresence> Players { get; } = new List<PlayerPresence>();
        public string Error { get; private set; } = "";
        public AttemptDto CurrentAttempt { get; private set; }
        public string PendingOptionId { get; private set; } = "";

        public event Action PlayersChanged;
        public event Action<string> ConnectionLost;

        private Catalog _practiceCatalog;
        private string _token = "";
        private bool _operationBusy;
        private bool _requestBusy;
        private bool _destroyed;
        private int _sessionEpoch;
        private UnityWebRequest _activeRequest;
        private QuizDto _currentQuiz;
        private string _pendingQuestionId = "";
        private readonly PresenceRequest _presence = new PresenceRequest();
        private readonly Dictionary<string, AnswerResult> _answers = new Dictionary<string, AnswerResult>();

        private void Awake()
        {
            ReadPracticeCatalog();
            PlayerName = ProgressStore.Instance.ProfileName;
            AvatarId = ProgressStore.Instance.ProfileAvatarId;
        }

        private void OnDestroy()
        {
            _destroyed = true;
            _sessionEpoch++;
            _activeRequest?.Abort();
        }

        public void ClearError() => Error = "";

        public IEnumerator LoadCatalog()
        {
            if (!IsOnline)
            {
                ReadPracticeCatalog();
                yield break;
            }
            var epoch = _sessionEpoch;
            HttpResult response = null;
            yield return Request("GET", "/api/catalog", null, false, result => response = result);
            // A catalog request can finish after Leave or a new practice session.
            if (epoch != _sessionEpoch || !IsOnline) yield break;
            if (response.success && TryParse(response.json, out Catalog catalog) && IsValidCatalog(catalog))
            {
                Catalog = catalog;
                Error = "";
            }
            else Error = response.success ? "O catálogo de desafios não pôde ser lido." : response.message;
        }

        public IEnumerator Join(string serverUrl, string code, string name, string avatarId, bool create, Action<bool> done)
        {
            if (_operationBusy || IsJoined)
            {
                Error = IsJoined ? "Saia da sala atual antes de entrar em outra." : "Espere só um pouquinho. Estamos terminando a última ação.";
                done?.Invoke(false);
                yield break;
            }
            if (!ValidateProfile(name, avatarId, out var cleanName, out var cleanAvatar) || !ValidateServerUrl(serverUrl, out var cleanUrl))
            {
                done?.Invoke(false);
                yield break;
            }
            var cleanCode = (code ?? "").Trim().ToUpperInvariant();
            if ((!create || cleanCode.Length > 0) && !ValidCode(cleanCode))
            {
                Error = "O código da sala precisa ter de 4 a 8 letras ou números.";
                done?.Invoke(false);
                yield break;
            }

            _operationBusy = true;
            var success = false;
            Error = "";
            ServerUrl = cleanUrl;
            try
            {
                HttpResult response = null;
                yield return Request("GET", "/api/catalog", null, false, result => response = result);
                if (!response.success || !TryParse(response.json, out Catalog onlineCatalog) || !IsValidCatalog(onlineCatalog))
                {
                    Error = response.success ? "O catálogo de desafios não pôde ser lido." : response.message;
                    yield break;
                }
                if (create)
                {
                    yield return Request("POST", "/api/sessions", new SessionCodeDto { code = cleanCode }, false, result => response = result);
                    if (!response.success || !TryParse(response.json, out SessionCodeDto created) || !ValidCode(created.code))
                    {
                        Error = response.success ? "Não foi possível criar a sala." : response.message;
                        yield break;
                    }
                    cleanCode = created.code;
                }
                yield return Request("POST", "/api/sessions/" + Uri.EscapeDataString(cleanCode) + "/join",
                    new JoinRequest { name = cleanName, avatarId = cleanAvatar }, false, result => response = result);
                if (!response.success || !TryParse(response.json, out JoinResult joined) ||
                    string.IsNullOrEmpty(joined.token) || string.IsNullOrEmpty(joined.playerId))
                {
                    Error = response.success ? "Não foi possível entrar nesta sala." : response.message;
                    yield break;
                }

                Catalog = onlineCatalog;
                _token = joined.token;
                PlayerId = joined.playerId;
                PlayerName = cleanName;
                AvatarId = cleanAvatar;
                SessionCode = string.IsNullOrEmpty(joined.code) ? cleanCode : joined.code;
                IsOnline = true;
                IsJoined = true;
                // Uma sessão aberta pelo portal pode ter quizzes exclusivos da
                // turma. O catálogo público continua sendo o plano B caso o
                // servidor seja uma versão anterior que não ofereça esta rota.
                yield return Request("GET", "/api/sessions/" + Uri.EscapeDataString(SessionCode) + "/catalog", null, true, result => response = result);
                if (response.success && TryParse(response.json, out Catalog sessionCatalog) && IsValidCatalog(sessionCatalog))
                    Catalog = sessionCatalog;
                ResetPresence();
                ResetAttempt();
                Players.Clear();
                Players.Add(SelfPresence());
                ProgressStore.Instance.SaveProfile(PlayerName, AvatarId);
                PlayersChanged?.Invoke();
                var epoch = ++_sessionEpoch;
                StartCoroutine(Heartbeat(epoch));
                success = true;
            }
            finally
            {
                _operationBusy = false;
                if (!_destroyed) done?.Invoke(success);
            }
        }

        public void JoinPractice(string name, string avatarId)
        {
            if (_operationBusy || IsJoined)
            {
                Error = "Saia da sala atual antes de começar a praticar.";
                return;
            }
            if (!ValidateProfile(name, avatarId, out var cleanName, out var cleanAvatar)) return;
            if (!ReadPracticeCatalog()) return;
            Error = "";
            _sessionEpoch++;
            _token = "";
            PlayerId = "practice-" + Guid.NewGuid().ToString("N");
            PlayerName = cleanName;
            AvatarId = cleanAvatar;
            SessionCode = "";
            IsOnline = false;
            IsJoined = true;
            ResetPresence();
            ResetAttempt();
            Players.Clear();
            Players.Add(SelfPresence());
            ProgressStore.Instance.SaveProfile(PlayerName, AvatarId);
            PlayersChanged?.Invoke();
        }

        public IEnumerator BeginQuiz(string quizId, Action<AttemptDto> done)
        {
            if (!CanAct(done)) yield break;
            var quiz = Catalog.FindQuiz(quizId);
            if (quiz == null || quiz.questions == null || quiz.questions.Length == 0)
            {
                Error = "Este desafio ainda não está disponível.";
                done?.Invoke(null);
                yield break;
            }
            _operationBusy = true;
            Error = "";
            var epoch = _sessionEpoch;
            AttemptDto completedAttempt = null;
            try
            {
                AttemptDto attempt;
                if (IsOnline)
                {
                    HttpResult response = null;
                    yield return Request("POST", SessionPath + "/attempts", new AttemptRequest { quizId = quizId }, true, result => response = result);
                    if (epoch != _sessionEpoch || !response.success || !TryParse(response.json, out attempt) || string.IsNullOrEmpty(attempt.id))
                    {
                        Error = epoch != _sessionEpoch ? "A conexão com a sala foi encerrada." :
                            response.success ? "Não foi possível iniciar este desafio." : response.message;
                        yield break;
                    }
                    if (attempt.quizId != quizId || attempt.totalQuestions != quiz.questions.Length || attempt.questionIndex != 0)
                    {
                        Error = "O desafio foi atualizado. Saia e entre novamente na sala.";
                        yield break;
                    }
                }
                else attempt = new AttemptDto { id = Guid.NewGuid().ToString("N"), quizId = quizId, totalQuestions = quiz.questions.Length };

                CurrentAttempt = attempt;
                _currentQuiz = quiz;
                _answers.Clear();
                _pendingQuestionId = PendingOptionId = "";
                _presence.quizId = quizId;
                ProgressStore.Instance.BeginAttempt(attempt, IsOnline);
                UpdateSelfPresence();
                completedAttempt = attempt;
            }
            finally
            {
                _operationBusy = false;
                if (!_destroyed) done?.Invoke(completedAttempt);
            }
        }

        public IEnumerator Answer(string questionId, string optionId, Action<AnswerResult> done)
        {
            if (_answers.TryGetValue(questionId ?? "", out var cached))
            {
                Error = "";
                done?.Invoke(cached);
                yield break;
            }
            if (!CanAct(done)) yield break;
            if (CurrentAttempt == null || _currentQuiz == null || CurrentAttempt.completed ||
                CurrentAttempt.questionIndex < 0 || CurrentAttempt.questionIndex >= _currentQuiz.questions.Length)
            {
                Error = "Escolha um desafio para começar.";
                done?.Invoke(null);
                yield break;
            }
            var question = _currentQuiz.questions[CurrentAttempt.questionIndex];
            if (question.id != questionId || Array.Find(question.options, option => option.id == optionId) == null)
            {
                Error = "Escolha uma das respostas desta pergunta.";
                done?.Invoke(null);
                yield break;
            }
            if (_pendingQuestionId == questionId && PendingOptionId != optionId)
            {
                Error = "Sua primeira escolha já foi enviada. Confira a mesma resposta novamente para continuar.";
                done?.Invoke(null);
                yield break;
            }
            _operationBusy = true;
            Error = "";
            var epoch = _sessionEpoch;
            AnswerResult completedAnswer = null;
            try
            {
                AnswerResult answer;
                if (IsOnline)
                {
                    // A lost response does not tell us whether the server accepted the
                    // choice. Preserve it so the retry uses the server's replay key.
                    _pendingQuestionId = questionId;
                    PendingOptionId = optionId;
                    HttpResult response = null;
                    yield return Request("POST", SessionPath + "/attempts/" + Uri.EscapeDataString(CurrentAttempt.id) + "/answers",
                        new AnswerRequest { questionId = questionId, optionId = optionId }, true, result => response = result);
                    if (epoch != _sessionEpoch || !response.success || !TryParse(response.json, out answer))
                    {
                        Error = epoch != _sessionEpoch ? "A conexão com a sala foi encerrada." :
                            response.success ? "Não foi possível conferir a resposta." : response.message;
                        yield break;
                    }
                    if (answer.total != CurrentAttempt.totalQuestions || answer.answered != CurrentAttempt.questionIndex + 1 ||
                        answer.score < 0 || answer.score > answer.total * 100 || string.IsNullOrEmpty(answer.correctOptionId))
                    {
                        Error = "A resposta da sala não pôde ser conferida. Tente o desafio novamente.";
                        yield break;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(question.correctOptionId))
                    {
                        Error = "A resposta deste desafio de prática não foi encontrada.";
                        yield break;
                    }
                    var correct = optionId == question.correctOptionId;
                    var answered = CurrentAttempt.questionIndex + 1;
                    answer = new AnswerResult
                    {
                        correct = correct,
                        correctOptionId = question.correctOptionId,
                        explanation = question.explanation,
                        score = CurrentAttempt.score + (correct ? 100 : 0),
                        answered = answered,
                        total = CurrentAttempt.totalQuestions,
                        completed = answered == CurrentAttempt.totalQuestions
                    };
                }
                answer.stars = answer.completed ? ProgressStore.CalculateStars(answer.score, answer.total) : 0;
                CurrentAttempt.questionIndex = answer.answered;
                CurrentAttempt.score = answer.score;
                CurrentAttempt.completed = answer.completed;
                _pendingQuestionId = PendingOptionId = "";
                _answers.Add(questionId, answer);
                ProgressStore.Instance.RecordAnswer(CurrentAttempt, questionId, optionId, answer);
                completedAnswer = answer;
            }
            finally
            {
                _operationBusy = false;
                if (!_destroyed) done?.Invoke(completedAnswer);
            }
        }

        public void SetPresence(string roomId, Vector3 position, float yaw, string quizId)
        {
            if (!IsJoined) return;
            _presence.roomId = string.IsNullOrEmpty(roomId) ? "hub" : roomId;
            _presence.x = Finite(position.x) ? Mathf.Clamp(position.x, -1000f, 1000f) : 0f;
            _presence.z = Finite(position.z) ? Mathf.Clamp(position.z, -1000f, 1000f) : 0f;
            _presence.yaw = Finite(yaw) ? Mathf.Repeat(yaw, 360f) : 0f;
            _presence.quizId = quizId ?? "";
            UpdateSelfPresence();
        }

        public IEnumerator Leave(Action done = null)
        {
            while (_operationBusy && !_destroyed) yield return null;
            _operationBusy = true;
            var online = IsOnline && IsJoined;
            var path = SessionPath + "/players/me";
            _sessionEpoch++;
            IsJoined = false;
            try
            {
                if (online)
                    yield return Request("DELETE", path, null, true, ignored => { });
                IsOnline = false;
                _token = "";
                SessionCode = "";
                PlayerId = "";
                Players.Clear();
                ResetAttempt();
                ReadPracticeCatalog();
                PlayersChanged?.Invoke();
            }
            finally
            {
                _operationBusy = false;
                if (!_destroyed) done?.Invoke();
            }
        }

        private IEnumerator Heartbeat(int epoch)
        {
            var failures = 0;
            var failureSince = 0f;
            while (IsJoined && IsOnline && epoch == _sessionEpoch)
            {
                var nextPoll = Time.realtimeSinceStartup + PollInterval;
                HttpResult presence = null;
                yield return Request("PUT", SessionPath + "/presence", _presence, true, result => presence = result);
                if (epoch != _sessionEpoch || !IsJoined) yield break;
                HttpResult stateResponse = null;
                if (presence.success)
                    yield return Request("GET", SessionPath + "/state", null, true, result => stateResponse = result);
                if (epoch != _sessionEpoch || !IsJoined) yield break;
                SessionStateDto state = null;
                var healthy = presence.success && stateResponse != null && stateResponse.success &&
                    TryParse(stateResponse.json, out state) && state.players != null && state.selfId == PlayerId;
                if (healthy)
                {
                    failures = 0;
                    Players.Clear();
                    foreach (var player in state.players)
                        if (player != null && !string.IsNullOrEmpty(player.id)) Players.Add(player);
                    UpdateSelfPresence();
                    PlayersChanged?.Invoke();
                }
                else
                {
                    if (failures++ == 0) failureSince = Time.realtimeSinceStartup;
                    if (failures >= 3 && Time.realtimeSinceStartup - failureSince >= DisconnectAfterSeconds)
                    {
                        Disconnect("A conexão com a sala foi perdida. Entre novamente para reencontrar seus amigos.");
                        yield break;
                    }
                }
                while (Time.realtimeSinceStartup < nextPoll && epoch == _sessionEpoch) yield return null;
            }
        }

        private string SessionPath => "/api/sessions/" + Uri.EscapeDataString(SessionCode);

        private IEnumerator Request(string method, string path, object body, bool authorized, Action<HttpResult> done)
        {
            while (_requestBusy && !_destroyed) yield return null;
            if (_destroyed)
            {
                done(new HttpResult { message = "O jogo foi fechado." });
                yield break;
            }
            if (authorized && string.IsNullOrEmpty(_token))
            {
                done(new HttpResult { message = "Entre novamente na sala para continuar." });
                yield break;
            }
            _requestBusy = true;
            HttpResult response;
            using (var request = new UnityWebRequest(ServerUrl + path, method))
            {
                _activeRequest = request;
                request.timeout = RequestTimeoutSeconds;
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Accept", "application/json");
                if (authorized) request.SetRequestHeader("Authorization", "Bearer " + _token);
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                    request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                }
                try
                {
                    yield return request.SendWebRequest();
                    response = new HttpResult
                    {
                        success = request.result == UnityWebRequest.Result.Success,
                        json = request.downloadHandler.text,
                        message = FriendlyError(request)
                    };
                }
                finally
                {
                    _activeRequest = null;
                    _requestBusy = false;
                }
            }
            done(response);
        }

        private static string FriendlyError(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.Success) return "";
            if (TryParse(request.downloadHandler.text, out ApiErrorEnvelope error) && !string.IsNullOrEmpty(error.error?.message))
                return error.error.message;
            if (request.responseCode == 401 || request.responseCode == 403) return "Entre novamente na sala para continuar.";
            if (request.responseCode == 404) return "Esta sala não foi encontrada. Confira o código com o professor.";
            if (request.responseCode == 429) return "A sala está recebendo muitas ações. Espere alguns segundos e tente novamente.";
            if (request.responseCode >= 500) return "A sala está com um problema. Tente novamente em alguns instantes.";
            return "Não consegui conversar com a sala. Confira a conexão e o endereço do servidor.";
        }

        private bool ReadPracticeCatalog()
        {
            if (_practiceCatalog == null)
            {
                var resource = Resources.Load<TextAsset>("quiz-catalog");
                if (resource == null || !TryParse(resource.text, out _practiceCatalog) || !IsValidCatalog(_practiceCatalog))
                {
                    _practiceCatalog = null;
                    Error = "Os desafios de prática não foram encontrados nos arquivos do jogo.";
                    return false;
                }
            }
            Catalog = _practiceCatalog;
            Error = "";
            return true;
        }

        private static bool IsValidCatalog(Catalog catalog)
        {
            if (catalog?.subjects == null || catalog.subjects.Length == 0) return false;
            var ids = new HashSet<string>();
            foreach (var subject in catalog.subjects)
            {
                if (subject == null || string.IsNullOrEmpty(subject.id) || subject.quizzes == null) return false;
                foreach (var quiz in subject.quizzes)
                {
                    if (quiz == null || string.IsNullOrEmpty(quiz.id) || !ids.Add(quiz.id) || quiz.questions == null || quiz.questions.Length == 0) return false;
                    var questionIds = new HashSet<string>();
                    foreach (var question in quiz.questions)
                    {
                        if (question == null || string.IsNullOrEmpty(question.id) || !questionIds.Add(question.id) ||
                            question.options == null || question.options.Length < 2) return false;
                        var optionIds = new HashSet<string>();
                        foreach (var option in question.options)
                            if (option == null || string.IsNullOrEmpty(option.id) || !optionIds.Add(option.id)) return false;
                    }
                }
            }
            return true;
        }

        private bool ValidateProfile(string name, string avatarId, out string cleanName, out string cleanAvatar)
        {
            cleanName = (name ?? "").Trim();
            cleanAvatar = (avatarId ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(cleanName, @"^[\p{L}\p{N}\p{M} -]{2,20}$"))
            {
                Error = "Escolha um nome com 2 a 20 letras ou números.";
                return false;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(cleanAvatar, @"^[A-Za-z0-9_-]{1,48}$"))
            {
                Error = "Escolha um personagem para a sua aventura.";
                return false;
            }
            return true;
        }

        private bool ValidateServerUrl(string value, out string url)
        {
            url = (value ?? "").Trim().TrimEnd('/');
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                Error = "Use um endereço de servidor começando com http:// ou https://, sem parâmetros.";
                return false;
            }
            if (url.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) url = url.Substring(0, url.Length - 4);
            return true;
        }

        private bool CanAct<T>(Action<T> done) where T : class
        {
            if (IsJoined && !_operationBusy) return true;
            Error = !IsJoined ? "Entre em uma sala ou escolha praticar para começar." : "Espere só um pouquinho. Estamos conferindo a última ação.";
            done?.Invoke(null);
            return false;
        }

        private static bool ValidCode(string code) => !string.IsNullOrEmpty(code) && System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9]{4,8}$");
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool TryParse<T>(string json, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try { value = JsonUtility.FromJson<T>(json); return value != null; }
            catch (ArgumentException) { return false; }
        }

        private void ResetPresence()
        {
            _presence.roomId = "hub";
            _presence.x = _presence.z = _presence.yaw = 0f;
            _presence.quizId = "";
        }

        private void ResetAttempt()
        {
            CurrentAttempt = null;
            _currentQuiz = null;
            _answers.Clear();
            _pendingQuestionId = PendingOptionId = "";
            _presence.quizId = "";
        }

        private PlayerPresence SelfPresence() => new PlayerPresence
        {
            id = PlayerId, name = PlayerName, avatarId = AvatarId,
            roomId = _presence.roomId, x = _presence.x, z = _presence.z,
            yaw = _presence.yaw, quizId = _presence.quizId
        };

        private void UpdateSelfPresence()
        {
            var self = Players.Find(player => player.id == PlayerId);
            if (self == null) return;
            self.roomId = _presence.roomId;
            self.x = _presence.x;
            self.z = _presence.z;
            self.yaw = _presence.yaw;
            self.quizId = _presence.quizId;
        }

        private void Disconnect(string message)
        {
            _sessionEpoch++;
            IsJoined = false;
            IsOnline = false;
            _token = "";
            Players.Clear();
            ResetAttempt();
            ReadPracticeCatalog();
            Error = message;
            PlayersChanged?.Invoke();
            ConnectionLost?.Invoke(message);
        }

        private sealed class HttpResult
        {
            public bool success;
            public string json;
            public string message;
        }
    }
}
