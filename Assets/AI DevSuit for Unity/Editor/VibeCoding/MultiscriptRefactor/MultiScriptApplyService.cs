using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class MultiScriptApplyService
{
    public static bool ApplyFile(RefactoredFileResult fileResult, bool createBackup = true)
    {
        if (fileResult == null)
        {
            Debug.LogError("MultiScriptApplyService: fileResult es null.");
            return false;
        }

        string assetPath = fileResult.AssetPath?.Trim();
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogError("MultiScriptApplyService: AssetPath vacío.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(fileResult.UpdatedCode))
        {
            Debug.LogError("MultiScriptApplyService: UpdatedCode vacío.");
            return false;
        }

        bool fileExists = File.Exists(assetPath);
        if (!fileExists && !fileResult.IsNew)
        {
            Debug.LogWarning($"MultiScriptApplyService: El archivo no existe pero no está marcado como nuevo: {assetPath}");
        }

        try
        {
            string directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (fileExists && createBackup)
            {
                string backupPath = assetPath + ".bak";
                File.Copy(assetPath, backupPath, true);
            }

            File.WriteAllText(assetPath, fileResult.UpdatedCode);
            AssetDatabase.Refresh();

            Debug.Log($"Archivo aplicado correctamente: {assetPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error aplicando archivo {assetPath}: {ex}");
            return false;
        }
    }
}