using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class GameViewAnalyzerWindow : EditorWindow
{
    private const string PrefKeyLastReport = "UIAuditor_LastReport";
    private const string PrefKeySelectedScreenType = "UIAuditor_SelectedScreenType";
    private const string PrefKeySelectedTargetPlatform = "UIAuditor_SelectedTargetPlatform";
    private const string PrefKeyLastActionPlan = "UIAuditor_LastActionPlan";

    private const string ScoresJsonStartMarker = "===UI_AUDIT_SCORES_JSON_START===";
    private const string ScoresJsonEndMarker = "===UI_AUDIT_SCORES_JSON_END===";

    private const string OpenAiApiKeyPref = "GPTPlugin_ApiKey";
    private const string ProviderPref = "AI_PROVIDER";

    private const string BaseAnalysisPrompt =
        "Analyze the UI/UX quality of the game in this screenshot. " +
        "Evaluate: readability, contrast, visual hierarchy, alignment, spacing, grouping, affordance, feedback, accessibility, navigation, button placement and size, consistency, responsiveness, localization, HUD vs diegetic UI. " +
        "Point out specific issues, severity, and actionable recommendations with priority.";

    private enum ScreenType
    {
        MainMenu,
        OptionsSettings,
        Selection,
        InGameHUD,
        PauseMenu,
        FeedbackScreen,
        InventoryMapSkills,
        TutorialOnboarding,
        LoadingScreen,
        Credits
    }

    private enum TargetPlatform
    {
        General,
        GooglePlay,
        AppleAppStore,
        Steam,
        EpicGamesStore,
        NintendoSwitch,
        PlayStation,
        Xbox
    }

    [Serializable]
    private class AuditScores
    {
        public float claridadVisual = 0f;
        public float jerarquiaVisual = 0f;
        public float facilidadDeUso = 0f;
        public float feedbackDelSistema = 0f;
        public float consistenciaGeneral = 0f;
        public float adecuacionAPlataforma = 0f;

        public void ClampAll()
        {
            claridadVisual = Mathf.Clamp01(claridadVisual);
            jerarquiaVisual = Mathf.Clamp01(jerarquiaVisual);
            facilidadDeUso = Mathf.Clamp01(facilidadDeUso);
            feedbackDelSistema = Mathf.Clamp01(feedbackDelSistema);
            consistenciaGeneral = Mathf.Clamp01(consistenciaGeneral);
            adecuacionAPlataforma = Mathf.Clamp01(adecuacionAPlataforma);
        }
    }

    private Texture _livePreviewTexture;
    private Texture _capturedTexture;
    private bool _captureRequested;
    private bool _analyzeEnabled;
    private Texture2D _persistedCaptureTexture;

    private string _analysisResult;
    private string _actionPlanResult;

    private bool _isAnalyzing;
    private bool _isGeneratingActionPlan;

    private Vector2 _reportScroll;
    private Vector2 _actionPlanScroll;

    private bool _lastRequestFailed;
    private string _lastErrorMessage;
    private string _lastErrorSource;

    private ScreenType _selectedScreenType = ScreenType.InGameHUD;
    private TargetPlatform _selectedTargetPlatform = TargetPlatform.General;

    private AuditScores _scores = new AuditScores();

    private double _analysisStartTime;
    private double _actionPlanStartTime;
    private float _indeterminateBarOffset;
    private float _actionPlanBarOffset;

    [MenuItem("Tools/AI DevSuite/UI & UX AI Auditor")]
    public static void ShowWindow()
    {
        var window = GetWindow<GameViewAnalyzerWindow>("Game View Analyzer");
        window.minSize = new Vector2(980, 650);
    }

    private void OnEnable()
    {
        EditorApplication.update += EditorUpdate;
        TryLoadPersistedCapture();

        _analysisResult = EditorPrefs.GetString(PrefKeyLastReport, string.Empty);
        _actionPlanResult = EditorPrefs.GetString(PrefKeyLastActionPlan, string.Empty);

        int savedScreenType = EditorPrefs.GetInt(PrefKeySelectedScreenType, (int)ScreenType.InGameHUD);
        _selectedScreenType = Enum.IsDefined(typeof(ScreenType), savedScreenType)
            ? (ScreenType)savedScreenType
            : ScreenType.InGameHUD;

        int savedTargetPlatform = EditorPrefs.GetInt(PrefKeySelectedTargetPlatform, (int)TargetPlatform.General);
        _selectedTargetPlatform = Enum.IsDefined(typeof(TargetPlatform), savedTargetPlatform)
            ? (TargetPlatform)savedTargetPlatform
            : TargetPlatform.General;

        ParseScoresFromStoredReport();
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;

        if (_persistedCaptureTexture != null)
        {
            DestroyImmediate(_persistedCaptureTexture);
            _persistedCaptureTexture = null;
        }

        EditorPrefs.SetString(PrefKeyLastReport, _analysisResult ?? string.Empty);
        EditorPrefs.SetString(PrefKeyLastActionPlan, _actionPlanResult ?? string.Empty);
        EditorPrefs.SetInt(PrefKeySelectedScreenType, (int)_selectedScreenType);
        EditorPrefs.SetInt(PrefKeySelectedTargetPlatform, (int)_selectedTargetPlatform);
    }

    private void EditorUpdate()
    {
        if (Application.isPlaying)
            GameViewCaptureBridge.Instance?.EnsurePreviewRunning();

        if (_isAnalyzing)
        {
            _indeterminateBarOffset = (float)((EditorApplication.timeSinceStartup - _analysisStartTime) * 220.0);
            Repaint();
        }

        if (_isGeneratingActionPlan)
        {
            _actionPlanBarOffset = (float)((EditorApplication.timeSinceStartup - _actionPlanStartTime) * 220.0);
            Repaint();
        }

        if (_captureRequested && !GameViewCaptureBridge.IsCapturing)
        {
            _captureRequested = false;
            _capturedTexture = GameViewCaptureBridge.LatestCapture;
            _analyzeEnabled = _capturedTexture != null && !_isAnalyzing;
            Repaint();
            return;
        }

        Texture latestCapture = GameViewCaptureBridge.LatestCapture;
        Texture latestPreview = GameViewCaptureBridge.LatestPreview;

        _livePreviewTexture = latestPreview;

        if (latestCapture == null && _persistedCaptureTexture == null)
            TryLoadPersistedCapture();

        Texture effectiveCapture = latestCapture != null ? latestCapture : _persistedCaptureTexture;

        if (_capturedTexture != effectiveCapture)
        {
            _capturedTexture = effectiveCapture;
            _analyzeEnabled = _capturedTexture != null && !_isAnalyzing;
            Repaint();
        }

        if (_livePreviewTexture != latestPreview)
        {
            _livePreviewTexture = latestPreview;
            Repaint();
        }
    }

    private void TryLoadPersistedCapture()
    {
        if (_persistedCaptureTexture != null)
            return;

        _persistedCaptureTexture = GameViewCaptureBridge.LoadPersistedCapture();
        if (_persistedCaptureTexture != null)
        {
            _capturedTexture = _persistedCaptureTexture;
            _analyzeEnabled = !_isAnalyzing;
        }
    }

    private void OnGUI()
    {
        bool openAiReady = HasOpenAiApiKey();

        DrawGlobalOpenAiWarningBanner();
        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("UX & UI AI Auditor", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);
        EditorGUILayout.HelpBox(
            "This window uses OpenAI for image analysis. Left: live GameView preview. Center: captured frame. Text-based fix recipes use the currently selected text provider.",
            MessageType.Info
        );
        EditorGUILayout.Space(5);

        if (!openAiReady)
        {
            DrawDisabledWindowPreview();
            return;
        }

        float bottomUiHeight = 320f;
        float panelsHeight = Mathf.Max(180f, position.height - bottomUiHeight);

        Rect contentRect = GUILayoutUtility.GetRect(
            10,
            10000,
            10,
            panelsHeight,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true)
        );

        float spacing = 8f;
        float leftWidth = contentRect.width * 0.22f;
        float centerWidth = contentRect.width * 0.22f;
        float rightWidth = contentRect.width - leftWidth - centerWidth - (spacing * 2f);

        Rect liveRect = new Rect(contentRect.x, contentRect.y, leftWidth, contentRect.height);
        Rect captureRect = new Rect(liveRect.xMax + spacing, contentRect.y, centerWidth, contentRect.height);
        Rect reportRect = new Rect(captureRect.xMax + spacing, contentRect.y, rightWidth, contentRect.height);

        DrawPanel(
            liveRect,
            _livePreviewTexture,
            "Live GameView",
            Application.isPlaying ? "Waiting for frames..." : "Enter Play Mode to see live preview."
        );

        DrawPanel(
            captureRect,
            _capturedTexture,
            "Capture",
            "Press 'Capture' to freeze an image."
        );

        DrawRightPanel(reportRect);

        GUILayout.Space(8);
        DrawControlsUI();
    }

    private void DrawDisabledWindowPreview()
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.45f);

        float bottomUiHeight = 320f;
        float panelsHeight = Mathf.Max(180f, position.height - bottomUiHeight);

        Rect contentRect = GUILayoutUtility.GetRect(
            10,
            10000,
            10,
            panelsHeight,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true)
        );

        float spacing = 8f;
        float leftWidth = contentRect.width * 0.22f;
        float centerWidth = contentRect.width * 0.22f;
        float rightWidth = contentRect.width - leftWidth - centerWidth - (spacing * 2f);

        Rect liveRect = new Rect(contentRect.x, contentRect.y, leftWidth, contentRect.height);
        Rect captureRect = new Rect(liveRect.xMax + spacing, contentRect.y, centerWidth, contentRect.height);
        Rect reportRect = new Rect(captureRect.xMax + spacing, contentRect.y, rightWidth, contentRect.height);

        DrawPanel(liveRect, null, "Live GameView", "Live preview will appear here in Play Mode.");
        DrawPanel(captureRect, null, "Capture", "Capture preview will appear here after OpenAI setup.");
        DrawDisabledRightPanel(reportRect);

        GUILayout.Space(8);
        DrawDisabledControlsUI();

        GUI.color = previousColor;
    }

    private void DrawGlobalOpenAiWarningBanner()
    {
        if (HasOpenAiApiKey())
            return;

        Color previousBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.95f, 0.35f, 0.35f);

        EditorGUILayout.BeginVertical("box");
        GUILayout.Space(2);

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.normal.textColor = Color.white;
        titleStyle.fontSize = 12;

        GUIStyle bodyStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
        bodyStyle.normal.textColor = Color.white;

        EditorGUILayout.LabelField("⚠ OPENAI API KEY NOT SET", titleStyle);
        GUILayout.Space(2);
        EditorGUILayout.LabelField(
            "This tool requires OpenAI for screenshot/image analysis. Configure an OpenAI API key in Setup before using Capture analysis.",
            bodyStyle
        );
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

    private bool HasOpenAiApiKey()
    {
        return !string.IsNullOrWhiteSpace(EditorPrefs.GetString(OpenAiApiKeyPref, ""));
    }

    private string NormalizeErrorMessage(string msg)
    {
        if (string.IsNullOrEmpty(msg))
            return "Unknown error.";

        string lower = msg.ToLowerInvariant();

        if (lower.Contains("api key"))
            return "No API key configured for the selected provider. Open Setup and configure it.";

        if (lower.Contains("model") && lower.Contains("not"))
            return "The selected model is not compatible with this operation.";

        if (lower.Contains("network") || lower.Contains("timeout"))
            return "Network error. Check your internet connection and try again.";

        if (lower.Contains("provider"))
            return "The selected provider is not properly configured.";

        return msg;
    }

    private void HandleError(string source, Exception ex)
    {
        Debug.LogError("[" + source + "] Error: " + ex);

        string msg = NormalizeErrorMessage(ex != null ? ex.Message : null);

        EditorApplication.delayCall += () =>
        {
            _lastRequestFailed = true;
            _lastErrorMessage = msg;
            _lastErrorSource = source;

            EditorUtility.DisplayDialog(source + " error", msg, "OK");
            Repaint();
        };
    }

    private string GetActionPlanNullResponseMessage()
    {
        string provider = EditorPrefs.GetString(ProviderPref, "OpenAI");

        if (string.Equals(provider, "Anthropic", StringComparison.OrdinalIgnoreCase))
            return "[AnthropicManager] API Key not configured.";

        return "The selected provider did not return a valid response.";
    }

    private void DrawRightPanel(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

        Rect titleRect = new Rect(rect.x + 6, rect.y + 6, rect.width - 12, 18);
        GUI.Label(titleRect, "Report + fix recipe", EditorStyles.boldLabel);

        float scoresHeight = 162f;
        float actionPlanHeight = Mathf.Max(120f, rect.height * 0.28f);
        float spacing = 6f;

        Rect scoresRect = new Rect(rect.x + 6, rect.y + 28, rect.width - 12, scoresHeight);
        DrawScoresBox(scoresRect);

        float reportY = scoresRect.yMax + spacing;
        float reportHeight = Mathf.Max(110f, rect.height - scoresHeight - actionPlanHeight - 52f);
        Rect reportRect = new Rect(rect.x + 6, reportY, rect.width - 12, reportHeight);
        DrawReportBox(reportRect);

        Rect actionPlanRect = new Rect(rect.x + 6, reportRect.yMax + spacing, rect.width - 12, actionPlanHeight);
        DrawActionPlanBox(actionPlanRect);
    }

    private void DrawDisabledRightPanel(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

        Rect titleRect = new Rect(rect.x + 6, rect.y + 6, rect.width - 12, 18);
        GUI.Label(titleRect, "Report + fix recipe", EditorStyles.boldLabel);

        float scoresHeight = 162f;
        float actionPlanHeight = Mathf.Max(120f, rect.height * 0.28f);
        float spacing = 6f;

        Rect scoresRect = new Rect(rect.x + 6, rect.y + 28, rect.width - 12, scoresHeight);
        DrawDisabledScoresBox(scoresRect);

        float reportY = scoresRect.yMax + spacing;
        float reportHeight = Mathf.Max(110f, rect.height - scoresHeight - actionPlanHeight - 52f);
        Rect reportRect = new Rect(rect.x + 6, reportY, rect.width - 12, reportHeight);
        DrawDisabledTextBox(reportRect, "Detailed report", "The AI audit report will appear here after setup.");

        Rect actionPlanRect = new Rect(rect.x + 6, reportRect.yMax + spacing, rect.width - 12, actionPlanHeight);
        DrawDisabledTextBox(actionPlanRect, "Critical fixes recipe", "The fix recipe will appear here after the report is generated.");
    }

    private void DrawControlsUI()
    {
        bool openAiReady = HasOpenAiApiKey();

        EditorGUILayout.LabelField("Analysis context", EditorStyles.label);

        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            EditorGUI.BeginChangeCheck();
            _selectedScreenType = (ScreenType)EditorGUILayout.EnumPopup("Screen type", _selectedScreenType);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetInt(PrefKeySelectedScreenType, (int)_selectedScreenType);

            EditorGUI.BeginChangeCheck();
            _selectedTargetPlatform = (TargetPlatform)EditorGUILayout.EnumPopup("Target platform", _selectedTargetPlatform);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetInt(PrefKeySelectedTargetPlatform, (int)_selectedTargetPlatform);

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(GetScreenTypeDescription(_selectedScreenType), MessageType.Info);
            EditorGUILayout.HelpBox(GetTargetPlatformDescription(_selectedTargetPlatform), MessageType.None);
        }

        if (!openAiReady)
        {
            EditorGUILayout.HelpBox(
                "OpenAI API key is required for screenshot analysis. Fix recipes use the currently selected text provider.",
                MessageType.Warning
            );
        }

        if (_isAnalyzing)
        {
            GUILayout.Space(6);
            Rect progressRect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
            DrawIndeterminateProgressBar(progressRect, "Analyzing screenshot with OpenAI...", _indeterminateBarOffset);
            GUILayout.Space(4);
        }

        if (_isGeneratingActionPlan)
        {
            GUILayout.Space(6);
            Rect progressRect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
            DrawIndeterminateProgressBar(progressRect, "Generating fix recipe...", _actionPlanBarOffset);
            GUILayout.Space(4);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = Application.isPlaying && !GameViewCaptureBridge.IsCapturing && !_isGeneratingActionPlan;

            if (GUILayout.Button(GameViewCaptureBridge.IsCapturing ? "Capturing..." : "Capture", GUILayout.Height(28), GUILayout.Width(120)))
            {
                GameViewCaptureBridge.Instance?.RequestCaptureNow();
                _captureRequested = true;
            }

            GUI.enabled = openAiReady && _capturedTexture != null && !_isAnalyzing && !_isGeneratingActionPlan && _analyzeEnabled;

            if (GUILayout.Button(_isAnalyzing ? "Analyzing..." : "Analyze", GUILayout.Height(28), GUILayout.Width(120)))
            {
                AnalyzeImageQuality();
            }

            GUI.enabled = !string.IsNullOrWhiteSpace(_analysisResult) && !_isAnalyzing && !_isGeneratingActionPlan;

            if (GUILayout.Button(_isGeneratingActionPlan ? "Thinking..." : "Tell me what to do", GUILayout.Height(28), GUILayout.Width(160)))
            {
                GenerateActionPlan();
            }

            GUI.enabled = !string.IsNullOrEmpty(_analysisResult) && !_isAnalyzing && !_isGeneratingActionPlan;
            if (GUILayout.Button("Copy report", GUILayout.Height(28), GUILayout.Width(120)))
            {
                EditorGUIUtility.systemCopyBuffer = _analysisResult;
                ShowNotificationSafe("Report copied to clipboard");
            }

            GUI.enabled = !string.IsNullOrEmpty(_actionPlanResult) && !_isAnalyzing && !_isGeneratingActionPlan;
            if (GUILayout.Button("Copy recipe", GUILayout.Height(28), GUILayout.Width(120)))
            {
                EditorGUIUtility.systemCopyBuffer = _actionPlanResult;
                ShowNotificationSafe("Recipe copied to clipboard");
            }

            GUI.enabled = !string.IsNullOrEmpty(_analysisResult) && !_isAnalyzing && !_isGeneratingActionPlan;
            if (GUILayout.Button("Save report...", GUILayout.Height(28), GUILayout.Width(140)))
            {
                SaveReportToFile();
            }

            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            string status = GetStatusText();
            Color prevColor = GUI.color;
            if (_isAnalyzing || _isGeneratingActionPlan)
                GUI.color = new Color(1f, 0.85f, 0.2f);
            else if (_lastRequestFailed)
                GUI.color = new Color(1f, 0.4f, 0.4f);
            else
                GUI.color = Color.white;

            GUILayout.Label(status, EditorStyles.miniBoldLabel);
            GUI.color = prevColor;
        }

        if (_lastRequestFailed && !string.IsNullOrEmpty(_lastErrorMessage))
        {
            string prefix = string.IsNullOrEmpty(_lastErrorSource) ? "Error" : ("Error (" + _lastErrorSource + ")");
            EditorGUILayout.HelpBox(prefix + ": " + _lastErrorMessage, MessageType.Error);
        }
    }

    private void DrawDisabledControlsUI()
    {
        EditorGUILayout.LabelField("Analysis context", EditorStyles.label);

        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.EnumPopup("Screen type", _selectedScreenType);
            EditorGUILayout.EnumPopup("Target platform", _selectedTargetPlatform);

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(GetScreenTypeDescription(_selectedScreenType), MessageType.Info);
            EditorGUILayout.HelpBox(GetTargetPlatformDescription(_selectedTargetPlatform), MessageType.None);
        }

        EditorGUILayout.HelpBox(
            "OpenAI API key is required for screenshot analysis.",
            MessageType.Warning
        );

        using (new EditorGUILayout.HorizontalScope())
        using (new EditorGUI.DisabledScope(true))
        {
            GUILayout.Button("Capture", GUILayout.Height(28), GUILayout.Width(120));
            GUILayout.Button("Analyze", GUILayout.Height(28), GUILayout.Width(120));
            GUILayout.Button("Tell me what to do", GUILayout.Height(28), GUILayout.Width(160));
            GUILayout.Button("Copy report", GUILayout.Height(28), GUILayout.Width(120));
            GUILayout.Button("Copy recipe", GUILayout.Height(28), GUILayout.Width(120));
            GUILayout.Button("Save report...", GUILayout.Height(28), GUILayout.Width(140));

            GUILayout.FlexibleSpace();
            GUILayout.Label("OpenAI key required", EditorStyles.miniBoldLabel);
        }
    }

    private string BuildFinalPrompt()
    {
        string screenContext = GetScreenTypePromptContext(_selectedScreenType);
        string platformContext = GetTargetPlatformPromptContext(_selectedTargetPlatform);

        return
            BaseAnalysisPrompt + "\n\n" +
            "Context of the analyzed screen:\n" +
            screenContext + "\n\n" +
            "Publishing context / target platform:\n" +
            platformContext + "\n\n" +

            "In addition to your textual report, you must return a numeric evaluation of 6 criteria.\n" +
            "The output must follow EXACTLY this format:\n\n" +

            ScoresJsonStartMarker + "\n" +
            "{\n" +
            "  \"claridadVisual\": 0.00,\n" +
            "  \"jerarquiaVisual\": 0.00,\n" +
            "  \"facilidadDeUso\": 0.00,\n" +
            "  \"feedbackDelSistema\": 0.00,\n" +
            "  \"consistenciaGeneral\": 0.00,\n" +
            "  \"adecuacionAPlataforma\": 0.00\n" +
            "}\n" +
            ScoresJsonEndMarker + "\n\n" +

            "After that JSON block, write the report in English, clearly and usefully.\n\n" +

            "Rules for the scores:\n" +
            "- Each score must be a float between 0.0 and 1.0.\n" +
            "- 0.0 = does not meet the criterion at all.\n" +
            "- 1.0 = meets the criterion perfectly.\n" +
            "- Use real intermediate values, not all the same.\n" +
            "- Be strict and consistent.\n\n" +

            "Definitions of the 6 criteria:\n" +
            "1. claridadVisual: how clear, legible, and visually understandable the screen is.\n" +
            "2. jerarquiaVisual: how well it directs attention and prioritizes what matters.\n" +
            "3. facilidadDeUso: how easy it is to interact and complete the main task.\n" +
            "4. feedbackDelSistema: how well it communicates states, responses, and consequences.\n" +
            "5. consistenciaGeneral: how coherent and polished the UI feels.\n" +
            "6. adecuacionAPlataforma: how aligned the UI is with the target platform and its expectations.\n\n" +

            "The textual report must:\n" +
            "- explain strengths and weaknesses,\n" +
            "- mention specific problems,\n" +
            "- provide prioritized actionable recommendations,\n" +
            "- and consider the specific goal of this screen and the target platform.\n\n" +

            "Do not include text before the JSON block. Do not change the key names.";
    }

    private string BuildActionPlanPrompt()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("Act as a senior UI/UX auditor for video games.");
        sb.AppendLine("Based on the audit report I provide, return a short and very clear recipe of critical fixes.");
        sb.AppendLine("The goal is that a programmer or designer can follow it step by step without ambiguity.");
        sb.AppendLine();
        sb.AppendLine("Context:");
        sb.AppendLine("- Screen type: " + _selectedScreenType);
        sb.AppendLine("- Target platform: " + _selectedTargetPlatform);
        sb.AppendLine("- Visual clarity: " + _scores.claridadVisual.ToString("0.00"));
        sb.AppendLine("- Visual hierarchy: " + _scores.jerarquiaVisual.ToString("0.00"));
        sb.AppendLine("- Ease of use: " + _scores.facilidadDeUso.ToString("0.00"));
        sb.AppendLine("- System feedback: " + _scores.feedbackDelSistema.ToString("0.00"));
        sb.AppendLine("- Overall consistency: " + _scores.consistenciaGeneral.ToString("0.00"));
        sb.AppendLine("- Platform fit: " + _scores.adecuacionAPlataforma.ToString("0.00"));
        sb.AppendLine();
        sb.AppendLine("Audit report:");
        sb.AppendLine(_analysisResult);
        sb.AppendLine();
        sb.AppendLine("Strict instructions for the response:");
        sb.AppendLine("- Write in English.");
        sb.AppendLine("- Return only a numbered list.");
        sb.AppendLine("- Between 5 and 10 points maximum.");
        sb.AppendLine("- Each point must be concrete and actionable.");
        sb.AppendLine("- Use recipe style: 'Move...', 'Reduce...', 'Increase...', 'Separate...', 'Replace...', 'Align...'.");
        sb.AppendLine("- Prioritize only the critical fixes.");
        sb.AppendLine("- Do not explain theory.");
        sb.AppendLine("- Do not repeat the report.");
        sb.AppendLine("- Do not be vague: avoid phrases like 'improve the UI'.");
        sb.AppendLine("- Whenever possible, mention exactly what to change and in which direction.");
        sb.AppendLine();
        sb.AppendLine("Example of the expected tone:");
        sb.AppendLine("1. Move the main button higher and center it so it becomes the first thing the user sees.");
        sb.AppendLine("2. Reduce the size of the secondary text block so it does not compete with the main action.");
        sb.AppendLine("3. Increase the contrast between text and background on the right panel.");
        sb.AppendLine();
        sb.AppendLine("Now return only the final list.");

        return sb.ToString();
    }

    private void AnalyzeImageQuality()
    {
        if (!HasOpenAiApiKey())
        {
            EditorUtility.DisplayDialog(
                "OpenAI API Key Not Set",
                "This tool requires an OpenAI API key because it performs image analysis. Open Setup before using Analyze.",
                "OK"
            );
            return;
        }

        if (_capturedTexture == null)
        {
            EditorUtility.DisplayDialog("No capture", "There is no capture available to analyze. Press 'Capture' first.", "OK");
            return;
        }

        Texture2D tex2D = _capturedTexture as Texture2D;
        if (tex2D == null)
        {
            EditorUtility.DisplayDialog("Unsupported format", "The captured texture is not a Texture2D. Try capturing again.", "OK");
            return;
        }

        string finalPrompt = BuildFinalPrompt();
        RunAnalysisAsync(tex2D, finalPrompt);
    }

    private void GenerateActionPlan()
    {
        if (string.IsNullOrWhiteSpace(_analysisResult))
        {
            EditorUtility.DisplayDialog("No report", "Generate the report first before asking for the fix recipe.", "OK");
            return;
        }

        string prompt = BuildActionPlanPrompt();
        RunActionPlanAsync(prompt);
    }

    private async void RunAnalysisAsync(Texture2D image, string prompt)
    {
        EditorApplication.delayCall += () =>
        {
            _isAnalyzing = true;
            _analysisStartTime = EditorApplication.timeSinceStartup;
            _indeterminateBarOffset = 0f;
            _lastRequestFailed = false;
            _lastErrorMessage = null;
            _lastErrorSource = null;
            _analyzeEnabled = false;
            Repaint();
        };

        try
        {
            AIManager aiManager = AIManager.EnsureManagerInstanceForEditor();
            string resultRaw = await aiManager.AnalyzeUIImageQuality(image, prompt);
            string normalized = (resultRaw ?? string.Empty).Trim();
            bool isEmpty = string.IsNullOrWhiteSpace(normalized);

            EditorApplication.delayCall += () =>
            {
                if (isEmpty)
                {
                    _analysisResult = string.Empty;
                    _scores = new AuditScores();
                }
                else
                {
                    ApplyAnalysisResponse(normalized);
                }

                _actionPlanResult = string.Empty;
                _actionPlanScroll = Vector2.zero;
                _reportScroll = Vector2.zero;

                EditorPrefs.SetString(PrefKeyLastReport, _analysisResult ?? string.Empty);
                EditorPrefs.SetString(PrefKeyLastActionPlan, _actionPlanResult ?? string.Empty);
                Repaint();
            };
        }
        catch (Exception ex)
        {
            HandleError("Analysis", ex);
        }
        finally
        {
            EditorApplication.delayCall += () =>
            {
                _isAnalyzing = false;
                _indeterminateBarOffset = 0f;
                _analyzeEnabled = _capturedTexture != null;
                Repaint();
            };
        }
    }

    private async void RunActionPlanAsync(string prompt)
    {
        EditorApplication.delayCall += () =>
        {
            _isGeneratingActionPlan = true;
            _actionPlanStartTime = EditorApplication.timeSinceStartup;
            _actionPlanBarOffset = 0f;
            _lastRequestFailed = false;
            _lastErrorMessage = null;
            _lastErrorSource = null;
            Repaint();
        };

        try
        {
            AIManager aiManager = AIManager.EnsureManagerInstanceForEditor();

            var responseMessages = await aiManager.GetConfiguredEditorCompletionResponse(
                prompt,
                "Respond in English. Return only a short, concrete, actionable numbered list. Do not explain theory."
            );

            if (responseMessages == null || responseMessages.Count == 0)
            {
                string msg = GetActionPlanNullResponseMessage();

                EditorApplication.delayCall += () =>
                {
                    _lastRequestFailed = true;
                    _lastErrorMessage = msg;
                    _lastErrorSource = "Recipe";
                    EditorUtility.DisplayDialog("Recipe error", msg, "OK");
                    Repaint();
                };

                return;
            }

            string result = string.Empty;
            var lastMessage = responseMessages[responseMessages.Count - 1];
            result = lastMessage != null ? lastMessage.content : string.Empty;

            if (string.IsNullOrWhiteSpace(result))
            {
                string msg = GetActionPlanNullResponseMessage();

                EditorApplication.delayCall += () =>
                {
                    _lastRequestFailed = true;
                    _lastErrorMessage = msg;
                    _lastErrorSource = "Recipe";
                    EditorUtility.DisplayDialog("Recipe error", msg, "OK");
                    Repaint();
                };

                return;
            }

            EditorApplication.delayCall += () =>
            {
                _actionPlanResult = result.Trim();
                _actionPlanScroll = Vector2.zero;
                EditorPrefs.SetString(PrefKeyLastActionPlan, _actionPlanResult ?? string.Empty);
                Repaint();
            };
        }
        catch (Exception ex)
        {
            HandleError("Recipe", ex);
        }
        finally
        {
            EditorApplication.delayCall += () =>
            {
                _isGeneratingActionPlan = false;
                _actionPlanBarOffset = 0f;
                Repaint();
            };
        }
    }

    private void ApplyAnalysisResponse(string rawResponse)
    {
        AuditScores parsedScores = TryExtractScores(rawResponse);
        string cleanedReport = StripScoresBlock(rawResponse).Trim();

        if (parsedScores != null)
        {
            parsedScores.ClampAll();
            _scores = parsedScores;
        }
        else
        {
            _scores = new AuditScores();
        }

        _analysisResult = cleanedReport;
    }

    private void ParseScoresFromStoredReport()
    {
        if (string.IsNullOrWhiteSpace(_analysisResult))
        {
            _scores = new AuditScores();
            return;
        }

        AuditScores parsedScores = TryExtractScores(_analysisResult);
        if (parsedScores != null)
        {
            parsedScores.ClampAll();
            _scores = parsedScores;
            _analysisResult = StripScoresBlock(_analysisResult).Trim();
        }
        else
        {
            _scores = new AuditScores();
        }
    }

    private AuditScores TryExtractScores(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        int startIndex = raw.IndexOf(ScoresJsonStartMarker, StringComparison.Ordinal);
        int endIndex = raw.IndexOf(ScoresJsonEndMarker, StringComparison.Ordinal);

        if (startIndex >= 0 && endIndex > startIndex)
        {
            int jsonStart = startIndex + ScoresJsonStartMarker.Length;
            string json = raw.Substring(jsonStart, endIndex - jsonStart).Trim();

            try
            {
                AuditScores scores = JsonUtility.FromJson<AuditScores>(json);
                if (scores != null)
                    return scores;
            }
            catch
            {
            }
        }

        Match fallbackJson = Regex.Match(raw, @"\{[\s\S]*?""claridadVisual""[\s\S]*?\}", RegexOptions.Multiline);
        if (fallbackJson.Success)
        {
            try
            {
                AuditScores scores = JsonUtility.FromJson<AuditScores>(fallbackJson.Value);
                if (scores != null)
                    return scores;
            }
            catch
            {
            }
        }

        return null;
    }

    private string StripScoresBlock(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string result = raw;

        int startIndex = result.IndexOf(ScoresJsonStartMarker, StringComparison.Ordinal);
        int endIndex = result.IndexOf(ScoresJsonEndMarker, StringComparison.Ordinal);

        if (startIndex >= 0 && endIndex > startIndex)
        {
            int removeLength = (endIndex + ScoresJsonEndMarker.Length) - startIndex;
            result = result.Remove(startIndex, removeLength).Trim();
        }
        else
        {
            result = Regex.Replace(result, @"^\s*\{[\s\S]*?""claridadVisual""[\s\S]*?\}\s*", "", RegexOptions.Multiline).Trim();
        }

        return result;
    }

    private void DrawScoresBox(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.10f, 0.10f, 0.10f));

        Rect inner = new Rect(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16);

        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
        headerStyle.fontSize = 11;

        GUI.Label(new Rect(inner.x, inner.y, inner.width, 18), "Audit scores", headerStyle);

        float startY = inner.y + 24f;
        float rowHeight = 20f;
        float labelWidth = 170f;
        float gap = 8f;
        float barWidth = inner.width - labelWidth - gap;

        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 0, inner.width, rowHeight), "Visual clarity", _scores.claridadVisual, labelWidth, barWidth);
        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 1, inner.width, rowHeight), "Visual hierarchy", _scores.jerarquiaVisual, labelWidth, barWidth);
        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 2, inner.width, rowHeight), "Ease of use", _scores.facilidadDeUso, labelWidth, barWidth);
        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 3, inner.width, rowHeight), "System feedback", _scores.feedbackDelSistema, labelWidth, barWidth);
        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 4, inner.width, rowHeight), "Overall consistency", _scores.consistenciaGeneral, labelWidth, barWidth);
        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 5, inner.width, rowHeight), "Platform fit", _scores.adecuacionAPlataforma, labelWidth, barWidth);
    }

    private void DrawDisabledScoresBox(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.10f, 0.10f, 0.10f));

        Rect inner = new Rect(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16);

        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
        headerStyle.fontSize = 11;

        GUI.Label(new Rect(inner.x, inner.y, inner.width, 18), "Audit scores", headerStyle);

        float startY = inner.y + 24f;
        float rowHeight = 20f;
        float labelWidth = 170f;
        float gap = 8f;
        float barWidth = inner.width - labelWidth - gap;

        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 0, inner.width, rowHeight), "Visual clarity", 0f, labelWidth, barWidth);
        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 1, inner.width, rowHeight), "Visual hierarchy", 0f, labelWidth, barWidth);
        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 2, inner.width, rowHeight), "Ease of use", 0f, labelWidth, barWidth);
        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 3, inner.width, rowHeight), "System feedback", 0f, labelWidth, barWidth);
        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 4, inner.width, rowHeight), "Overall consistency", 0f, labelWidth, barWidth);
        DrawScoreRow(new Rect(inner.x, startY + rowHeight * 5, inner.width, rowHeight), "Platform fit", 0f, labelWidth, barWidth);
    }

    private void DrawDisabledTextBox(Rect rect, string title, string message)
    {
        EditorGUI.DrawRect(rect, new Color(0.10f, 0.10f, 0.10f));

        Rect titleRect = new Rect(rect.x + 6, rect.y + 4, rect.width - 12, 18);
        GUI.Label(titleRect, title, EditorStyles.miniBoldLabel);

        Rect inner = new Rect(rect.x + 6, rect.y + 24, rect.width - 12, rect.height - 30);

        GUIStyle placeholder = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
        placeholder.alignment = TextAnchor.MiddleCenter;
        placeholder.wordWrap = true;
        placeholder.fontSize = 12;

        GUI.Label(inner, message, placeholder);
    }

    private void DrawScoreRow(Rect rect, string label, float value, float labelWidth, float barWidth)
    {
        value = Mathf.Clamp01(value);

        Rect labelRect = new Rect(rect.x, rect.y + 2, labelWidth, rect.height - 4);
        Rect barRect = new Rect(rect.x + labelWidth + 8, rect.y + 3, barWidth - 54, rect.height - 6);
        Rect valueRect = new Rect(barRect.xMax + 6, rect.y + 2, 48, rect.height - 4);

        GUI.Label(labelRect, label, EditorStyles.miniLabel);

        EditorGUI.DrawRect(barRect, new Color(0.20f, 0.20f, 0.20f));

        Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * value, barRect.height);

        Color fillColor = Color.Lerp(new Color(0.75f, 0.20f, 0.20f), new Color(0.20f, 0.75f, 0.30f), value);
        EditorGUI.DrawRect(fillRect, fillColor);

        GUIStyle valueStyle = new GUIStyle(EditorStyles.miniBoldLabel);
        valueStyle.alignment = TextAnchor.MiddleRight;
        GUI.Label(valueRect, value.ToString("0.00"), valueStyle);
    }

    private void DrawIndeterminateProgressBar(Rect rect, string label, float offset)
    {
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));

        float stripeWidth = Mathf.Max(40f, rect.width * 0.28f);
        float travel = rect.width + stripeWidth;
        float x = rect.x + (offset % travel) - stripeWidth;

        Rect movingRect = new Rect(x, rect.y, stripeWidth, rect.height);
        EditorGUI.DrawRect(movingRect, new Color(0.25f, 0.65f, 1f, 0.95f));

        Rect movingRect2 = new Rect(x - stripeWidth - 20f, rect.y, stripeWidth, rect.height);
        EditorGUI.DrawRect(movingRect2, new Color(0.20f, 0.50f, 0.95f, 0.55f));

        GUIStyle centeredStyle = new GUIStyle(EditorStyles.whiteMiniLabel);
        centeredStyle.alignment = TextAnchor.MiddleCenter;
        centeredStyle.fontStyle = FontStyle.Bold;

        GUI.Label(rect, label, centeredStyle);
    }

    private void DrawReportBox(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.10f, 0.10f, 0.10f));

        Rect titleRect = new Rect(rect.x + 6, rect.y + 4, rect.width - 12, 18);
        GUI.Label(titleRect, "Detailed report", EditorStyles.miniBoldLabel);

        Rect inner = new Rect(rect.x + 6, rect.y + 24, rect.width - 12, rect.height - 30);

        if (string.IsNullOrEmpty(_analysisResult))
        {
            GUIStyle placeholder = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            placeholder.alignment = TextAnchor.MiddleCenter;
            placeholder.wordWrap = true;
            placeholder.fontSize = 12;

            GUI.Label(inner, "There is no report yet.\nCapture and press Analyze.", placeholder);
            return;
        }

        GUIStyle txtStyle = new GUIStyle(EditorStyles.textArea);
        txtStyle.wordWrap = true;
        txtStyle.richText = false;

        float contentHeight = txtStyle.CalcHeight(new GUIContent(_analysisResult), inner.width - 20f);
        Rect viewRect = new Rect(0, 0, inner.width - 16f, Mathf.Max(inner.height, contentHeight + 8f));

        _reportScroll = GUI.BeginScrollView(inner, _reportScroll, viewRect);
        EditorGUI.SelectableLabel(new Rect(0, 0, viewRect.width, viewRect.height), _analysisResult, txtStyle);
        GUI.EndScrollView();
    }

    private void DrawActionPlanBox(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.10f, 0.10f, 0.10f));

        Rect titleRect = new Rect(rect.x + 6, rect.y + 4, rect.width - 12, 18);
        GUI.Label(titleRect, "Critical fixes recipe", EditorStyles.miniBoldLabel);

        Rect inner = new Rect(rect.x + 6, rect.y + 24, rect.width - 12, rect.height - 30);

        if (string.IsNullOrWhiteSpace(_actionPlanResult))
        {
            GUIStyle placeholder = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            placeholder.alignment = TextAnchor.MiddleCenter;
            placeholder.wordWrap = true;
            placeholder.fontSize = 12;

            string msg = _isGeneratingActionPlan
                ? "Generating actionable recipe..."
                : "Press 'Tell me what to do' to get a clear list of critical fixes.";

            GUI.Label(inner, msg, placeholder);
            return;
        }

        GUIStyle txtStyle = new GUIStyle(EditorStyles.textArea);
        txtStyle.wordWrap = true;
        txtStyle.richText = false;

        float contentHeight = txtStyle.CalcHeight(new GUIContent(_actionPlanResult), inner.width - 20f);
        Rect viewRect = new Rect(0, 0, inner.width - 16f, Mathf.Max(inner.height, contentHeight + 8f));

        _actionPlanScroll = GUI.BeginScrollView(inner, _actionPlanScroll, viewRect);
        EditorGUI.SelectableLabel(new Rect(0, 0, viewRect.width, viewRect.height), _actionPlanResult, txtStyle);
        GUI.EndScrollView();
    }

    private void SaveReportToFile()
    {
        if (string.IsNullOrEmpty(_analysisResult))
        {
            EditorUtility.DisplayDialog("No report", "There is no report to save. Run an analysis first.", "OK");
            return;
        }

        string defaultName = $"UI_UX_Report_{DateTime.Now:yyyyMMdd_HHmm}.txt";
        string path = EditorUtility.SaveFilePanel("Save report", "", defaultName, "txt");
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                string exportText =
                    "UI / UX AUDIT REPORT\n" +
                    $"Date: {DateTime.Now:yyyy-MM-dd HH:mm}\n" +
                    $"Screen type: {_selectedScreenType}\n" +
                    $"Target platform: {_selectedTargetPlatform}\n\n" +
                    "SCORES\n" +
                    $"- Visual clarity: {_scores.claridadVisual:0.00}\n" +
                    $"- Visual hierarchy: {_scores.jerarquiaVisual:0.00}\n" +
                    $"- Ease of use: {_scores.facilidadDeUso:0.00}\n" +
                    $"- System feedback: {_scores.feedbackDelSistema:0.00}\n" +
                    $"- Overall consistency: {_scores.consistenciaGeneral:0.00}\n" +
                    $"- Platform fit: {_scores.adecuacionAPlataforma:0.00}\n\n" +
                    "REPORT\n" +
                    _analysisResult + "\n\n" +
                    "CRITICAL FIXES RECIPE\n" +
                    (string.IsNullOrWhiteSpace(_actionPlanResult) ? "(Not generated)" : _actionPlanResult);

                File.WriteAllText(path, exportText, Encoding.UTF8);
                ShowNotificationSafe("Report saved");
            }
            catch (Exception ex)
            {
                Debug.LogError("Could not save the report: " + ex);
                EditorUtility.DisplayDialog("Save error", ex.Message, "OK");
            }
        }
    }

    private string GetStatusText()
    {
        if (!HasOpenAiApiKey())
            return "OpenAI key required";

        if (_isAnalyzing)
            return "Analyzing report...";

        if (_isGeneratingActionPlan)
            return "Generating recipe...";

        if (_lastRequestFailed)
            return "API/Network error";

        if (!string.IsNullOrEmpty(_actionPlanResult))
            return "Recipe ready";

        if (!string.IsNullOrEmpty(_analysisResult))
            return "Report ready";

        if (_capturedTexture != null)
            return "Ready to analyze";

        return "No capture";
    }

    private void ShowNotificationSafe(string message)
    {
        try
        {
            ShowNotification(new GUIContent(message));
        }
        catch
        {
        }
    }

    private void DrawPanel(Rect rect, Texture texture, string title, string emptyMessage)
    {
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

        Rect titleRect = new Rect(rect.x + 6, rect.y + 6, rect.width - 12, 18);
        GUI.Label(titleRect, title, EditorStyles.boldLabel);

        Rect previewRect = new Rect(rect.x + 6, rect.y + 28, rect.width - 12, rect.height - 34);

        if (texture != null)
        {
            DrawTextureScaled(previewRect, texture);
        }
        else
        {
            GUIStyle centered = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            centered.alignment = TextAnchor.MiddleCenter;
            centered.fontSize = 12;
            centered.wordWrap = true;

            GUI.Label(previewRect, emptyMessage, centered);
        }
    }

    private void DrawTextureScaled(Rect rect, Texture texture)
    {
        float texWidth = texture.width;
        float texHeight = texture.height;

        if (texWidth <= 0 || texHeight <= 0)
            return;

        float rectAspect = rect.width / rect.height;
        float texAspect = texWidth / texHeight;

        Rect drawRect = rect;

        if (texAspect > rectAspect)
        {
            float height = rect.width / texAspect;
            drawRect = new Rect(rect.x, rect.y + (rect.height - height) * 0.5f, rect.width, height);
        }
        else
        {
            float width = rect.height * texAspect;
            drawRect = new Rect(rect.x + (rect.width - width) * 0.5f, rect.y, width, rect.height);
        }

        GUI.DrawTexture(drawRect, texture, ScaleMode.ScaleToFit, false);
    }

    private string GetScreenTypeDescription(ScreenType type)
    {
        switch (type)
        {
            case ScreenType.MainMenu:
                return "Main menu: where the player starts the game. Usually contains Play, Options, Credits, and Exit.";
            case ScreenType.OptionsSettings:
                return "Options / settings menu: audio, graphics, controls, language, and other configuration settings.";
            case ScreenType.Selection:
                return "Selection screen: can be level, character, save slot, difficulty, and so on.";
            case ScreenType.InGameHUD:
                return "HUD / in-game interface: what the player sees while playing. Health, score, minimap, ammo, timer, mission, etc.";
            case ScreenType.PauseMenu:
                return "Pause menu: continue, restart, options, back to menu, and similar actions.";
            case ScreenType.FeedbackScreen:
                return "Feedback screens: victory, defeat, game over, mission complete, rewards, results.";
            case ScreenType.InventoryMapSkills:
                return "Inventory / map / skills: organization of information and navigation between gameplay systems.";
            case ScreenType.TutorialOnboarding:
                return "Tutorial / onboarding: teaches how to play and guides the user's first steps.";
            case ScreenType.LoadingScreen:
                return "Loading screen: transition between scenes or levels, with waiting feedback.";
            case ScreenType.Credits:
                return "Credits: team, music, acknowledgments, and experience closure.";
            default:
                return "Game screen.";
        }
    }

    private string GetScreenTypePromptContext(ScreenType type)
    {
        switch (type)
        {
            case ScreenType.MainMenu:
                return "Type: Main menu. User goal: quickly understand where they are, identify the main action to start playing, and access secondary options without friction.";
            case ScreenType.OptionsSettings:
                return "Type: Options / settings menu. User goal: find and modify settings in a clear, predictable, and reversible way.";
            case ScreenType.Selection:
                return "Type: Selection screen. User goal: compare alternatives and easily choose a level, character, difficulty, or save slot.";
            case ScreenType.InGameHUD:
                return "Type: HUD / in-game interface. User goal: get useful gameplay information without the UI becoming distracting or covering important content.";
            case ScreenType.PauseMenu:
                return "Type: Pause menu. User goal: quickly regain control and understand the available options without mistakes.";
            case ScreenType.FeedbackScreen:
                return "Type: Feedback screen. User goal: understand the outcome of what happened and clearly know what the next step is.";
            case ScreenType.InventoryMapSkills:
                return "Type: Inventory / map / skills. User goal: inspect, compare, organize, or equip items and navigate complex information without getting lost.";
            case ScreenType.TutorialOnboarding:
                return "Type: Tutorial / onboarding. User goal: learn rules, controls, or initial steps with as little friction as possible.";
            case ScreenType.LoadingScreen:
                return "Type: Loading screen. User goal: feel that the game is still responsive and better tolerate waiting.";
            case ScreenType.Credits:
                return "Type: Credits. User goal: comfortably read names and roles and end the experience with a strong presentation.";
            default:
                return "Type: Game screen. User goal: complete the main task of this screen without friction.";
        }
    }

    private string GetTargetPlatformDescription(TargetPlatform platform)
    {
        switch (platform)
        {
            case TargetPlatform.General:
                return "General analysis, without a strong bias toward a specific store.";
            case TargetPlatform.GooglePlay:
                return "Google Play: focus on Android adaptation, multiple sizes, perceived quality, and robustness.";
            case TargetPlatform.AppleAppStore:
                return "Apple App Store: focus on polish, clarity, trust, consistency, and demanding review standards.";
            case TargetPlatform.Steam:
                return "Steam: focus on commercial clarity, screenshots, branding, PC UX, and conversion.";
            case TargetPlatform.EpicGamesStore:
                return "Epic Games Store: focus on strong commercial presentation and PC experience.";
            case TargetPlatform.NintendoSwitch:
                return "Nintendo Switch: focus on handheld/TV readability and gamepad navigation.";
            case TargetPlatform.PlayStation:
                return "PlayStation: focus on premium polish, distance readability, and console UX.";
            case TargetPlatform.Xbox:
                return "Xbox: focus on TV clarity, consistency, and gamepad control.";
            default:
                return "Target platform.";
        }
    }

    private string GetTargetPlatformPromptContext(TargetPlatform platform)
    {
        switch (platform)
        {
            case TargetPlatform.General:
                return "Target platform: General. Perform a cross-platform UI/UX analysis without assuming store-specific rules.";
            case TargetPlatform.GooglePlay:
                return "Target platform: Google Play. Consider the Android ecosystem, multiple resolutions and form factors, responsive layouts, safe areas, touch clarity, perceived stability, and the feeling of a well-adapted app.";
            case TargetPlatform.AppleAppStore:
                return "Target platform: Apple App Store. Consider visual polish, clarity, reliability, consistency, accessibility, careful flows, and the perception of a finished product.";
            case TargetPlatform.Steam:
                return "Target platform: Steam. Consider desktop readability, possible Steam Deck usage, value as a store screenshot, branding, commercial clarity, and the perception of a solid PC experience.";
            case TargetPlatform.EpicGamesStore:
                return "Target platform: Epic Games Store. Consider clear commercial presentation, readability, branding, and overall perception of quality on PC.";
            case TargetPlatform.NintendoSwitch:
                return "Target platform: Nintendo Switch. Consider readability in handheld and TV modes, gamepad navigation, distance clarity, and visual simplicity.";
            case TargetPlatform.PlayStation:
                return "Target platform: PlayStation. Consider living-room experience, visual polish, distance readability, and controller consistency.";
            case TargetPlatform.Xbox:
                return "Target platform: Xbox. Consider console-on-TV experience, focus clarity, gamepad control, and visual consistency.";
            default:
                return "General target platform.";
        }
    }
}