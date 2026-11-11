using UnityEditor;
using UnityEngine;

public class AIDevSuiteInstructionsWindow : EditorWindow
{
    private const string PrefKeyInstructionsAlreadyShown = "AIDevSuite_InstructionsAlreadyShown";
    private const string DocumentationUrl = "https://www.notion.so/AI-DevSuite-Documentation-33530dc442ec80398e48c9138a10cacd?source=copy_link";
    private const string OpenAiUrl = "https://platform.openai.com/";
    private const string AnthropicUrl = "https://console.anthropic.com/";

    private static bool _hooksRegistered;
    private static bool _autoOpenCheckStarted;
    private static int _autoOpenStableFrames;

    private Vector2 _scroll;

    [MenuItem("Tools/AI DevSuite/Instructions")]
    public static void ShowWindow()
    {
        var window = GetWindow<AIDevSuiteInstructionsWindow>("AI DevSuite Instructions");
        window.minSize = new Vector2(700, 620);
        window.Show();
    }

    public static void ShowFromMonitor()
    {
        ShowWindow();
    }

    [InitializeOnLoadMethod]
    private static void AutoOpenOnFirstUse()
    {
        RegisterAutoOpenHooks();
        BeginAutoOpenCheck();
    }

    private static void RegisterAutoOpenHooks()
    {
        if (_hooksRegistered)
            return;

        _hooksRegistered = true;

        EditorApplication.projectChanged -= OnProjectChanged;
        EditorApplication.projectChanged += OnProjectChanged;

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnProjectChanged()
    {
        BeginAutoOpenCheck();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            BeginAutoOpenCheck();
        }
    }

    private static void BeginAutoOpenCheck()
    {
        if (EditorPrefs.GetBool(PrefKeyInstructionsAlreadyShown, false))
            return;

        _autoOpenStableFrames = 0;

        if (_autoOpenCheckStarted)
            return;

        _autoOpenCheckStarted = true;

        EditorApplication.update -= WaitForEditorReadyAndShow;
        EditorApplication.update += WaitForEditorReadyAndShow;
    }

    private static void WaitForEditorReadyAndShow()
    {
        if (EditorPrefs.GetBool(PrefKeyInstructionsAlreadyShown, false))
        {
            StopAutoOpenCheck();
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            _autoOpenStableFrames = 0;
            return;
        }

        _autoOpenStableFrames++;

        if (_autoOpenStableFrames < 10)
            return;

        StopAutoOpenCheck();

        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(PrefKeyInstructionsAlreadyShown, false))
                return;

            ShowWindow();
            EditorPrefs.SetBool(PrefKeyInstructionsAlreadyShown, true);
        };
    }

    private static void StopAutoOpenCheck()
    {
        EditorApplication.update -= WaitForEditorReadyAndShow;
        _autoOpenCheckStarted = false;
        _autoOpenStableFrames = 0;
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        GUILayout.Space(8);

        DrawHero();
        GUILayout.Space(10);

        DrawQuickStart();
        GUILayout.Space(10);

        DrawProviderSection();
        GUILayout.Space(10);

        DrawSetupSection();
        GUILayout.Space(10);

        DrawTryToolsSection();
        GUILayout.Space(10);

        DrawTipsSection();
        GUILayout.Space(10);

        DrawDocsSection();
        GUILayout.Space(16);

        EditorGUILayout.EndScrollView();
    }

    private void DrawHero()
    {
        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.22f, 0.55f, 0.95f);

        EditorGUILayout.BeginVertical("box");
        GUI.backgroundColor = oldColor;

        GUILayout.Space(4);

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.fontSize = 18;
        titleStyle.wordWrap = true;

        GUIStyle bodyStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
        bodyStyle.fontSize = 12;

        EditorGUILayout.LabelField("🚀 AI DevSuite – Quick Start", titleStyle);
        GUILayout.Space(4);
        EditorGUILayout.LabelField(
            "Get up and running with AI DevSuite in just a few minutes. " +
            "Before using the tools, you must connect at least one supported AI provider and add your API key in Setup.",
            bodyStyle
        );

        GUILayout.Space(8);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Open Setup Window", GUILayout.Height(30)))
        {
            SetupWindow.ShowWindow();
        }

        if (GUILayout.Button("Open Documentation", GUILayout.Height(30)))
        {
            Application.OpenURL(DocumentationUrl);
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4);
        EditorGUILayout.EndVertical();
    }

    private void DrawQuickStart()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("1. Quick Start", EditorStyles.boldLabel);
        GUILayout.Space(6);

        EditorGUILayout.LabelField(
            "To use this asset, follow these basic steps:",
            EditorStyles.wordWrappedLabel
        );

        GUILayout.Space(6);
        EditorGUILayout.LabelField("• Create an account with OpenAI or Anthropic", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Generate an API key from your provider dashboard", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Open the Setup Window inside Unity", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Paste your API key there", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Choose the provider/model you want to use", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Start using the tools", EditorStyles.wordWrappedLabel);

        GUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Important: API usage costs are NOT included in this asset. " +
            "Costs depend on the provider and the model you choose.",
            MessageType.Warning
        );

        EditorGUILayout.EndVertical();
    }

    private void DrawProviderSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("2. Supported Providers", EditorStyles.boldLabel);
        GUILayout.Space(6);

        EditorGUILayout.LabelField("Supported providers:", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• OpenAI", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Anthropic", EditorStyles.wordWrappedLabel);

        GUILayout.Space(8);

        EditorGUILayout.HelpBox(
            "Provider usage:\n" +
            "• Graphics, image and audio tools → require OpenAI\n" +
            "• Vibe Coding and script tools → work with OpenAI or Anthropic",
            MessageType.Info
        );

        GUILayout.Space(6);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Go to OpenAI", GUILayout.Height(28)))
        {
            Application.OpenURL(OpenAiUrl);
        }

        if (GUILayout.Button("Go to Anthropic", GUILayout.Height(28)))
        {
            Application.OpenURL(AnthropicUrl);
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6);

        EditorGUILayout.HelpBox(
            "Keep your API key private. Do not share it with other people or publish it inside your project.",
            MessageType.Warning
        );

        EditorGUILayout.EndVertical();
    }

    private void DrawSetupSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("3. Setup in Unity", EditorStyles.boldLabel);
        GUILayout.Space(6);

        EditorGUILayout.LabelField(
            "Once you have your provider account and API key, open the Setup Window and paste your credentials there.",
            EditorStyles.wordWrappedLabel
        );

        GUILayout.Space(6);
        EditorGUILayout.LabelField("Recommended flow:", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Select your provider", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Paste the API key", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Choose a model", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• You are ready!", EditorStyles.wordWrappedLabel);

        GUILayout.Space(8);

        if (GUILayout.Button("Open Setup Window", GUILayout.Height(32)))
        {
            SetupWindow.ShowWindow();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawTryToolsSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("4. Try the Main Tools", EditorStyles.boldLabel);
        GUILayout.Space(6);

        EditorGUILayout.LabelField("💻 Vibe Coding", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Use it to generate scripts, refactor code, explain errors, and speed up development workflows.",
            EditorStyles.wordWrappedLabel
        );

        GUILayout.Space(4);

        if (GUILayout.Button("Open Vibe Coding", GUILayout.Height(28)))
        {
            EditorApplication.ExecuteMenuItem("AI DevSuite/Vibe Coding");
        }

        GUILayout.Space(8);

        EditorGUILayout.LabelField("🛠 Error Explainer", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Open the Error Explainer to read compiler/runtime errors in a clearer way and use Fast Fix when available.",
            EditorStyles.wordWrappedLabel
        );

        GUILayout.Space(4);

        if (GUILayout.Button("Open Error Explainer", GUILayout.Height(28)))
        {
            EditorApplication.ExecuteMenuItem("AI DevSuite/Errors Explainer");
        }

        GUILayout.Space(8);

        EditorGUILayout.LabelField("🎨 AI Graphics Tools", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Generate textures, characters, UI assets and more. These tools require OpenAI.",
            EditorStyles.wordWrappedLabel
        );

        GUILayout.Space(4);

        if (GUILayout.Button("Open AI Graphics Tools", GUILayout.Height(28)))
        {
            EditorApplication.ExecuteMenuItem("AI DevSuite/Graphic Tools");
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawTipsSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("5. Tips", EditorStyles.boldLabel);
        GUILayout.Space(6);

        EditorGUILayout.LabelField("• Start with small, simple prompts", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Be specific about what you want", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Try cheaper models first if you want to control costs", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• For image/audio tools, make sure OpenAI is configured", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("• Combine tools for better results", EditorStyles.wordWrappedLabel);

        EditorGUILayout.EndVertical();
    }

    private void DrawDocsSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("6. Need More Help?", EditorStyles.boldLabel);
        GUILayout.Space(6);

        EditorGUILayout.LabelField(
            "Open the full documentation for a deeper guide to all tools and workflows.",
            EditorStyles.wordWrappedLabel
        );

        GUILayout.Space(8);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Open Documentation", GUILayout.Height(30)))
        {
            Application.OpenURL(DocumentationUrl);
        }

        if (GUILayout.Button("Open Setup Again", GUILayout.Height(30)))
        {
            SetupWindow.ShowWindow();
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(8);

        GUIStyle footerStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
        footerStyle.wordWrap = true;

        EditorGUILayout.LabelField(
            "This window opens automatically only the first time. " +
            "After that, you can reopen it manually from AI DevSuite/Instructions or from your monitor window.",
            footerStyle
        );

        EditorGUILayout.EndVertical();
    }
}