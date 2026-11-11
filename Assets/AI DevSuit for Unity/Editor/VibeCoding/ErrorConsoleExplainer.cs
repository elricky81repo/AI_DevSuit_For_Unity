using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

public class ErrorConsoleExplainer : EditorWindow
{
    static readonly Regex FilePathRegex = new Regex(
        @"(?<path>.*?\.(?:cs|js|shader))\((?<line>\d+),(?<column>\d+)\)",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    private const string AI_PROVIDER_PREF = "AI_PROVIDER";
    private const string ANTHROPIC_API_KEY_PREF = "ANTHROPIC_API_KEY";

    private static ErrorConsoleExplainer _instance;

    public class LogItem
    {
        public string message;
        public string stackTrace;
        public LogType type;

        public string relativeFilePath;
        public string fullFilePath;
        public string originalCode;
        public string explanation;
    }

    private readonly List<LogItem> _logs = new List<LogItem>();
    private Vector2 _scroll;

    private bool _isFastFixRunning = false;
    private LogItem _currentFixLog = null;

    private bool _isExplainRunning = false;
    private LogItem _currentExplainLog = null;

    [MenuItem("Tools/AI DevSuite/Errors Explainer")]
    public static void OpenWindow()
    {
        var window = GetWindow<ErrorConsoleExplainer>("Errors Explainer");
        window.minSize = new Vector2(500, 300);
        window.Show();
    }

    void OnEnable()
    {
        _instance = this;
        Application.logMessageReceived += HandleLog;
        CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        CompilationPipeline.compilationStarted += OnCompilationStarted;
    }

    void OnDisable()
    {
        if (_instance == this)
            _instance = null;

        Application.logMessageReceived -= HandleLog;
        CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
        CompilationPipeline.compilationStarted -= OnCompilationStarted;
    }

    private void OnCompilationStarted(object context)
    {
        _logs.RemoveAll(l =>
            !string.IsNullOrEmpty(l.message) &&
            l.message.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 &&
            !string.IsNullOrEmpty(l.relativeFilePath));
        Repaint();
    }

    private void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
    {
        foreach (var msg in messages)
        {
            if (msg.type != CompilerMessageType.Error)
                continue;

            string filePart = string.IsNullOrEmpty(msg.file)
                ? assemblyPath
                : msg.file.Replace("\\", "/");

            string formattedMessage =
                $"{filePart}({msg.line},{msg.column}): error {msg.message}";

            bool alreadyExists = _logs.Any(l =>
                string.Equals(l.message, formattedMessage, StringComparison.Ordinal));

            if (alreadyExists)
                continue;

            var item = new LogItem
            {
                message = formattedMessage,
                stackTrace = "",
                type = LogType.Error
            };

            ExtractCodeMetadata(item);
            _logs.Add(item);
        }

        Repaint();
    }

    private void HandleLog(string message, string stackTrace, LogType type)
    {
        if (type != LogType.Exception)
            return;

        bool alreadyExists = _logs.Any(l =>
            string.Equals(l.message, message, StringComparison.Ordinal) &&
            string.Equals(l.stackTrace, stackTrace, StringComparison.Ordinal));

        if (alreadyExists)
            return;

        var item = new LogItem
        {
            message = message,
            stackTrace = stackTrace,
            type = type
        };

        ExtractCodeMetadata(item);
        _logs.Add(item);
        Repaint();
    }

    private void ExtractCodeMetadata(LogItem log)
    {
        var textToSearch = (log.message ?? "") + "\n" + (log.stackTrace ?? "");
        var m = FilePathRegex.Match(textToSearch);

        if (!m.Success)
            return;

        log.relativeFilePath = m.Groups["path"].Value.Replace("\\", "/").Trim();

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        log.fullFilePath = Path.Combine(projectRoot, log.relativeFilePath);

        if (File.Exists(log.fullFilePath))
            log.originalCode = File.ReadAllText(log.fullFilePath);
    }

    void OnGUI()
    {
        bool providerReady = HasApiKeyForCurrentProvider();

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
        {
            CompilationPipeline.RequestScriptCompilation();
        }

        if (GUILayout.Button("Clear", EditorStyles.toolbarButton))
        {
            _logs.Clear();
            Repaint();
        }

        EditorGUILayout.EndHorizontal();

        DrawGlobalProviderWarningBanner();
        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("Errors Explainer", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        if (_isExplainRunning)
        {
            Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
            float prog = Mathf.PingPong((float)EditorApplication.timeSinceStartup, 1f);
            EditorGUI.ProgressBar(progressRect, prog, "Generating explanation...");
            EditorGUILayout.Space(6);
            Repaint();
        }

        if (_isFastFixRunning)
        {
            Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
            float prog = Mathf.PingPong((float)EditorApplication.timeSinceStartup, 1f);
            EditorGUI.ProgressBar(progressRect, prog, "Applying Fast Fix...");
            EditorGUILayout.Space(6);
            Repaint();
        }

        if (_logs.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No errors were loaded. Tap Refresh to recompile and catch compilation errors.",
                MessageType.Info);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        foreach (var log in _logs)
        {
            var style = new GUIStyle(EditorStyles.wordWrappedLabel);
            style.normal.textColor = log.type == LogType.Exception ? Color.yellow : Color.white;

            var icon = EditorGUIUtility.IconContent("console.erroricon");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            EditorGUILayout.LabelField(log.message, style);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(!providerReady || _isFastFixRunning || _isExplainRunning))
            {
                if (GUILayout.Button("Explain to me!"))
                    ShowErrorExplanationAsync(log);
            }

            using (new EditorGUI.DisabledScope(!providerReady || string.IsNullOrEmpty(log.originalCode) || _isFastFixRunning || _isExplainRunning))
            {
                if (GUILayout.Button("Try Fast Fix"))
                    TryFastFixAsync(log);
            }

            if (_isExplainRunning && _currentExplainLog == log)
            {
                Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
                float prog = Mathf.PingPong((float)EditorApplication.timeSinceStartup, 1f);
                EditorGUI.ProgressBar(progressRect, prog, "Generating explanation...");
                GUILayout.Space(6);
                Repaint();
            }

            if (_isFastFixRunning && _currentFixLog == log)
            {
                Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
                float prog = Mathf.PingPong((float)EditorApplication.timeSinceStartup, 1f);
                EditorGUI.ProgressBar(progressRect, prog, "Applying Fast Fix...");
                GUILayout.Space(6);
                Repaint();
            }

            EditorGUILayout.Space(12);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawGlobalProviderWarningBanner()
    {
        AIManager.AIProviderType provider = GetCurrentProvider();

        if (HasApiKeyForProvider(provider))
            return;

        string title;
        string body;

        if (provider == AIManager.AIProviderType.OpenAI)
        {
            title = "OPENAI API KEY NOT SET";
            body = "OpenAI is currently selected, but no API key is configured. You must complete Setup before using Error Explainer or Fast Fix.";
        }
        else if (provider == AIManager.AIProviderType.Anthropic)
        {
            title = "ANTHROPIC API KEY NOT SET";
            body = "Anthropic is currently selected, but no API key is configured. You must complete Setup before using Error Explainer or Fast Fix.";
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

    private static bool HasApiKeyForCurrentProvider()
    {
        return HasApiKeyForProvider(GetCurrentProvider());
    }

    private static bool HasApiKeyForProvider(AIManager.AIProviderType provider)
    {
        if (provider == AIManager.AIProviderType.OpenAI)
        {
            return !string.IsNullOrEmpty(VibeToolsWindow.GPTPrefs.ApiKey);
        }

        if (provider == AIManager.AIProviderType.Anthropic)
        {
            string anthropicKey = EditorPrefs.GetString(ANTHROPIC_API_KEY_PREF, "");
            return !string.IsNullOrEmpty(anthropicKey);
        }

        return false;
    }

    private async void ShowErrorExplanationAsync(LogItem log)
    {
        if (!HasApiKeyForCurrentProvider())
        {
            EditorUtility.DisplayDialog(
                "API Key Not Set",
                "The selected AI provider does not have a configured API key. Open Setup before using Error Explainer.",
                "OK");
            return;
        }

        if (_isExplainRunning || _isFastFixRunning)
            return;

        _isExplainRunning = true;
        _currentExplainLog = log;
        Repaint();

        try
        {
            var manager = AIManager.EnsureManagerInstanceForEditor();

            string instructionContent = string.IsNullOrEmpty(log.originalCode)
                ? "Eres un asistente experto en errores de Unity. Resume el error en 2 oraciones. En ingles"
                : "Eres un asistente experto en Unity y C#. Resume el error en 2 oraciones y da una corrección breve. En ingles.";

            string userContent =
                $"Error:\n{log.message}\n\nStackTrace:\n{log.stackTrace}";

            if (!string.IsNullOrEmpty(log.originalCode))
            {
                userContent +=
                    $"\n\nCódigo fuente ({Path.GetFileName(log.relativeFilePath)}):\n{log.originalCode}";
            }

            var resp = await manager.GetConfiguredEditorCompletionResponse(userContent, instructionContent);

            if (resp != null && resp.Count > 0)
            {
                log.explanation = resp.Last().content.Trim();
                ExplanationWindow.ShowExplanation(log, log.explanation);
            }

            Repaint();
        }
        catch (Exception ex)
        {
            Debug.LogError("Error generating explanation: " + ex.Message);
            EditorUtility.DisplayDialog("Explain to me!", "An error occurred while trying to generate the explanation.", "OK");
        }
        finally
        {
            _isExplainRunning = false;
            _currentExplainLog = null;
            Repaint();
        }
    }

    private async void TryFastFixAsync(LogItem log)
    {
        if (!HasApiKeyForCurrentProvider())
        {
            EditorUtility.DisplayDialog(
                "API Key Not Set",
                "The selected AI provider does not have a configured API key. Open Setup before using Fast Fix.",
                "OK");
            return;
        }

        if (string.IsNullOrEmpty(log.fullFilePath) || string.IsNullOrEmpty(log.originalCode))
            return;

        if (_isFastFixRunning || _isExplainRunning)
            return;

        _isFastFixRunning = true;
        _currentFixLog = log;
        Repaint();
        ExplanationWindow.RepaintIfOpen();

        try
        {
            var manager = AIManager.EnsureManagerInstanceForEditor();

            string instructionContent =
                "Eres un asistente experto en C#. Devuelve el archivo .cs completo corregido, sin explicaciones.";

            string userContent =
                $"Descripción del error:\n{log.message}\n\n" +
                $"Explicación:\n{log.explanation}\n\n" +
                $"Código original:\n{log.originalCode}";

            var resp = await manager.GetConfiguredEditorCompletionResponse(userContent, instructionContent);
            if (resp == null || resp.Count == 0)
                return;

            var fixedCode = resp.Last().content.Trim();

            var fencePattern = new Regex(@"^```(?:\w*\r?\n)?([\s\S]*?)```$", RegexOptions.Compiled);
            var m = fencePattern.Match(fixedCode);
            if (m.Success)
                fixedCode = m.Groups[1].Value;

            File.WriteAllText(log.fullFilePath, fixedCode);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Fast Fix", "The file was corrected and saved.", "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError("Error en Fast Fix: " + ex.Message);
            EditorUtility.DisplayDialog("Fast Fix", "An error occurred while trying to correct the file.", "OK");
        }
        finally
        {
            _isFastFixRunning = false;
            _currentFixLog = null;
            Repaint();
            ExplanationWindow.RepaintIfOpen();
        }
    }

    public class ExplanationWindow : EditorWindow
    {
        private static ExplanationWindow _window;
        private static string _explanation;
        private static LogItem _log;

        private Vector2 _scrollPos;

        public static void ShowExplanation(LogItem log, string explanation)
        {
            _log = log;
            _explanation = explanation;

            _window = GetWindow<ExplanationWindow>("Error Explanation");
            _window.minSize = new Vector2(450, 240);
            _window.Show();
            _window.Repaint();
        }

        public static void RepaintIfOpen()
        {
            if (_window != null)
                _window.Repaint();
        }

        void OnGUI()
        {
            DrawPopupProviderWarningBanner();
            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("Error explanation:", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            EditorGUILayout.LabelField(_explanation ?? "", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            bool providerReady = HasApiKeyForCurrentProvider();

            bool canFastFix =
                providerReady &&
                _log != null &&
                !string.IsNullOrEmpty(_log.originalCode) &&
                _instance != null;

            bool isBusy =
                _instance != null &&
                (_instance._isFastFixRunning || _instance._isExplainRunning);

            if (_instance != null && _instance._isFastFixRunning && _instance._currentFixLog == _log)
            {
                Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
                float prog = Mathf.PingPong((float)EditorApplication.timeSinceStartup, 1f);
                EditorGUI.ProgressBar(progressRect, prog, "Applying Fast Fix...");
                GUILayout.Space(6);
                Repaint();
            }

            using (new EditorGUI.DisabledScope(!canFastFix || isBusy))
            {
                if (GUILayout.Button("Try Fast Fix!", GUILayout.Height(28)))
                {
                    _instance.TryFastFixAsync(_log);
                }
            }

            if (!providerReady)
            {
                EditorGUILayout.HelpBox(
                    "The selected AI provider does not have a configured API key. Open Setup before using Fast Fix.",
                    MessageType.Warning);
            }
            else if (_log == null || string.IsNullOrEmpty(_log.originalCode))
            {
                EditorGUILayout.HelpBox(
                    "Fast Fix is only available when the source file was detected correctly.",
                    MessageType.Info);
            }
        }

        private void DrawPopupProviderWarningBanner()
        {
            AIManager.AIProviderType provider = GetCurrentProvider();

            if (HasApiKeyForProvider(provider))
                return;

            string title;
            string body;

            if (provider == AIManager.AIProviderType.OpenAI)
            {
                title = "OPENAI API KEY NOT SET";
                body = "OpenAI is currently selected, but no API key is configured.";
            }
            else if (provider == AIManager.AIProviderType.Anthropic)
            {
                title = "ANTHROPIC API KEY NOT SET";
                body = "Anthropic is currently selected, but no API key is configured.";
            }
            else
            {
                title = "AI PROVIDER NOT READY";
                body = "The selected AI provider is not configured yet.";
            }

            Color previousBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.95f, 0.35f, 0.35f);

            EditorGUILayout.BeginVertical("box");

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.normal.textColor = Color.white;
            titleStyle.fontSize = 12;

            GUIStyle bodyStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
            bodyStyle.normal.textColor = Color.white;

            EditorGUILayout.LabelField("⚠ " + title, titleStyle);
            EditorGUILayout.LabelField(body, bodyStyle);
            GUILayout.Space(6);

            if (GUILayout.Button("Open Setup", GUILayout.Height(24)))
            {
                SetupWindow.ShowWindow();
            }

            EditorGUILayout.EndVertical();

            GUI.backgroundColor = previousBg;
        }
    }
}