using System.Collections.Generic;
using System.IO;
using UnityEditor;

public static class MultiScriptContextBuilder
{
    public static List<ScriptContextData> Build(List<MonoScript> scripts)
    {
        var results = new List<ScriptContextData>();

        if (scripts == null)
            return results;

        foreach (var script in scripts)
        {
            if (script == null)
                continue;

            string assetPath = AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrWhiteSpace(assetPath))
                continue;

            if (!File.Exists(assetPath))
                continue;

            string content = File.ReadAllText(assetPath);
            int lineCount = content.Split('\n').Length;

            results.Add(new ScriptContextData
            {
                AssetPath = assetPath,
                FileName = Path.GetFileName(assetPath),
                FullContent = content,
                LineCount = lineCount,
                CharacterCount = content.Length
            });
        }

        return results;
    }
}