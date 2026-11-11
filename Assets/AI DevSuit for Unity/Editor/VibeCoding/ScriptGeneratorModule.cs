using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

public class ScriptGeneratorModule : IEditorModule
{
    private const string READY_PREF = "GPTPlugin_ScriptReady";
    private const string CLASSNAME_PREF = "GPTPlugin_GeneratedClassName";
    private const string COMPILE_PREF = "GPTPlugin_CompileSuccess";
    private const string COMPILE_ERRORS_PREF = "GPTPlugin_CompileErrors";
    private const string TARGET_PREF = "GPTPlugin_TargetID";
    private const string MODEL_PREF = "GPTPlugin_Model";
    private const string PROMPT_PREF = "GPTPlugin_Prompt";

    private const string PROVIDER_PREF = "AI_PROVIDER";
    private const string ANTHROPIC_API_KEY_PREF = "ANTHROPIC_API_KEY";

    private const string GeneratedScriptsFolder = "Assets/AI-Generated/Scripts/";

    private string _prompt;
    private GameObject _targetGO;
    private GptModel _modelEnum;
    private bool _isGenerating;
    private bool _scriptReady;
    private string _generatedClassName;
    private bool _compileSuccess;
    private string _compileErrors;

    private bool _isAudioRecording;
    private GameObject _audioRecorderGO;
    private SoundRecordSys _audioRecorder;

    private bool _isRequestingInstructions;
    private int _recordControlID;

    private string _audioNoticeMessage;
    private MessageType _audioNoticeType = MessageType.Info;
    private double _audioNoticeUntilTime;

    private string _pendingGeneratedCode;
    private string _pendingGeneratedClassName;
    private bool _hasPendingGeneratedCode;

    public string TabName => "Generate";

    private double _progressStartTime;

    public ScriptGeneratorModule()
    {
        _prompt = EditorPrefs.GetString(PROMPT_PREF, "What do you want the script do?");
        _generatedClassName = EditorPrefs.GetString(CLASSNAME_PREF, "");
        _scriptReady = EditorPrefs.GetBool(READY_PREF, false);
        _compileSuccess = EditorPrefs.GetBool(COMPILE_PREF, false);
        _compileErrors = EditorPrefs.GetString(COMPILE_ERRORS_PREF, "");

        int targetID = SessionState.GetInt(TARGET_PREF, 0);
#pragma warning disable CS0618
        _targetGO = targetID != 0 ? EditorUtility.InstanceIDToObject(targetID) as GameObject : null;
#pragma warning restore CS0618

        _modelEnum = LoadSavedModel();

        if (!HasValidGeneratedScriptState())
        {
            ClearGeneratedScriptState();
        }

        CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompiled;
    }

    public void Draw()
    {
        ClearExpiredAudioNoticeIfNeeded();

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Select the target GameObject to add the script", EditorStyles.boldLabel);
        var newTarget = (GameObject)EditorGUILayout.ObjectField("Target", _targetGO, typeof(GameObject), true);
        if (newTarget != _targetGO)
            SessionState.SetInt(TARGET_PREF, (_targetGO = newTarget) ? newTarget.GetInstanceID() : 0);

        GUILayout.Space(10);
        EditorGUILayout.LabelField("What do you want the GameObject to do?", EditorStyles.boldLabel);

        var wrapStyle = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = true,
        };

        _prompt = EditorGUILayout.TextArea(_prompt, wrapStyle, GUILayout.Height(80));
        EditorPrefs.SetString(PROMPT_PREF, _prompt);

        Rect recordRect = GUILayoutUtility.GetRect(120, 30);
        bool isOpenAIProvider = GetSelectedProvider() == AIManager.AIProviderType.OpenAI;
        bool canUseRecording = isOpenAIProvider && HasOpenAIKey();
        string label = !isOpenAIProvider
            ? "🎤 OpenAI only"
            : (_isAudioRecording ? "● Recording..." : "🎤 Hold to Record");

        using (new EditorGUI.DisabledScope(!canUseRecording))
        {
            _recordControlID = GUIUtility.GetControlID(FocusType.Passive, recordRect);
            Event e = Event.current;

            switch (e.GetTypeForControl(_recordControlID))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && recordRect.Contains(e.mousePosition) && !_isAudioRecording)
                    {
                        GUIUtility.hotControl = _recordControlID;
                        StartAudioRecording();
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == _recordControlID && _isAudioRecording)
                    {
                        GUIUtility.hotControl = 0;
                        StopAudioRecording();
                        e.Use();
                    }
                    break;
            }

            GUI.Button(recordRect, label);
        }

        if (!string.IsNullOrWhiteSpace(_audioNoticeMessage))
        {
            GUILayout.Space(6);
            EditorGUILayout.HelpBox(_audioNoticeMessage, _audioNoticeType);
        }

        GUILayout.Space(10);
        bool canGenerate =
            _targetGO != null
            && HasValidSelectedProviderKey()
            && !_isGenerating
            && !string.IsNullOrWhiteSpace(_prompt);

        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            if (GUILayout.Button(_isGenerating ? "Generating..." : "Generate Script"))
                _ = GenerateScriptAsync();
        }

        if (_isGenerating)
        {
            ProgressBar();
        }

        if (_hasPendingGeneratedCode)
        {
            GUILayout.Space(10);
            EditorGUILayout.HelpBox(
                $"Script preview ready: {_pendingGeneratedClassName}. Review it and click Save Script to write it into the project.",
                MessageType.Info
            );

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Preview"))
                {
                    ScriptPreviewWindow.ShowWindow(
                        _pendingGeneratedCode,
                        _pendingGeneratedClassName,
                        SavePendingScriptFromPreview,
                        DiscardPendingScriptFromPreview
                    );
                }

                if (GUILayout.Button("Save Script"))
                {
                    SavePendingScriptToProject();
                }

                if (GUILayout.Button("Discard"))
                {
                    DiscardPendingScript();
                }
            }
        }

        GUILayout.Space(10);
        if (_scriptReady)
        {
            var okStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.green } };
            EditorGUILayout.LabelField("✔ Script saved successfully", okStyle);

            if (_compileSuccess)
            {
                EditorGUILayout.LabelField("✔ Compiled without errors", okStyle);
                GUILayout.Space(5);

                using (new EditorGUI.DisabledScope(!_compileSuccess || _isRequestingInstructions))
                {
                    if (GUILayout.Button(_isRequestingInstructions ? "Getting Instructions..." : "Get Instructions"))
                        _ = ShowEditorInstructionsAsync();
                }
            }
            else
            {
                EditorGUILayout.LabelField("Waiting for compilation...", EditorStyles.wordWrappedLabel);

                if (!string.IsNullOrWhiteSpace(_compileErrors))
                {
                    var errStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.red } };
                    EditorGUILayout.LabelField("✖ Compilation Errors:", errStyle);
                    EditorGUILayout.HelpBox(_compileErrors, MessageType.Error);
                }
            }

            if (_isRequestingInstructions)
            {
                ProgressBar();
            }
        }

        GUILayout.Space(10);
        using (new EditorGUI.DisabledScope(!_scriptReady || !_compileSuccess || _targetGO == null))
        {
            if (GUILayout.Button("Assign Script"))
                AssignGeneratedScript();
        }
    }

    private void ProgressBar()
    {
        float progress = (float)((EditorApplication.timeSinceStartup - _progressStartTime) % 1.0);
        EditorGUILayout.LabelField("Waiting AI...", EditorStyles.wordWrappedLabel);
        var barRect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth, 20, GUILayout.ExpandWidth(true));
        EditorGUI.ProgressBar(barRect, progress, "");
        RepaintToolWindow();
        GUILayout.Space(10);
    }

    private void StartAudioRecording()
    {
        _isAudioRecording = true;
        ClearAudioNotice();

        AIManager aiMgr = AIManager.EnsureManagerInstanceForEditor();
        aiMgr.SetProvider(AIManager.AIProviderType.OpenAI);

        _audioRecorderGO = new GameObject("AudioRecorder", typeof(SoundRecordSys))
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        _audioRecorder = _audioRecorderGO.GetComponent<SoundRecordSys>();
        _audioRecorder.AIManagerObject = aiMgr.gameObject;
        _audioRecorder.maxRecordedTime = 25;
        _audioRecorder.transcriptionLanguage = "es";
        _audioRecorder.StartRecording();
    }

    private void StopAudioRecording()
    {
        _isAudioRecording = false;
        _ = StopAudioAndFillPromptAsync();
    }

    private async Task StopAudioAndFillPromptAsync()
    {
        try
        {
            if (_audioRecorder == null)
                return;

            string text = await _audioRecorder.EndRecordingAsync();

            if (!string.IsNullOrEmpty(text))
            {
                _prompt = text;
                EditorPrefs.SetString(PROMPT_PREF, _prompt);

                EditorApplication.delayCall += () =>
                {
                    EditorGUIUtility.editingTextField = false;
                    RepaintToolWindow();
                };
            }
            else
            {
                EditorApplication.delayCall += () =>
                {
                    ShowAudioNotice(
                        "Recording was too short. Hold the button a little longer and try again.",
                        MessageType.Warning,
                        3.0d
                    );
                };
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[ScriptGeneratorModule] Error transcribing audio: " + ex);

            EditorApplication.delayCall += () =>
            {
                ShowAudioNotice(
                    "Audio transcription failed. Please try again.",
                    MessageType.Error,
                    4.0d
                );
            };
        }
        finally
        {
            _audioRecorder = null;

            if (_audioRecorderGO != null)
                UnityEngine.Object.DestroyImmediate(_audioRecorderGO);

            _audioRecorderGO = null;
        }
    }

    private void ShowAudioNotice(string message, MessageType type, double durationSeconds)
    {
        _audioNoticeMessage = message;
        _audioNoticeType = type;
        _audioNoticeUntilTime = EditorApplication.timeSinceStartup + durationSeconds;
        RepaintToolWindow();
    }

    private void ClearAudioNotice()
    {
        _audioNoticeMessage = null;
        _audioNoticeType = MessageType.Info;
        _audioNoticeUntilTime = 0;
    }

    private void ClearExpiredAudioNoticeIfNeeded()
    {
        if (string.IsNullOrWhiteSpace(_audioNoticeMessage))
            return;

        if (EditorApplication.timeSinceStartup <= _audioNoticeUntilTime)
            return;

        ClearAudioNotice();
    }

    private async Task GenerateScriptAsync()
    {
        _isGenerating = true;
        _progressStartTime = EditorApplication.timeSinceStartup;

        ClearGeneratedScriptState();
        ClearPendingGeneratedScript();

        RepaintToolWindow();

        try
        {
            var manager = AIManager.EnsureManagerInstanceForEditor();

            string instructionContent =
                "Eres un asistente que genera scripts de Unity C#. " +
                "Debes responder únicamente con código C# válido, completo y compilable para Unity. " +
                "No expliques nada. No agregues texto antes ni después. " +
                "No agregues frases como 'Aquí tienes el script'. " +
                "Devuelve solamente un bloque de código entre triple backticks.";

            string userContent = "Genera un script completo de Unity C# para este pedido:\n\n" + _prompt;

            var response = await manager.GetConfiguredEditorCompletionResponse(userContent, instructionContent);

            if (response == null || response.Count == 0)
            {
                Debug.LogError("[GPTPlugin] No valid response was received while generating the script.");
                return;
            }

            ChatMessage lastMessage = response.Last();

            if (lastMessage == null || lastMessage.role != "assistant" || string.IsNullOrWhiteSpace(lastMessage.content))
            {
                Debug.LogError("[GPTPlugin] The AI did not return a valid assistant response.");
                return;
            }

            string raw = lastMessage.content;
            string code = ExtractCodeBlock(raw);

            if (string.IsNullOrWhiteSpace(code) || !LooksLikeCSharp(code))
            {
                Debug.LogError("[GPTPlugin] The response does not appear to be valid C# code.\n\nRAW RESPONSE:\n" + raw);
                EditorUtility.DisplayDialog(
                    "Invalid AI Response",
                    "The AI response did not contain valid C# code. Check the Console.",
                    "OK"
                );
                return;
            }

            string className = ExtractClassName(code);

            _pendingGeneratedCode = code;
            _pendingGeneratedClassName = className;
            _hasPendingGeneratedCode = true;

            ScriptPreviewWindow.ShowWindow(
                _pendingGeneratedCode,
                _pendingGeneratedClassName,
                SavePendingScriptFromPreview,
                DiscardPendingScriptFromPreview
            );
        }
        catch (Exception ex)
        {
            Debug.LogError("[ScriptGeneratorModule] Error generating script: " + ex);
            EditorUtility.DisplayDialog(
                "Generate Script",
                "An error occurred while generating the script.\n\n" + ex.Message,
                "OK"
            );
        }
        finally
        {
            _isGenerating = false;
            RepaintToolWindow();
        }
    }

    private void SavePendingScriptFromPreview()
    {
        SavePendingScriptToProject();
    }

    private void DiscardPendingScriptFromPreview()
    {
        DiscardPendingScript();
    }

    private void SavePendingScriptToProject()
    {
        if (!_hasPendingGeneratedCode || string.IsNullOrWhiteSpace(_pendingGeneratedClassName))
            return;

        try
        {
            Directory.CreateDirectory(GeneratedScriptsFolder);

            string path = Path.Combine(GeneratedScriptsFolder, _pendingGeneratedClassName + ".cs");
            File.WriteAllText(path, _pendingGeneratedCode);

            AssetDatabase.ImportAsset(path);
            AssetDatabase.Refresh();

            _generatedClassName = _pendingGeneratedClassName;
            _scriptReady = true;
            _compileSuccess = false;
            _compileErrors = "";

            EditorPrefs.SetBool(READY_PREF, true);
            EditorPrefs.SetString(CLASSNAME_PREF, _generatedClassName);
            EditorPrefs.SetBool(COMPILE_PREF, false);
            EditorPrefs.SetString(COMPILE_ERRORS_PREF, "");

            ClearPendingGeneratedScript();

            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompiled;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;

            RepaintToolWindow();
        }
        catch (Exception ex)
        {
            Debug.LogError("[ScriptGeneratorModule] Error saving generated script: " + ex);
            EditorUtility.DisplayDialog(
                "Save Script",
                "An error occurred while saving the script.\n\n" + ex.Message,
                "OK"
            );
        }
    }

    private void DiscardPendingScript()
    {
        ClearPendingGeneratedScript();
        RepaintToolWindow();
    }

    private void ClearPendingGeneratedScript()
    {
        _pendingGeneratedCode = null;
        _pendingGeneratedClassName = null;
        _hasPendingGeneratedCode = false;
    }

    private void OnAssemblyCompiled(string asmPath, CompilerMessage[] messages)
    {
        if (!asmPath.EndsWith("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
            return;

        CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompiled;

        var errs = new List<string>();
        foreach (var m in messages)
        {
            if (m.type == CompilerMessageType.Error &&
                !string.IsNullOrEmpty(m.file) &&
                m.file.Replace("\\", "/").Contains(GeneratedScriptsFolder.Replace("\\", "/")))
            {
                errs.Add(m.message);
            }
        }

        _compileSuccess = errs.Count == 0;
        _compileErrors = string.Join("\n", errs);

        EditorPrefs.SetBool(COMPILE_PREF, _compileSuccess);
        EditorPrefs.SetString(COMPILE_ERRORS_PREF, _compileErrors);
        RepaintToolWindow();
    }

    private async Task ShowEditorInstructionsAsync()
    {
        if (!_compileSuccess || string.IsNullOrEmpty(_generatedClassName))
            return;

        _isRequestingInstructions = true;
        _progressStartTime = EditorApplication.timeSinceStartup;
        RepaintToolWindow();

        try
        {
            string path = Path.Combine(GeneratedScriptsFolder, _generatedClassName + ".cs").Replace("\\", "/");

            if (!File.Exists(path))
            {
                Debug.LogError("[ScriptGeneratorModule] Generated script not found at: " + path);
                EditorUtility.DisplayDialog(
                    "Get Instructions",
                    "The generated script could not be found.\n\n" + path,
                    "OK"
                );
                return;
            }

            string code = File.ReadAllText(path);

            var manager = AIManager.EnsureManagerInstanceForEditor();

            string instructionContent =
                "You are an assistant specialized in Unity C# setup instructions. " +
                "Your job is to read a Unity C# script and return ONLY the practical setup steps required to make that specific script work correctly after it has been attached to a GameObject. " +
                "Assume Unity is already open and the user already attached the script or is about to attach it. " +
                "Do not explain Unity basics. Do not explain how to create a script. Do not repeat redundant setup advice. " +
                "Focus only on this specific script. " +
                "Analyze the code carefully and infer the concrete requirements it has. " +
                "If the script requires other components on the same GameObject, say exactly which ones must exist or be added. " +
                "If it requires child objects, references in the Inspector, prefabs, colliders, rigidbody, animator, audio source, camera, canvas, event system, UI objects, tags, layers, navmesh, input actions, or other scene dependencies, mention them explicitly only if they are actually relevant to this code. " +
                "If there are public or serialized fields that must be assigned in the Inspector, mention them specifically by name and what should be assigned there. " +
                "If the script depends on a certain object name, tag, hierarchy structure, or scene condition, mention it. " +
                "If the script does not need any special setup beyond being attached, say that clearly. " +
                "Return the answer in English. " +
                "Return only a short, clean, actionable checklist. " +
                "Prefer 3 to 8 bullet points. " +
                "Each point must describe something the user should do to make this exact script run correctly. " +
                "Do not include theory. Do not summarize the code. Do not restate what the script does unless that is necessary to explain a setup step.";

            string userContent =
                "Analyze this Unity C# script and return only the concrete setup instructions required to use it correctly after attaching it to a GameObject:\n\n" +
                code;

            var resp = await manager.GetConfiguredEditorCompletionResponse(userContent, instructionContent);

            if (resp != null && resp.Count > 0)
            {
                string instructions = resp.Last().content != null ? resp.Last().content.Trim() : "";
                InstructionsWindow.ShowInstructions(instructions);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[ScriptGeneratorModule] Error getting instructions: " + ex);
            EditorUtility.DisplayDialog(
                "Get Instructions",
                "An error occurred while getting the instructions.\n\n" + ex.Message,
                "OK"
            );
        }
        finally
        {
            _isRequestingInstructions = false;
            RepaintToolWindow();
        }
    }

    private void AssignGeneratedScript()
    {
        if (!_scriptReady || !_compileSuccess || _targetGO == null)
            return;

        var type = Type.GetType(_generatedClassName + ", Assembly-CSharp");
        if (type != null)
        {
            Undo.AddComponent(_targetGO, type);

            _prompt = "";
            EditorPrefs.SetString(PROMPT_PREF, _prompt);

            ClearGeneratedScriptState();
            RepaintToolWindow();
        }
        else
        {
            Debug.LogError($"[GPTPlugin] Could not find type {_generatedClassName}.");
        }
    }

    private bool HasValidGeneratedScriptState()
    {
        if (string.IsNullOrWhiteSpace(_generatedClassName))
            return false;

        string path = Path.Combine(GeneratedScriptsFolder, _generatedClassName + ".cs");
        return File.Exists(path);
    }

    private void ClearGeneratedScriptState()
    {
        _scriptReady = false;
        _generatedClassName = "";
        _compileSuccess = false;
        _compileErrors = "";

        EditorPrefs.SetBool(READY_PREF, false);
        EditorPrefs.SetString(CLASSNAME_PREF, "");
        EditorPrefs.SetBool(COMPILE_PREF, false);
        EditorPrefs.SetString(COMPILE_ERRORS_PREF, "");
    }

    private static string ExtractCodeBlock(string input)
    {
        var pattern = @"```(?:csharp|cs)?\r?\n([\s\S]*?)```";
        var m = Regex.Match(input, pattern, RegexOptions.IgnoreCase);

        if (m.Success)
            return m.Groups[1].Value.Trim();

        string trimmed = input.TrimStart();
        return LooksLikeCSharp(trimmed) ? trimmed : string.Empty;
    }

    private static bool LooksLikeCSharp(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("class ")
            || text.Contains("using UnityEngine")
            || text.Contains("MonoBehaviour")
            || text.Contains("namespace ");
    }

    private static string ExtractClassName(string code)
    {
        var m = Regex.Match(code, @"class\s+(\w+)");
        return m.Success ? m.Groups[1].Value : "GPTGenerated";
    }

    private static void RepaintToolWindow()
    {
        var win = EditorWindow.GetWindow<VibeToolsWindow>();
        if (win != null)
            win.Repaint();
    }

    private static AIManager.AIProviderType GetSelectedProvider()
    {
        string saved = EditorPrefs.GetString(PROVIDER_PREF, AIManager.AIProviderType.OpenAI.ToString());

        if (Enum.TryParse(saved, out AIManager.AIProviderType parsed))
            return parsed;

        return AIManager.AIProviderType.OpenAI;
    }

    private static bool HasOpenAIKey()
    {
        return !string.IsNullOrWhiteSpace(VibeToolsWindow.GPTPrefs.ApiKey);
    }

    private static bool HasAnthropicKey()
    {
        return !string.IsNullOrWhiteSpace(EditorPrefs.GetString(ANTHROPIC_API_KEY_PREF, ""));
    }

    private static bool HasValidSelectedProviderKey()
    {
        switch (GetSelectedProvider())
        {
            case AIManager.AIProviderType.OpenAI:
                return HasOpenAIKey();

            case AIManager.AIProviderType.Anthropic:
                return HasAnthropicKey();

            default:
                return false;
        }
    }

    private static GptModel LoadSavedModel()
    {
        string savedValue = EditorPrefs.GetString(MODEL_PREF, GetDefaultModel().ToString());
        savedValue = NormalizeSavedModelValue(savedValue);

        if (Enum.TryParse(savedValue, out GptModel parsed))
            return parsed;

        Debug.LogWarning($"[GPTPlugin] Invalid saved model: {savedValue}. Using default.");
        return GetDefaultModel();
    }

    private static void SaveModelPref(GptModel model)
    {
        EditorPrefs.SetString(MODEL_PREF, model.ToString());
    }

    private static string NormalizeSavedModelValue(string savedValue)
    {
        if (string.IsNullOrWhiteSpace(savedValue))
            return GetDefaultModel().ToString();

        return savedValue switch
        {
            "gpt_4o_mini" => nameof(GptModel.Gpt4oMini),
            "gpt_4o" => nameof(GptModel.Gpt4o),
            "o4_mini" => nameof(GptModel.O4Mini),
            "o3" => nameof(GptModel.O3),
            "gpt_5" => nameof(GptModel.Gpt5),
            "gpt_5_mini" => nameof(GptModel.Gpt5Mini),
            "gpt_5_nano" => nameof(GptModel.Gpt5Nano),
            "gpt_5_1_codex_mini" => nameof(GptModel.gpt51CodexMini),
            "gpt_5_3_codex" => nameof(GptModel.gpt53Codex),

            "gpt_4o_mini_high" => nameof(GptModel.Gpt4oMini),
            "o4_mini_high" => nameof(GptModel.O4Mini),

            "gpt-4o-mini" => nameof(GptModel.Gpt4oMini),
            "gpt-4o" => nameof(GptModel.Gpt4o),
            "o4-mini" => nameof(GptModel.O4Mini),
            "gpt-5" => nameof(GptModel.Gpt5),
            "gpt-5-mini" => nameof(GptModel.Gpt5Mini),
            "gpt-5-nano" => nameof(GptModel.Gpt5Nano),
            "gpt-5.1-codex-mini" => nameof(GptModel.gpt51CodexMini),
            "gpt-5.3-codex" => nameof(GptModel.gpt53Codex),

            _ => savedValue
        };
    }

    private static GptModel GetDefaultModel()
    {
        return GptModel.Gpt4oMini;
    }

    private static string GetApiModelString(GptModel model)
    {
        switch (model)
        {
            case GptModel.Gpt4oMini:
                return "gpt-4o-mini";
            case GptModel.Gpt4o:
                return "gpt-4o";
            case GptModel.O4Mini:
                return "o4-mini";
            case GptModel.O3:
                return "o3";
            case GptModel.Gpt5:
                return "gpt-5";
            case GptModel.Gpt5Mini:
                return "gpt-5-mini";
            case GptModel.Gpt5Nano:
                return "gpt-5-nano";
            case GptModel.gpt51CodexMini:
                return "gpt-5.1-codex-mini";
            case GptModel.gpt53Codex:
                return "gpt-5.3-codex";
            default:
                Debug.LogWarning("[GPTPlugin] Unknown model. Falling back to gpt-4o-mini.");
                return "gpt-4o-mini";
        }
    }

    public class ScriptPreviewWindow : EditorWindow
    {
        private string _code;
        private string _className;
        private Vector2 _scroll;
        private GUIStyle _codeStyle;
        private GUIStyle _titleStyle;
        private Action _onSave;
        private Action _onDiscard;

        public static void ShowWindow(string code, string className, Action onSave, Action onDiscard)
        {
            var win = GetWindow<ScriptPreviewWindow>(true, "Script Preview", true);
            win._code = code ?? "";
            win._className = className ?? "GeneratedScript";
            win._onSave = onSave;
            win._onDiscard = onDiscard;
            win.minSize = new Vector2(700, 420);
            win.position = new Rect(win.position.x, win.position.y, 900, 640);
            win.ShowUtility();
            win.Focus();
        }

        private void EnsureStyles()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(EditorStyles.boldLabel);
                _titleStyle.wordWrap = true;
                _titleStyle.fontSize = 12;
            }

            if (_codeStyle == null)
            {
                _codeStyle = new GUIStyle(EditorStyles.textArea);
                _codeStyle.wordWrap = false;
                _codeStyle.richText = false;
                _codeStyle.alignment = TextAnchor.UpperLeft;
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Review Generated Script", _titleStyle);
            EditorGUILayout.LabelField($"Class: {_className}", EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(6);

            EditorGUILayout.HelpBox(
                "The script has not been saved yet. Review it here. Click Save Script to write it into the project and trigger compilation.",
                MessageType.Info
            );

            using (new EditorGUILayout.VerticalScope("box"))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextArea(
                    string.IsNullOrWhiteSpace(_code) ? "// No code generated." : _code,
                    _codeStyle,
                    GUILayout.ExpandHeight(true)
                );
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndScrollView();
            }

            GUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy", GUILayout.Width(100), GUILayout.Height(28)))
                {
                    EditorGUIUtility.systemCopyBuffer = _code ?? "";
                    try
                    {
                        ShowNotification(new GUIContent("Code copied"));
                    }
                    catch
                    {
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Discard", GUILayout.Width(100), GUILayout.Height(28)))
                {
                    _onDiscard?.Invoke();
                    Close();
                }

                if (GUILayout.Button("Save Script", GUILayout.Width(120), GUILayout.Height(28)))
                {
                    _onSave?.Invoke();
                    Close();
                }
            }

            GUILayout.Space(6);
        }
    }

    public class InstructionsWindow : EditorWindow
    {
        private string _instructions;
        private Vector2 _scroll;
        private GUIStyle _wrappedTextStyle;
        private GUIStyle _titleStyle;

        public static void ShowInstructions(string instructions)
        {
            var win = CreateInstance<InstructionsWindow>();
            win.titleContent = new GUIContent("Instructions");
            win._instructions = instructions ?? "";
            win.minSize = new Vector2(460, 320);
            win.position = new Rect(win.position.x, win.position.y, 680, 520);
            win.ShowUtility();
        }

        private void EnsureStyles()
        {
            if (_wrappedTextStyle == null)
            {
                _wrappedTextStyle = new GUIStyle(EditorStyles.textArea);
                _wrappedTextStyle.wordWrap = true;
                _wrappedTextStyle.richText = false;
                _wrappedTextStyle.alignment = TextAnchor.UpperLeft;
            }

            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(EditorStyles.boldLabel);
                _titleStyle.wordWrap = true;
                _titleStyle.fontSize = 12;
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Editor Instructions", _titleStyle);
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextArea(
                    string.IsNullOrWhiteSpace(_instructions) ? "No instructions available." : _instructions,
                    _wrappedTextStyle,
                    GUILayout.ExpandHeight(true)
                );
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndScrollView();
            }

            GUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Copy", GUILayout.Width(100), GUILayout.Height(28)))
                {
                    EditorGUIUtility.systemCopyBuffer = _instructions ?? "";
                    try
                    {
                        ShowNotification(new GUIContent("Instructions copied"));
                    }
                    catch
                    {
                    }
                }

                if (GUILayout.Button("OK", GUILayout.Width(100), GUILayout.Height(28)))
                {
                    Close();
                }
            }

            GUILayout.Space(6);
        }
    }

    private string MaskMiddle(string value, int visibleStart = 3, int visibleEnd = 4)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= visibleStart + visibleEnd)
            return value;

        int starsCount = value.Length - visibleStart - visibleEnd;
        return value.Substring(0, visibleStart)
             + new string('*', starsCount)
             + value.Substring(value.Length - visibleEnd);
    }

    private static bool UsesDeveloperRole(GptModel model)
    {
        switch (model)
        {
            case GptModel.O3:
            case GptModel.O4Mini:
            case GptModel.Gpt5:
            case GptModel.Gpt5Mini:
            case GptModel.Gpt5Nano:
            case GptModel.gpt51CodexMini:
            case GptModel.gpt53Codex:
                return true;

            case GptModel.Gpt4o:
            case GptModel.Gpt4oMini:
            default:
                return false;
        }
    }
}