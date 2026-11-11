using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using System;

public class ProjectStructureExporter : EditorWindow
{
    [MenuItem("AI DevSuite Extras/Utils/AI Context/Export Project Tree")]
    public static void ShowWindow()
    {
        GetWindow<ProjectStructureExporter>("Project Tree");
    }

    private void OnGUI()
    {
        GUILayout.Label("Export Unity Project Structure", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "Generates a project_tree.txt file in the root of the Unity project, including folders and relevant files.",
            MessageType.Info
        );

        GUILayout.Space(10);

        if (GUILayout.Button("Export project_tree.txt", GUILayout.Height(35)))
        {
            ExportProjectTree();
        }
    }

    private static void ExportProjectTree()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;

        string outputDir = Path.Combine(projectRoot, "Assets/AI-Generated");
        string outputPath = Path.Combine(outputDir, "project_tree.txt");

        // Asegura que la carpeta exista
        Directory.CreateDirectory(outputDir);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine(Path.GetFileName(projectRoot) + "/");

        AddDirectoryRecursive(sb, projectRoot, 1);

        try
        {
            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
            Debug.Log("Project tree exported to: " + outputPath);
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to export project tree:\n" + outputPath + "\n" + ex);
        }
    }

    private static void AddDirectoryRecursive(StringBuilder sb, string currentPath, int indentLevel)
    {
        string[] directories;

        try
        {
            directories = Directory.GetDirectories(currentPath);
        }
        catch
        {
            return;
        }

        foreach (string dir in directories)
        {
            string dirName = Path.GetFileName(dir);

            if (ShouldSkipFolder(dirName))
                continue;

            sb.AppendLine(new string(' ', indentLevel * 2) + dirName + "/");
            AddDirectoryRecursive(sb, dir, indentLevel + 1);
        }

        string[] files;

        try
        {
            files = Directory.GetFiles(currentPath);
        }
        catch
        {
            return;
        }

        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);

            if (ShouldSkipFile(fileName))
                continue;

            sb.AppendLine(new string(' ', indentLevel * 2) + fileName);
        }
    }

    private static bool ShouldSkipFolder(string folderName)
    {
        switch (folderName)
        {
            case "Library":
            case "Temp":
            case "Logs":
            case "Obj":
            case "obj":
            case ".git":
            case ".vs":
            case ".idea":
            case "Build":
            case "Builds":
            case "UserSettings":
                return true;

            default:
                return false;
        }
    }

    private static bool ShouldSkipFile(string fileName)
    {
        if (fileName.EndsWith(".meta"))
            return true;

        string extension = Path.GetExtension(fileName).ToLower();

        switch (extension)
        {
            case ".cs":
            case ".json":
            case ".txt":
            case ".md":
            case ".shader":
            case ".cginc":
            case ".compute":
            case ".uxml":
            case ".uss":
            case ".asmdef":
            case ".asset":
            case ".prefab":
            case ".unity":
            case ".mat":
                return false;

            default:
                return true;
        }
    }
}