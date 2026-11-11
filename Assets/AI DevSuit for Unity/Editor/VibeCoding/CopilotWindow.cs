using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public class CopilotWindow : EditorWindow
{
    private const string InputControlNameBase = "CopilotInputField";
    private const int MaxMessagesForContext = 16;
    private const string GeneratedScriptsFolder = "Assets/AI-Generated/Scripts/Copilot/";

    private const string AI_PROVIDER_PREF = "AI_PROVIDER";
    private const string OPENAI_API_KEY_PREF = "GPTPlugin_ApiKey";
    private const string ANTHROPIC_API_KEY_PREF = "ANTHROPIC_API_KEY";

    private enum ChatMode
    {
        Plan,
        Codex,
        Teacher,
        Copilot
    }

    private Vector2 chatScroll;
    private string inputMessage = "";

    [SerializeField] private List<ChatEntry> chatHistory = new List<ChatEntry>();

    private GUIStyle windowBackgroundStyle;
    private GUIStyle headerStyle;
    private GUIStyle headerTitleStyle;
    private GUIStyle headerSubtitleStyle;

    private GUIStyle chatPanelStyle;
    private GUIStyle inputPanelStyle;
    private GUIStyle inputLabelStyle;
    private GUIStyle inputTextAreaStyle;
    private GUIStyle sendButtonStyle;
    private GUIStyle copyButtonStyle;
    private GUIStyle copiedLabelStyle;
    private GUIStyle hintLabelStyle;
    private GUIStyle roleLabelStyle;
    private GUIStyle bubbleTextStyle;
    private GUIStyle bubbleUserStyle;
    private GUIStyle bubbleAssistantStyle;
    private GUIStyle statusLabelStyle;
    private GUIStyle codeContainerStyle;
    private GUIStyle codeTextAreaStyle;
    private GUIStyle codeLabelStyle;
    private GUIStyle modeToolbarStyle;
    private GUIStyle modeDescriptionStyle;
    private GUIStyle secondaryButtonStyle;
    private GUIStyle modeBadgeStyle;

    private Texture2D backgroundTex;
    private Texture2D headerTex;
    private Texture2D chatPanelTex;
    private Texture2D inputPanelTex;
    private Texture2D assistantBubbleTex;
    private Texture2D userBubbleTex;
    private Texture2D buttonTex;
    private Texture2D buttonHoverTex;
    private Texture2D buttonActiveTex;
    private Texture2D copyButtonTex;
    private Texture2D copyButtonHoverTex;
    private Texture2D copyButtonActiveTex;
    private Texture2D inputFieldTex;
    private Texture2D codePanelTex;
    private Texture2D codeTextAreaTex;
    private Texture2D modeToolbarTex;
    private Texture2D secondaryButtonTex;
    private Texture2D secondaryButtonHoverTex;
    private Texture2D secondaryButtonActiveTex;
    private Texture2D modeBadgeTex;

    private bool stylesInitialized;
    private bool shouldScrollToBottom;
    private bool shouldFocusInput;
    private bool shouldResetTextEditingState;
    private bool isSending;
    private bool isAudioRecording;
    private bool suppressImmediateRefocus;

    private int copiedMessageIndex = -1;
    private double copiedUntilTime = 0.0;
    private int recordControlID;

    private int inputControlVersion = 0;
    private int skipTextAreaOverwriteFrames = 0;
    private int delayedRefocusFrames = 0;

    private AIManager aiManager;
    private GameObject audioRecorderGO;
    private SoundRecordSys audioRecorder;

    private string pendingTranscribedText;
    private string pendingTranscriptionError;
    private bool hasPendingUiApply;

    private ChatMode currentMode = ChatMode.Copilot;

    [System.Serializable]
    private class ChatEntry
    {
        public string role;
        public string message;
        public bool isCode;

        public ChatEntry(string role, string message, bool isCode = false)
        {
            this.role = role;
            this.message = message;
            this.isCode = isCode;
        }
    }

    [MenuItem("Tools/AI DevSuite/Copilot Window")]
    public static void ShowWindow()
    {
        CopilotWindow window = GetWindow<CopilotWindow>("Copilot Window");
        window.minSize = new Vector2(440, 620);
        window.Show();
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        EnsureAIManager();

        if (chatHistory.Count == 0)
        {
            chatHistory.Add(new ChatEntry("assistant", "Hello! I am ready."));
        }

        stylesInitialized = false;
        shouldFocusInput = true;

        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorTick;
        CleanupAudioRecorder();
        DestroyGeneratedTextures();
        stylesInitialized = false;
    }

    private void OnFocus()
    {
        EnsureAIManager();
        shouldFocusInput = true;
        ForceFullEditorRefresh();
    }

    private void EditorTick()
    {
        bool needsRepaint = false;

        if (copiedMessageIndex >= 0 && EditorApplication.timeSinceStartup > copiedUntilTime)
        {
            copiedMessageIndex = -1;
            needsRepaint = true;
        }

        if (hasPendingUiApply)
        {
            ApplyPendingTranscriptionNow();
            needsRepaint = true;
        }

        if (delayedRefocusFrames > 0)
        {
            delayedRefocusFrames--;
            needsRepaint = true;

            if (delayedRefocusFrames == 0)
            {
                suppressImmediateRefocus = false;
                shouldFocusInput = true;
            }
        }

        if (isAudioRecording || isSending || audioRecorder != null)
        {
            needsRepaint = true;
        }

        if (needsRepaint)
        {
            ForceFullEditorRefresh();
        }
    }

    private void ApplyPendingTranscriptionNow()
    {
        hasPendingUiApply = false;

        if (!string.IsNullOrEmpty(pendingTranscriptionError))
        {
            chatHistory.Add(new ChatEntry("assistant", "Error transcribing audio: " + pendingTranscriptionError));
            pendingTranscriptionError = null;
            shouldScrollToBottom = true;
        }
        else if (!string.IsNullOrWhiteSpace(pendingTranscribedText))
        {
            inputMessage = string.IsNullOrWhiteSpace(inputMessage)
                ? pendingTranscribedText.Trim()
                : inputMessage.TrimEnd() + Environment.NewLine + pendingTranscribedText.Trim();

            pendingTranscribedText = null;
        }

        GUIUtility.keyboardControl = 0;
        EditorGUIUtility.editingTextField = false;
        GUI.FocusControl(string.Empty);

        shouldResetTextEditingState = true;

        inputControlVersion++;
        skipTextAreaOverwriteFrames = 2;

        suppressImmediateRefocus = true;
        delayedRefocusFrames = 2;
    }

    private string GetCurrentInputControlName()
    {
        return InputControlNameBase + "_" + inputControlVersion;
    }

    private void ForceFullEditorRefresh()
    {
        Repaint();
        EditorApplication.QueuePlayerLoopUpdate();
        InternalEditorUtility.RepaintAllViews();
    }

    private void EnsureAIManager()
    {
#if UNITY_EDITOR
        if (aiManager == null)
        {
            aiManager = AIManager.EnsureManagerInstanceForEditor();
        }
#endif
    }

    private void InitStyles()
    {
        DestroyGeneratedTextures();

        backgroundTex = MakeTex(new Color(0.08f, 0.09f, 0.11f, 1f));
        headerTex = MakeTex(new Color(0.11f, 0.13f, 0.17f, 1f));
        chatPanelTex = MakeTex(new Color(0.13f, 0.14f, 0.17f, 1f));
        inputPanelTex = MakeTex(new Color(0.14f, 0.15f, 0.18f, 1f));
        assistantBubbleTex = MakeTex(new Color(0.22f, 0.24f, 0.29f, 1f));
        userBubbleTex = MakeTex(new Color(0.17f, 0.45f, 0.95f, 1f));
        buttonTex = MakeTex(new Color(0.21f, 0.48f, 0.98f, 1f));
        buttonHoverTex = MakeTex(new Color(0.28f, 0.55f, 1.00f, 1f));
        buttonActiveTex = MakeTex(new Color(0.16f, 0.39f, 0.84f, 1f));
        copyButtonTex = MakeTex(new Color(0.25f, 0.27f, 0.33f, 1f));
        copyButtonHoverTex = MakeTex(new Color(0.31f, 0.34f, 0.40f, 1f));
        copyButtonActiveTex = MakeTex(new Color(0.20f, 0.22f, 0.27f, 1f));
        inputFieldTex = MakeTex(new Color(0.10f, 0.11f, 0.13f, 1f));
        codePanelTex = MakeTex(new Color(0.10f, 0.11f, 0.14f, 1f));
        codeTextAreaTex = MakeTex(new Color(0.07f, 0.08f, 0.10f, 1f));
        modeToolbarTex = MakeTex(new Color(0.12f, 0.14f, 0.19f, 1f));
        secondaryButtonTex = MakeTex(new Color(0.22f, 0.24f, 0.29f, 1f));
        secondaryButtonHoverTex = MakeTex(new Color(0.28f, 0.31f, 0.36f, 1f));
        secondaryButtonActiveTex = MakeTex(new Color(0.18f, 0.20f, 0.24f, 1f));
        modeBadgeTex = MakeTex(new Color(0.15f, 0.32f, 0.64f, 1f));

        windowBackgroundStyle = new GUIStyle();
        windowBackgroundStyle.normal.background = backgroundTex;

        headerStyle = new GUIStyle(GUI.skin.box);
        headerStyle.normal.background = headerTex;
        headerStyle.padding = new RectOffset(16, 16, 14, 14);
        headerStyle.margin = new RectOffset(12, 12, 12, 8);
        headerStyle.border = new RectOffset(6, 6, 6, 6);

        headerTitleStyle = new GUIStyle(EditorStyles.label);
        headerTitleStyle.fontSize = 19;
        headerTitleStyle.fontStyle = FontStyle.Bold;
        headerTitleStyle.normal.textColor = new Color(0.96f, 0.97f, 0.99f);
        headerTitleStyle.alignment = TextAnchor.MiddleLeft;

        headerSubtitleStyle = new GUIStyle(EditorStyles.label);
        headerSubtitleStyle.fontSize = 11;
        headerSubtitleStyle.wordWrap = true;
        headerSubtitleStyle.normal.textColor = new Color(0.72f, 0.76f, 0.84f);
        headerSubtitleStyle.alignment = TextAnchor.MiddleLeft;

        chatPanelStyle = new GUIStyle(GUI.skin.box);
        chatPanelStyle.normal.background = chatPanelTex;
        chatPanelStyle.padding = new RectOffset(14, 14, 14, 14);
        chatPanelStyle.margin = new RectOffset(12, 12, 0, 8);
        chatPanelStyle.border = new RectOffset(6, 6, 6, 6);

        inputPanelStyle = new GUIStyle(GUI.skin.box);
        inputPanelStyle.normal.background = inputPanelTex;
        inputPanelStyle.padding = new RectOffset(14, 14, 14, 14);
        inputPanelStyle.margin = new RectOffset(12, 12, 0, 12);
        inputPanelStyle.border = new RectOffset(6, 6, 6, 6);

        inputLabelStyle = new GUIStyle(EditorStyles.label);
        inputLabelStyle.fontSize = 12;
        inputLabelStyle.fontStyle = FontStyle.Bold;
        inputLabelStyle.normal.textColor = new Color(0.90f, 0.92f, 0.96f);

        roleLabelStyle = new GUIStyle(EditorStyles.label);
        roleLabelStyle.fontSize = 11;
        roleLabelStyle.fontStyle = FontStyle.Bold;
        roleLabelStyle.wordWrap = true;
        roleLabelStyle.normal.textColor = new Color(0.83f, 0.86f, 0.92f);

        bubbleTextStyle = new GUIStyle(EditorStyles.label);
        bubbleTextStyle.fontSize = 13;
        bubbleTextStyle.wordWrap = true;
        bubbleTextStyle.alignment = TextAnchor.UpperLeft;
        bubbleTextStyle.normal.textColor = new Color(0.97f, 0.98f, 1f);
        bubbleTextStyle.richText = false;

        bubbleAssistantStyle = new GUIStyle(GUI.skin.box);
        bubbleAssistantStyle.normal.background = assistantBubbleTex;
        bubbleAssistantStyle.padding = new RectOffset(14, 14, 10, 10);
        bubbleAssistantStyle.margin = new RectOffset(6, 90, 4, 10);
        bubbleAssistantStyle.border = new RectOffset(8, 8, 8, 8);
        bubbleAssistantStyle.alignment = TextAnchor.UpperLeft;

        bubbleUserStyle = new GUIStyle(GUI.skin.box);
        bubbleUserStyle.normal.background = userBubbleTex;
        bubbleUserStyle.padding = new RectOffset(14, 14, 10, 10);
        bubbleUserStyle.margin = new RectOffset(90, 6, 4, 10);
        bubbleUserStyle.border = new RectOffset(8, 8, 8, 8);
        bubbleUserStyle.alignment = TextAnchor.UpperLeft;

        inputTextAreaStyle = new GUIStyle(EditorStyles.textArea);
        inputTextAreaStyle.wordWrap = true;
        inputTextAreaStyle.fontSize = 14;
        inputTextAreaStyle.padding = new RectOffset(12, 12, 12, 12);
        inputTextAreaStyle.margin = new RectOffset(0, 0, 6, 6);
        inputTextAreaStyle.normal.background = inputFieldTex;
        inputTextAreaStyle.focused.background = inputFieldTex;
        inputTextAreaStyle.hover.background = inputFieldTex;
        inputTextAreaStyle.active.background = inputFieldTex;
        inputTextAreaStyle.normal.textColor = new Color(0.96f, 0.97f, 0.99f);
        inputTextAreaStyle.focused.textColor = new Color(0.96f, 0.97f, 0.99f);
        inputTextAreaStyle.hover.textColor = new Color(0.96f, 0.97f, 0.99f);
        inputTextAreaStyle.active.textColor = new Color(0.96f, 0.97f, 0.99f);

        sendButtonStyle = new GUIStyle(GUI.skin.button);
        sendButtonStyle.fontSize = 14;
        sendButtonStyle.fontStyle = FontStyle.Bold;
        sendButtonStyle.alignment = TextAnchor.MiddleCenter;
        sendButtonStyle.fixedHeight = 42;
        sendButtonStyle.fixedWidth = 110;
        sendButtonStyle.normal.background = buttonTex;
        sendButtonStyle.hover.background = buttonHoverTex;
        sendButtonStyle.active.background = buttonActiveTex;
        sendButtonStyle.normal.textColor = Color.white;
        sendButtonStyle.hover.textColor = Color.white;
        sendButtonStyle.active.textColor = Color.white;
        sendButtonStyle.border = new RectOffset(8, 8, 8, 8);

        copyButtonStyle = new GUIStyle(GUI.skin.button);
        copyButtonStyle.fontSize = 11;
        copyButtonStyle.fontStyle = FontStyle.Bold;
        copyButtonStyle.fixedHeight = 24;
        copyButtonStyle.normal.background = copyButtonTex;
        copyButtonStyle.hover.background = copyButtonHoverTex;
        copyButtonStyle.active.background = copyButtonActiveTex;
        copyButtonStyle.normal.textColor = new Color(0.94f, 0.96f, 0.99f);
        copyButtonStyle.hover.textColor = new Color(0.94f, 0.96f, 0.99f);
        copyButtonStyle.active.textColor = new Color(0.94f, 0.96f, 0.99f);
        copyButtonStyle.padding = new RectOffset(8, 8, 2, 2);

        secondaryButtonStyle = new GUIStyle(GUI.skin.button);
        secondaryButtonStyle.fontSize = 11;
        secondaryButtonStyle.fontStyle = FontStyle.Bold;
        secondaryButtonStyle.fixedHeight = 24;
        secondaryButtonStyle.normal.background = secondaryButtonTex;
        secondaryButtonStyle.hover.background = secondaryButtonHoverTex;
        secondaryButtonStyle.active.background = secondaryButtonActiveTex;
        secondaryButtonStyle.normal.textColor = new Color(0.94f, 0.96f, 0.99f);
        secondaryButtonStyle.hover.textColor = new Color(0.94f, 0.96f, 0.99f);
        secondaryButtonStyle.active.textColor = new Color(0.94f, 0.96f, 0.99f);
        secondaryButtonStyle.padding = new RectOffset(8, 8, 2, 2);

        copiedLabelStyle = new GUIStyle(EditorStyles.label);
        copiedLabelStyle.fontSize = 11;
        copiedLabelStyle.fontStyle = FontStyle.Bold;
        copiedLabelStyle.alignment = TextAnchor.MiddleRight;
        copiedLabelStyle.normal.textColor = new Color(0.55f, 0.90f, 0.65f);

        hintLabelStyle = new GUIStyle(EditorStyles.label);
        hintLabelStyle.fontSize = 11;
        hintLabelStyle.wordWrap = true;
        hintLabelStyle.normal.textColor = new Color(0.67f, 0.70f, 0.77f);

        statusLabelStyle = new GUIStyle(EditorStyles.label);
        statusLabelStyle.fontSize = 11;
        statusLabelStyle.wordWrap = true;
        statusLabelStyle.normal.textColor = new Color(0.86f, 0.88f, 0.92f);

        codeContainerStyle = new GUIStyle(GUI.skin.box);
        codeContainerStyle.normal.background = codePanelTex;
        codeContainerStyle.padding = new RectOffset(12, 12, 10, 10);
        codeContainerStyle.margin = new RectOffset(6, 90, 4, 10);
        codeContainerStyle.border = new RectOffset(8, 8, 8, 8);

        codeTextAreaStyle = new GUIStyle(EditorStyles.textArea);
        codeTextAreaStyle.wordWrap = false;
        codeTextAreaStyle.fontSize = 12;
        codeTextAreaStyle.padding = new RectOffset(10, 10, 10, 10);
        codeTextAreaStyle.normal.background = codeTextAreaTex;
        codeTextAreaStyle.focused.background = codeTextAreaTex;
        codeTextAreaStyle.hover.background = codeTextAreaTex;
        codeTextAreaStyle.active.background = codeTextAreaTex;
        codeTextAreaStyle.normal.textColor = new Color(0.92f, 0.95f, 0.98f);
        codeTextAreaStyle.focused.textColor = new Color(0.92f, 0.95f, 0.98f);
        codeTextAreaStyle.hover.textColor = new Color(0.92f, 0.95f, 0.98f);
        codeTextAreaStyle.active.textColor = new Color(0.92f, 0.95f, 0.98f);

        codeLabelStyle = new GUIStyle(EditorStyles.label);
        codeLabelStyle.fontSize = 11;
        codeLabelStyle.fontStyle = FontStyle.Bold;
        codeLabelStyle.normal.textColor = new Color(0.74f, 0.84f, 0.98f);

        modeToolbarStyle = new GUIStyle(GUI.skin.box);
        modeToolbarStyle.normal.background = modeToolbarTex;
        modeToolbarStyle.padding = new RectOffset(10, 10, 8, 8);
        modeToolbarStyle.margin = new RectOffset(0, 0, 10, 0);
        modeToolbarStyle.border = new RectOffset(6, 6, 6, 6);

        modeDescriptionStyle = new GUIStyle(EditorStyles.label);
        modeDescriptionStyle.fontSize = 11;
        modeDescriptionStyle.wordWrap = true;
        modeDescriptionStyle.normal.textColor = new Color(0.80f, 0.84f, 0.92f);

        modeBadgeStyle = new GUIStyle(GUI.skin.box);
        modeBadgeStyle.normal.background = modeBadgeTex;
        modeBadgeStyle.alignment = TextAnchor.MiddleCenter;
        modeBadgeStyle.padding = new RectOffset(8, 8, 4, 4);
        modeBadgeStyle.margin = new RectOffset(0, 0, 0, 0);
        modeBadgeStyle.fontSize = 10;
        modeBadgeStyle.fontStyle = FontStyle.Bold;
        modeBadgeStyle.normal.textColor = new Color(0.96f, 0.98f, 1f);

        stylesInitialized = true;
    }

    private void OnGUI()
    {
        if (!stylesInitialized || AnyCriticalStyleMissing())
        {
            InitStyles();
        }

        bool providerReady = HasApiKeyForProvider(GetCurrentProviderFromPrefs());

        if (providerReady)
        {
            HandleKeyboard();
        }

        DrawWindowBackground();

        EditorGUILayout.BeginVertical();
        DrawHeader(providerReady);

        if (!providerReady)
        {
            DrawGlobalProviderWarningBanner();
            GUILayout.Space(6);
            DrawLockedCopilotPreview();
            EditorGUILayout.EndVertical();
            return;
        }

        DrawBody();

        EditorGUILayout.EndVertical();

        if (shouldResetTextEditingState)
        {
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
            GUI.FocusControl(string.Empty);
            shouldResetTextEditingState = false;
        }

        if (providerReady && shouldFocusInput && !suppressImmediateRefocus)
        {
            shouldFocusInput = false;
            string currentControlName = GetCurrentInputControlName();
            GUI.FocusControl(currentControlName);
            EditorGUI.FocusTextInControl(currentControlName);
        }

        ProcessDelayedScroll();
    }

    private bool AnyCriticalStyleMissing()
    {
        return windowBackgroundStyle == null ||
               headerStyle == null ||
               chatPanelStyle == null ||
               inputPanelStyle == null ||
               inputTextAreaStyle == null ||
               sendButtonStyle == null ||
               copyButtonStyle == null ||
               bubbleAssistantStyle == null ||
               bubbleUserStyle == null ||
               codeContainerStyle == null ||
               codeTextAreaStyle == null ||
               modeToolbarStyle == null ||
               modeDescriptionStyle == null ||
               secondaryButtonStyle == null ||
               modeBadgeStyle == null ||
               backgroundTex == null ||
               headerTex == null ||
               chatPanelTex == null ||
               inputPanelTex == null ||
               assistantBubbleTex == null ||
               userBubbleTex == null ||
               buttonTex == null ||
               buttonHoverTex == null ||
               buttonActiveTex == null ||
               copyButtonTex == null ||
               copyButtonHoverTex == null ||
               copyButtonActiveTex == null ||
               inputFieldTex == null ||
               codePanelTex == null ||
               codeTextAreaTex == null ||
               modeToolbarTex == null ||
               secondaryButtonTex == null ||
               secondaryButtonHoverTex == null ||
               secondaryButtonActiveTex == null ||
               modeBadgeTex == null;
    }

    private void DrawWindowBackground()
    {
        GUI.Box(new Rect(0, 0, position.width, position.height), GUIContent.none, windowBackgroundStyle);
    }

    private void DrawHeader(bool providerReady)
    {
        EditorGUILayout.BeginVertical(headerStyle);
        GUILayout.Label("Copilot Window", headerTitleStyle);

        string providerText = "Provider: Unknown";
        if (aiManager != null)
        {
            providerText = "Provider: " + aiManager.GetCurrentProvider();
        }

        GUILayout.Space(2);
        GUILayout.Label(providerText + " · Shared chat context across mode changes.", headerSubtitleStyle);

        GUILayout.Space(10);

        if (!providerReady)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.45f);

            using (new EditorGUI.DisabledScope(true))
            {
                DrawModeSelector();
            }

            GUI.color = previousColor;
        }
        else
        {
            DrawModeSelector();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawGlobalProviderWarningBanner()
    {
        AIManager.AIProviderType provider = GetCurrentProviderFromPrefs();

        if (HasApiKeyForProvider(provider))
            return;

        string title;
        string body;

        if (provider == AIManager.AIProviderType.OpenAI)
        {
            title = "OPENAI API KEY NOT SET";
            body = "OpenAI is currently selected, but no API key is configured. You must complete Setup before using Copilot.";
        }
        else if (provider == AIManager.AIProviderType.Anthropic)
        {
            title = "ANTHROPIC API KEY NOT SET";
            body = "Anthropic is currently selected, but no API key is configured. You must complete Setup before using Copilot.";
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

    private void DrawLockedCopilotPreview()
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.45f);

        EditorGUILayout.BeginVertical(chatPanelStyle, GUILayout.ExpandHeight(true));
        GUILayout.Space(4);

        DrawLockedBubble("AI", "Copilot is locked until the selected AI provider has a valid API key configured in Setup.");
        DrawLockedBubble("You", "Your message will go here...");
        DrawLockedBubble("AI", "Responses, code blocks, copy buttons, and voice input will be enabled after setup.");

        GUILayout.Space(8);
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(inputPanelStyle, GUILayout.Height(230));
        GUILayout.Label("Write your message", inputLabelStyle);
        GUILayout.Space(6);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextArea(
                "Copilot is disabled until Setup is completed.",
                inputTextAreaStyle,
                GUILayout.ExpandHeight(true),
                GUILayout.MinHeight(95)
            );

            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            GUILayout.Label("- Press Enter to send", hintLabelStyle);
            GUILayout.Label("- Hold the mic button to dictate", hintLabelStyle);
            GUILayout.Label("- Complete Setup to unlock this window", hintLabelStyle);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            GUILayout.Button("Hold to Record", sendButtonStyle, GUILayout.Width(120), GUILayout.Height(42));
            GUILayout.Space(8);
            GUILayout.Button("Send", sendButtonStyle);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();

        GUI.color = previousColor;
    }

    private void DrawLockedBubble(string role, string message)
    {
        EditorGUILayout.BeginHorizontal();

        bool isUser = role == "You";
        if (isUser)
            GUILayout.FlexibleSpace();

        GUIStyle bubbleStyle = isUser ? bubbleUserStyle : bubbleAssistantStyle;

        EditorGUILayout.BeginVertical(
            bubbleStyle,
            GUILayout.MaxWidth(Mathf.Max(230f, position.width * 0.68f))
        );

        GUILayout.Label(role, roleLabelStyle);
        GUILayout.Space(3);
        GUILayout.Label(message, bubbleTextStyle);

        EditorGUILayout.EndVertical();

        if (!isUser)
            GUILayout.FlexibleSpace();

        EditorGUILayout.EndHorizontal();
    }

    private AIManager.AIProviderType GetCurrentProviderFromPrefs()
    {
        string providerStr = EditorPrefs.GetString(
            AI_PROVIDER_PREF,
            AIManager.AIProviderType.OpenAI.ToString()
        );

        if (Enum.TryParse(providerStr, out AIManager.AIProviderType parsed))
            return parsed;

        return AIManager.AIProviderType.OpenAI;
    }

    private bool HasApiKeyForProvider(AIManager.AIProviderType provider)
    {
        if (provider == AIManager.AIProviderType.OpenAI)
        {
            string openAiKey = EditorPrefs.GetString(OPENAI_API_KEY_PREF, "");
            return !string.IsNullOrWhiteSpace(openAiKey);
        }

        if (provider == AIManager.AIProviderType.Anthropic)
        {
            string anthropicKey = EditorPrefs.GetString(ANTHROPIC_API_KEY_PREF, "");
            return !string.IsNullOrWhiteSpace(anthropicKey);
        }

        return false;
    }

    private void DrawModeSelector()
    {
        EditorGUILayout.BeginVertical(modeToolbarStyle);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Chat Mode", inputLabelStyle, GUILayout.Width(80));

        EditorGUI.BeginChangeCheck();
        ChatMode newMode = (ChatMode)GUILayout.Toolbar(
            (int)currentMode,
            new[] { "Plan", "Codex", "Teacher", "Copilot" },
            GUILayout.Height(26)
        );

        if (EditorGUI.EndChangeCheck())
        {
            currentMode = newMode;
            shouldFocusInput = true;
            ForceFullEditorRefresh();
        }

        GUILayout.Space(8);
        GUILayout.Label(GetModeBadgeText(currentMode), modeBadgeStyle, GUILayout.Width(92), GUILayout.Height(24));
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label(GetModeDescription(currentMode), modeDescriptionStyle);

        GUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Clear Chat", secondaryButtonStyle, GUILayout.Width(90)))
        {
            ClearChat();
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label("Mode changes keep the same recent conversation context.", hintLabelStyle);

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private string GetModeBadgeText(ChatMode mode)
    {
        switch (mode)
        {
            case ChatMode.Plan: return "Strategy";
            case ChatMode.Codex: return "Code First";
            case ChatMode.Teacher: return "Learning";
            default: return "Hybrid";
        }
    }

    private string GetModeDescription(ChatMode mode)
    {
        switch (mode)
        {
            case ChatMode.Plan:
                return "Plan mode prioritizes architecture, debugging strategy, implementation steps, risks, and file-by-file direction before coding.";
            case ChatMode.Codex:
                return "Codex mode prioritizes clean copy-paste-ready code with minimal extra text unless the user explicitly asks for explanations.";
            case ChatMode.Teacher:
                return "Teacher mode prioritizes learning. It explains clearly, step by step, using intuition first, then code, then a guided walkthrough.";
            default:
                return "Copilot mode mixes planning and implementation. It reasons briefly, then helps solve the task end to end with practical code when useful.";
        }
    }

    private void ClearChat()
    {
        chatHistory.Clear();
        chatHistory.Add(new ChatEntry("assistant", "Hello! I am ready."));
        shouldScrollToBottom = true;
        shouldFocusInput = true;
        ForceFullEditorRefresh();
    }

    private void DrawBody()
    {
        DrawChatArea();
        DrawInputArea();
    }

    private void DrawChatArea()
    {
        EditorGUILayout.BeginVertical(chatPanelStyle, GUILayout.ExpandHeight(true));

        chatScroll = EditorGUILayout.BeginScrollView(
            chatScroll,
            GUIStyle.none,
            GUI.skin.verticalScrollbar,
            GUILayout.ExpandHeight(true)
        );

        GUILayout.Space(4);

        for (int i = 0; i < chatHistory.Count; i++)
        {
            DrawChatEntry(chatHistory[i], i);
        }

        if (isSending)
        {
            DrawTypingIndicator();
        }

        GUILayout.Space(8);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawTypingIndicator()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical(
            bubbleAssistantStyle,
            GUILayout.MaxWidth(Mathf.Max(230f, position.width * 0.68f))
        );

        GUILayout.Label("AI", roleLabelStyle);
        GUILayout.Space(3);
        GUILayout.Label("Thinking in " + currentMode + " mode...", bubbleTextStyle);

        EditorGUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawChatEntry(ChatEntry entry, int index)
    {
        bool isUser = entry.role == "user";

        EditorGUILayout.BeginHorizontal();

        if (isUser)
            GUILayout.FlexibleSpace();

        if (entry.isCode && !isUser)
        {
            DrawCodeEntry(entry, index);
        }
        else
        {
            DrawStandardEntry(entry, index, isUser);
        }

        if (!isUser)
            GUILayout.FlexibleSpace();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawStandardEntry(ChatEntry entry, int index, bool isUser)
    {
        GUIStyle bubbleStyle = isUser ? bubbleUserStyle : bubbleAssistantStyle;

        EditorGUILayout.BeginVertical(
            bubbleStyle,
            GUILayout.MaxWidth(Mathf.Max(230f, position.width * 0.68f))
        );

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(isUser ? "You" : "AI", roleLabelStyle);
        GUILayout.FlexibleSpace();

        if (!isUser)
        {
            bool copiedThisOne = copiedMessageIndex == index && EditorApplication.timeSinceStartup <= copiedUntilTime;

            if (copiedThisOne)
            {
                GUILayout.Label("Copied!", copiedLabelStyle, GUILayout.Height(24));
            }
            else
            {
                if (GUILayout.Button("Copy", copyButtonStyle))
                {
                    EditorGUIUtility.systemCopyBuffer = entry.message;
                    copiedMessageIndex = index;
                    copiedUntilTime = EditorApplication.timeSinceStartup + 1.5d;
                    ForceFullEditorRefresh();
                }
            }
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(3);
        GUILayout.Label(entry.message, bubbleTextStyle);

        EditorGUILayout.EndVertical();
    }

    private void DrawCodeEntry(ChatEntry entry, int index)
    {
        EditorGUILayout.BeginVertical(
            codeContainerStyle,
            GUILayout.MaxWidth(Mathf.Max(260f, position.width * 0.75f))
        );

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Code", codeLabelStyle);
        GUILayout.FlexibleSpace();

        bool copiedThisOne = copiedMessageIndex == index && EditorApplication.timeSinceStartup <= copiedUntilTime;

        if (copiedThisOne)
        {
            GUILayout.Label("Copied!", copiedLabelStyle, GUILayout.Height(24));
        }
        else
        {
            if (GUILayout.Button("Copy Code", copyButtonStyle, GUILayout.Width(85)))
            {
                EditorGUIUtility.systemCopyBuffer = entry.message;
                copiedMessageIndex = index;
                copiedUntilTime = EditorApplication.timeSinceStartup + 1.5d;
                ForceFullEditorRefresh();
            }

            GUILayout.Space(6);

            if (GUILayout.Button("Create Script", secondaryButtonStyle, GUILayout.Width(95)))
            {
                OpenCreateScriptPopup(entry.message);
            }
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4);

        EditorGUILayout.TextArea(
            entry.message,
            codeTextAreaStyle,
            GUILayout.MinHeight(140),
            GUILayout.MaxWidth(Mathf.Max(240f, position.width * 0.72f))
        );

        EditorGUILayout.EndVertical();
    }

    private void OpenCreateScriptPopup(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            EditorUtility.DisplayDialog("Create Script", "The code is empty.", "OK");
            return;
        }

        string suggestedName = ExtractClassName(code);
        if (string.IsNullOrWhiteSpace(suggestedName))
        {
            suggestedName = "GeneratedScript";
        }

        CreateScriptPopupWindow.ShowPopup(
            code,
            suggestedName,
            finalName =>
            {
                CreateScriptFromCode(code, finalName);
            }
        );
    }

    private void CreateScriptFromCode(string code, string fileName)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            EditorUtility.DisplayDialog("Create Script", "The code is empty.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            EditorUtility.DisplayDialog("Create Script", "Please enter a valid script name.", "OK");
            return;
        }

        try
        {
            EnsureFolderExists(GeneratedScriptsFolder);

            string safeName = SanitizeFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "GeneratedScript";
            }

            string finalPath = GetUniqueScriptPath(GeneratedScriptsFolder, safeName);
            File.WriteAllText(finalPath, code, Encoding.UTF8);

            AssetDatabase.Refresh();

            MonoScript asset = AssetDatabase.LoadAssetAtPath<MonoScript>(finalPath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }

            chatHistory.Add(new ChatEntry("assistant", "Script created successfully at:\n" + finalPath));
            shouldScrollToBottom = true;
            ForceFullEditorRefresh();
        }
        catch (Exception ex)
        {
            Debug.LogError("[CopilotWindow] Error creating script: " + ex);
            chatHistory.Add(new ChatEntry("assistant", "Error creating script: " + ex.Message));
            shouldScrollToBottom = true;
            ForceFullEditorRefresh();
        }
    }

    private void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string currentPath = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string nextPath = currentPath + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, parts[i]);
            }

            currentPath = nextPath;
        }
    }

    private string ExtractClassName(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        Match match = Regex.Match(code, @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)");
        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value.Trim();
        }

        return null;
    }

    private string GetUniqueScriptPath(string folderPath, string baseFileName)
    {
        string safeName = SanitizeFileName(baseFileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "GeneratedScript";
        }

        string path = folderPath + "/" + safeName + ".cs";
        int counter = 1;

        while (File.Exists(path))
        {
            path = folderPath + "/" + safeName + "_" + counter + ".cs";
            counter++;
        }

        return path;
    }

    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "GeneratedScript";

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c.ToString(), "");
        }

        fileName = fileName.Replace(" ", "");
        return fileName.Trim();
    }

    private void DrawInputArea()
    {
        EditorGUILayout.BeginVertical(inputPanelStyle, GUILayout.Height(230));

        GUILayout.Label("Write your message", inputLabelStyle);
        GUILayout.Space(6);

        GUI.enabled = !isSending;

        string currentControlName = GetCurrentInputControlName();
        GUI.SetNextControlName(currentControlName);

        string newInputValue = EditorGUILayout.TextArea(
            inputMessage,
            inputTextAreaStyle,
            GUILayout.ExpandHeight(true),
            GUILayout.MinHeight(95)
        );

        if (skipTextAreaOverwriteFrames > 0)
        {
            skipTextAreaOverwriteFrames--;
        }
        else
        {
            inputMessage = newInputValue;
        }

        GUILayout.Space(8);

        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical();
        GUILayout.Label("- Press Enter to send", hintLabelStyle);
        GUILayout.Label("- Hold the mic button to dictate", hintLabelStyle);

        if (isSending)
        {
            GUILayout.Label("Sending request to AI...", statusLabelStyle);
        }
        else if (isAudioRecording)
        {
            GUILayout.Label("Recording audio...", statusLabelStyle);
        }

        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        DrawRecordButton();
        GUILayout.Space(8);

        if (GUILayout.Button(isSending ? "Sending..." : "Send", sendButtonStyle))
        {
            _ = SendMessageAsync();
        }

        EditorGUILayout.EndHorizontal();

        GUI.enabled = true;

        EditorGUILayout.EndVertical();
    }

    private void DrawRecordButton()
    {
        Rect recordRect = GUILayoutUtility.GetRect(120, 42, GUILayout.Width(120));

        bool isOpenAI = GetCurrentProviderFromPrefs() == AIManager.AIProviderType.OpenAI;
        bool hasKey = HasApiKeyForProvider(AIManager.AIProviderType.OpenAI);
        bool canUse = isOpenAI && hasKey && !isSending;

        string label;

        if (!isOpenAI)
            label = "OpenAI only";
        else if (!hasKey)
            label = "No API Key";
        else if (isAudioRecording)
            label = "● Recording...";
        else
            label = "Record";

        recordControlID = GUIUtility.GetControlID(FocusType.Passive, recordRect);
        Event e = Event.current;

        bool isPressed = GUIUtility.hotControl == recordControlID;

        Color prevColor = GUI.color;

        // 🎯 control total del color (ya no dependemos de DisabledScope)
        if (!canUse)
        {
            GUI.color = new Color(0.4f, 0.4f, 0.4f); // deshabilitado custom
        }
        else if (isAudioRecording)
        {
            GUI.color = new Color(1f, 0.3f, 0.3f); // rojo grabando
        }
        else if (isPressed)
        {
            GUI.color = new Color(0.7f, 0.7f, 0.7f); // feedback hold
        }

        // 👇 lógica SOLO si se puede usar
        if (canUse)
        {
            switch (e.GetTypeForControl(recordControlID))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && recordRect.Contains(e.mousePosition) && !isAudioRecording)
                    {
                        GUIUtility.hotControl = recordControlID;
                        StartAudioRecording();
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == recordControlID && isAudioRecording)
                    {
                        GUIUtility.hotControl = 0;
                        StopAudioRecording();
                        e.Use();
                    }
                    break;
            }
        }

        GUI.Button(recordRect, label, sendButtonStyle);

        GUI.color = prevColor;
    }

    private async Task SendMessageAsync()
    {
        if (isSending)
            return;

        string trimmed = inputMessage.Trim();

        if (string.IsNullOrEmpty(trimmed))
            return;

        EnsureAIManager();

        if (aiManager == null)
        {
            chatHistory.Add(new ChatEntry("assistant", "Error: AIManager is not available."));
            shouldScrollToBottom = true;
            ForceFullEditorRefresh();
            return;
        }

        chatHistory.Add(new ChatEntry("user", trimmed));
        inputMessage = "";
        isSending = true;
        shouldScrollToBottom = true;
        shouldFocusInput = true;
        ForceFullEditorRefresh();

        try
        {
            string fullPrompt = BuildConversationPrompt(trimmed);
            string response = await aiManager.GetSimpleCompletion(fullPrompt);

            if (string.IsNullOrWhiteSpace(response))
            {
                response = "I received the request, but the provider returned an empty response.";
            }

            response = response.Trim();

            string cleanCode;
            bool isCodeOnly = TryExtractCodeForDisplay(response, out cleanCode);

            if (isCodeOnly)
            {
                chatHistory.Add(new ChatEntry("assistant", cleanCode, true));
            }
            else
            {
                chatHistory.Add(new ChatEntry("assistant", response));
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[CopilotWindow] Error sending message to AI: " + ex);
            chatHistory.Add(new ChatEntry("assistant", "Error: " + ex.Message));
        }
        finally
        {
            isSending = false;
            shouldScrollToBottom = true;
            shouldFocusInput = true;
            ForceFullEditorRefresh();
        }
    }

    private bool TryExtractCodeForDisplay(string raw, out string cleanCode)
    {
        cleanCode = raw;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        bool startsWithFence = raw.TrimStart().StartsWith("```");
        bool endsWithFence = raw.TrimEnd().EndsWith("```");
        bool fencedOnly = startsWithFence && endsWithFence && CountOccurrences(raw, "```") >= 2;

        if (fencedOnly)
        {
            int firstFence = raw.IndexOf("```", StringComparison.Ordinal);
            int lineEndAfterFence = raw.IndexOf('\n', firstFence);
            if (lineEndAfterFence >= 0)
            {
                int closingFence = raw.LastIndexOf("```", StringComparison.Ordinal);
                if (closingFence > lineEndAfterFence)
                {
                    string block = raw.Substring(lineEndAfterFence + 1, closingFence - lineEndAfterFence - 1);
                    cleanCode = block.Trim();
                    return !string.IsNullOrWhiteSpace(cleanCode);
                }
            }
        }

        bool looksLikeCode =
            raw.Contains("using ") ||
            raw.Contains("class ") ||
            raw.Contains("public class ") ||
            raw.Contains("private ") ||
            raw.Contains("void ") ||
            raw.Contains("return ") ||
            raw.Contains("if (") ||
            raw.Contains("for (") ||
            raw.Contains("while (") ||
            (raw.Contains("{") && raw.Contains("}"));

        if (!looksLikeCode)
            return false;

        bool containsExplanatoryText =
            raw.Contains("Here is") ||
            raw.Contains("Here's") ||
            raw.Contains("Explanation") ||
            raw.Contains("This works because") ||
            raw.Contains("Why this") ||
            raw.Contains("Step by step") ||
            raw.Contains("First,") ||
            raw.Contains("Then,") ||
            raw.Contains("Finally,");

        if (currentMode == ChatMode.Codex)
        {
            cleanCode = ExtractBestCodeBlock(raw);
            return !string.IsNullOrWhiteSpace(cleanCode);
        }

        if (!containsExplanatoryText && LooksMostlyLikeCode(raw))
        {
            cleanCode = ExtractBestCodeBlock(raw);
            return !string.IsNullOrWhiteSpace(cleanCode);
        }

        return false;
    }

    private string ExtractBestCodeBlock(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        int firstFence = raw.IndexOf("```", StringComparison.Ordinal);
        if (firstFence >= 0)
        {
            int lineEndAfterFence = raw.IndexOf('\n', firstFence);
            if (lineEndAfterFence >= 0)
            {
                int closingFence = raw.IndexOf("```", lineEndAfterFence + 1, StringComparison.Ordinal);
                if (closingFence > lineEndAfterFence)
                {
                    string block = raw.Substring(lineEndAfterFence + 1, closingFence - lineEndAfterFence - 1);
                    if (!string.IsNullOrWhiteSpace(block))
                        return block.Trim();
                }
            }
        }

        return raw.Trim();
    }

    private bool LooksMostlyLikeCode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string[] lines = raw.Split('\n');
        int codeishLines = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (string.IsNullOrEmpty(line))
                continue;

            if (line.StartsWith("using ") ||
                line.StartsWith("public ") ||
                line.StartsWith("private ") ||
                line.StartsWith("protected ") ||
                line.StartsWith("internal ") ||
                line.StartsWith("class ") ||
                line.StartsWith("if ") ||
                line.StartsWith("if(") ||
                line.StartsWith("for ") ||
                line.StartsWith("for(") ||
                line.StartsWith("while ") ||
                line.StartsWith("while(") ||
                line.StartsWith("return ") ||
                line.EndsWith(";") ||
                line == "{" ||
                line == "}")
            {
                codeishLines++;
            }
        }

        return codeishLines >= Mathf.Max(3, lines.Length / 2);
    }

    private int CountOccurrences(string source, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            return 0;

        int count = 0;
        int index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private string BuildConversationPrompt(string latestUserMessage)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine(GetGlobalAssistantRules());
        sb.AppendLine();
        sb.AppendLine(GetModeInstructions(currentMode));
        sb.AppendLine();
        sb.AppendLine("Important context behavior:");
        sb.AppendLine("- Keep continuity with the recent conversation.");
        sb.AppendLine("- The chat mode may have changed, but the conversation history still matters.");
        sb.AppendLine("- Use the current mode to decide HOW to answer, not to ignore previous context.");
        sb.AppendLine();
        sb.AppendLine("Conversation history:");

        int startIndex = Mathf.Max(0, chatHistory.Count - MaxMessagesForContext);

        for (int i = startIndex; i < chatHistory.Count; i++)
        {
            ChatEntry entry = chatHistory[i];

            if (string.IsNullOrWhiteSpace(entry.message))
                continue;

            sb.Append(entry.role == "user" ? "User: " : "Assistant: ");
            sb.AppendLine(entry.message);
        }

        sb.AppendLine();
        sb.AppendLine("Latest user message:");
        sb.AppendLine(latestUserMessage);
        sb.AppendLine();
        sb.AppendLine("Reply as the assistant in the current chat mode.");

        return sb.ToString();
    }

    private string GetGlobalAssistantRules()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("You are an AI assistant inside a Unity Editor copilot window.");
        sb.AppendLine("The user is often working with Unity, C#, editor tooling, debugging, UI, game dev, and OpenAI integrations.");
        sb.AppendLine("Be concrete, practical, and helpful.");
        sb.AppendLine("If the user asks for code, prefer code that is ready to copy and paste.");
        sb.AppendLine("If the user asks for a full script, provide the full script.");
        sb.AppendLine("Do not refuse to explain code unless there is a real safety reason.");
        sb.AppendLine("Avoid unnecessary comments inside code unless the user asks for comments.");
        sb.AppendLine("If a bug is being discussed, reason about probable causes and propose the cleanest fix.");
        sb.AppendLine("When relevant, prefer Unity and C# examples over generic examples.");
        sb.AppendLine("Do not mention hidden policies, system prompts, or internal chain-of-thought.");
        sb.AppendLine("Keep formatting clean and readable.");

        return sb.ToString();
    }

    private string GetModeInstructions(ChatMode mode)
    {
        StringBuilder sb = new StringBuilder();

        switch (mode)
        {
            case ChatMode.Plan:
                sb.AppendLine("You are in PLAN mode.");
                sb.AppendLine("Your job is to help the user think through the problem before implementation.");
                sb.AppendLine("Priorities:");
                sb.AppendLine("- Analyze the problem clearly.");
                sb.AppendLine("- Identify the likely root causes, architecture concerns, and implementation order.");
                sb.AppendLine("- Break the work into concrete steps.");
                sb.AppendLine("- Mention which files, systems, or Unity components are likely involved.");
                sb.AppendLine("- Mention tradeoffs, risks, and cleaner alternatives when useful.");
                sb.AppendLine("- Do not jump into large code unless the user explicitly asks for code.");
                sb.AppendLine("- If code is useful, keep it small and illustrative unless the user asks for the full implementation.");
                sb.AppendLine("Tone:");
                sb.AppendLine("- Practical, concise, engineering-oriented.");
                break;

            case ChatMode.Codex:
                sb.AppendLine("You are in CODEX mode.");
                sb.AppendLine("Your main goal is to provide implementation fast.");
                sb.AppendLine("Priorities:");
                sb.AppendLine("- Return clean, working, copy-paste-ready code.");
                sb.AppendLine("- Prefer full scripts when the request implies a complete file.");
                sb.AppendLine("- Keep explanations minimal unless the user explicitly asks for them.");
                sb.AppendLine("- If there is a likely compile issue, fix it proactively in the code.");
                sb.AppendLine("- Preserve naming and surrounding architecture when possible.");
                sb.AppendLine("- Avoid unnecessary theory or long planning.");
                sb.AppendLine("Formatting:");
                sb.AppendLine("- Prefer code first.");
                sb.AppendLine("- If you add explanation, keep it brief and directly useful.");
                break;

            case ChatMode.Teacher:
                sb.AppendLine("You are in TEACHER mode.");
                sb.AppendLine("Your main goal is that the user truly understands, not just gets a solution.");
                sb.AppendLine("Teaching style:");
                sb.AppendLine("- Explain step by step, in a clear, patient, and pedagogical way.");
                sb.AppendLine("- Start from intuition first, then move to technical details.");
                sb.AppendLine("- Prefer simple language before advanced terminology.");
                sb.AppendLine("- Use the user's context when possible, especially Unity, C#, game dev, editor scripting, and debugging.");
                sb.AppendLine("- Build understanding progressively instead of dumping everything at once.");
                sb.AppendLine("- If the user seems confused, simplify further.");
                sb.AppendLine("- If the topic is complex, break it into smaller ideas.");
                sb.AppendLine("- When useful, use analogies, mental models, and concrete examples.");
                sb.AppendLine("When giving code:");
                sb.AppendLine("- First explain what the code will do and why.");
                sb.AppendLine("- Then provide the code.");
                sb.AppendLine("- Then briefly walk through the most important parts.");
                sb.AppendLine("- Highlight the key idea the user should learn from the solution.");
                sb.AppendLine("Interaction rules:");
                sb.AppendLine("- Prioritize learning over speed.");
                sb.AppendLine("- Encourage understanding, not blind copying.");
                sb.AppendLine("- Do not be dry or academic; be warm, clear, and practical.");
                sb.AppendLine("- Avoid unnecessarily huge answers, but never sacrifice clarity.");
                break;

            default:
                sb.AppendLine("You are in COPILOT mode.");
                sb.AppendLine("Your job is to help the user solve the task end to end.");
                sb.AppendLine("Priorities:");
                sb.AppendLine("- Think briefly about the best approach.");
                sb.AppendLine("- Then provide implementation or actionable steps.");
                sb.AppendLine("- Balance planning and execution.");
                sb.AppendLine("- If the request is clearly asking for code, provide full code.");
                sb.AppendLine("- If the request is clearly asking for explanation, explain clearly.");
                sb.AppendLine("- Be practical, proactive, and solution-oriented.");
                sb.AppendLine("Tone:");
                sb.AppendLine("- Helpful, concise, and hands-on.");
                break;
        }

        return sb.ToString();
    }

    private void HandleKeyboard()
    {
        Event e = Event.current;

        if (e == null || e.type != EventType.KeyDown)
            return;

        bool isEnter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
        if (!isEnter)
            return;

        string focused = GUI.GetNameOfFocusedControl();
        if (focused != GetCurrentInputControlName())
            return;

        if (e.shift || isSending)
            return;

        _ = SendMessageAsync();
        e.Use();
    }

    private void ProcessDelayedScroll()
    {
        if (!shouldScrollToBottom)
            return;

        shouldScrollToBottom = false;
        chatScroll.y = 999999f;
        Repaint();
    }

    private void StartAudioRecording()
    {
        EnsureAIManager();

        if (aiManager == null)
        {
            chatHistory.Add(new ChatEntry("assistant", "Error: AIManager is not available for audio transcription."));
            shouldScrollToBottom = true;
            ForceFullEditorRefresh();
            return;
        }

        CleanupAudioRecorder();

        isAudioRecording = true;

        audioRecorderGO = new GameObject("CopilotAudioRecorder", typeof(SoundRecordSys))
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        audioRecorder = audioRecorderGO.GetComponent<SoundRecordSys>();
        audioRecorder.AIManagerObject = aiManager.gameObject;
        audioRecorder.maxRecordedTime = 25;
        audioRecorder.transcriptionLanguage = "es";
        audioRecorder.StartRecording();

        ForceFullEditorRefresh();
    }

    private void StopAudioRecording()
    {
        if (!isAudioRecording)
            return;

        isAudioRecording = false;
        _ = StopAudioAndFillInputAsync();
        ForceFullEditorRefresh();
    }

    private async Task StopAudioAndFillInputAsync()
    {
        if (audioRecorder == null)
        {
            CleanupAudioRecorder();
            return;
        }

        try
        {
            string text = await audioRecorder.EndRecordingAsync();
            pendingTranscribedText = text;
            pendingTranscriptionError = null;
            hasPendingUiApply = true;
        }
        catch (Exception ex)
        {
            Debug.LogError("[CopilotWindow] Error transcribing audio: " + ex);
            pendingTranscriptionError = ex.Message;
            pendingTranscribedText = null;
            hasPendingUiApply = true;
        }
        finally
        {
            CleanupAudioRecorder();
            ForceFullEditorRefresh();
        }
    }

    private void CleanupAudioRecorder()
    {
        audioRecorder = null;

        if (audioRecorderGO != null)
        {
            if (Application.isPlaying)
                Destroy(audioRecorderGO);
            else
                DestroyImmediate(audioRecorderGO);
        }

        audioRecorderGO = null;
        isAudioRecording = false;
    }

    private Texture2D MakeTex(Color color)
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    private void DestroyGeneratedTextures()
    {
        SafeDestroyTexture(backgroundTex);
        SafeDestroyTexture(headerTex);
        SafeDestroyTexture(chatPanelTex);
        SafeDestroyTexture(inputPanelTex);
        SafeDestroyTexture(assistantBubbleTex);
        SafeDestroyTexture(userBubbleTex);
        SafeDestroyTexture(buttonTex);
        SafeDestroyTexture(buttonHoverTex);
        SafeDestroyTexture(buttonActiveTex);
        SafeDestroyTexture(copyButtonTex);
        SafeDestroyTexture(copyButtonHoverTex);
        SafeDestroyTexture(copyButtonActiveTex);
        SafeDestroyTexture(inputFieldTex);
        SafeDestroyTexture(codePanelTex);
        SafeDestroyTexture(codeTextAreaTex);
        SafeDestroyTexture(modeToolbarTex);
        SafeDestroyTexture(secondaryButtonTex);
        SafeDestroyTexture(secondaryButtonHoverTex);
        SafeDestroyTexture(secondaryButtonActiveTex);
        SafeDestroyTexture(modeBadgeTex);

        backgroundTex = null;
        headerTex = null;
        chatPanelTex = null;
        inputPanelTex = null;
        assistantBubbleTex = null;
        userBubbleTex = null;
        buttonTex = null;
        buttonHoverTex = null;
        buttonActiveTex = null;
        copyButtonTex = null;
        copyButtonHoverTex = null;
        copyButtonActiveTex = null;
        inputFieldTex = null;
        codePanelTex = null;
        codeTextAreaTex = null;
        modeToolbarTex = null;
        secondaryButtonTex = null;
        secondaryButtonHoverTex = null;
        secondaryButtonActiveTex = null;
        modeBadgeTex = null;
    }

    private void SafeDestroyTexture(Texture2D tex)
    {
        if (tex == null)
            return;

        if (Application.isPlaying)
            Destroy(tex);
        else
            DestroyImmediate(tex);
    }
}

public class CreateScriptPopupWindow : EditorWindow
{
    private string code;
    private string scriptName;
    private Action<string> onAccept;

    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;
    private GUIStyle textFieldStyle;
    private GUIStyle acceptButtonStyle;
    private GUIStyle cancelButtonStyle;
    private bool stylesReady;
    private bool shouldFocusNameField = true;

    public static void ShowPopup(string code, string suggestedName, Action<string> onAccept)
    {
        CreateScriptPopupWindow window = CreateInstance<CreateScriptPopupWindow>();
        window.code = code;
        window.scriptName = suggestedName;
        window.onAccept = onAccept;

        window.titleContent = new GUIContent("Create Script");
        window.minSize = new Vector2(360, 140);
        window.maxSize = new Vector2(360, 140);

        Rect main = EditorGUIUtility.GetMainWindowPosition();
        Rect pos = new Rect(
            main.x + (main.width - 360f) * 0.5f,
            main.y + (main.height - 140f) * 0.5f,
            360f,
            140f
        );

        window.position = pos;
        window.ShowUtility();
        window.Focus();
    }

    private void InitStyles()
    {
        titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.fontSize = 14;
        titleStyle.alignment = TextAnchor.MiddleLeft;

        subtitleStyle = new GUIStyle(EditorStyles.label);
        subtitleStyle.wordWrap = true;
        subtitleStyle.fontSize = 11;

        textFieldStyle = new GUIStyle(EditorStyles.textField);
        textFieldStyle.fontSize = 12;

        acceptButtonStyle = new GUIStyle(GUI.skin.button);
        acceptButtonStyle.fontStyle = FontStyle.Bold;
        acceptButtonStyle.fixedHeight = 28;

        cancelButtonStyle = new GUIStyle(GUI.skin.button);
        cancelButtonStyle.fixedHeight = 28;

        stylesReady = true;
    }

    private void OnGUI()
    {
        if (!stylesReady)
        {
            InitStyles();
        }

        GUILayout.Space(10);
        EditorGUILayout.BeginVertical();

        GUILayout.Label("Create new script", titleStyle);
        GUILayout.Space(4);
        GUILayout.Label("Choose the file name for the new .cs script.", subtitleStyle);

        GUILayout.Space(10);
        GUILayout.Label("Script Name");
        GUI.SetNextControlName("ScriptNameField");
        scriptName = EditorGUILayout.TextField(scriptName, textFieldStyle);

        if (shouldFocusNameField)
        {
            shouldFocusNameField = false;
            EditorGUI.FocusTextInControl("ScriptNameField");
        }

        GUILayout.Space(14);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Cancel", cancelButtonStyle))
        {
            Close();
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Accept", acceptButtonStyle, GUILayout.Width(100)))
        {
            Accept();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        HandleKeyboard();
    }

    private void HandleKeyboard()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown)
            return;

        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
        {
            Accept();
            e.Use();
        }
        else if (e.keyCode == KeyCode.Escape)
        {
            Close();
            e.Use();
        }
    }

    private void Accept()
    {
        string finalName = scriptName != null ? scriptName.Trim() : "";

        if (string.IsNullOrWhiteSpace(finalName))
        {
            EditorUtility.DisplayDialog("Create Script", "Please enter a valid script name.", "OK");
            return;
        }

        onAccept?.Invoke(finalName);
        Close();
    }
}