using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Aprender
{
    /// <summary>Device-local profile, best results and the last 100 attempts. No session tokens are saved.</summary>
    public sealed class ProgressStore
    {
        private static ProgressStore _instance;
        public static ProgressStore Instance => _instance ?? (_instance = new ProgressStore());

        private readonly string _path;
        private ProgressData _data;
        public string ProfileName => _data.profileName ?? "";
        public string ProfileAvatarId => _data.profileAvatarId ?? "";
        public string LastSaveError { get; private set; }
        public IReadOnlyList<SavedAttempt> Attempts => _data.attempts;
        public int CompletedQuizzes => _data.quizzes.Count;
        public int TotalStars
        {
            get
            {
                var total = 0;
                foreach (var quiz in _data.quizzes) total += quiz.stars;
                return total;
            }
        }

        private ProgressStore()
        {
            _path = Path.Combine(Application.persistentDataPath, "aprender-progress-v1.json");
            _data = Read(_path) ?? Read(_path + ".backup") ?? new ProgressData();
            if (_data.quizzes == null) _data.quizzes = new List<QuizProgress>();
            if (_data.attempts == null) _data.attempts = new List<SavedAttempt>();
        }

        private static ProgressData Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var data = JsonUtility.FromJson<ProgressData>(File.ReadAllText(path));
                return data != null && data.version == 1 ? data : null;
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void SaveProfile(string name, string avatarId)
        {
            _data.profileName = name ?? "";
            _data.profileAvatarId = avatarId ?? "";
            Save();
        }

        public int BestScore(string quizId) => GetQuizProgress(quizId)?.bestScore ?? 0;
        public int StarsFor(string quizId) => GetQuizProgress(quizId)?.stars ?? 0;
        public bool HasCompleted(string quizId) => GetQuizProgress(quizId) != null;

        public QuizProgress GetQuizProgress(string quizId)
        {
            foreach (var quiz in _data.quizzes)
                if (quiz.quizId == quizId) return quiz;
            return null;
        }

        internal void BeginAttempt(AttemptDto attempt, bool online)
        {
            _data.attempts.Add(new SavedAttempt
            {
                id = attempt.id,
                quizId = attempt.quizId,
                mode = online ? "online" : "practice",
                startedAt = DateTime.UtcNow.ToString("o"),
                total = attempt.totalQuestions
            });
            if (_data.attempts.Count > 100) _data.attempts.RemoveAt(0);
            Save();
        }

        internal void RecordAnswer(AttemptDto attempt, string questionId, string optionId, AnswerResult result)
        {
            var saved = _data.attempts.Find(item => item.id == attempt.id);
            if (saved == null) return;
            if (saved.answers == null) saved.answers = new List<SavedAnswer>();
            if (saved.answers.Exists(item => item.questionId == questionId)) return;
            saved.answers.Add(new SavedAnswer { questionId = questionId, optionId = optionId, correct = result.correct });
            saved.score = result.score;
            saved.answered = result.answered;
            saved.completed = result.completed;
            if (result.completed)
            {
                saved.finishedAt = DateTime.UtcNow.ToString("o");
                var stars = CalculateStars(result.score, result.total);
                var progress = GetQuizProgress(attempt.quizId);
                if (progress == null)
                {
                    progress = new QuizProgress { quizId = attempt.quizId };
                    _data.quizzes.Add(progress);
                }
                progress.bestScore = Math.Max(progress.bestScore, result.score);
                progress.stars = Math.Max(progress.stars, stars);
                progress.completions++;
            }
            Save();
        }

        public static int CalculateStars(int score, int totalQuestions)
        {
            if (totalQuestions <= 0) return 0;
            var maximum = totalQuestions * 100;
            if (score >= maximum) return 3;
            return score >= maximum * 0.6f ? 2 : 1;
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(_data, true));
                // Replace publishes the complete file atomically and keeps the previous
                // valid snapshot for recovery after a power loss or external corruption.
                if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".backup");
                else File.Move(temporary, _path);
                LastSaveError = "";
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
            {
                LastSaveError = "Não foi possível salvar o progresso neste aparelho.";
                Debug.LogWarning(LastSaveError + " " + exception.GetType().Name);
            }
        }

        [Serializable] private sealed class ProgressData
        {
            public int version = 1;
            public string profileName = "";
            public string profileAvatarId = "";
            public List<QuizProgress> quizzes = new List<QuizProgress>();
            public List<SavedAttempt> attempts = new List<SavedAttempt>();
        }
    }

    [Serializable]
    public sealed class QuizProgress
    {
        public string quizId;
        public int bestScore;
        public int stars;
        public int completions;
    }

    [Serializable]
    public sealed class SavedAttempt
    {
        public string id;
        public string quizId;
        public string mode;
        public string startedAt;
        public string finishedAt;
        public int score;
        public int answered;
        public int total;
        public bool completed;
        public List<SavedAnswer> answers = new List<SavedAnswer>();
    }

    [Serializable]
    public sealed class SavedAnswer
    {
        public string questionId;
        public string optionId;
        public bool correct;
    }
}
