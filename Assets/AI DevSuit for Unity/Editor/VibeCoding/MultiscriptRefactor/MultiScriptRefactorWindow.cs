using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class MultiScriptRefactorWindow : EditorWindow
{
    private Vector2 _scriptsScroll;
    private Vector2 _resultsScroll;

    private readonly List<MonoScript> _selectedScripts = new List<MonoScript>();

    private string _goal = "Describe the refactor or functionality to be added between several scripts.";
    private bool _analyzeOnly = false;
    private bool _createBackups = true;

    private bool _isProcessing = false;
    private string _status = string.Empty;

    private MultiScriptRefactorResult _result;
    private string _lastPromptPreview = string.Empty;

    private const string ANTHROPIC_MODEL_PREF = "ANTHROPIC_MODEL";
    private const string ANTHROPIC_API_KEY_PREF = "ANTHROPIC_API_KEY";
    private const string AI_PROVIDER_PREF = "AI_PROVIDER";
    private GptModel _modelEnum;

    private const int MaxAttemptsPerFile = 2;
    private const int LargeFileCharacterThreshold = 12000;

    private enum AnthropicModel
    {
        ClaudeHaiku45,
        ClaudeSonnet45,
        ClaudeSonnet46
    }

    [MenuItem("Tools/AI DevSuite/Multi Script Refactor")]
    public static void ShowWindow()
    {
        var window = GetWindow<MultiScriptRefactorWindow>("Multi Script Refactor");
        window.minSize = new Vector2(1200, 750);
        window.position = new Rect(80, 60, 1400, 900);
    }

    private void OnEnable()
    {
        _modelEnum = LoadSavedModel();
    }

    private void OnGUI()
    {
        bool providerReady = HasApiKeyForCurrentProvider();

        DrawHeader();

        if (!providerReady)
        {
            DrawDisabledWindow();
            return;
        }

        EditorGUILayout.BeginHorizontal();
        DrawLeftPanel();
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();

        DrawBottomBar();
    }

    private void DrawHeader()
    {
        EditorGUILayout.Space();

        DrawGlobalProviderWarningBanner();
        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("Multi Script Refactor - V1.0.0", EditorStyles.boldLabel);

        DrawGlobalAIHeader();

        EditorGUILayout.HelpBox(
            "Drag and drop .cs scripts here!, define a global goal and get a file-coordinated proposal.",
            MessageType.Info
        );

        EditorGUILayout.Space();
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
            body = "OpenAI is currently selected, but no API key is configured. You must complete Setup before using Multi Script Refactor.";
        }
        else if (provider == AIManager.AIProviderType.Anthropic)
        {
            title = "ANTHROPIC API KEY NOT SET";
            body = "Anthropic is currently selected, but no API key is configured. You must complete Setup before using Multi Script Refactor.";
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

    private void DrawDisabledWindow()
    {
        Color prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.45f);

        EditorGUILayout.BeginHorizontal();
        DrawDisabledLeftPanel();
        DrawDisabledRightPanel();
        EditorGUILayout.EndHorizontal();

        DrawDisabledBottomBar();

        GUI.color = prev;
    }

    private void DrawDisabledLeftPanel()
    {
        EditorGUILayout.BeginVertical("box", GUILayout.Width(position.width * 0.42f));

        EditorGUILayout.LabelField("Included Scripts", EditorStyles.boldLabel);

        GUILayout.Box("Drag .cs scripts from the Project here", GUILayout.Height(60), GUILayout.ExpandWidth(true));

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Button("Add selected scripts");
            GUILayout.Button("Clean");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUILayout.BeginScrollView(Vector2.zero, GUILayout.Height(220));
            EditorGUILayout.HelpBox("Scripts will appear here after setup.", MessageType.None);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Valid Scripts: 0");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Global objective", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(
                "Describe the refactor or functionality to be added between several scripts.",
                GUILayout.ExpandWidth(true),
                GUILayout.MinHeight(140),
                GUILayout.MaxHeight(position.height * 0.35f)
            );

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);

            EditorGUILayout.Toggle("Only Analyze", false);
            EditorGUILayout.Toggle("Create backups .bak", true);

            EditorGUILayout.Space();
            GUILayout.Button("Copy last promp");
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawDisabledRightPanel()
    {
        EditorGUILayout.BeginVertical("box", GUILayout.ExpandWidth(true));

        EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox("There are no results yet. Add scripts, enter the goal, and tap Refactor.", MessageType.None);

        EditorGUILayout.EndVertical();
    }

    private void DrawDisabledBottomBar()
    {
        EditorGUILayout.Space();
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.HelpBox(
            "The selected AI provider does not have a configured API key. Open Setup before running this tool.",
            MessageType.Warning
        );

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.BeginHorizontal();

            GUILayout.Button(_analyzeOnly ? "Analyze!" : "Search the solution!", GUILayout.Height(40));

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private AIManager.AIProviderType GetCurrentProvider()
    {
        string savedProvider = EditorPrefs.GetString(AI_PROVIDER_PREF, "OpenAI");

        if (System.Enum.TryParse(savedProvider, out AIManager.AIProviderType parsed))
            return parsed;

        return AIManager.AIProviderType.OpenAI;
    }

    private bool HasApiKeyForCurrentProvider()
    {
        return HasApiKeyForProvider(GetCurrentProvider());
    }

    private bool HasApiKeyForProvider(AIManager.AIProviderType provider)
    {
        if (provider == AIManager.AIProviderType.OpenAI)
        {
            return !string.IsNullOrWhiteSpace(VibeToolsWindow.GPTPrefs.ApiKey);
        }

        if (provider == AIManager.AIProviderType.Anthropic)
        {
            string anthropicKey = EditorPrefs.GetString(ANTHROPIC_API_KEY_PREF, "");
            return !string.IsNullOrWhiteSpace(anthropicKey);
        }

        return false;
    }

    private static GptModel LoadSavedModel()
    {
        string savedValue = EditorPrefs.GetString("GPTPlugin_Model", GptModel.Gpt4oMini.ToString());

        if (System.Enum.TryParse(savedValue, out GptModel parsed))
            return parsed;

        return GptModel.Gpt4oMini;
    }

    private void SaveModelPref(GptModel model)
    {
        EditorPrefs.SetString("GPTPlugin_Model", model.ToString());
    }

    private AnthropicModel ParseAnthropicModel(string value)
    {
        if (System.Enum.TryParse(value, out AnthropicModel parsed))
            return parsed;

        return AnthropicModel.ClaudeHaiku45;
    }

    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical("box", GUILayout.Width(position.width * 0.42f));

        EditorGUILayout.LabelField("Included Scripts", EditorStyles.boldLabel);

        DrawDropArea();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add selected scripts"))
        {
            AddCurrentSelection();
        }

        if (GUILayout.Button("Clean"))
        {
            _selectedScripts.Clear();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        _scriptsScroll = EditorGUILayout.BeginScrollView(_scriptsScroll, GUILayout.Height(220));

        for (int i = 0; i < _selectedScripts.Count; i++)
        {
            EditorGUILayout.BeginHorizontal("box");

            EditorGUILayout.BeginVertical();
            _selectedScripts[i] = (MonoScript)EditorGUILayout.ObjectField(_selectedScripts[i], typeof(MonoScript), false);

            if (_selectedScripts[i] != null)
            {
                string path = AssetDatabase.GetAssetPath(_selectedScripts[i]);
                EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();

            if (GUILayout.Button("X", GUILayout.Width(28)))
            {
                _selectedScripts.RemoveAt(i);
                i--;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        int validScriptCount = _selectedScripts.Count(s => s != null);
        EditorGUILayout.LabelField($"Valid Scripts: {validScriptCount}");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Global objective", EditorStyles.boldLabel);

        GUIStyle wrappedTextArea = new GUIStyle(EditorStyles.textArea);
        wrappedTextArea.wordWrap = true;

        _goal = EditorGUILayout.TextArea(
            _goal,
            wrappedTextArea,
            GUILayout.ExpandWidth(true),
            GUILayout.MinHeight(140),
            GUILayout.MaxHeight(position.height * 0.35f)
        );

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);

        _analyzeOnly = EditorGUILayout.Toggle("Only Analyze", _analyzeOnly);
        _createBackups = EditorGUILayout.Toggle("Create backups .bak", _createBackups);

        if (!string.IsNullOrWhiteSpace(_lastPromptPreview))
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("Copy last promp"))
            {
                EditorGUIUtility.systemCopyBuffer = _lastPromptPreview;
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical("box", GUILayout.ExpandWidth(true));

        EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);

        _resultsScroll = EditorGUILayout.BeginScrollView(_resultsScroll);

        if (_result == null)
        {
            EditorGUILayout.HelpBox("There are no results yet. Add scripts, enter the goal, and tap Refactor.", MessageType.None);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(_result.ErrorMessage))
            {
                EditorGUILayout.HelpBox(_result.ErrorMessage, MessageType.Error);
            }

            if (!string.IsNullOrWhiteSpace(_result.Plan))
            {
                EditorGUILayout.LabelField("Plan", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(_result.Plan, GUILayout.MinHeight(120));
                EditorGUILayout.Space();
            }

            if (_result.Files != null && _result.Files.Count > 0)
            {
                foreach (var file in _result.Files)
                {
                    DrawFileResult(file);
                    EditorGUILayout.Space();
                }
            }
            else if (_result.Success)
            {
                EditorGUILayout.HelpBox("No modified files were returned", MessageType.Warning);
            }
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    private void DrawFileResult(RefactoredFileResult file)
    {
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.LabelField(file.FileName, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(file.AssetPath, EditorStyles.miniLabel);

        if (file.IsNew)
        {
            EditorGUILayout.HelpBox("Este archivo no existe en el proyecto. Se creará cuando apliques el cambio.", MessageType.Info);
        }

        float codeHeight = 250f;

        file.ScrollPosition = EditorGUILayout.BeginScrollView(
            file.ScrollPosition,
            GUILayout.Height(codeHeight)
        );

        GUIStyle codeStyle = new GUIStyle(EditorStyles.textArea);
        codeStyle.wordWrap = false;

        file.UpdatedCode = EditorGUILayout.TextArea(
            file.UpdatedCode,
            codeStyle,
            GUILayout.ExpandHeight(true)
        );

        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Copy"))
        {
            EditorGUIUtility.systemCopyBuffer = file.UpdatedCode ?? string.Empty;
        }

        if (!file.WasApplied)
        {
            string buttonLabel = file.IsNew ? "Create new file" : "Apply change";
            if (GUILayout.Button(buttonLabel))
            {
                bool ok = MultiScriptApplyService.ApplyFile(file, _createBackups);
                if (ok)
                {
                    file.WasApplied = true;
                    ShowNotification(new GUIContent($"Applied: {file.FileName}"));
                }
            }
        }
        else
        {
            GUILayout.Label("✓ Applied", EditorStyles.boldLabel, GUILayout.Width(90));
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private double _lastAnimTime;
    private float _progressAnim = 0f;

    private void DrawBottomBar()
    {
        EditorGUILayout.Space();
        EditorGUILayout.BeginVertical("box");

        bool providerReady = HasApiKeyForCurrentProvider();

        if (_isProcessing)
        {
            double now = EditorApplication.timeSinceStartup;
            float deltaTime = 0f;

            if (_lastAnimTime > 0)
            {
                deltaTime = (float)(now - _lastAnimTime);
            }

            _lastAnimTime = now;

            _progressAnim += deltaTime * 0.35f;
            if (_progressAnim > 1f) _progressAnim -= 1f;

            Rect rect = GUILayoutUtility.GetRect(18, 18, "TextField");

            EditorGUI.ProgressBar(
                rect,
                Mathf.PingPong(_progressAnim, 1f),
                string.IsNullOrWhiteSpace(_status) ? "Processing..." : _status
            );

            GUILayout.Space(6);

            EditorGUILayout.LabelField(
                "This action may take several minutes depending on the complexity and number of scripts.",
                EditorStyles.wordWrappedMiniLabel
            );

            Repaint();
        }
        else if (!string.IsNullOrWhiteSpace(_status))
        {
            EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
        }

        if (!providerReady)
        {
            EditorGUILayout.HelpBox(
                "The selected AI provider does not have a configured API key. Open Setup before running this tool.",
                MessageType.Warning
            );
        }

        EditorGUILayout.BeginHorizontal();

        GUI.enabled = !_isProcessing && providerReady;

        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = _analyzeOnly
            ? new Color(0.85f, 0.75f, 0.25f)
            : new Color(0.25f, 0.65f, 0.95f);

        if (GUILayout.Button(_analyzeOnly ? "Analyze!" : "Search the solution!", GUILayout.Height(40)))
        {
            _ = RunRefactorAsync();
        }

        GUI.backgroundColor = oldColor;
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawDropArea()
    {
        Rect dropArea = GUILayoutUtility.GetRect(0f, 60f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drag .cs scripts from the Project here");

        Event evt = Event.current;
        if (!dropArea.Contains(evt.mousePosition))
            return;

        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                bool hasMonoScript = DragAndDrop.objectReferences.Any(o => o is MonoScript);

                DragAndDrop.visualMode = hasMonoScript
                    ? DragAndDropVisualMode.Copy
                    : DragAndDropVisualMode.Rejected;

                if (evt.type == EventType.DragPerform && hasMonoScript)
                {
                    DragAndDrop.AcceptDrag();

                    foreach (Object obj in DragAndDrop.objectReferences)
                    {
                        if (obj is MonoScript script && !_selectedScripts.Contains(script))
                        {
                            _selectedScripts.Add(script);
                        }
                    }
                }

                evt.Use();
                break;
        }
    }

    private void AddCurrentSelection()
    {
        var selectedScripts = Selection.objects
            .OfType<MonoScript>()
            .Where(s => s != null);

        foreach (var script in selectedScripts)
        {
            if (!_selectedScripts.Contains(script))
                _selectedScripts.Add(script);
        }
    }

    private async Task RunRefactorAsync()
    {
        if (!HasApiKeyForCurrentProvider())
        {
            EditorUtility.DisplayDialog(
                "API Key Not Set",
                "The selected AI provider does not have a configured API key. Open Setup before running Multi Script Refactor.",
                "OK");
            return;
        }

        var cleanScripts = _selectedScripts
            .Where(s => s != null)
            .Distinct()
            .ToList();

        if (cleanScripts.Count == 0)
        {
            EditorUtility.DisplayDialog("Attention", "Add at least one valid script.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(_goal))
        {
            EditorUtility.DisplayDialog("Attention", "Write an overall goal.", "OK");
            return;
        }

        _isProcessing = true;
        _status = "Preparing context...";
        _result = null;
        Repaint();

        await Task.Delay(50);

        var context = MultiScriptContextBuilder.Build(cleanScripts);

        if (context.Count == 0)
        {
            _isProcessing = false;
            _status = string.Empty;
            EditorUtility.DisplayDialog("Error", "No valid scripts could be read.", "OK");
            Repaint();
            return;
        }

        var planRequest = new MultiScriptRefactorRequest
        {
            Goal = _goal,
            Scripts = context,
            AnalyzeOnly = true,
            ReturnFullFiles = false
        };

        _status = "Generating global plan...";
        Repaint();

        string planPrompt = MultiScriptPromptBuilder.BuildPlanPrompt(planRequest);
        _lastPromptPreview = planPrompt;

        string planResponse = await RequestAIAsync(planPrompt, true);

        if (string.IsNullOrWhiteSpace(planResponse))
        {
            _result = new MultiScriptRefactorResult
            {
                Success = false,
                ErrorMessage = "La IA no devolvió el plan global."
            };

            _isProcessing = false;
            _status = "Failed";
            Repaint();
            return;
        }

        if (_analyzeOnly)
        {
            _result = new MultiScriptRefactorResult
            {
                RawResponse = planResponse,
                Plan = planResponse,
                Success = true
            };

            _isProcessing = false;
            _status = "End";
            Repaint();
            return;
        }

        List<ScriptContextData> scriptsToGenerate = ExtractScriptsFromPlan(planResponse, context);

        if (scriptsToGenerate.Count == 0)
        {
            _result = new MultiScriptRefactorResult
            {
                RawResponse = planResponse,
                Plan = planResponse,
                Success = false,
                ErrorMessage = "No se detectaron archivos a modificar dentro del plan global."
            };

            _isProcessing = false;
            _status = "End";
            Repaint();
            return;
        }

        List<RefactoredFileResult> allFiles = new List<RefactoredFileResult>();
        List<string> failedFiles = new List<string>();

        foreach (ScriptContextData script in scriptsToGenerate)
        {
            RefactoredFileResult generatedFile = await GenerateFileWithRetry(script, planResponse);

            if (generatedFile != null)
            {
                if (!allFiles.Any(f => string.Equals(f.AssetPath, generatedFile.AssetPath, System.StringComparison.OrdinalIgnoreCase)))
                {
                    allFiles.Add(generatedFile);
                }
            }
            else
            {
                failedFiles.Add(script.FileName);
            }
        }

        string errorMessage = null;

        if (allFiles.Count == 0)
        {
            errorMessage = "No se pudieron generar archivos completos desde el plan global.";
        }
        else if (failedFiles.Count > 0)
        {
            errorMessage = "Algunos archivos no pudieron generarse completos después de 2 intentos: " + string.Join(", ", failedFiles);
        }

        _result = new MultiScriptRefactorResult
        {
            RawResponse = planResponse,
            Plan = planResponse,
            Files = allFiles,
            Success = allFiles.Count > 0,
            ErrorMessage = errorMessage
        };

        _isProcessing = false;
        _status = "End";
        Repaint();
    }

    private async Task<RefactoredFileResult> GenerateFileWithRetry(ScriptContextData script, string globalPlan)
    {
        bool isLargeFile = IsLargeFile(script);

        for (int attempt = 1; attempt <= MaxAttemptsPerFile; attempt++)
        {
            string perFilePlan;
            bool compactSource;

            if (isLargeFile)
            {
                _status = attempt == 1
                    ? $"Planning {script.FileName}..."
                    : $"Retry planning {script.FileName} ({attempt}/{MaxAttemptsPerFile})...";
                Repaint();

                perFilePlan = await GetPerFilePlanAsync(script, globalPlan, attempt);
                compactSource = true;
            }
            else
            {
                perFilePlan = BuildPerFilePlanSummary(globalPlan, script, attempt == 2);
                compactSource = attempt == 2;
            }

            if (string.IsNullOrWhiteSpace(perFilePlan))
            {
                Debug.LogWarning($"[MultiScriptRefactor] Empty per-file plan for {script.FileName} on attempt {attempt}");
                continue;
            }

            _status = attempt == 1
                ? $"Generating {script.FileName}..."
                : $"Retry generating {script.FileName} ({attempt}/{MaxAttemptsPerFile})...";
            Repaint();

            string filePrompt = MultiScriptPromptBuilder.BuildFilePrompt(perFilePlan, script, compactSource);
            string fileResponse = await RequestAIAsync(filePrompt, false);

            MultiScriptRefactorResult parsed = MultiScriptResponseParser.Parse(fileResponse);

            if (parsed == null || parsed.Files == null || parsed.Files.Count == 0)
            {
                Debug.LogWarning($"[MultiScriptRefactor] Empty/invalid response for {script.FileName} on attempt {attempt}");
                continue;
            }

            RefactoredFileResult file = parsed.Files.FirstOrDefault(f =>
                string.Equals(NormalizePath(f.AssetPath), NormalizePath(script.AssetPath), System.StringComparison.OrdinalIgnoreCase));

            if (file == null)
            {
                file = parsed.Files.FirstOrDefault(f =>
                    string.Equals(f.FileName, script.FileName, System.StringComparison.OrdinalIgnoreCase));
            }

            if (file == null)
            {
                Debug.LogWarning($"[MultiScriptRefactor] No matching file block for {script.FileName} on attempt {attempt}");
                continue;
            }

            if (!IsValidGeneratedFile(file, script))
            {
                Debug.LogWarning($"[MultiScriptRefactor] Incomplete/invalid code for {script.FileName} on attempt {attempt}");
                continue;
            }

            return file;
        }

        Debug.LogError($"[MultiScriptRefactor] Failed to generate complete file after retries: {script.FileName}");
        return null;
    }

    private async Task<string> GetPerFilePlanAsync(ScriptContextData script, string globalPlan, int attempt)
    {
        bool ultraCompact = attempt >= 2;
        string globalPlanSummary = BuildPerFilePlanSummary(globalPlan, script, ultraCompact);
        string prompt = BuildPerFilePlanPrompt(script, globalPlanSummary, ultraCompact);
        return await RequestAIAsync(prompt, true);
    }

    private string BuildPerFilePlanPrompt(ScriptContextData script, string globalPlanSummary, bool ultraCompact)
    {
        string sourceForPlanning = ultraCompact
            ? BuildCompactFilePlanningSource(script.FullContent)
            : script.FullContent;

        return
$@"OBJETIVO GLOBAL:
{_goal}

PLAN GLOBAL RELEVANTE:
{globalPlanSummary}

ARCHIVO OBJETIVO:
{script.FileName}
RUTA: {script.AssetPath}

CONTENIDO ACTUAL:
```csharp
{sourceForPlanning}

Devuelve SOLO:
PLAN:

cambios concretos para este archivo

CONTRATO GLOBAL:

métodos, firmas, clases o dependencias necesarias para este archivo

REGLAS DE CAMBIO:

decisiones a aplicar en este archivo

IMPORTANTE:

No generes código
Nombra el archivo exacto: {script.AssetPath}

Sé breve y específico para este archivo";
    }

    private bool IsLargeFile(ScriptContextData script)
    {
        if (script == null)
            return false;

        return script.CharacterCount >= LargeFileCharacterThreshold || script.LineCount >= 250;
    }

    private bool IsValidGeneratedFile(RefactoredFileResult file, ScriptContextData sourceScript)
    {
        if (file == null || string.IsNullOrWhiteSpace(file.UpdatedCode))
            return false;

        string code = file.UpdatedCode;

        if (ContainsForbiddenPlaceholder(code))
            return false;

        if (!HasBalancedBraces(code))
            return false;

        if (code.Length < 120)
            return false;

        string className = System.IO.Path.GetFileNameWithoutExtension(sourceScript.FileName);
        if (!string.IsNullOrWhiteSpace(className) &&
            code.IndexOf(className, System.StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        return true;
    }

    private bool ContainsForbiddenPlaceholder(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        string lower = text.ToLowerInvariant();

        string[] forbidden =
        {
            "rest of existing code",
            "resto del código existente",
            "resto del codigo existente",
            "same as before",
            "unchanged code",
            "existing implementation",
            "mantener el resto",
            "[código completo",
            "[codigo completo",
            "// código completo",
            "// codigo completo",
            "```text"
        };

        foreach (string item in forbidden)
        {
            if (lower.Contains(item))
                return true;
        }

        return false;
    }

    private bool HasBalancedBraces(string code)
    {
        int balance = 0;

        for (int i = 0; i < code.Length; i++)
        {
            if (code[i] == '{')
                balance++;
            else if (code[i] == '}')
                balance--;
        }

        return balance == 0;
    }

    private List<ScriptContextData> ExtractScriptsFromPlan(string plan, List<ScriptContextData> context)
    {
        var files = new List<ScriptContextData>();

        if (string.IsNullOrWhiteSpace(plan) || context == null || context.Count == 0)
            return files;

        foreach (ScriptContextData script in context)
        {
            if (script == null || string.IsNullOrWhiteSpace(script.FileName) || string.IsNullOrWhiteSpace(script.AssetPath))
                continue;

            bool matchByPath = plan.IndexOf(script.AssetPath, System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool matchByName = plan.IndexOf(script.FileName, System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (matchByPath || matchByName)
            {
                if (!files.Any(f => string.Equals(NormalizePath(f.AssetPath), NormalizePath(script.AssetPath), System.StringComparison.OrdinalIgnoreCase)))
                    files.Add(script);
            }
        }

        return files;
    }

    private string BuildPerFilePlanSummary(string fullPlan, ScriptContextData script, bool ultraCompact)
    {
        if (string.IsNullOrWhiteSpace(fullPlan) || script == null)
            return string.Empty;

        List<string> lines = fullPlan
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

        List<string> selected = new List<string>();

        foreach (string rawLine in lines)
        {
            string line = rawLine?.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            bool keep =
                line.StartsWith("PLAN", System.StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("CONTRATO", System.StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("REGLAS", System.StringComparison.OrdinalIgnoreCase) ||
                line.IndexOf(script.FileName, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf(script.AssetPath, System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (keep)
                selected.Add(rawLine);
        }

        string summary = string.Join("\n", selected).Trim();

        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = fullPlan.Length > 2200
                ? fullPlan.Substring(0, 2200)
                : fullPlan;
        }

        if (ultraCompact && summary.Length > 1000)
            summary = summary.Substring(0, 1000);

        return summary;
    }

    private string BuildCompactFilePlanningSource(string fullContent)
    {
        if (string.IsNullOrEmpty(fullContent))
            return string.Empty;

        const int headLength = 2200;
        const int tailLength = 1400;

        if (fullContent.Length <= headLength + tailLength + 300)
            return fullContent;

        string head = fullContent.Substring(0, headLength);
        string tail = fullContent.Substring(fullContent.Length - tailLength, tailLength);

        return
$@"{head}

// ... CONTENIDO INTERMEDIO OMITIDO SOLO PARA EL PLAN POR ARCHIVO ...

{tail}";
    }

    private string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').Trim();
    }

    private async Task<string> RequestAIAsync(string prompt, bool isPlan)
    {
        AIManager aiManager = AIManager.EnsureManagerInstanceForEditor();

        string instruction;

        if (isPlan)
        {
            instruction = @"

Sos un experto en C# y Unity Editor tooling.
Respondé siempre en castellano.
Devolvé solo:

PLAN
CONTRATO GLOBAL
REGLAS DE CAMBIO
No generes código.
Sé breve, preciso y nombra archivos exactos cuando corresponda.";
        }
        else
        {
            instruction = @"
Respondé en castellano.
Devolvé SOLO:
FILE: ruta/del/archivo.cs
[código completo]

Sin explicaciones, sin placeholders, sin texto extra y sin omitir partes del archivo.";
        }

        try
        {
            var response = await aiManager.GetConfiguredEditorCompletionResponse(prompt, instruction);

            if (response == null || response.Count == 0)
            {
                Debug.LogError("[MultiScriptRefactor] Respuesta vacía del AIManager.");
                return "";
            }

            ChatMessage lastAssistantMessage = null;

            for (int i = response.Count - 1; i >= 0; i--)
            {
                ChatMessage msg = response[i];

                if (msg != null &&
                    !string.IsNullOrWhiteSpace(msg.content) &&
                    string.Equals(msg.role, "assistant", System.StringComparison.OrdinalIgnoreCase))
                {
                    lastAssistantMessage = msg;
                    break;
                }
            }

            if (lastAssistantMessage == null)
            {
                Debug.LogError("[MultiScriptRefactor] No assistant message found.");
                return "";
            }

            return lastAssistantMessage.content.Trim();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[MultiScriptRefactor] Error llamando a AIManager: " + ex);
            return "";
        }
    }
}