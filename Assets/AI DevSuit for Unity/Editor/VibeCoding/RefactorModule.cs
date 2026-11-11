using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Module for "Refactor" tab in VIBE Hub:
// - Lists GameObject scripts
// - Opens RefactorScriptPopUp
// - Opens AddFunctionWindow
public class RefactorModule : IEditorModule
{
    public string TabName => "Refactor";

    private GameObject _targetGO;
    public string objectComponentsPromp = "";

    public void Draw()
    {
        GUILayout.Label("Review and tune a script", EditorStyles.boldLabel);
        GUILayout.Space(8);

        EditorGUILayout.LabelField("Put the GameObject with the script", EditorStyles.boldLabel);
        _targetGO = (GameObject)EditorGUILayout.ObjectField("Target", _targetGO, typeof(GameObject), true);
        GUILayout.Space(10);

        if (_targetGO == null)
        {
            EditorGUILayout.HelpBox("Select a GameObject to view its scripts.", MessageType.Info);
            return;
        }

        // Get all MonoBehaviours (scripts)
        var monos = _targetGO.GetComponents<MonoBehaviour>();
        if (monos == null || monos.Length == 0)
        {
            EditorGUILayout.HelpBox("No user scripts attached.", MessageType.Warning);
            return;
        }

        // Get all components
        var comps = _targetGO.GetComponents<Component>();
        List<string> componentNames = new List<string>();

        foreach (var comp in comps)
        {
            if (comp == null) continue;
            componentNames.Add(comp.GetType().Name);
        }

        objectComponentsPromp =
            "This GameObject features the following additional components: "
            + string.Join("; ", componentNames);

        // =========================
        // SCRIPT LIST ONLY
        // =========================
        EditorGUILayout.LabelField("GameObject Scripts:", EditorStyles.boldLabel);
        GUILayout.Space(5);

        foreach (var mb in monos)
        {
            if (mb == null) continue;

            var ms = MonoScript.FromMonoBehaviour(mb);
            if (ms == null) continue;

            string path = AssetDatabase.GetAssetPath(ms);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                continue;

            string scriptName = Path.GetFileNameWithoutExtension(path);
            EditorGUILayout.LabelField("- " + scriptName);
        }

        GUILayout.Space(10);

        // =========================
        // ATTACHED COMPONENTS
        // =========================
        EditorGUILayout.LabelField("Attached Components:", EditorStyles.boldLabel);

        foreach (var comp in comps)
        {
            if (comp == null) continue;
            EditorGUILayout.LabelField("- " + comp.GetType().Name);
        }

        GUILayout.Space(12);

        // =========================
        // REVIEW & TUNE
        // =========================
        EditorGUILayout.LabelField("Review & tune:", EditorStyles.boldLabel);
        GUILayout.Space(5);

        foreach (var mb in monos)
        {
            if (mb == null) continue;

            var ms = MonoScript.FromMonoBehaviour(mb);
            if (ms == null) continue;

            string path = AssetDatabase.GetAssetPath(ms);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                continue;

            string scriptName = Path.GetFileNameWithoutExtension(path);

            Rect btnRect = GUILayoutUtility.GetRect(
                new GUIContent(scriptName),
                GUI.skin.button,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(22)
            );

            if (GUI.Button(btnRect, scriptName))
            {
                RefactorScriptPopUp.ShowWindow(path, objectComponentsPromp);
            }
        }

        GUILayout.Space(12);

        // =========================
        // ADD A FUNCTIONALITY
        // =========================
        EditorGUILayout.LabelField("Add a functionality:", EditorStyles.boldLabel);
        GUILayout.Space(5);

        foreach (var mb in monos)
        {
            if (mb == null) continue;

            var ms = MonoScript.FromMonoBehaviour(mb);
            if (ms == null) continue;

            string path = AssetDatabase.GetAssetPath(ms);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                continue;

            string scriptName = Path.GetFileNameWithoutExtension(path);

            Rect btnRect = GUILayoutUtility.GetRect(
                new GUIContent(scriptName),
                GUI.skin.button,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(22)
            );

            if (GUI.Button(btnRect, scriptName))
            {
                AddFunctionWindow.ShowWindow(path, objectComponentsPromp);
            }
        }
    }
}