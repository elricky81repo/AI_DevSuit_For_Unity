using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class MultiScriptRefactorRequest
{
    public string Goal;
    public List<ScriptContextData> Scripts = new List<ScriptContextData>();
    public bool AnalyzeOnly;
    public bool ReturnFullFiles = true;
}

[Serializable]
public class ScriptContextData
{
    public string AssetPath;
    public string FileName;
    public string FullContent;
    public int LineCount;
    public int CharacterCount;
}


[Serializable]
public class RefactoredFileResult
{
    public string AssetPath;
    public string FileName;
    public string UpdatedCode;
    public bool HasChanges;
    public string Summary;
    public bool WasApplied;
    public bool IsNew;
    public Vector2 ScrollPosition;
}

[Serializable]
public class MultiScriptRefactorResult
{
    public string RawResponse;
    public string Plan;
    public List<RefactoredFileResult> Files = new List<RefactoredFileResult>();
    public bool Success;
    public string ErrorMessage;
}