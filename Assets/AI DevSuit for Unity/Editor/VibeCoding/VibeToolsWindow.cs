using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class VibeToolsWindow : EditorWindow
{
    private const string MODEL_PREF = "GPTPlugin_Model";
    private const string AI_PROVIDER_PREF = "AI_PROVIDER";
    private const string ANTHROPIC_MODEL_PREF = "ANTHROPIC_MODEL";
    private const string ANTHROPIC_API_KEY_PREF = "ANTHROPIC_API_KEY";

    private Vector2 scrollPos;
    private GptModel _modelEnum;

    private enum AnthropicModel
    {
        ClaudeHaiku45,
        ClaudeSonnet45,
        ClaudeSonnet46
    }

    public static class GPTPrefs
    {
        public const string API_KEY = "GPTPlugin_ApiKey";
        public const string ORG_KEY = "GPTPlugin_OrgKey";

        public static string ApiKey
        {
            get => EditorPrefs.GetString(API_KEY, "");
            set => EditorPrefs.SetString(API_KEY, value);
        }

        public static string OrgKey
        {
            get => EditorPrefs.GetString(ORG_KEY, "");
            set => EditorPrefs.SetString(ORG_KEY, value);
        }
    }

    private IEditorModule[] _modules;
    private int _tab;

    [MenuItem("Tools/AI DevSuite/Vibe Coding")]
    public static new void Show()
    {
        GetWindow<VibeToolsWindow>("Vibe Coding");
    }

    private void OnEnable()
    {
        _modules = new IEditorModule[]
        {
            new GeneralModule(),
            new ScriptGeneratorModule(),
            new RefactorModule()
        };
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        _modelEnum = LoadSavedModel();

        if (_modules == null || _modules.Length == 0)
        {
            EditorGUILayout.HelpBox("No modules loaded.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        bool providerReady = HasApiKeyForProvider(GetCurrentProvider());

        DrawGlobalProviderWarningBanner();
        GUILayout.Space(6);

        DrawGlobalAIHeader();

        GUILayout.Space(6);

        using (new EditorGUI.DisabledScope(!providerReady))
        {
            _tab = GUILayout.Toolbar(_tab, _modules.Select(m => m.TabName).ToArray());
            EditorGUILayout.Space(4);

            if (_tab >= 0 && _tab < _modules.Length)
                _modules[_tab].Draw();
        }

        if (!providerReady)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "All modules are disabled until the selected AI provider has a valid API key configured in Setup.",
                MessageType.Warning
            );
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawGlobalProviderWarningBanner()
    {
        AIManager.AIProviderType provider = GetCurrentProvider();

        bool hasKey = HasApiKeyForProvider(provider);
        if (hasKey)
            return;

        string title;
        string body;

        if (provider == AIManager.AIProviderType.OpenAI)
        {
            title = "OPENAI API KEY NOT SET";
            body = "OpenAI is currently selected, but no API key is configured. You must complete Setup before using any module.";
        }
        else if (provider == AIManager.AIProviderType.Anthropic)
        {
            title = "ANTHROPIC API KEY NOT SET";
            body = "Anthropic is currently selected, but no API key is configured. You must complete Setup before using any module.";
        }
        else
        {
            title = "AI PROVIDER NOT READY";
            body = "The selected AI provider is not configured yet. Open Setup and complete the credentials first.";
        }

        Color previousBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.95f, 0.35f, 0.35f);

        EditorGUILayout.BeginVertical("box");
        GUILayout.Space(2);

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.normal.textColor = Color.white;
        titleStyle.fontSize = 12;

        GUIStyle bodyStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
        bodyStyle.normal.textColor = Color.white;

        EditorGUILayout.LabelField("⚠ " + title, titleStyle);
        GUILayout.Space(2);
        EditorGUILayout.LabelField(body, bodyStyle);
        GUILayout.Space(6);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Open Setup", GUILayout.Height(26)))
        {
            SetupWindow.ShowWindow();
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(2);
        EditorGUILayout.EndVertical();

        GUI.backgroundColor = previousBg;
    }

    private bool HasApiKeyForProvider(AIManager.AIProviderType provider)
    {
        if (provider == AIManager.AIProviderType.OpenAI)
        {
            return !string.IsNullOrEmpty(GPTPrefs.ApiKey);
        }

        if (provider == AIManager.AIProviderType.Anthropic)
        {
            string anthropicKey = EditorPrefs.GetString(ANTHROPIC_API_KEY_PREF, "");
            return !string.IsNullOrEmpty(anthropicKey);
        }

        return false;
    }

    private void DrawGlobalAIHeader()
    {
        AIManager.AIProviderType provider = GetCurrentProvider();

        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.LabelField("AI Engine", EditorStyles.boldLabel);
        GUILayout.Space(4);

        if (provider == AIManager.AIProviderType.OpenAI)
        {
            EditorGUILayout.LabelField("✔ OpenAI selected");

            var newModel = (GptModel)EditorGUILayout.EnumPopup("Model", _modelEnum);
            if (newModel != _modelEnum)
            {
                _modelEnum = newModel;
                SaveModelPref(_modelEnum);
            }

            if (GUILayout.Button("See OpenAI Pricing", GUILayout.Height(24)))
            {
                Application.OpenURL("https://platform.openai.com/pricing");
            }
        }
        else if (provider == AIManager.AIProviderType.Anthropic)
        {
            EditorGUILayout.LabelField("✔ Anthropic selected");

            string saved = EditorPrefs.GetString(ANTHROPIC_MODEL_PREF, AnthropicModel.ClaudeHaiku45.ToString());

            AnthropicModel currentModel = ParseAnthropicModel(saved);

            AnthropicModel newModel =
                (AnthropicModel)EditorGUILayout.EnumPopup("Model", currentModel);

            if (newModel != currentModel)
            {
                EditorPrefs.SetString(ANTHROPIC_MODEL_PREF, newModel.ToString());
            }

            if (GUILayout.Button("See Anthropic Pricing", GUILayout.Height(24)))
            {
                Application.OpenURL("https://claude.com/pricing#api");
            }
        }
        else
        {
            EditorGUILayout.LabelField("Unknown provider");
        }

        EditorGUILayout.EndVertical();
    }

    private static AIManager.AIProviderType GetCurrentProvider()
    {
        string providerStr = EditorPrefs.GetString(
            AI_PROVIDER_PREF,
            AIManager.AIProviderType.OpenAI.ToString()
        );

        if (Enum.TryParse(providerStr, out AIManager.AIProviderType parsed))
            return parsed;

        return AIManager.AIProviderType.OpenAI;
    }

    private static AnthropicModel ParseAnthropicModel(string value)
    {
        if (Enum.TryParse(value, out AnthropicModel parsed))
            return parsed;

        return AnthropicModel.ClaudeHaiku45;
    }

    private static GptModel LoadSavedModel()
    {
        string savedValue = EditorPrefs.GetString(MODEL_PREF, GptModel.Gpt4oMini.ToString());

        if (Enum.TryParse(savedValue, out GptModel parsed))
            return parsed;

        return GptModel.Gpt4oMini;
    }

    private static void SaveModelPref(GptModel model)
    {
        EditorPrefs.SetString(MODEL_PREF, model.ToString());
    }
}

public class GeneralModule : IEditorModule
{
    public string TabName => "General";

    public void Draw()
    {
        EditorGUILayout.LabelField("OpenAI Settings", EditorStyles.boldLabel);
        GUILayout.Space(5);

        var apiKey = VibeToolsWindow.GPTPrefs.ApiKey;
        var orgKey = VibeToolsWindow.GPTPrefs.OrgKey;

        EditorGUILayout.LabelField("API Key", MaskMiddle(apiKey, 3, 4));
        //EditorGUILayout.LabelField("Org Key", string.IsNullOrEmpty(orgKey) ? "(empty)" : MaskMiddle(orgKey, 3, 4));
        EditorGUILayout.HelpBox("NEVER share your API keys with other people!", MessageType.Warning);

        if (string.IsNullOrEmpty(apiKey))
        {
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);

            GUILayout.BeginVertical("box");
            GUILayout.Label("⚠ OPENAI API KEY NOT SET", EditorStyles.boldLabel);
            GUILayout.Label("You must configure your API key in Setup before using this tool.");
            GUILayout.EndVertical();

            GUI.backgroundColor = Color.white;
        }

        GUILayout.Space(8);

        if (GUILayout.Button("⚙ Open Setup", GUILayout.Height(30)))
        {
            SetupWindow.ShowWindow();
        }
    }

    private string MaskMiddle(string input, int startVisible, int endVisible)
    {
        if (string.IsNullOrEmpty(input))
            return "(empty)";

        if (input.Length <= startVisible + endVisible)
            return input;

        string masked = new string('*', input.Length - startVisible - endVisible);
        return input.Substring(0, startVisible) + masked + input.Substring(input.Length - endVisible);
    }
}