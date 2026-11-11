using UnityEditor;
using UnityEngine;

public class SetupWindow : EditorWindow
{
    private const string PROVIDER_PREF = "AI_PROVIDER";
    private const string ANTHROPIC_API_KEY_PREF = "ANTHROPIC_API_KEY";
    private const string ANTHROPIC_MODEL_PREF = "ANTHROPIC_MODEL";

    private const string TRANSCRIPTION_LANGUAGE_PREF = "GPTPlugin_TranscriptionLanguage";
    private const string DEFAULT_TRANSCRIPTION_LANGUAGE = "es";

    private enum AnthropicModel
    {
        ClaudeHaiku45,
        ClaudeSonnet45,
        ClaudeSonnet46
    }

    [MenuItem("Tools/AI DevSuite/Setup")]
    public static void ShowWindow()
    {
        var window = GetWindow<SetupWindow>("Setup");
        window.minSize = new Vector2(500, 420);
        window.Show();
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
        GUILayout.Space(10);

        DrawProviderSelector();

        GUILayout.Space(12);

        AIManager.AIProviderType provider = GetSelectedProvider();

        switch (provider)
        {
            case AIManager.AIProviderType.OpenAI:
                DrawOpenAISettings();
                break;

            case AIManager.AIProviderType.Anthropic:
                DrawAnthropicSettings();
                break;
        }

        GUILayout.Space(12);

        EditorGUILayout.HelpBox(
            "The plugin will use the provider selected here. Only the active provider settings are shown to avoid mixing models and API keys.",
            MessageType.Info
        );

        GUILayout.Space(10);

        DrawProviderLinks(provider);
    }

    private void DrawProviderSelector()
    {
        EditorGUILayout.LabelField("AI Provider", EditorStyles.boldLabel);
        GUILayout.Space(5);

        AIManager.AIProviderType currentProvider = GetSelectedProvider();

        AIManager.AIProviderType newProvider =
            (AIManager.AIProviderType)EditorGUILayout.EnumPopup("Provider", currentProvider);

        if (newProvider != currentProvider)
        {
            EditorPrefs.SetString(PROVIDER_PREF, newProvider.ToString());
        }
    }

    private void DrawOpenAISettings()
    {
        GUILayout.Space(5);
        EditorGUILayout.HelpBox(
        "\r\nThe OpenAI key enables the plugin's graphics and vibe coding tools. Loading this API key will give you access to all the plugin's features: coding, refactoring, and asset generator.",
        MessageType.None
        );

        EditorGUILayout.LabelField("OpenAI Settings", EditorStyles.boldLabel);
        GUILayout.Space(5);

        string currentApiKey = VibeToolsWindow.GPTPrefs.ApiKey;
        string newApiKey = EditorGUILayout.PasswordField("API Key", currentApiKey);

        if (newApiKey != currentApiKey)
        {
            VibeToolsWindow.GPTPrefs.ApiKey = newApiKey.Trim();
            OpenaiManager.ClearCachedCredentials();
        }

        //VibeToolsWindow.GPTPrefs.OrgKey =
        //    EditorGUILayout.PasswordField("Org Key", VibeToolsWindow.GPTPrefs.OrgKey);

        GUILayout.Space(8);

        GptModel currentModel = LoadOpenAIModel();
        GptModel newModel = (GptModel)EditorGUILayout.EnumPopup("Model", currentModel);

        if (newModel != currentModel)
        {
            SaveOpenAIModel(newModel);
        }

        GUILayout.Space(8);

        string currentLanguage = LoadTranscriptionLanguage();
        string newLanguage = EditorGUILayout.TextField("Transcription Language", currentLanguage);

        if (newLanguage != currentLanguage)
        {
            SaveTranscriptionLanguage(newLanguage);
        }

        EditorGUILayout.HelpBox(
            "Use ISO language codes like: es, en, pt, fr, it. Example: 'es' for Spanish.",
            MessageType.None
        );

        GUILayout.Space(8);

        EditorGUILayout.HelpBox(
            "These settings are used only when OpenAI is selected as provider.",
            MessageType.None
        );
    }

    private void DrawAnthropicSettings()
    {
        GUILayout.Space(5);
        EditorGUILayout.HelpBox(
        "The Anthropic API key does not enable the plugin's graphical tools. It is used for vibe coding, scripting, and code refactoring applications. If you want graphical tools, enable the OpenAI Service!",
        MessageType.None
        );
        GUILayout.Space(5);
        EditorGUILayout.LabelField("Anthropic Settings", EditorStyles.boldLabel);
        string currentApiKey = EditorPrefs.GetString(ANTHROPIC_API_KEY_PREF, "");
        string newApiKey = EditorGUILayout.PasswordField("API Key", currentApiKey);
        
        if (newApiKey != currentApiKey)
        {
            EditorPrefs.SetString(ANTHROPIC_API_KEY_PREF, newApiKey.Trim());
            AnthropicManager.ClearCachedCredentials();
        }

        GUILayout.Space(8);

        AnthropicModel currentModel = LoadAnthropicModel();
        AnthropicModel newModel = (AnthropicModel)EditorGUILayout.EnumPopup("Model", currentModel);

        if (newModel != currentModel)
        {
            SaveAnthropicModel(newModel);
        }

        GUILayout.Space(8);

        EditorGUILayout.HelpBox(
            "These settings are used only when Anthropic is selected as provider.",
            MessageType.None
        );
    }

    private void DrawProviderLinks(AIManager.AIProviderType provider)
    {
        switch (provider)
        {
            case AIManager.AIProviderType.OpenAI:
                if (GUILayout.Button("Go to OpenAI Console", GUILayout.Height(30)))
                {
                    Application.OpenURL("https://platform.openai.com/");
                }

                if (GUILayout.Button("See OpenAI Pricing", GUILayout.Height(30)))
                {
                    Application.OpenURL("https://platform.openai.com/pricing");
                }
                break;

            case AIManager.AIProviderType.Anthropic:
                if (GUILayout.Button("Go to Anthropic Console", GUILayout.Height(30)))
                {
                    Application.OpenURL("https://console.anthropic.com/");
                }

                if (GUILayout.Button("See Anthropic Pricing", GUILayout.Height(30)))
                {
                    Application.OpenURL("https://www.anthropic.com/pricing");
                }
                break;
        }
    }

    private AIManager.AIProviderType GetSelectedProvider()
    {
        string saved = EditorPrefs.GetString(PROVIDER_PREF, AIManager.AIProviderType.OpenAI.ToString());

        if (System.Enum.TryParse(saved, out AIManager.AIProviderType parsed))
            return parsed;

        return AIManager.AIProviderType.OpenAI;
    }

    private GptModel LoadOpenAIModel()
    {
        string saved = EditorPrefs.GetString("GPTPlugin_Model", GptModel.Gpt4oMini.ToString());

        if (System.Enum.TryParse(saved, out GptModel parsed))
            return parsed;

        return GptModel.Gpt4oMini;
    }

    private void SaveOpenAIModel(GptModel model)
    {
        EditorPrefs.SetString("GPTPlugin_Model", model.ToString());
    }

    private string LoadTranscriptionLanguage()
    {
        string saved = EditorPrefs.GetString(
            TRANSCRIPTION_LANGUAGE_PREF,
            DEFAULT_TRANSCRIPTION_LANGUAGE
        );

        if (string.IsNullOrWhiteSpace(saved))
            return DEFAULT_TRANSCRIPTION_LANGUAGE;

        return saved.Trim().ToLower();
    }

    private void SaveTranscriptionLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            language = DEFAULT_TRANSCRIPTION_LANGUAGE;

        EditorPrefs.SetString(
            TRANSCRIPTION_LANGUAGE_PREF,
            language.Trim().ToLower()
        );
    }

    private AnthropicModel LoadAnthropicModel()
    {
        string saved = EditorPrefs.GetString(
            ANTHROPIC_MODEL_PREF,
            AnthropicModel.ClaudeHaiku45.ToString()
        );

        if (System.Enum.TryParse(saved, out AnthropicModel parsed))
            return parsed;

        return AnthropicModel.ClaudeHaiku45;
    }

    private void SaveAnthropicModel(AnthropicModel model)
    {
        EditorPrefs.SetString(ANTHROPIC_MODEL_PREF, model.ToString());
    }
}