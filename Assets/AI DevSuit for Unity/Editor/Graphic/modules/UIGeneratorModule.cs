using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using System;

public class UIGeneratorModule
{
    private readonly MaterialAssignerWindow owner;

    private string uiPrompt = "";
    private Texture2D uiTexture;
    private bool isGeneratingUI = false;
    private string uiErrorMessage = "";
    private string uiTextureName = "";
    private MaterialAssignerWindow.GameUIStyle uiStyle = MaterialAssignerWindow.GameUIStyle.Cartoon;
    private MaterialAssignerWindow.ImageQuality imageQuality = MaterialAssignerWindow.ImageQuality.High;

    private string savedUIAssetPath = "";
    private string savedUIResourcesFolder = "";
    private bool uiPackWasSaved = false;
    private int lastCreatedUIResourcesCount = 0;

    private int uiAlphaThreshold = 10;
    private int uiMinObjectPixels = 20;
    private int uiPadding = 2;
    private bool uiSortTopToBottom = true;

    public UIGeneratorModule(MaterialAssignerWindow owner)
    {
        this.owner = owner;
    }

    public void Draw()
    {
        GUILayout.Label("Generate UI elements", EditorStyles.boldLabel);
        GUILayout.Space(8);

        if (!string.IsNullOrEmpty(uiErrorMessage))
            EditorGUILayout.HelpBox(uiErrorMessage, MessageType.Error);

        GUILayout.Space(4);
        uiStyle = (MaterialAssignerWindow.GameUIStyle)EditorGUILayout.EnumPopup("Game Style", uiStyle);
        GUILayout.Space(4);

        GUILayout.Label("Describe your UI pack:", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Describe your game, give context for the UI.",
            MessageType.None
        );

        GUIStyle wrapStyle = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = true
        };

        uiPrompt = EditorGUILayout.TextArea(
            uiPrompt,
            wrapStyle,
            GUILayout.Height(80)
        );

        GUILayout.Space(4);
        imageQuality = (MaterialAssignerWindow.ImageQuality)EditorGUILayout.EnumPopup("Intent Quality", imageQuality);

        MaterialAssignerWindow.DrawQualityBar(imageQuality);

        GUILayout.Space(8);

        GUI.enabled = !isGeneratingUI && !string.IsNullOrEmpty(uiPrompt.Trim());

        if (GUILayout.Button("Generate UI Pack", GUILayout.Height(30)))
        {
            uiErrorMessage = "";
            _ = GenerateUIPackAsync();
        }

        GUI.enabled = true;

        GUILayout.Space(6);

        if (isGeneratingUI)
        {
            Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
            float prog = Mathf.PingPong((float)EditorApplication.timeSinceStartup, 1f);
            EditorGUI.ProgressBar(progressRect, prog, "Generating UI Pack...");
            GUILayout.Space(8);
            owner.Repaint();
        }

        if (!isGeneratingUI && uiTexture != null)
        {
            GUILayout.Space(10);
            GUILayout.Label("Preview:");

            Rect previewRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.Height(250));
            EditorGUI.DrawPreviewTexture(previewRect, uiTexture);

            GUILayout.Space(8);

            uiTextureName = EditorGUILayout.TextField("File Name", uiTextureName);

            GUI.enabled = !string.IsNullOrEmpty(uiTextureName.Trim());

            if (GUILayout.Button("Save UI Pack"))
                SaveUITextureOnly();

            GUI.enabled = true;

            GUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                GUILayout.Label("Create Resources From Saved UI Pack", EditorStyles.boldLabel);
                GUILayout.Space(4);

                EditorGUILayout.HelpBox(
                    "After saving the UI pack, this will cut the transparent PNG into separate UI resources using TransparentObjectSplitterCore.",
                    MessageType.Info
                );

                uiAlphaThreshold = EditorGUILayout.IntSlider("Alpha Threshold", uiAlphaThreshold, 0, 255);
                uiMinObjectPixels = EditorGUILayout.IntField("Min Object Pixels", uiMinObjectPixels);
                uiPadding = EditorGUILayout.IntSlider("Padding", uiPadding, 0, 32);
                uiSortTopToBottom = EditorGUILayout.Toggle("Sort Top To Bottom", uiSortTopToBottom);

                GUILayout.Space(6);

                GUI.enabled = uiPackWasSaved && !string.IsNullOrEmpty(savedUIAssetPath);

                if (GUILayout.Button("Create Resources", GUILayout.Height(30)))
                {
                    CreateUIResourcesFromSavedPack();
                }

                GUI.enabled = true;

                if (!uiPackWasSaved)
                {
                    EditorGUILayout.HelpBox(
                        "Save the UI pack first to enable resource creation.",
                        MessageType.None
                    );
                }
                else
                {
                    EditorGUILayout.LabelField("Saved UI Path:", savedUIAssetPath);

                    if (!string.IsNullOrEmpty(savedUIResourcesFolder))
                        EditorGUILayout.LabelField("Resources Folder:", savedUIResourcesFolder);

                    if (lastCreatedUIResourcesCount > 0)
                        EditorGUILayout.LabelField("Last Created Resources:", lastCreatedUIResourcesCount.ToString());
                }
            }
        }
    }

    private async Task GenerateUIPackAsync()
    {
        isGeneratingUI = true;
        uiErrorMessage = "";
        uiTexture = null;

        savedUIAssetPath = "";
        savedUIResourcesFolder = "";
        uiPackWasSaved = false;
        lastCreatedUIResourcesCount = 0;

        owner.Repaint();

        try
        {
            var manager = AIManager.EnsureManagerInstanceForEditor();

            string fullPrompt =
                "Make sure the image stays inside the canvas margins and no object gets cut off. " +
                "Create a centered composition that occupies around 70% of the image, with the rest transparent. " +
                "Create a professional game UI pack in a single image, organized like a clean asset sheet. " +
                "Never put text in the elements. " +
                "Include only UI elements, no characters, no scenery, no mockup, no device frame, no background scene. " +
                "Transparent background. " +
                "The pack should be suitable for slicing in Unity. " +
                "Include cohesive UI elements such as buttons, panels, windows, frames, bars, icons, badges, tabs, slots, HUD pieces, and decorative interface parts when appropriate. " +
                "All elements must share the same visual style and look like part of the same game UI system. " +
                "Keep enough spacing between elements so they can be cut easily. " +
                "High readability, polished shapes, production-ready UI assets. " +
                "Style: " + uiStyle.GetUIStyle() + ". " +
                "Requested content. The UI is for a game with this description: " + uiPrompt + ". " +
                "Pack description: 1 square button, 1 rectangular button, 1 points container, 1 message container, 1 close button, 1 next button, 1 round button, 1 money icon, 1 resource icon, 1 chest.";

            Texture2D generatedUI = await manager.GenerateImageFromPrompt(
                fullPrompt,
                size: "1024x1024",
                quality: MaterialAssignerWindow.GetImageQualityString(imageQuality)
            );

            if (generatedUI == null)
            {
                uiTexture = null;
                uiErrorMessage = "Request error: the generation was rejected or did not return an image.";
            }
            else
            {
                uiTexture = generatedUI;
            }
        }
        catch (System.Exception ex)
        {
            uiTexture = null;

            string msg = ex.Message;

            if (msg.Contains("moderation_blocked"))
                msg = "Rejected: content violates security policies.";
            else if (msg.Contains("image_generation_user_error"))
                msg = "Image generation error.";
            else
                msg = ex.Message;

            uiErrorMessage = "Request error: " + msg;
            Debug.LogError(ex);
        }
        finally
        {
            isGeneratingUI = false;
            owner.Repaint();
        }
    }

    private void SaveUITextureOnly()
    {
        if (uiTexture == null)
        {
            Debug.LogWarning("There is no UI texture to save.");
            return;
        }

        const string uiFolder = "Assets/AI-Generated/Graphics/UI";
        string absoluteUiFolder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, uiFolder);

        Directory.CreateDirectory(absoluteUiFolder);

        string fileName = uiTextureName.Trim();

        if (string.IsNullOrEmpty(fileName))
        {
            Debug.LogWarning("Enter a name to save the UI pack.");
            return;
        }

        string filePath = Path.Combine(uiFolder, fileName + ".png").Replace("\\", "/");
        string absoluteFilePath = Path.Combine(absoluteUiFolder, fileName + ".png");

        byte[] pngData = uiTexture.EncodeToPNG();

        try
        {
            File.WriteAllBytes(absoluteFilePath, pngData);

            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(filePath);

            savedUIAssetPath = filePath;
            savedUIResourcesFolder = Path.Combine(uiFolder, fileName + "_Resources").Replace("\\", "/");
            uiPackWasSaved = true;
            lastCreatedUIResourcesCount = 0;

            Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(filePath);
            if (importedTexture != null)
            {
                uiTexture = importedTexture;
            }

            Debug.Log($"UI pack saved in {filePath}");
            owner.Repaint();
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to save UI texture:\n" + absoluteFilePath + "\n" + ex);
        }
    }

    private void CreateUIResourcesFromSavedPack()
    {
        if (!uiPackWasSaved || string.IsNullOrEmpty(savedUIAssetPath))
        {
            EditorUtility.DisplayDialog("Error", "Save the UI pack first.", "OK");
            return;
        }

        Texture2D savedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(savedUIAssetPath);

        if (savedTexture == null)
        {
            EditorUtility.DisplayDialog("Error", "Could not load the saved UI texture from Assets.", "OK");
            return;
        }

        string outputFolder = savedUIResourcesFolder;

        var settings = new TransparentObjectSplitterCore.SplitSettings
        {
            alphaThreshold = uiAlphaThreshold,
            minObjectPixels = Mathf.Max(1, uiMinObjectPixels),
            padding = Mathf.Max(0, uiPadding),
            sortTopToBottom = uiSortTopToBottom
        };

        List<TransparentObjectSplitterCore.SplitResult> results =
            TransparentObjectSplitterCore.SplitTexture(savedTexture, outputFolder, settings);

        lastCreatedUIResourcesCount = results != null ? results.Count : 0;

        if (results == null || results.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Result",
                "No separated UI resources were found.",
                "OK"
            );
            return;
        }

        Object folderObj = AssetDatabase.LoadAssetAtPath<Object>(outputFolder);
        if (folderObj != null)
        {
            EditorGUIUtility.PingObject(folderObj);
            Selection.activeObject = folderObj;
        }

        EditorUtility.DisplayDialog(
            "Done",
            $"Created {results.Count} UI resources in:\n{outputFolder}",
            "OK"
        );

        owner.Repaint();
    }
}