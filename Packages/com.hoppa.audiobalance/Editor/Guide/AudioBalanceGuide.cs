using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Opens the bundled Audio Balance guide (Markdown) rendered as a styled HTML page in the
    /// user's default browser. The guide ships inside the package's <c>Documentation~</c> folder
    /// -- ignored by Unity's importer, but resolvable on disk -- so the button works in this
    /// project and in any project that consumes the package.
    /// </summary>
    public static class AudioBalanceGuide
    {
        // Package-relative; Path.GetFullPath resolves both embedded and registry packages.
        internal const string PackageGuidePath =
            "Packages/com.hoppa.audiobalance/Documentation~/audio-balance-guide.md";

        internal const string WindowTitle = "Audio Balance Guide";

        [MenuItem("Window/Hoppa/Audio Balance Guide")]
        public static void Open()
        {
            string md = LoadGuideMarkdown();
            if (md == null)
            {
                Debug.LogError("[AudioBalanceGuide] Guide markdown not found at " + PackageGuidePath);
                EditorUtility.DisplayDialog(WindowTitle,
                    "Could not find the guide file (audio-balance-guide.md).", "OK");
                return;
            }

            string html = MarkdownToHtml.Convert(md, "Hoppa Audio Balance — Guide");
            string outPath = Path.Combine(Path.GetTempPath(), "hoppa-audio-balance-guide.html");
            try
            {
                File.WriteAllText(outPath, html, new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // A locked or read-only TEMP is the realistic failure here, and it must not be a
                // silent no-op -- the user pressed a button and is owed an answer either way.
                Debug.LogError("[AudioBalanceGuide] Failed to write guide HTML: " + ex.Message);
                EditorUtility.DisplayDialog(WindowTitle, "Could not write the guide page.", "OK");
                return;
            }

            Application.OpenURL("file:///" + outPath.Replace('\\', '/'));
        }

        /// <summary>
        /// Reads the packaged guide, or returns null when it cannot be found or read. Split out
        /// from <see cref="Open"/> so the path resolution is testable without launching a browser.
        /// </summary>
        internal static string LoadGuideMarkdown()
        {
            try
            {
                string packaged = Path.GetFullPath(PackageGuidePath);
                return File.Exists(packaged) ? File.ReadAllText(packaged) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogError("[AudioBalanceGuide] Failed to read guide markdown: " + ex.Message);
                return null;
            }
        }
    }
}
