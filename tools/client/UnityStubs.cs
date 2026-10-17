using System;
using System.Collections;
using System.IO;
using System.Text.Json;
namespace UnityEngine {
 public class MonoBehaviour { public Coroutine StartCoroutine(IEnumerator routine) { return new Coroutine(); } }
 public class Coroutine {}
 public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) {this.x=x;this.y=y;this.z=z;} }
 public static class Mathf { public static float Clamp(float v,float a,float b)=>Math.Min(b,Math.Max(a,v)); public static float Repeat(float t,float length)=>t-(float)Math.Floor(t/length)*length; }
 public static class Time { public static float realtimeSinceStartup=0; }
 public static class Application { public static string persistentDataPath; }
 public static class Debug { public static void LogWarning(object value) { Console.WriteLine(value); } }
 public class TextAsset { public string text; public TextAsset(string t) {text=t;} }
 public static class Resources { public static string CatalogJson; public static T Load<T>(string path) where T:class { return new TextAsset(CatalogJson) as T; } }
 public static class JsonUtility {
  static readonly JsonSerializerOptions Options = new JsonSerializerOptions {IncludeFields=true};
  public static T FromJson<T>(string json) {return JsonSerializer.Deserialize<T>(json,Options);}
  public static string ToJson(object value,bool pretty=false) {return JsonSerializer.Serialize(value,value.GetType(),Options);}
 }
}
namespace UnityEngine.Networking {
 public class DownloadHandler { public string text=""; }
 public class DownloadHandlerBuffer:DownloadHandler {}
 public class UploadHandlerRaw { public byte[] data; public UploadHandlerRaw(byte[] data) {this.data=data;} }
 public class UnityWebRequest:IDisposable {
  public enum Result {InProgress,Success,ConnectionError,ProtocolError,DataProcessingError}
  public class NetworkResponse {public Result result=Result.Success; public string json="{}"; public long status=200;}
  public static Func<string,string,string,NetworkResponse> Transport;
  public static int RequestCount;
  public Result result; public int timeout; public long responseCode; public DownloadHandler downloadHandler; public UploadHandlerRaw uploadHandler;
  readonly string url; readonly string method;
  public UnityWebRequest(string url,string method) {this.url=url;this.method=method;}
  public void SetRequestHeader(string key,string value) {}
  public IEnumerator SendWebRequest() {
   RequestCount++;
   var response=Transport?.Invoke(url,method,uploadHandler==null ? "" : System.Text.Encoding.UTF8.GetString(uploadHandler.data));
   if(response!=null) {result=response.result; responseCode=response.status; downloadHandler.text=response.json;}
   yield break;
  }
  public void Abort() {}
  public void Dispose() {}
 }
}
