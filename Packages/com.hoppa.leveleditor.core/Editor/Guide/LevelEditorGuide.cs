using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Opens the bundled level-editor guide (Markdown) rendered as a styled HTML page
    // in the user's default browser. The guide ships inside the package's
    // Documentation~ folder (ignored by Unity's importer but resolvable on disk), so
    // the button works in this project and in any project that consumes the package.
    public static class LevelEditorGuide
    {
        // Package-relative; Path.GetFullPath resolves both embedded and registry packages.
        private const string PackageGuidePath =
            "Packages/com.hoppa.leveleditor.core/Documentation~/level-editor-guide.md";

        [MenuItem("Window/Hoppa/Level Editor Guide")]
        public static void Open()
        {
            string md = LoadGuideMarkdown();
            if (md == null)
            {
                EditorUtility.DisplayDialog("Level Editor Guide",
                    "Could not find the guide file (level-editor-guide.md).", "OK");
                return;
            }

            string html = MarkdownToHtml.Convert(md, "Hoppa Level Editor — Guide");
            string outPath = Path.Combine(Path.GetTempPath(), "hoppa-level-editor-guide.html");
            try
            {
                File.WriteAllText(outPath, html, new UTF8Encoding(false));
            }
            catch (IOException ex)
            {
                Debug.LogError("[LevelEditorGuide] Failed to write guide HTML: " + ex.Message);
                EditorUtility.DisplayDialog("Level Editor Guide", "Could not write the guide page.", "OK");
                return;
            }

            Application.OpenURL("file:///" + outPath.Replace('\\', '/'));
        }

        private static string LoadGuideMarkdown()
        {
            string packaged = Path.GetFullPath(PackageGuidePath);
            if (File.Exists(packaged)) return File.ReadAllText(packaged);

            // Dev fallback: the repo-root docs copy.
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "LEVEL-EDITOR-BEGINNER-GUIDE.md"));
            if (File.Exists(repo)) return File.ReadAllText(repo);

            return null;
        }
    }
}
