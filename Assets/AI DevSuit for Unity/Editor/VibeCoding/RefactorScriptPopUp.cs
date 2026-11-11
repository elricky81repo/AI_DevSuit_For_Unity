using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class RefactorScriptPopUp : EditorWindow
{
    private const float DEFAULT_WIDTH = 1280f;
    private const float DEFAULT_HEIGHT = 800f;

    private string _scriptPath;
    private Vector2[] _scrollPositions = new Vector2[3];

    private string _codeContent;
    private string _analysisResult;
    private string _refactoredCode;
    private string _othersComponentPromp;

    private bool _isAnalyzing;
    private bool _isRefactoring;

    public static void ShowWindow(string scriptPath, string othersComponentPromp)
    {
        var wnd = CreateInstance<RefactorScriptPopUp>();
        wnd._scriptPath = scriptPath;
        wnd._othersComponentPromp = othersComponentPromp;
        wnd._codeContent = File.ReadAllText(scriptPath);
        wnd.titleContent = new GUIContent("Refactor: " + Path.GetFileNameWithoutExtension(scriptPath));
        wnd.minSize = new Vector2(400, 300);
        wnd.maxSize = new Vector2(1920, 1080);
        wnd._analysisResult = string.Empty;
        wnd._refactoredCode = string.Empty;
        wnd._isAnalyzing = false;
        wnd._isRefactoring = false;

        var screen = new Rect(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);
        wnd.position = new Rect(
            (screen.width - DEFAULT_WIDTH) * 0.5f,
            (screen.height - DEFAULT_HEIGHT) * 0.5f,
            DEFAULT_WIDTH,
            DEFAULT_HEIGHT);

        wnd.ShowUtility();
    }

    private void OnGUI()
    {
        float totalWidth = position.width - 20f;
        float colWidth = totalWidth / 3f;

        EditorGUILayout.BeginHorizontal();

        // ===== Column 1 =====
        EditorGUILayout.BeginVertical(GUILayout.Width(colWidth), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("Code", EditorStyles.boldLabel);

        _scrollPositions[0] = EditorGUILayout.BeginScrollView(_scrollPositions[0], GUILayout.ExpandHeight(true));
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextArea(_codeContent, GUILayout.ExpandHeight(true));
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // ===== Column 2 =====
        EditorGUILayout.BeginVertical(GUILayout.Width(colWidth), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("Code Analysis", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(_isAnalyzing || _isRefactoring))
        {
            if (GUILayout.Button(_isAnalyzing ? "Analyzing..." : "Analyze Script", GUILayout.Height(24)))
                _ = AnalyzeScriptAsync();
        }

        _scrollPositions[1] = EditorGUILayout.BeginScrollView(_scrollPositions[1], GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField(
            _isAnalyzing ? "Processing analysis..." : (string.IsNullOrEmpty(_analysisResult) ? "No results" : _analysisResult),
            EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // ===== Column 3 =====
        EditorGUILayout.BeginVertical(GUILayout.Width(colWidth), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("Refactored Code", EditorStyles.boldLabel);

        bool canRefactor = !string.IsNullOrEmpty(_analysisResult) && !_isAnalyzing;

        using (new EditorGUI.DisabledScope(!canRefactor || _isRefactoring))
        {
            if (GUILayout.Button(_isRefactoring ? "Refactoring..." : "Refactor Code", GUILayout.Height(24)))
                _ = RefactorCodeAsync();
        }

        if (!string.IsNullOrEmpty(_refactoredCode) && !_isRefactoring)
        {
            GUILayout.Space(5);
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = Color.yellow;

            if (GUILayout.Button("Apply Changes", GUILayout.Height(24)))
            {
                if (EditorUtility.DisplayDialog(
                    "Confirm Overwrite",
                    "Are you sure you want to overwrite '" + Path.GetFileName(_scriptPath) + "'?",
                    "Yes",
                    "Cancel"))
                {
                    File.WriteAllText(_scriptPath, _refactoredCode);
                    AssetDatabase.Refresh();
                    Close();
                }
            }

            GUI.backgroundColor = prev;
        }

        _scrollPositions[2] = EditorGUILayout.BeginScrollView(_scrollPositions[2], GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField(
            _isRefactoring ? "Processing refactor..." : (string.IsNullOrEmpty(_refactoredCode) ? "No refactored code" : _refactoredCode),
            EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private async System.Threading.Tasks.Task AnalyzeScriptAsync()
    {
        _isAnalyzing = true;
        _analysisResult = string.Empty;
        Repaint();

        try
        {
            var manager = AIManager.EnsureManagerInstanceForEditor();

            string instructionContent =
                "You are an assistant specialized in C# code analysis. " +
                "Look for bugs, performance issues, bad practices, and improvement opportunities without forcing unnecessary changes. " +
                "Do not return code. " +
                _othersComponentPromp;

            string userContent = _codeContent;

            var response = await manager.GetConfiguredEditorCompletionResponse(userContent, instructionContent);

            _analysisResult = response != null && response.Count > 0
                ? response.Last().content.Trim()
                : "No response received.";
        }
        finally
        {
            _isAnalyzing = false;
            Repaint();
        }
    }

    private async System.Threading.Tasks.Task RefactorCodeAsync()
    {
        _isRefactoring = true;
        _refactoredCode = string.Empty;
        Repaint();

        try
        {
            var manager = AIManager.EnsureManagerInstanceForEditor();

            string instructionContent =
                "Refactor the code and return only the final code.";

            string userContent =
                "Original code:\n" + _codeContent +
                "\n\nRecommendations:\n" + _analysisResult;

            var response = await manager.GetConfiguredEditorCompletionResponse(userContent, instructionContent);

            if (response != null && response.Count > 0)
            {
                var raw = response.Last().content;
                _refactoredCode = ExtractCodeBlock(raw).Trim();
            }
        }
        finally
        {
            _isRefactoring = false;
            Repaint();
        }
    }

    private static string ExtractCodeBlock(string input)
    {
        var pattern = @"```(?:csharp|cs)?\r?\n([\s\S]*?)```";
        var m = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : input;
    }
}