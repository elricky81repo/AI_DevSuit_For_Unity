using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class CharacterGeneratorModule
{
    private readonly MaterialAssignerWindow owner;

    private string characterPrompt = "";
    private Texture2D characterTexture;
    private bool isGeneratingCharacter = false;

#pragma warning disable CS0414
    private bool isGeneratingAnimation = false;
#pragma warning restore CS0414

    private Texture2D characterAnimationTexture;

    private string characterErrorMessage = "";
#pragma warning disable CS0414
    private string characterAnimationErrorMessage = "";
#pragma warning restore CS0414

    private MaterialAssignerWindow.ImageStyle selectedStyle = MaterialAssignerWindow.ImageStyle.General;
    private MaterialAssignerWindow.CharBackgroundStyle charBackStyle = MaterialAssignerWindow.CharBackgroundStyle.General;
    private MaterialAssignerWindow.ImageQuality imageQuality = MaterialAssignerWindow.ImageQuality.High;
    private string characterTextureName = "";

    public CharacterGeneratorModule(MaterialAssignerWindow owner)
    {
        this.owner = owner;
    }

    public void Draw()
    {
        GUILayout.Label("🧍 2D Character", EditorStyles.boldLabel);
        GUILayout.Space(8);

        EditorGUILayout.HelpBox(
            "This module always uses OpenAI for image generation, regardless of the globally selected provider.",
            MessageType.Info
        );

        selectedStyle = (MaterialAssignerWindow.ImageStyle)EditorGUILayout.EnumPopup("Style", selectedStyle);
        charBackStyle = (MaterialAssignerWindow.CharBackgroundStyle)EditorGUILayout.EnumPopup("Background", charBackStyle);

        GUILayout.Space(6);

        GUILayout.Label("Describe your character:", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Describe your character: appearance, clothing, style, colors, etc.",
            MessageType.None
        );

        GUIStyle wrapStyle = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = true
        };

        characterPrompt = EditorGUILayout.TextArea(characterPrompt, wrapStyle, GUILayout.Height(80));

        GUILayout.Space(10);

        GUI.enabled = !isGeneratingCharacter && !string.IsNullOrEmpty(characterPrompt.Trim());

        imageQuality = (MaterialAssignerWindow.ImageQuality)EditorGUILayout.EnumPopup("Intent Quality", imageQuality);

        MaterialAssignerWindow.DrawQualityBar(imageQuality);

        if (GUILayout.Button("Generate Character", GUILayout.Height(30)))
        {
            characterErrorMessage = "";
            _ = GenerateCharacterAsync();
        }

        GUI.enabled = true;

        if (!string.IsNullOrEmpty(characterErrorMessage))
        {
            EditorGUILayout.HelpBox(characterErrorMessage, MessageType.Error);
        }

        if (isGeneratingCharacter)
        {
            Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
            float prog = Mathf.PingPong((float)EditorApplication.timeSinceStartup, 1f);
            EditorGUI.ProgressBar(progressRect, prog, "Generating Character...");
            owner.Repaint();
        }

        if (!isGeneratingCharacter && characterTexture != null)
        {
            GUILayout.Space(10);
            GUILayout.Label("Preview:");

            Rect previewRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.Height(250));
            EditorGUI.DrawPreviewTexture(previewRect, characterTexture);

            characterTextureName = EditorGUILayout.TextField(characterTextureName);

            GUI.enabled = !string.IsNullOrEmpty(characterTextureName.Trim());

            if (GUILayout.Button("Save Character"))
                SaveCharacterTextureOnly();

            GUI.enabled = true;

            GUILayout.Space(10);

            if (GUILayout.Button("Generate Character Action", GUILayout.Height(30)))
            {
                CharacterActionWindow.ShowWindow(characterTexture);
            }
        }
    }

    private async Task GenerateCharacterAsync()
    {
        isGeneratingCharacter = true;
        characterErrorMessage = "";
        characterTexture = null;
        owner.Repaint();

        try
        {
            var manager = AIManager.EnsureManagerInstanceForEditor();

            string fullPrompt =
                "Create a stylized videogame character, standing pose, centered, simple plain background, character design reference, fully clothed. Character description: "
                + characterPrompt
                + MaterialAssignerWindow.GetStylePromptSuffix(selectedStyle)
                + MaterialAssignerWindow.GetBackgroundStyle(charBackStyle);

            Texture2D generatedCharacter = await manager.GenerateImageFromPrompt(
                fullPrompt,
                size: "1024x1024",
                quality: MaterialAssignerWindow.GetImageQualityString(imageQuality)
            );

            if (generatedCharacter == null)
                characterErrorMessage = "Request error: generation failed.";
            else
                characterTexture = generatedCharacter;
        }
        catch (System.Exception ex)
        {
            characterErrorMessage = ex.Message;
            Debug.LogError(ex);
        }
        finally
        {
            isGeneratingCharacter = false;
            owner.Repaint();
        }
    }

    private void SaveCharacterTextureOnly()
    {
        if (characterTexture == null) return;

        const string relativeFolder = "Assets/AI-Generated/Graphics/2dCharacters";
        string absoluteFolder = Path.Combine(Application.dataPath, "AI-Generated", "Graphics", "2dCharacters");

        string fileName = characterTextureName.Trim();
        if (string.IsNullOrEmpty(fileName)) return;

        Directory.CreateDirectory(absoluteFolder);

        string relativePath = Path.Combine(relativeFolder, fileName + ".png").Replace("\\", "/");
        string absolutePath = Path.Combine(absoluteFolder, fileName + ".png");

        File.WriteAllBytes(absolutePath, characterTexture.EncodeToPNG());

        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(relativePath);
    }
}