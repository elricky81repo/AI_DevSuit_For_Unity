using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class ObjectGeneratorModule
{
    private const string ProviderPref = "AI_PROVIDER";

    private readonly MaterialAssignerWindow owner;

    private string objectPrompt = "";
    private Texture2D objectTexture;
    private bool isGeneratingObject = false;
    private string objectErrorMessage = "";
    private string objectTextureName = "";
    private MaterialAssignerWindow.ImageStyle selectedObjectStyle = MaterialAssignerWindow.ImageStyle.General;
    private MaterialAssignerWindow.CharBackgroundStyle objectBackStyle = MaterialAssignerWindow.CharBackgroundStyle.General;
    private MaterialAssignerWindow.ImageQuality imageQuality = MaterialAssignerWindow.ImageQuality.High;

    public ObjectGeneratorModule(MaterialAssignerWindow owner)
    {
        this.owner = owner;
    }

    public void Draw()
    {
        GUILayout.Label("🧩 2D Object", EditorStyles.boldLabel);
        GUILayout.Space(8);

        EditorGUILayout.HelpBox(
            "This module always uses OpenAI for image generation, regardless of the globally selected provider.",
            MessageType.Info
        );

        GUILayout.Space(4);
        selectedObjectStyle = (MaterialAssignerWindow.ImageStyle)EditorGUILayout.EnumPopup("Style", selectedObjectStyle);
        GUILayout.Space(4);

        objectBackStyle = (MaterialAssignerWindow.CharBackgroundStyle)EditorGUILayout.EnumPopup("Background", objectBackStyle);

        GUILayout.Space(6);

        GUILayout.Label("Describe your object:", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Describe the object you want: shape, material, colors, theme, details, etc.",
            MessageType.None
        );

        GUIStyle wrapStyle = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = true
        };

        objectPrompt = EditorGUILayout.TextArea(
            objectPrompt,
            wrapStyle,
            GUILayout.Height(80)
        );

        GUILayout.Space(10);

        GUI.enabled = !isGeneratingObject && !string.IsNullOrEmpty(objectPrompt.Trim());

        GUILayout.Space(4);
        imageQuality = (MaterialAssignerWindow.ImageQuality)EditorGUILayout.EnumPopup("Intent Quality", imageQuality);

        MaterialAssignerWindow.DrawQualityBar(imageQuality);

        if (GUILayout.Button("Generate Object", GUILayout.Height(30)))
        {
            objectErrorMessage = "";
            _ = GenerateObjectAsync();
        }

        GUI.enabled = true;

        GUILayout.Space(6);

        if (!string.IsNullOrEmpty(objectErrorMessage))
        {
            EditorGUILayout.HelpBox(objectErrorMessage, MessageType.Error);
            GUILayout.Space(6);
        }

        if (isGeneratingObject)
        {
            Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
            float prog = Mathf.PingPong((float)EditorApplication.timeSinceStartup, 1f);
            EditorGUI.ProgressBar(progressRect, prog, "Generating Object...");
            GUILayout.Space(8);
            owner.Repaint();
        }

        if (!isGeneratingObject && objectTexture != null)
        {
            GUILayout.Space(10);
            GUILayout.Label("Preview:");

            Rect previewRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.Height(250));
            EditorGUI.DrawPreviewTexture(previewRect, objectTexture);

            GUILayout.Space(8);

            objectTextureName = EditorGUILayout.TextField("File Name", objectTextureName);

            GUI.enabled = !string.IsNullOrEmpty(objectTextureName.Trim());

            if (GUILayout.Button("Save Object"))
                SaveObjectTextureOnly();

            GUI.enabled = true;
        }
    }

    private string GetCurrentProviderString()
    {
        return EditorPrefs.GetString(ProviderPref, AIManager.AIProviderType.OpenAI.ToString());
    }

    private void SetProviderString(string provider)
    {
        EditorPrefs.SetString(ProviderPref, provider);
    }

    private AIManager ForceOpenAiProvider()
    {
        SetProviderString(AIManager.AIProviderType.OpenAI.ToString());

        AIManager aiManager = AIManager.EnsureManagerInstanceForEditor();
        if (aiManager != null)
        {
            aiManager.SetProvider(AIManager.AIProviderType.OpenAI);
        }

        return aiManager;
    }

    private void RestoreProvider(string previousProvider)
    {
        if (string.IsNullOrWhiteSpace(previousProvider))
            previousProvider = AIManager.AIProviderType.OpenAI.ToString();

        SetProviderString(previousProvider);

        AIManager aiManager = AIManager.EnsureManagerInstanceForEditor();
        if (aiManager != null &&
            Enum.TryParse(previousProvider, out AIManager.AIProviderType parsedProvider))
        {
            aiManager.SetProvider(parsedProvider);
        }
    }

    private async Task GenerateObjectAsync()
    {
        string previousProvider = GetCurrentProviderString();

        isGeneratingObject = true;
        objectErrorMessage = "";
        objectTexture = null;
        owner.Repaint();

        try
        {
            var manager = ForceOpenAiProvider();

            string fullPrompt =
                "Create a stylized 2D videogame object, centered, clear silhouette, single object only, object design reference. " +
                "No characters, no scene, no environment, no floor, no platform, no extra props. " +
                "Object description: " + objectPrompt +
                MaterialAssignerWindow.GetStylePromptSuffix(selectedObjectStyle) +
                MaterialAssignerWindow.GetBackgroundStyle(objectBackStyle);

            Texture2D generatedObject = await manager.GenerateImageFromPrompt(
                fullPrompt,
                size: "1024x1024",
                quality: MaterialAssignerWindow.GetImageQualityString(imageQuality)
            );

            if (generatedObject == null)
            {
                objectTexture = null;
                objectErrorMessage = "Request error: the generation was rejected or did not return an image.";
            }
            else
            {
                objectTexture = generatedObject;
            }
        }
        catch (System.Exception ex)
        {
            objectTexture = null;

            string msg = ex.Message;

            if (msg.Contains("moderation_blocked"))
                msg = "Rejected: content violates security policies.";
            else if (msg.Contains("image_generation_user_error"))
                msg = "Image generation error.";
            else
                msg = ex.Message;

            objectErrorMessage = "Request error: " + msg;
            Debug.LogError(ex);
        }
        finally
        {
            RestoreProvider(previousProvider);
            isGeneratingObject = false;
            owner.Repaint();
        }
    }

    private void SaveObjectTextureOnly()
    {
        if (objectTexture == null)
        {
            Debug.LogWarning("There is no object to save.");
            return;
        }

        const string relativeFolder = "Assets/AI-Generated/Graphics/Objects2D";
        string absoluteFolder = Path.Combine(Application.dataPath, "AI-Generated", "Graphics", "Objects2D");

        string fileName = objectTextureName.Trim();

        if (string.IsNullOrEmpty(fileName))
        {
            Debug.LogWarning("Enter a name to save the object.");
            return;
        }

        try
        {
            Directory.CreateDirectory(absoluteFolder);

            string relativePath = Path.Combine(relativeFolder, fileName + ".png").Replace("\\", "/");
            string absolutePath = Path.Combine(absoluteFolder, fileName + ".png");

            byte[] pngData = objectTexture.EncodeToPNG();

            if (pngData == null || pngData.Length == 0)
            {
                Debug.LogError("Failed to encode object texture to PNG.");
                return;
            }

            File.WriteAllBytes(absolutePath, pngData);

            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(relativePath);

            Debug.Log($"Object saved in {relativePath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to save object texture:\n" + ex);
        }
    }
}