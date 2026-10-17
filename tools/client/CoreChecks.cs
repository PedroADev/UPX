using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Aprender;
using UnityEngine;
using UnityEngine.Networking;
public static class CoreChecks {
 static int checks;
 static void Assert(bool condition,string message) {if(!condition) throw new Exception(message); checks++;}
 static void Drain(IEnumerator operation) { while(operation.MoveNext()) {if(operation.Current is IEnumerator nested) Drain(nested);} }
 public static string Run(string fixture,string dataPath) {
  Application.persistentDataPath=dataPath;
  Resources.CatalogJson=File.ReadAllText(fixture);
  var session=new GameSession();
  typeof(GameSession).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(session,null);
  Assert(session.Catalog.subjects.Length==3,"3 disciplines load");
  session.JoinPractice("Ana","dog");
  Assert(session.IsJoined&&!session.IsOnline,"practice joins without a server");
  Assert(session.Players.Count==1&&session.Players[0].roomId=="hub","practice contains only real local player");
  Assert(ProgressStore.Instance.ProfileName=="Ana","profile persisted");
  var quiz=session.Catalog.subjects[0].quizzes[0];
  AttemptDto attempt=null;
  Drain(session.BeginQuiz(quiz.id,value=>attempt=value));
  Assert(attempt!=null&&attempt.totalQuestions==4,"practice attempt starts");
  AnswerResult result=null;
  var first=quiz.questions[0];
  Drain(session.Answer(first.id,"missing",value=>result=value));
  Assert(result==null&&attempt.questionIndex==0,"invalid option does not advance");
  var all=quiz.questions;
  foreach(var question in all) {
   Drain(session.Answer(question.id,question.correctOptionId,value=>result=value));
   Assert(result!=null&&result.correct,"correct answer grades");
   var index=attempt.questionIndex;
   var score=attempt.score;
   AnswerResult replay=null;
   Drain(session.Answer(question.id,question.correctOptionId,value=>replay=value));
   Assert(replay==result&&attempt.score==score&&attempt.questionIndex==index,"duplicate does not score twice");
  }
  Assert(result.completed&&result.score==400&&result.CorrectCount==4&&result.stars==3,"perfect result totals");
  Assert(ProgressStore.Instance.CompletedQuizzes==1&&ProgressStore.Instance.TotalStars==3,"completed progress once");
  Assert(ProgressStore.Instance.Attempts.Count==1&&ProgressStore.Instance.Attempts[0].answers.Count==4,"attempt persists individual answers");
  Drain(session.BeginQuiz(quiz.id,value=>attempt=value));
  foreach(var question in all) {
   var wrong=question.options.First(option=>option.id!=question.correctOptionId).id;
   Drain(session.Answer(question.id,wrong,value=>result=value));
  }
  Assert(result.completed&&result.score==0&&result.stars==1,"mistakes still earn completion star");
  Assert(ProgressStore.Instance.TotalStars==3&&ProgressStore.Instance.BestScore(quiz.id)==400,"best result survives replay");
  session.SetPresence("matematica",new Vector3(2,0,5),370,quiz.id);
  Assert(session.Players[0].x==2&&session.Players[0].z==5&&session.Players[0].yaw==10,"presence updates player");
  Drain(session.Leave());
  Assert(!session.IsJoined&&session.Players.Count==0,"leave clears presence");
  session.JoinPractice("A","dog");
  Assert(!session.IsJoined&&!string.IsNullOrEmpty(session.Error),"invalid profile rejected");
  var path=Path.Combine(dataPath,"aprender-progress-v1.json");
  Assert(File.Exists(path)&&File.Exists(path+".backup")&&!File.Exists(path+".tmp"),"atomic progress snapshot and backup");
  typeof(ProgressStore).GetField("_instance",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,null);
  Assert(ProgressStore.Instance.TotalStars==3&&ProgressStore.Instance.ProfileName=="Ana","profile and progress reload");
  Assert(ProgressStore.CalculateStars(200,4)==1&&ProgressStore.CalculateStars(300,4)==2&&ProgressStore.CalculateStars(400,4)==3,"star thresholds");
  // Exercise lifecycle races with deterministic responses. This does not run
  // UnityWebRequest, Unity coroutine scheduling, or the heartbeat transport.
  var publicCatalog=JsonUtility.FromJson<Catalog>(Resources.CatalogJson);
  foreach(var subject in publicCatalog.subjects)
   foreach(var publicQuiz in subject.quizzes)
    foreach(var question in publicQuiz.questions) {question.correctOptionId=null;question.explanation=null;}
  var publicJson=JsonUtility.ToJson(publicCatalog);
  var loseAnswer=true;
  UnityWebRequest.Transport=(url,method,body)=> {
   if(url.EndsWith("/api/catalog")) return new UnityWebRequest.NetworkResponse {json=publicJson};
   if(url.EndsWith("/join")) return new UnityWebRequest.NetworkResponse {json="{\"token\":\"test-secret-token\",\"playerId\":\"online-self\",\"code\":\"ABC123\"}"};
   if(url.EndsWith("/attempts")) return new UnityWebRequest.NetworkResponse {json="{\"id\":\"online-attempt\",\"quizId\":\"mat-somas\",\"questionIndex\":0,\"totalQuestions\":4}"};
   if(url.EndsWith("/answers")) {
    if(loseAnswer) {loseAnswer=false; return new UnityWebRequest.NetworkResponse {result=UnityWebRequest.Result.ConnectionError,status=0,json=""};}
    return new UnityWebRequest.NetworkResponse {json="{\"correct\":true,\"correctOptionId\":\"b\",\"explanation\":\"2 + 3 = 5\",\"score\":100,\"answered\":1,\"total\":4,\"completed\":false}"};
   }
   return new UnityWebRequest.NetworkResponse();
  };
  var joined=false;
  attempt=null;
  Drain(session.Join("http://localhost:8787","abc123","Ana","dog",false,success=> {
   joined=success;
   Drain(session.BeginQuiz(quiz.id,value=>attempt=value));
  }));
  Assert(joined&&session.IsOnline&&attempt!=null,"join callback can start the next operation after unlock");
  Assert(string.IsNullOrEmpty(session.Catalog.FindQuiz(quiz.id).questions[0].correctOptionId),"online catalog contains no answer keys");
  Drain(session.Answer(first.id,first.correctOptionId,value=>result=value));
  Assert(result==null&&session.PendingOptionId==first.correctOptionId&&attempt.questionIndex==0,"lost answer response preserves selection and question");
  var beforeRetry=UnityWebRequest.RequestCount;
  var other=first.options.First(option=>option.id!=first.correctOptionId).id;
  Drain(session.Answer(first.id,other,value=>result=value));
  Assert(result==null&&UnityWebRequest.RequestCount==beforeRetry,"uncertain answer cannot switch selection on retry");
  Drain(session.Answer(first.id,first.correctOptionId,value=>result=value));
  Assert(result!=null&&attempt.score==100&&attempt.questionIndex==1&&session.PendingOptionId=="","same selection retry accepts server result exactly once");
  var lateCatalog=session.LoadCatalog();
  Assert(lateCatalog.MoveNext()&&lateCatalog.Current is IEnumerator,"online catalog refresh can wait for a response");
  var lateRequest=(IEnumerator)lateCatalog.Current;
  Drain(session.Leave(()=>session.JoinPractice("Ana","dog")));
  Assert(session.IsJoined&&!session.IsOnline,"leave callback can immediately join practice after unlock");
  Assert(!string.IsNullOrEmpty(session.Catalog.FindQuiz(quiz.id).questions[0].correctOptionId),"online leave restores practice answer keys");
  Drain(lateRequest);
  Drain(lateCatalog);
  Assert(!session.IsOnline&&!string.IsNullOrEmpty(session.Catalog.FindQuiz(quiz.id).questions[0].correctOptionId),"stale online catalog response cannot replace practice catalog");
  Drain(session.Leave());
  Drain(session.Join("http://localhost:8787","ABC123","Ana","dog",false,success=>joined=success));
  Drain(session.BeginQuiz(quiz.id,value=>attempt=value));
  var disconnected=0;
  session.ConnectionLost+=message=>disconnected++;
  typeof(GameSession).GetMethod("Disconnect",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(session,new object[]{"Conexão interrompida"});
  Assert(disconnected==1&&!session.IsJoined&&!session.IsOnline&&session.Players.Count==0&&session.CurrentAttempt==null,"disconnect clears session and attempt before notifying UI");
  Assert(session.Error=="Conexão interrompida"&&!string.IsNullOrEmpty(session.Catalog.FindQuiz(quiz.id).questions[0].correctOptionId),"disconnect preserves error and returns to practice catalog");
  Assert(!File.ReadAllText(path).Contains("test-secret-token"),"session bearer token never enters local progress");
  UnityWebRequest.Transport=null;
  return checks+" checks passed (Unity API stubs; offline core and simulated API lifecycle)";
 }
}
