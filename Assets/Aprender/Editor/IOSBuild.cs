using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Aprender.Editor
{
    /// <summary>iPhone configuration and Xcode export; does not install SDKs or sign an app.</summary>
    public static class IOSBuild
    {
        public const string ExportPath = "Builds/iOS";
        const string DefaultIdentifier = "com.ilhadosaber.app";
        static readonly string[] LocalHttpRanges = { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" };

        [MenuItem("Ilha do Saber/iOS/Preparar iPhone")]
        public static void PrepareIPhone()
        {
            EnsureIdle();
            LearningProjectSetup.PrepareProject();
            ApplyPlayerSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("iPhone preparado: iOS 15+, paisagem, toque e teclado nativo. "
                + "A configuração não instala módulos. Para exportar, adicione iOS Build Support "
                + "à Unity " + Application.unityVersion + " no Unity Hub.");
        }

        [MenuItem("Ilha do Saber/iOS/Exportar projeto Xcode")]
        public static void ExportXcode()
        {
            EnsureIdle();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
                throw new BuildFailedException("O módulo iOS Build Support não está instalado para Unity "
                    + Application.unityVersion + ". No Unity Hub, abra Installs > engrenagem desta versão > Add modules "
                    + "e instale iOS Build Support. Para compilar e instalar no iPhone, instale também Xcode. "
                    + "A prática e a prévia do layout continuam disponíveis no Editor.");
            PrepareIPhone();
            Directory.CreateDirectory(ExportPath);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { LearningProjectSetup.ScenePath },
                locationPathName = ExportPath,
                target = BuildTarget.iOS,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("A exportação iOS falhou. Veja o Console e o relatório de build.");
            Debug.Log("Projeto Xcode exportado em " + Path.GetFullPath(ExportPath)
                + ". Abra Unity-iPhone.xcodeproj, escolha sua equipe em Signing & Capabilities "
                + "e selecione o iPhone conectado. Este comando não cria um IPA assinado.");
        }

        [MenuItem("Ilha do Saber/iOS/Preparar iPhone", true)]
        [MenuItem("Ilha do Saber/iOS/Exportar projeto Xcode", true)]
        static bool CanPrepare() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

        static void EnsureIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new BuildFailedException("Saia do modo Play e aguarde a compilação antes de preparar ou exportar o iPhone.");
        }

        internal static bool IsLearningBuild(BuildReport report) => report.summary.platform == BuildTarget.iOS
            && EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == LearningProjectSetup.ScenePath);

        internal static void ApplyPlayerSettings()
        {
            var identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
            if (string.IsNullOrWhiteSpace(identifier)
                || identifier.IndexOf("DefaultCompany", StringComparison.OrdinalIgnoreCase) >= 0)
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, DefaultIdentifier);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.applicationDisplayName = "Ilha do Saber";
            if (string.IsNullOrWhiteSpace(PlayerSettings.iOS.buildNumber) || PlayerSettings.iOS.buildNumber == "0")
                PlayerSettings.iOS.buildNumber = "1";
            PlayerSettings.iOS.requiresFullScreen = true;
            // Keep the user's existing team and provisioning configuration.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        }

        // Uses framework XML instead of UnityEditor.iOS.Xcode, so this editor file
        // compiles even before iOS Build Support has been installed.
        public static void ConfigureInfoPlist(string plistPath)
        {
            var plist = XDocument.Load(plistPath);
            var values = plist.Root?.Element("dict")
                ?? throw new BuildFailedException("Info.plist iOS não contém o dicionário esperado: " + plistPath);
            Set(values, "NSLocalNetworkUsageDescription", new XElement("string",
                "Usamos a rede local para conectar o iPhone à turma e ao servidor da escola."));
            var ats = Dictionary(values, "NSAppTransportSecurity");
            // Unity's general HTTP option can emit this broad exception; keep HTTP
            // limited to the school LAN instead of disabling ATS for the Internet.
            Remove(ats, "NSAllowsArbitraryLoads");
            Set(ats, "NSAllowsLocalNetworking", new XElement("true"));
            var domains = Dictionary(ats, "NSExceptionDomains");
            // Apple accepts IP/CIDR exceptions starting with iOS 17. Earlier iOS
            // versions already exempt IP connections from ATS; LAN privacy still applies.
            foreach (var range in LocalHttpRanges)
                Set(Dictionary(domains, range), "NSExceptionAllowsInsecureHTTPLoads", new XElement("true"));
            // Mono loads an absent DTD subset as an empty string, then writes [];
            // Apple's plist parser rejects that form. Preserve the standard DTD
            // identifiers with an explicitly absent internal subset.
            var type = plist.DocumentType;
            if (type != null) type.ReplaceWith(new XDocumentType(type.Name, type.PublicId, type.SystemId, null));
            plist.Save(plistPath);
        }

        static XElement Dictionary(XElement parent, string key)
        {
            var value = Get(parent, key);
            if (value != null && value.Name == "dict") return value;
            var dictionary = new XElement("dict");
            Set(parent, key, dictionary);
            return dictionary;
        }

        static XElement Get(XElement dictionary, string key)
        {
            var item = dictionary.Elements("key").FirstOrDefault(element => element.Value == key);
            return item?.ElementsAfterSelf().FirstOrDefault();
        }

        static void Set(XElement dictionary, string key, XElement value)
        {
            var item = dictionary.Elements("key").FirstOrDefault(element => element.Value == key);
            if (item == null) dictionary.Add(new XElement("key", key), value);
            else
            {
                var previous = item.ElementsAfterSelf().FirstOrDefault();
                if (previous != null) previous.ReplaceWith(value);
                else item.AddAfterSelf(value);
            }
        }

        static void Remove(XElement dictionary, string key)
        {
            var item = dictionary.Elements("key").FirstOrDefault(element => element.Value == key);
            if (item == null) return;
            item.ElementsAfterSelf().FirstOrDefault()?.Remove();
            item.Remove();
        }
    }

    public sealed class IPhoneBuildPreparation : IPreprocessBuildWithReport
    {
        // Runs after LearningBuildPreparation (-100), which prepares common settings.
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (IOSBuild.IsLearningBuild(report)) IOSBuild.ApplyPlayerSettings();
        }
    }

    public sealed class IPhoneBuildPostprocess : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;
        public void OnPostprocessBuild(BuildReport report)
        {
            if (!IOSBuild.IsLearningBuild(report)) return;
            var path = Path.Combine(report.summary.outputPath, "Info.plist");
            if (!File.Exists(path)) throw new BuildFailedException("Info.plist não foi gerado em " + path);
            IOSBuild.ConfigureInfoPlist(path);
        }
    }
}
