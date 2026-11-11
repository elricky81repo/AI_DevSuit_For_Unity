using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ProjectIndexExporter : EditorWindow
{
    [Serializable]
    public class FileIndexEntry
    {
        public string path;
        public string fileName;
        public string namespaceName;
        public List<string> classes = new List<string>();
        public List<string> methods = new List<string>();
        public string summary;
    }

    [Serializable]
    public class FileIndexWrapper
    {
        public List<FileIndexEntry> files = new List<FileIndexEntry>();
    }

    [MenuItem("AI DevSuite Extras/Utils/AI Context/Export Project Index")]
    public static void ShowWindow()
    {
        GetWindow<ProjectIndexExporter>("Project Index");
    }

    private void OnGUI()
    {
        GUILayout.Label("Export Project Index", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "Generates a project_index.json file in the root of the Unity project with paths, classes, methods and a simple summary of each C# file.",
            MessageType.Info
        );

        GUILayout.Space(10);

        if (GUILayout.Button("Export project_index.json", GUILayout.Height(35)))
        {
            ExportProjectIndex();
        }
    }

    private static void ExportProjectIndex()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;

        string outputDir = Path.Combine(projectRoot, "Assets/AI-Generated");
        string outputPath = Path.Combine(outputDir, "project_index.json");

        // Crear carpeta si no existe
        Directory.CreateDirectory(outputDir);

        string[] allCsFiles = Directory.GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories);

        FileIndexWrapper wrapper = new FileIndexWrapper();

        foreach (string fullPath in allCsFiles)
        {
            if (ShouldSkipPath(fullPath))
                continue;

            string content;

            try
            {
                content = File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Could not read file: " + fullPath + "\n" + ex.Message);
                continue;
            }

            FileIndexEntry entry = new FileIndexEntry
            {
                path = MakeRelative(projectRoot, fullPath),
                fileName = Path.GetFileName(fullPath),
                namespaceName = ExtractNamespace(content),
                classes = ExtractMatches(content, @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)"),
                methods = ExtractMatches(
                    content,
                    @"\b(?:public|private|protected|internal)\s+(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:async\s+)?[A-Za-z_<>\[\],\s]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\("
                )
            };

            entry.summary = BuildSummary(entry);
            wrapper.files.Add(entry);
        }

        string json = JsonUtility.ToJson(wrapper, true);

        try
        {
            File.WriteAllText(outputPath, json, Encoding.UTF8);
            AssetDatabase.Refresh();
            Debug.Log("Project index exported to: " + outputPath);
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to export project index:\n" + outputPath + "\n" + ex);
        }
    }

    private static string ExtractNamespace(string content)
    {
        Match match = Regex.Match(content, @"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static List<string> ExtractMatches(string content, string pattern)
    {
        List<string> results = new List<string>();
        MatchCollection matches = Regex.Matches(content, pattern);

        foreach (Match match in matches)
        {
            if (match.Groups.Count < 2)
                continue;

            string value = match.Groups[1].Value;

            if (!string.IsNullOrWhiteSpace(value) && !results.Contains(value))
                results.Add(value);
        }

        return results;
    }

    private static string BuildSummary(FileIndexEntry entry)
    {
        StringBuilder sb = new StringBuilder();

        if (entry.classes.Count == 0)
        {
            sb.Append("C# file");
        }
        else if (entry.classes.Count == 1)
        {
            sb.Append("Contains class ").Append(entry.classes[0]);
        }
        else
        {
            sb.Append("Contains classes ").Append(string.Join(", ", entry.classes));
        }

        if (!string.IsNullOrEmpty(entry.namespaceName))
        {
            sb.Append(". Namespace: ").Append(entry.namespaceName);
        }

        if (entry.methods.Count > 0)
        {
            sb.Append(". Methods: ");

            int shownCount = Mathf.Min(5, entry.methods.Count);
            for (int i = 0; i < shownCount; i++)
            {
                if (i > 0)
                    sb.Append(", ");

                sb.Append(entry.methods[i]);
            }

            if (entry.methods.Count > shownCount)
            {
                sb.Append(", ...");
            }
        }

        sb.Append(".");
        return sb.ToString();
    }

    private static bool ShouldSkipPath(string fullPath)
    {
        string normalized = fullPath.Replace("\\", "/");

        return normalized.Contains("/Library/")
            || normalized.Contains("/Temp/")
            || normalized.Contains("/Logs/")
            || normalized.Contains("/obj/")
            || normalized.Contains("/Obj/")
            || normalized.Contains("/.git/")
            || normalized.Contains("/.vs/")
            || normalized.Contains("/.idea/")
            || normalized.Contains("/Packages/com.unity.");
    }

    private static string MakeRelative(string root, string fullPath)
    {
        return fullPath.Replace(root, "").TrimStart('\\', '/').Replace("\\", "/");
    }
}