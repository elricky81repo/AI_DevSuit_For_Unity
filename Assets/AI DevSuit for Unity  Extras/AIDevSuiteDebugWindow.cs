using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class AIDevSuiteDebugWindow : EditorWindow
{
    private Vector2 _scroll;

    private static readonly string[] ExplicitEditorPrefsKeys =
    {
        "GPTPlugin_ApiKey",
        "GPTPlugin_OrgKey",
        "GPTPlugin_Model",
        "GPTPlugin_Provider",

        "GPTPlugin_ScriptReady",
        "GPTPlugin_GeneratedClassName",
        "GPTPlugin_CompileSuccess",
        "GPTPlugin_CompileErrors",
        "GPTPlugin_Prompt",

        "AI_PROVIDER",
        "ANTHROPIC_API_KEY",
        "ANTHROPIC_MODEL",
        "GPTPlugin_TranscriptionLanguage",

        "VibeCoding_LastProvider",
        "VibeCoding_LastModel",
        "AIDevSuite_OnboardingDone",
        "AIDevSuite_FirstRunCompleted",
        "AIDevSuite_InstructionsAlreadyShown",
        "AIDevSuite_SelectedProvider",
        "AIDevSuite_SelectedModel",
        "OpenAI_ApiKey",
        "Anthropic_ApiKey",
        "MaterialAssigner_LastTab",
        "CopilotWindow_Mode",
        "CopilotWindow_Input",
        "ErrorConsoleExplainer_SelectedLog",
        "MultiScriptRefactor_LastPath",
        "ScriptGenerator_LastPrompt",
        "TextureGenerator_LastPrompt",
        "CharacterGenerator_LastPrompt",
        "UIGenerator_LastPrompt",
        "ObjectGenerator_LastPrompt",
        "SetupWindow_LastProvider"
    };

    private static readonly string[] SessionStateKeys =
    {
        "VibeCoding_IsGenerating",
        "VibeCoding_LastPrompt",
        "CopilotWindow_Input",
        "CopilotWindow_Mode",
        "ErrorConsoleExplainer_SelectedLog",
        "MaterialAssigner_IsGenerating",
        "MultiScriptRefactor_Running",
        "SetupWindow_IsInitialized",
        "GPTPlugin_TargetID"
    };

    private bool _resetEditorPrefs = true;
    private bool _resetSessionState = true;
    private bool _deleteLibraryDebugFolder = true;

    private string _lastResult = "";
    private MessageType _lastResultType = MessageType.Info;

    [MenuItem("AI DevSuite Extras/Debug")]
    public static void Open()
    {
        var window = GetWindow<AIDevSuiteDebugWindow>("AI DevSuit Debug");
        window.minSize = new Vector2(520, 420);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("AI DevSuit Debug", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        EditorGUILayout.HelpBox(
            "This window resets local plugin data to simulate a clean first run.",
            MessageType.Info);

        EditorGUILayout.Space(8);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("Reset options", EditorStyles.boldLabel);
        _resetEditorPrefs = EditorGUILayout.ToggleLeft("Reset plugin EditorPrefs", _resetEditorPrefs);
        _resetSessionState = EditorGUILayout.ToggleLeft("Reset plugin SessionState", _resetSessionState);
        _deleteLibraryDebugFolder = EditorGUILayout.ToggleLeft("Delete Library debug/cache folder", _deleteLibraryDebugFolder);

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("EditorPrefs keys to remove", EditorStyles.boldLabel);
        DrawReadonlyList(ExplicitEditorPrefsKeys);

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("SessionState keys to clear", EditorStyles.boldLabel);
        DrawReadonlyList(SessionStateKeys);

        EditorGUILayout.Space(14);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);

            if (GUILayout.Button("RESET ALL SELECTED PLUGIN DATA", GUILayout.Height(34)))
            {
                RunReset();
                OpenaiManager.ClearCachedCredentials();
            }

            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Open Library Debug Folder", GUILayout.Height(24)))
            {
                OpenLibraryDebugFolder();
            }
        }

        EditorGUILayout.Space(12);

        if (!string.IsNullOrEmpty(_lastResult))
        {
            EditorGUILayout.HelpBox(_lastResult, _lastResultType);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawReadonlyList(string[] items)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            if (items == null || items.Length == 0)
            {
                EditorGUILayout.LabelField("(empty)");
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                EditorGUILayout.SelectableLabel(items[i], EditorStyles.textField, GUILayout.Height(18));
            }
        }
    }

    private void RunReset()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Reset AI DevSuit data",
            "This will remove local plugin settings and temporary state to simulate a first run.\n\nContinue?",
            "Yes, reset",
            "Cancel");

        if (!confirmed)
            return;

        int deletedEditorPrefsCount = 0;
        int deletedSessionCount = 0;
        bool libraryFolderDeleted = false;

        try
        {
            if (_resetEditorPrefs)
            {
                deletedEditorPrefsCount = DeletePluginEditorPrefs();
            }

            if (_resetSessionState)
            {
                deletedSessionCount = ClearPluginSessionState();
            }

            if (_deleteLibraryDebugFolder)
            {
                libraryFolderDeleted = DeleteLibraryDebugFolder();
            }

            AssetDatabase.Refresh();

            _lastResultType = MessageType.Info;
            _lastResult =
                "Reset completed.\n\n" +
                "- EditorPrefs removed: " + deletedEditorPrefsCount + "\n" +
                "- SessionState cleared: " + deletedSessionCount + "\n" +
                "- Library debug folder deleted: " + (libraryFolderDeleted ? "Yes" : "No");

            Debug.Log("[AI DevSuit Debug] Reset completed.");
            Debug.Log("[AI DevSuit Debug] EditorPrefs removed: " + deletedEditorPrefsCount);
            Debug.Log("[AI DevSuit Debug] SessionState cleared: " + deletedSessionCount);
            Debug.Log("[AI DevSuit Debug] Library debug folder deleted: " + libraryFolderDeleted);
        }
        catch (Exception ex)
        {
            _lastResultType = MessageType.Error;
            _lastResult = "Reset failed:\n" + ex.Message;
            Debug.LogError("[AI DevSuit Debug] Reset failed: " + ex);
        }
    }

    private int DeletePluginEditorPrefs()
    {
        int count = 0;

        foreach (string key in ExplicitEditorPrefsKeys)
        {
            if (EditorPrefs.HasKey(key))
            {
                EditorPrefs.DeleteKey(key);
                count++;
            }
        }

        Debug.LogWarning("[AI DevSuit Debug] Only explicit EditorPrefs keys were removed. Prefix-based deletion is disabled for compatibility.");

        return count;
    }

    private int ClearPluginSessionState()
    {
        int count = 0;

        foreach (string key in SessionStateKeys)
        {
            SessionState.EraseBool(key);
            SessionState.EraseInt(key);
            SessionState.EraseFloat(key);
            SessionState.EraseString(key);
            count++;
        }

        return count;
    }

    private bool DeleteLibraryDebugFolder()
    {
        string folder = GetLibraryDebugFolderPath();

        if (!Directory.Exists(folder))
            return false;

        try
        {
            Directory.Delete(folder, true);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AI DevSuit Debug] Could not delete Library debug folder: " + ex.Message);
            return false;
        }
    }

    private void OpenLibraryDebugFolder()
    {
        string folder = GetLibraryDebugFolderPath();

        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        EditorUtility.RevealInFinder(folder);
    }

    private string GetLibraryDebugFolderPath()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, "Library", "AIDevSuitDebug");
    }
}