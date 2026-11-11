using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class CharacterActionWindow : EditorWindow
{
    private Texture2D sourceCharacterTexture;
    private Texture2D actionResultTexture;

    private bool isGeneratingAction = false;
    private string actionErrorMessage = "";
    private string actionTextureName = "";
    private string actionDescription = "giving a speech";

    private Vector2 scrollPos;

    public static void ShowWindow(Texture2D sourceTexture)
    {
        var window = GetWindow<CharacterActionWindow>("Character Action");

        window.minSize = new Vector2(420, 560);

        window.sourceCharacterTexture = sourceTexture;
        window.actionResultTexture = null;
        window.actionErrorMessage = "";
        window.actionTextureName = "";
        window.actionDescription = "giving a speech";
        window.isGeneratingAction = false;

        window.Show();
    }

    [MenuItem("Tools/AI DevSuite/Character Action Generator")]
    public static void ShowEmptyWindow()
    {
        ShowWindow(null);
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("🧍 Character Action Generator", EditorStyles.boldLabel);
        GUILayout.Space(6);

        EditorGUILayout.HelpBox(
            "Generate a new pose/action of the SAME character based on a reference image. " +
            "This creates a single character variation, not an animation.",
            MessageType.Info
        );

        GUILayout.Space(4);

        EditorGUILayout.HelpBox(
            "This window always uses OpenAI for image generation, regardless of the globally selected provider.",
            MessageType.Info
        );

        GUILayout.Space(8);

        bool canUseWindow = HasOpenAIApiKey();

        if (!canUseWindow)
        {
            DrawOpenAIRequirementBanner();
            GUILayout.Space(6);

            EditorGUILayout.HelpBox(
                "This window requires a valid OpenAI API key configured in Setup before it can generate character actions.",
                MessageType.Warning
            );

            EditorGUILayout.EndScrollView();
            return;
        }

        GUILayout.Label("Source Character:", EditorStyles.boldLabel);

        sourceCharacterTexture = (Texture2D)EditorGUILayout.ObjectField(
            "Source Texture",
            sourceCharacterTexture,
            typeof(Texture2D),
            false
        );

        GUILayout.Space(8);

        if (sourceCharacterTexture == null)
        {
            EditorGUILayout.HelpBox("No source character image was provided.", MessageType.Error);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawTextureFit(sourceCharacterTexture, 220f);

        GUILayout.Space(12);

        GUI.enabled = !isGeneratingAction;

        GUILayout.Label("Action Settings:", EditorStyles.boldLabel);
        actionDescription = EditorGUILayout.TextField("Describe the action", actionDescription);

        GUILayout.Space(8);

        if (GUILayout.Button("Generate Action", GUILayout.Height(35)))
        {
            actionErrorMessage = "";
            _ = GenerateActionAsync();
        }

        GUI.enabled = true;

        GUILayout.Space(8);

        if (!string.IsNullOrEmpty(actionErrorMessage))
        {
            EditorGUILayout.HelpBox(actionErrorMessage, MessageType.Error);
            GUILayout.Space(6);
        }

        if (isGeneratingAction)
        {
            Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
            float prog = Mathf.PingPong((float)EditorApplication.timeSinceStartup * 0.35f, 1f);
            EditorGUI.ProgressBar(progressRect, prog, "Generating Character Action...");
            GUILayout.Space(10);
            Repaint();
        }

        if (!isGeneratingAction && actionResultTexture != null)
        {
            GUILayout.Space(15);
            GUILayout.Label("Action Result:", EditorStyles.boldLabel);

            DrawTextureFit(actionResultTexture, Mathf.Max(180f, position.height * 0.55f));

            GUILayout.Space(10);

            EditorGUILayout.LabelField("File Name:");
            actionTextureName = EditorGUILayout.TextField(actionTextureName);

            GUI.enabled = actionResultTexture != null && !string.IsNullOrEmpty(actionTextureName.Trim());

            if (GUILayout.Button("Save Action", GUILayout.Height(30)))
                SaveActionTextureOnly();

            GUI.enabled = true;
        }

        EditorGUILayout.EndScrollView();
    }

    private bool HasOpenAIApiKey()
    {
        string key = VibeToolsWindow.GPTPrefs.ApiKey;
        return !string.IsNullOrWhiteSpace(key);
    }

    private void DrawOpenAIRequirementBanner()
    {
        if (HasOpenAIApiKey())
            return;

        string title = "OPENAI API KEY NOT SET";
        string body = "Character Action Generator requires OpenAI and no OpenAI API key is configured. You must complete Setup before using this window.";

        Color previousBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.95f, 0.35f, 0.35f);

        EditorGUILayout.BeginVertical("box");
        GUILayout.Space(2);

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.normal.textColor = Color.white;
        titleStyle.fontSize = 12;

        GUIStyle bodyStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
        bodyStyle.normal.textColor = Color.white;

        EditorGUILayout.LabelField("⚠ " + title, titleStyle);
        GUILayout.Space(2);
        EditorGUILayout.LabelField(body, bodyStyle);
        GUILayout.Space(6);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Open Setup", GUILayout.Height(26)))
        {
            SetupWindow.ShowWindow();
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(2);
        EditorGUILayout.EndVertical();

        GUI.backgroundColor = previousBg;
    }

    private void DrawTextureFit(Texture2D tex, float maxHeight)
    {
        if (tex == null) return;

        float viewWidth = position.width - 40f;
        float aspect = (float)tex.width / tex.height;

        float drawWidth = Mathf.Min(viewWidth, tex.width);
        float drawHeight = drawWidth / aspect;

        if (drawHeight > maxHeight)
        {
            drawHeight = maxHeight;
            drawWidth = drawHeight * aspect;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        Rect rect = GUILayoutUtility.GetRect(
            drawWidth,
            drawHeight,
            GUILayout.Width(drawWidth),
            GUILayout.Height(drawHeight)
        );

        GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private async Task GenerateActionAsync()
    {
        if (sourceCharacterTexture == null)
        {
            actionErrorMessage = "Request error: there is no base character image to use as reference.";
            Repaint();
            return;
        }

        if (string.IsNullOrWhiteSpace(actionDescription))
        {
            actionErrorMessage = "Request error: please describe the action.";
            Repaint();
            return;
        }

        isGeneratingAction = true;
        actionErrorMessage = "";
        actionResultTexture = null;
        Repaint();

        try
        {
            var manager = AIManager.EnsureManagerInstanceForEditor();

            string prompt =
                "Create a SINGLE new image of the SAME character from the reference image performing this action: " + actionDescription + ". " +

                "CRITICAL PRIORITY: preserve the exact same character identity from the reference. " +
                "Do not redesign the character. Do not change the face, hairstyle, proportions, body type, outfit, accessories, colors, art style, silhouette, or visual identity. " +
                "This must clearly look like the exact same character, only in a different pose/action. " +

                "The only thing that may change is the body pose, gesture, and expression needed to represent the action. " +
                "Keep the character consistent and recognizable. " +

                "IMPORTANT: generate ONLY ONE character, not multiple frames, not a spritesheet, not animation, not motion sequence. " +
                "This is a single static action pose. " +

                "The character must be full body and fully visible inside the canvas. " +
                "No cropped head, no cropped feet, no cropped hands, no cropped accessories. " +
                "Leave enough transparent margin around the character. " +

                "IMPORTANT: the final image must be a PNG with REAL transparent background using alpha channel (RGBA). " +
                "Everything that is not character must be fully transparent. " +
                "No background, no floor, no shadows on background, no environment, no scenery, no text, no UI, no frame, no border. " +
                "Transparency must be real, not simulated. " +

                "Visual requirements: clean 2D game-ready character art, centered in the image, using around 60% of the canvas size so it remains easy to save and use in Unity. " +

                "FINAL RULE: return only one PNG image with a truly transparent background, showing the exact same character from the reference performing the requested action.";

            Texture2D generatedAction = await manager.GenerateImageFromPromptAndReference(
                prompt,
                sourceCharacterTexture,
                "1024x1024"
            );

            if (generatedAction == null)
            {
                actionResultTexture = null;
                actionErrorMessage = "Request error: the action generation was rejected or did not return an image.";
            }
            else
            {
                actionResultTexture = generatedAction;
            }
        }
        catch (System.Exception ex)
        {
            actionResultTexture = null;

            string msg = ex.Message;

            if (msg.Contains("moderation_blocked"))
                msg = "Rejected: content violates security policies.";
            else if (msg.Contains("image_generation_user_error"))
                msg = "Image generation error.";
            else
                msg = ex.Message;

            actionErrorMessage = "Request error: " + msg;
            Debug.LogError(ex);
        }
        finally
        {
            isGeneratingAction = false;
            Repaint();
        }
    }

    private void SaveActionTextureOnly()
    {
        if (actionResultTexture == null)
        {
            Debug.LogWarning("There is no action result to save.");
            return;
        }

        const string relativeFolder = "Assets/AI-Generated/Graphics/2dCharacters/Actions";
        string absoluteFolder = Path.Combine(Application.dataPath, "AI-Generated", "Graphics", "2dCharacters", "Actions");

        string fileName = actionTextureName.Trim();

        if (string.IsNullOrEmpty(fileName))
        {
            Debug.LogWarning("Enter a name to save the action image.");
            return;
        }

        try
        {
            Directory.CreateDirectory(absoluteFolder);

            string relativePath = Path.Combine(relativeFolder, fileName + ".png").Replace("\\", "/");
            string absolutePath = Path.Combine(absoluteFolder, fileName + ".png");

            byte[] pngData = actionResultTexture.EncodeToPNG();

            if (pngData == null || pngData.Length == 0)
            {
                Debug.LogError("Failed to encode action texture to PNG.");
                return;
            }

            File.WriteAllBytes(absolutePath, pngData);

            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(relativePath);

            Debug.Log($"Character action saved in {relativePath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to save character action texture:\n" + ex);
        }
    }
}