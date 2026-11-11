using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

public static class MultiScriptResponseParser
{
    private static readonly Regex FileBlockRegex = new Regex(
        @"FILE:\s*(?<path>[^\r\n]+)\s*```(?:csharp|cs|C#)?\s*(?<code>[\s\S]*?)```",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static MultiScriptRefactorResult Parse(string raw)
    {
        var result = new MultiScriptRefactorResult
        {
            RawResponse = raw ?? string.Empty,
            Files = new List<RefactoredFileResult>(),
            Success = false,
            Plan = string.Empty
        };

        if (string.IsNullOrWhiteSpace(raw))
            return result;

        int planIndex = raw.IndexOf("PLAN:", StringComparison.OrdinalIgnoreCase);
        int firstFileIndex = raw.IndexOf("FILE:", StringComparison.OrdinalIgnoreCase);

        if (planIndex >= 0)
        {
            if (firstFileIndex > planIndex)
                result.Plan = raw.Substring(planIndex, firstFileIndex - planIndex).Trim();
            else
                result.Plan = raw.Substring(planIndex).Trim();
        }

        var matches = FileBlockRegex.Matches(raw);
        foreach (Match match in matches)
        {
            if (!match.Success) continue;

            string path = match.Groups["path"].Value.Trim();
            string code = match.Groups["code"].Value.Trim();

            if (string.IsNullOrWhiteSpace(path))
                continue;

            if (string.IsNullOrWhiteSpace(code))
                continue;

            result.Files.Add(new RefactoredFileResult
            {
                AssetPath = path,
                FileName = Path.GetFileName(path),
                UpdatedCode = code,
                HasChanges = true
            });
        }

        result.Success = result.Files.Count > 0;
        return result;
    }

    public static bool LooksTruncated(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        int fileCount = Regex.Matches(raw, @"(?im)^\s*FILE:\s*").Count;
        int fenceCount = Regex.Matches(raw, "```").Count;

        if (fileCount <= 0)
            return false;

        if (fenceCount < fileCount * 2)
            return true;

        string trimmed = raw.TrimEnd();

        if (!trimmed.EndsWith("```", StringComparison.Ordinal))
            return true;

        return false;
    }

    public static bool TryGetSingleFile(string raw, string expectedAssetPath, out RefactoredFileResult file)
    {
        file = null;

        var parsed = Parse(raw);
        if (parsed == null || parsed.Files == null || parsed.Files.Count == 0)
            return false;

        foreach (var item in parsed.Files)
        {
            if (item == null) continue;

            if (string.Equals(item.AssetPath?.Trim(), expectedAssetPath?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                file = item;
                return true;
            }
        }

        if (parsed.Files.Count == 1)
        {
            file = parsed.Files[0];
            return true;
        }

        return false;
    }
}