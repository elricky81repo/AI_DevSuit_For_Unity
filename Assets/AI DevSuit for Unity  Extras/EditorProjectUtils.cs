using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System;
using System.IO;

public class EditorProjectUtils : EditorWindow
{
    private Vector2 scroll;

    [MenuItem("Tools/Project Utils")]
    public static void ShowWindow()
    {
        GetWindow<EditorProjectUtils>("Project Utils");
    }

    private void OnGUI()
    {
        GUILayout.Space(5);

        if (GUILayout.Button("Make Backup Scripts", GUILayout.Height(30)))
        {
            MakeScriptsBackup();
        }

        GUILayout.Space(10);
        GUILayout.Label("Scenes in Project", EditorStyles.boldLabel);
        GUILayout.Space(5);

        var guids = AssetDatabase.FindAssets("t:Scene");

        if (guids.Length == 0)
        {
            EditorGUILayout.HelpBox("No scenes found in project", MessageType.Warning);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string sceneName = Path.GetFileNameWithoutExtension(path);

            EditorGUILayout.BeginHorizontal("box");

            GUILayout.Label(sceneName, GUILayout.Width(200));

            if (GUILayout.Button("Open", GUILayout.Width(60)))
            {
                if (CheckSaveCurrentScene())
                {
                    EditorSceneManager.OpenScene(path);
                }
            }

            if (GUILayout.Button("Play", GUILayout.Width(60)))
            {
                if (CheckSaveCurrentScene())
                {
                    EditorSceneManager.OpenScene(path);
                    EditorApplication.isPlaying = true;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private bool CheckSaveCurrentScene()
    {
        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        return true;
    }

    private void MakeScriptsBackup()
    {
        string sourceScriptsPath = Path.Combine(Application.dataPath, "AI DevSuit for Unity");

        if (!Directory.Exists(sourceScriptsPath))
        {
            Debug.LogError("No existe la carpeta Assets/Scripts");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string backupRoot = Path.Combine(projectRoot, "Backup");
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // Carpeta única del backup
        string backupFolder = Path.Combine(backupRoot, $"ScriptsBackup_{timestamp}");

        // Adentro de esa carpeta, copiamos la carpeta Scripts completa
        string destinationScriptsPath = Path.Combine(backupFolder, "Scripts");

        try
        {
            Directory.CreateDirectory(backupFolder);
            CopyDirectory(sourceScriptsPath, destinationScriptsPath);

            Debug.Log("Backup creado correctamente en: " + backupFolder);
            EditorUtility.RevealInFinder(backupFolder);
        }
        catch (Exception e)
        {
            Debug.LogError("Error al hacer backup: " + e);
        }
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (string filePath in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(filePath);
            string destinationFilePath = Path.Combine(destinationDir, fileName);
            File.Copy(filePath, destinationFilePath, true);
        }

        foreach (string subDirPath in Directory.GetDirectories(sourceDir))
        {
            string folderName = Path.GetFileName(subDirPath);
            string destinationSubDirPath = Path.Combine(destinationDir, folderName);
            CopyDirectory(subDirPath, destinationSubDirPath);
        }
    }
}