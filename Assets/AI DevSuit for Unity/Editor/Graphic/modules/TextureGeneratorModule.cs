using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class TextureGeneratorModule
{
    private readonly MaterialAssignerWindow owner;

    private GameObject targetObject;
    private Texture2D texture;
    private string imagePrompt = "Describe here the texture you want to generate";
    private string textureName = "";
    private string materialName = "";
    private bool isGenerating = false;
    private bool makeTileable = true;
    private string textureErrorMessage = "";
    private MaterialAssignerWindow.ImageQuality imageQuality = MaterialAssignerWindow.ImageQuality.High;

    public TextureGeneratorModule(MaterialAssignerWindow owner)
    {
        this.owner = owner;
    }

    public void Draw()
    {
        GUILayout.Label("Generate & Assign Texture", EditorStyles.boldLabel);

        if (!string.IsNullOrEmpty(textureErrorMessage))
            EditorGUILayout.HelpBox(textureErrorMessage, MessageType.Error);

        EditorGUILayout.LabelField("Texture Name:");
        textureName = EditorGUILayout.TextField(textureName);

        EditorGUILayout.LabelField("Image Prompt:");
        imagePrompt = EditorGUILayout.TextField(imagePrompt);

        GUILayout.Space(4);
        makeTileable = EditorGUILayout.ToggleLeft("Make Tileable / Ready for Tiling", makeTileable);

        EditorGUILayout.HelpBox(
            makeTileable
                ? "The prompt will request a seamless tileable texture ready to repeat on 3D objects in Unity."
                : "The prompt will request a front-facing material texture for Unity, but not necessarily seamless.",
            MessageType.None
        );

        GUI.enabled = !isGenerating
                      && !string.IsNullOrEmpty(textureName.Trim())
                      && !string.IsNullOrEmpty(imagePrompt.Trim());

        GUILayout.Space(4);
        imageQuality = (MaterialAssignerWindow.ImageQuality)EditorGUILayout.EnumPopup("Intent Quality", imageQuality);

        MaterialAssignerWindow.DrawQualityBar(imageQuality);

        GUILayout.Space(4);

        if (GUILayout.Button("Generate Texture"))
        {
            textureErrorMessage = "";
            _ = GenerateImageAsync();
        }

        GUI.enabled = true;

        if (isGenerating)
        {
            Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
            float prog = Mathf.PingPong((float)EditorApplication.timeSinceStartup, 1f);
            EditorGUI.ProgressBar(progressRect, prog, "Generating texture...");
            GUILayout.Space(8);
            owner.Repaint();
        }

        if (!isGenerating && texture != null)
        {
            GUILayout.Label("Preview:");

            Rect previewRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.Height(200));
            EditorGUI.DrawPreviewTexture(previewRect, texture);

            GUILayout.Space(8);

            if (GUILayout.Button("Save Texture"))
                SaveTextureOnly();

            GUILayout.Space(12);
        }

        GUILayout.Label("⚙️ Create Material and Assign Texture", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("Material Name:");
        materialName = EditorGUILayout.TextField(materialName);

        targetObject = (GameObject)EditorGUILayout.ObjectField("GameObject", targetObject, typeof(GameObject), true);
        texture = (Texture2D)EditorGUILayout.ObjectField("Texture", texture, typeof(Texture2D), false);

        bool canAssign = !string.IsNullOrEmpty(materialName.Trim()) && targetObject != null;

        GUI.enabled = canAssign;

        if (GUILayout.Button("Assign and Save Material"))
            AssignAndSaveMaterial();

        GUI.enabled = true;
    }

    private async Task GenerateImageAsync()
    {
        isGenerating = true;
        textureErrorMessage = "";
        texture = null;
        owner.Repaint();

        try
        {
            var manager = AIManager.EnsureManagerInstanceForEditor(); // ✅ FIX

            string fullPrompt = BuildTexturePrompt();

            Texture2D generatedTexture = await manager.GenerateImageFromPrompt(
                fullPrompt,
                size: "1024x1024",
                quality: MaterialAssignerWindow.GetImageQualityString(imageQuality)
            );

            if (generatedTexture == null)
            {
                texture = null;
                textureErrorMessage = "Request error: the generation was rejected or did not return an image.";
            }
            else
            {
                texture = generatedTexture;
            }
        }
        catch (System.Exception ex)
        {
            texture = null;

            string msg = ex.Message;

            if (msg.Contains("moderation_blocked"))
                msg = "Rejected: content violates security policies.";
            else if (msg.Contains("image_generation_user_error"))
                msg = "Image generation error.";
            else
                msg = ex.Message;

            textureErrorMessage = "Request error: " + msg;
            Debug.LogError(ex);
        }
        finally
        {
            isGenerating = false;
            owner.Repaint();
        }
    }

    private string BuildTexturePrompt()
    {
        string basePrompt =
            "Create a game-ready material texture for Unity. " +
            "Front-facing orthographic view of a material surface. " +
            "The image must contain only the surface material, filling the entire image edge to edge. " +
            "No perspective, no camera angle, no scene, no horizon, no environment, no background composition. " +
            "No isolated objects, no extra props, no framing. " +
            "Uniform neutral lighting. " +
            "Suitable for use as a texture on 3D objects in Unity. " +
            "Material description: " + imagePrompt + ". ";

        if (makeTileable)
        {
            basePrompt +=
                "Make it seamless and tileable. " +
                "The texture must repeat cleanly on all sides without visible seams. " +
                "Ready for tiling on walls, floors, props, or other 3D surfaces. ";
        }

        return basePrompt;
    }

    private void SaveTextureOnly()
    {
        if (texture == null)
        {
            Debug.LogWarning("There is no texture to save.");
            return;
        }

        const string relativeFolder = "Assets/AI-Generated/Graphics/Textures";
        string absoluteFolder = Path.Combine(Application.dataPath, "AI-Generated", "Graphics", "Textures");

        string texName = textureName.Trim();

        if (string.IsNullOrEmpty(texName))
        {
            Debug.LogWarning("Enter a name to save the texture.");
            return;
        }

        try
        {
            Directory.CreateDirectory(absoluteFolder);

            string relativePath = Path.Combine(relativeFolder, texName + ".png").Replace("\\", "/");
            string absolutePath = Path.Combine(absoluteFolder, texName + ".png");

            byte[] pngData = texture.EncodeToPNG();

            if (pngData == null || pngData.Length == 0)
            {
                Debug.LogError("Failed to encode texture to PNG.");
                return;
            }

            File.WriteAllBytes(absolutePath, pngData);

            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(relativePath);

            //Debug.Log($"Texture saved in {relativePath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to save texture:\n" + ex);
        }
    }

    private void AssignAndSaveMaterial()
    {
        if (targetObject == null)
        {
            Debug.LogWarning("Please select a GameObject.");
            return;
        }

        if (texture == null)
        {
            Debug.LogWarning("Please generate or select a texture first.");
            return;
        }

        const string relativeTexFolder = "Assets/AI-Generated/Graphics/Textures";
        string absoluteTexFolder = Path.Combine(Application.dataPath, "AI-Generated", "Graphics", "Textures");

        string texName = textureName.Trim();

        if (string.IsNullOrEmpty(texName))
        {
            Debug.LogWarning("Please enter a texture name.");
            return;
        }

        try
        {
            Directory.CreateDirectory(absoluteTexFolder);

            string relativeTexPath = Path.Combine(relativeTexFolder, texName + ".png").Replace("\\", "/");
            string absoluteTexPath = Path.Combine(absoluteTexFolder, texName + ".png");

            byte[] pngData = texture.EncodeToPNG();

            if (pngData == null || pngData.Length == 0)
            {
                Debug.LogError("Failed to encode texture.");
                return;
            }

            File.WriteAllBytes(absoluteTexPath, pngData);

            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(relativeTexPath);

            Texture2D savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(relativeTexPath);

            const string materialsFolder = "Assets/AI-Generated/Graphics/Materials";
            string absoluteMatFolder = Path.Combine(Application.dataPath, "AI-Generated", "Graphics", "Materials");

            Directory.CreateDirectory(absoluteMatFolder);

            string matName = materialName.Trim();

            if (string.IsNullOrEmpty(matName))
            {
                Debug.LogWarning("Please enter a material name.");
                return;
            }

            string matPath = Path.Combine(materialsFolder, matName + ".mat").Replace("\\", "/");

            Material newMat = new Material(Shader.Find("Standard"))
            {
                mainTexture = savedTex != null ? savedTex : texture
            };

            AssetDatabase.CreateAsset(newMat, matPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var renderer = targetObject.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.material = newMat;
                Debug.Log($"Material '{matName}' created and assigned successfully.");
            }
            else
            {
                Debug.LogWarning("The selected GameObject does not have a Renderer component.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to assign and save material:\n" + ex);
        }
    }
}