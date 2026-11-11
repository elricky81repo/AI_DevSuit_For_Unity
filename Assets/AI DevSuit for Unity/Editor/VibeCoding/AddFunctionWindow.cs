using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class AddFunctionWindow : EditorWindow
{
    private string _scriptPath;
    private string _contextPrompt;
    private string _scriptContent = "";
    private string _userPrompt = "";
    private string _generatedCode = "";

    private Vector2 _leftScroll;
    private Vector2 _rightScroll;

    private bool _isGenerating = false;
    private double _lastRepaintTime;
    private float _loadingBarValue = 0f;
    private const float LoadingBarSpeed = 0.7f;

    public static void ShowWindow(string scriptPath, string contextPrompt)
    {
        var window = GetWindow<AddFunctionWindow>("Add Functionality");
        window._scriptPath = scriptPath;
        window._contextPrompt = contextPrompt;
        window.minSize = new Vector2(1000, 600);
        window.LoadScriptContent();
        window.Show();
    }

    private void LoadScriptContent()
    {
        if (string.IsNullOrEmpty(_scriptPath))
        {
            _scriptContent = "// No script path received.";
            return;
        }

        try
        {
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), _scriptPath);

            if (!File.Exists(fullPath))
            {
                _scriptContent = "// File not found:\n" + fullPath;
                return;
            }

            _scriptContent = File.ReadAllText(fullPath);
        }
        catch (System.Exception ex)
        {
            _scriptContent = "// Error reading file:\n" + ex.Message;
        }
    }

    private void OnGUI()
    {
        UpdateLoadingBarAnimation();

        GUILayout.Space(6);
        EditorGUILayout.LabelField("Add functionality to script", EditorStyles.boldLabel);
        GUILayout.Space(6);

        EditorGUILayout.LabelField("Script Path:", _scriptPath);
        GUILayout.Space(8);

        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(position.width * 0.48f), GUILayout.ExpandHeight(true));

        EditorGUILayout.LabelField("Current Script", EditorStyles.boldLabel);
        GUILayout.Space(5);

        _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextArea(_scriptContent, GUILayout.ExpandHeight(true));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndScrollView();

        GUILayout.EndVertical();

        GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(position.width * 0.48f), GUILayout.ExpandHeight(true));

        EditorGUILayout.LabelField("Add functionality", EditorStyles.boldLabel);
        GUILayout.Space(5);

        _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));

        EditorGUILayout.LabelField("Context", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            string.IsNullOrEmpty(_contextPrompt) ? "No context available." : _contextPrompt,
            MessageType.Info
        );

        GUILayout.Space(10);

        EditorGUILayout.LabelField("Prompt (describe exactly what you want to add)", EditorStyles.boldLabel);
        _userPrompt = EditorGUILayout.TextArea(_userPrompt, GUILayout.MinHeight(120));

        GUILayout.Space(10);

        using (new EditorGUI.DisabledScope(_isGenerating))
        {
            if (GUILayout.Button(_isGenerating ? "Generating..." : "Execute Change", GUILayout.Height(32)))
            {
                _ = ExecuteChangeAsync();
            }
        }

        if (_isGenerating)
        {
            GUILayout.Space(8);

            Rect rect = GUILayoutUtility.GetRect(18, 18, "TextField");
            EditorGUI.ProgressBar(rect, _loadingBarValue, "Generating script...");
            GUILayout.Space(6);

            EditorGUILayout.HelpBox("Waiting for OpenAI response...", MessageType.Info);
        }

        if (!string.IsNullOrEmpty(_generatedCode) && !_isGenerating)
        {
            GUILayout.Space(8);

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = Color.yellow;

            if (GUILayout.Button("Apply Changes", GUILayout.Height(30)))
            {
                ApplyChanges();
            }

            GUI.backgroundColor = prev;
        }

        GUILayout.Space(12);
        EditorGUILayout.LabelField("Generated Script", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(_isGenerating);
        EditorGUILayout.TextArea(
            _isGenerating
                ? "Processing request..."
                : (string.IsNullOrEmpty(_generatedCode) ? "No generated code yet." : _generatedCode),
            GUILayout.ExpandHeight(true)
        );
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndScrollView();

        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
    }

    private void UpdateLoadingBarAnimation()
    {
        if (!_isGenerating)
        {
            _loadingBarValue = 0f;
            _lastRepaintTime = EditorApplication.timeSinceStartup;
            return;
        }

        double currentTime = EditorApplication.timeSinceStartup;
        float deltaTime = (float)(currentTime - _lastRepaintTime);
        _lastRepaintTime = currentTime;

        _loadingBarValue += deltaTime * LoadingBarSpeed;
        if (_loadingBarValue > 1f)
            _loadingBarValue = 0f;

        Repaint();
    }

    private async System.Threading.Tasks.Task ExecuteChangeAsync()
    {
        if (string.IsNullOrWhiteSpace(_userPrompt))
        {
            EditorUtility.DisplayDialog("Warning", "Please write the functionality you want to add.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(_scriptContent))
        {
            EditorUtility.DisplayDialog("Warning", "The script content is empty or could not be loaded.", "OK");
            return;
        }

        _isGenerating = true;
        _generatedCode = "";
        _loadingBarValue = 0f;
        _lastRepaintTime = EditorApplication.timeSinceStartup;
        Repaint();

        try
        {
            AIManager manager = AIManager.EnsureManagerInstanceForEditor();

            string instructionContent = BuildSystemPrompt();
            string userContent = BuildUserPrompt();

            var response = await manager.GetConfiguredEditorCompletionResponse(userContent, instructionContent);

            if (response == null || response.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "No response was received from OpenAI.", "OK");
                return;
            }

            string raw = response.Last().content;
            _generatedCode = ExtractCodeBlock(raw).Trim();

            if (string.IsNullOrWhiteSpace(_generatedCode))
            {
                EditorUtility.DisplayDialog("Warning", "OpenAI returned an empty result.", "OK");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[AddFunctionWindow] Error while generating code: " + ex);
            EditorUtility.DisplayDialog("Error", "An error occurred while generating the script:\n\n" + ex.Message, "OK");
        }
        finally
        {
            _isGenerating = false;
            _loadingBarValue = 0f;
            Repaint();
        }
    }

    private string BuildSystemPrompt()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("You are an expert senior Unity C# developer.");
        sb.AppendLine("You will receive an existing Unity C# script, extra context about the GameObject, and a request describing a new functionality to add.");
        sb.AppendLine();
        sb.AppendLine("Your task is to modify the provided script by adding ONLY the requested functionality.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL INSTRUCTIONS:");
        sb.AppendLine("1. Make the MINIMUM NECESSARY CHANGES required to add the requested functionality.");
        sb.AppendLine("2. Do NOT refactor, optimize, reorganize, clean up, modernize, or improve unrelated parts of the code.");
        sb.AppendLine("3. Do NOT rename variables, methods, classes, fields, comments, or regions unless it is strictly necessary for the requested feature.");
        sb.AppendLine("4. Preserve the original structure, behavior, style, and formatting as much as possible.");
        sb.AppendLine("5. If some code looks imperfect but is unrelated to the request, leave it unchanged.");
        sb.AppendLine("6. Only touch the parts of the script directly involved in implementing the requested functionality.");
        sb.AppendLine("7. The final script must compile correctly in Unity.");
        sb.AppendLine("8. Return the COMPLETE final script, ready to copy-paste over the original file.");
        sb.AppendLine("9. Do NOT explain what you changed.");
        sb.AppendLine("10. Do NOT include markdown fences unless absolutely necessary.");
        sb.AppendLine("11. Output ONLY the final C# script.");
        sb.AppendLine("12. Think like a conservative editor: apply the smallest safe patch possible.");

        return sb.ToString();
    }

    private string BuildUserPrompt()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("SCRIPT FILE PATH:");
        sb.AppendLine(_scriptPath);
        sb.AppendLine();

        sb.AppendLine("GAMEOBJECT / COMPONENT CONTEXT:");
        sb.AppendLine(string.IsNullOrWhiteSpace(_contextPrompt) ? "No additional context provided." : _contextPrompt);
        sb.AppendLine();

        sb.AppendLine("USER REQUEST:");
        sb.AppendLine(_userPrompt);
        sb.AppendLine();

        sb.AppendLine("CURRENT SCRIPT:");
        sb.AppendLine(_scriptContent);

        return sb.ToString();
    }

    private void ApplyChanges()
    {
        if (string.IsNullOrWhiteSpace(_generatedCode))
        {
            EditorUtility.DisplayDialog("Warning", "There is no generated code to apply.", "OK");
            return;
        }

        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), _scriptPath);

        if (!File.Exists(fullPath))
        {
            EditorUtility.DisplayDialog("Error", "The target file no longer exists:\n\n" + fullPath, "OK");
            return;
        }

        bool confirm = EditorUtility.DisplayDialog(
            "Confirm Overwrite",
            "Are you sure you want to overwrite:\n\n" + Path.GetFileName(fullPath) +
            "\n\nA backup will be created in Backups/Scripts/",
            "Yes, overwrite",
            "Cancel"
        );

        if (!confirm)
            return;

        try
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string backupDirectory = Path.Combine(projectRoot, "Backups/Scripts");

            if (!Directory.Exists(backupDirectory))
                Directory.CreateDirectory(backupDirectory);

            string fileName = Path.GetFileNameWithoutExtension(fullPath);
            string extension = Path.GetExtension(fullPath);
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string backupPath = Path.Combine(
                backupDirectory,
                fileName + "_backup_" + timestamp + extension
            );

            File.Copy(fullPath, backupPath, true);
            File.WriteAllText(fullPath, _generatedCode, Encoding.UTF8);

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Success",
                "Script updated successfully.\n\nBackup saved at:\n" + backupPath,
                "OK"
            );

            Close();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[AddFunctionWindow] Error applying changes: " + ex);
            EditorUtility.DisplayDialog("Error", "Could not overwrite the script:\n\n" + ex.Message, "OK");
        }
    }

    private static string ExtractCodeBlock(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var pattern = @"```(?:csharp|cs)?\r?\n([\s\S]*?)```";
        var m = Regex.Match(input, pattern, RegexOptions.IgnoreCase);

        return m.Success ? m.Groups[1].Value : input;
    }
}