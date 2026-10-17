using System;
using UnityEngine;

namespace Aprender
{
    [Serializable]
    public sealed class Catalog
    {
        public SubjectDto[] subjects = Array.Empty<SubjectDto>();

        public QuizDto FindQuiz(string id)
        {
            foreach (var subject in subjects ?? Array.Empty<SubjectDto>())
                foreach (var quiz in subject.quizzes ?? Array.Empty<QuizDto>())
                    if (quiz.id == id) return quiz;
            return null;
        }
    }

    [Serializable]
    public sealed class SubjectDto
    {
        public string id;
        public string title;
        public string subtitle;
        public string color;
        public string icon;
        public QuizDto[] quizzes = Array.Empty<QuizDto>();
    }

    [Serializable]
    public sealed class QuizDto
    {
        public string id;
        public string title;
        public string description;
        public QuestionDto[] questions = Array.Empty<QuestionDto>();
    }

    [Serializable]
    public sealed class QuestionDto
    {
        public string id;
        public string text;
        public OptionDto[] options = Array.Empty<OptionDto>();
        // Answer keys exist only in the bundled practice catalog. The online
        // catalog omits them and every online answer is graded by the server.
        public string correctOptionId;
        public string explanation;
    }

    [Serializable]
    public sealed class OptionDto
    {
        public string id;
        public string text;
    }

    [Serializable]
    public sealed class PlayerPresence
    {
        public string id;
        public string name;
        public string avatarId;
        public string roomId;
        public float x;
        public float z;
        public float yaw;
        public string quizId;

        public Vector3 Position => new Vector3(x, 0f, z);
    }

    [Serializable]
    public sealed class AttemptDto
    {
        public string id;
        public string quizId;
        public int questionIndex;
        public int totalQuestions;
        public int score;
        public bool completed;
    }

    [Serializable]
    public sealed class AnswerResult
    {
        public bool correct;
        public string correctOptionId;
        public string explanation;
        public int score;
        public int answered;
        public int total;
        public bool completed;
        public int stars;
        public int CorrectCount => score / 100;
    }

    [Serializable] internal sealed class SessionCodeDto { public string code; }
    [Serializable] internal sealed class JoinRequest { public string name; public string avatarId; }
    [Serializable] internal sealed class JoinResult { public string token; public string playerId; public string code; }
    [Serializable] internal sealed class AttemptRequest { public string quizId; }
    [Serializable] internal sealed class AnswerRequest { public string questionId; public string optionId; }
    [Serializable] internal sealed class SessionStateDto
    {
        public string code;
        public string selfId;
        public PlayerPresence[] players;
    }
    [Serializable] internal sealed class PresenceRequest
    {
        public string roomId = "hub";
        public float x;
        public float z;
        public float yaw;
        public string quizId = "";
    }
    [Serializable] internal sealed class ApiErrorEnvelope { public ApiError error; }
    [Serializable] internal sealed class ApiError { public string code; public string message; }
}
