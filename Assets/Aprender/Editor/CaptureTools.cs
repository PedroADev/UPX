using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Aprender.Editor
{
    /// <summary>Captures the rendered game, including UI Toolkit, without Editor panels.</summary>
    public static class CaptureTools
    {
        [MenuItem("Ilha do Saber/Capturas/Capturar Game View")]
        public static void CaptureGameView()
        {
            CaptureNamed("jogo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        }

        [MenuItem("Ilha do Saber/Capturas/Capturar Game View", true)]
        static bool CanCapture() => EditorApplication.isPlaying && !EditorApplication.isPaused;

        public static string CaptureNamed(string name)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Inicie o jogo e retire a pausa antes de capturar a Game View.");
            if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,79}\z"))
                throw new ArgumentException("Use um nome de 1 a 80 letras, números, traços ou sublinhados, sem extensão.", nameof(name));

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var directory = Path.GetFullPath(Path.Combine(projectRoot, "docs", "screenshots"));
            Directory.CreateDirectory(directory);
            var output = Path.GetFullPath(Path.Combine(directory, name + ".png"));
            if (!output.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("O caminho da captura deve permanecer na pasta docs/screenshots.", nameof(name));
            if (File.Exists(output))
                throw new IOException("Já existe uma captura com este nome: " + output);

            // Unity writes the PNG after the frame has rendered, including UIDocument overlays.
            ScreenCapture.CaptureScreenshot(output, 2);
            Debug.Log("Captura da Game View solicitada: " + output);
            return output;
        }
    }
}
