using UnityEditor;
using UnityEngine;

public class MaterialAssignerWindow : EditorWindow
{
    public enum Tab
    {
        TextureGenerator = 0,
        Characters2D = 1,
        Objects2D = 2,
        UIGenerator = 3
    }

    public enum ImageStyle
    {
        General,
        Toon,
        Realistic,
        ThreeD,
        Japan,
        PixelArt,
        Anime
    }

    public enum CharBackgroundStyle
    {
        General,
        transparent,
        landscape,
        white
    }

    public enum GameUIStyle
    {
        Flat,
        Material,
        Cartoon,
        Fantasy,
        SciFi,
        PixelArt,
        Glass,
        DarkTech,
        HandDrawn
    }

    public enum ImageQuality
    {
        Low,
        Medium,
        High
    }

    private Tab currentTab = Tab.TextureGenerator;
    private Vector2 scrollPos;

    private TextureGeneratorModule textureModule;
    private CharacterGeneratorModule characterModule;
    private ObjectGeneratorModule objectModule;
    private UIGeneratorModule uiModule;

    [MenuItem("Tools/AI DevSuite/Graphic Tools")]
    public static void ShowWindow()
    {
        GetWindow<MaterialAssignerWindow>("AI Graphics");
    }

    private void OnEnable()
    {
        textureModule = new TextureGeneratorModule(this);
        characterModule = new CharacterGeneratorModule(this);
        objectModule = new ObjectGeneratorModule(this);
        uiModule = new UIGeneratorModule(this);
    }

    private void OnGUI()
    {
        EnsureModules();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("🎨 AI GRAPHIC GENERATOR TOOL", EditorStyles.boldLabel);
        GUILayout.Space(8);

        bool canUseWindow = CanUseThisWindow();

        if (!canUseWindow)
        {
            DrawOpenAIRequirementBanner();
            GUILayout.Space(6);

            using (new EditorGUI.DisabledScope(true))
            {
                DrawTabs();
            }

            EditorGUILayout.Space(8);

            EditorGUILayout.HelpBox(
                "All graphic tools are disabled until OpenAI has a valid API key configured in Setup.",
                MessageType.Warning
            );

            EditorGUILayout.Space(6);

            EditorGUILayout.HelpBox(
                "Even with a valid OpenAI API key, image generation can still fail if the account does not have access to the required image models or endpoints.",
                MessageType.Info
            );

            EditorGUILayout.EndScrollView();
            return;
        }

        DrawTabs();

        EditorGUILayout.Space(8);

        switch (currentTab)
        {
            case Tab.TextureGenerator:
                textureModule.Draw();
                break;

            case Tab.Characters2D:
                characterModule.Draw();
                break;

            case Tab.Objects2D:
                objectModule.Draw();
                break;

            case Tab.UIGenerator:
                uiModule.Draw();
                break;
        }

        EditorGUILayout.EndScrollView();
    }

    private void EnsureModules()
    {
        if (textureModule == null) textureModule = new TextureGeneratorModule(this);
        if (characterModule == null) characterModule = new CharacterGeneratorModule(this);
        if (objectModule == null) objectModule = new ObjectGeneratorModule(this);
        if (uiModule == null) uiModule = new UIGeneratorModule(this);
    }

    public bool CanUseThisWindow()
    {
        return HasOpenAIApiKey();
    }

    public bool HasOpenAIApiKey()
    {
        string key = VibeToolsWindow.GPTPrefs.ApiKey;
        return !string.IsNullOrWhiteSpace(key);
    }

    private void DrawOpenAIRequirementBanner()
    {
        if (HasOpenAIApiKey())
            return;

        string title = "OPENAI API KEY NOT SET";
        string body = "Graphic Tools require OpenAI and no OpenAI API key is configured. You must complete Setup before using this window.";

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

    private void DrawTabs()
    {
        currentTab = (Tab)GUILayout.Toolbar(
            (int)currentTab,
            new[] { "Texture", "2D Character", "2D Object", "UI" }
        );
    }

    public static void DrawQualityBar(ImageQuality imageQuality)
    {
        Rect rect = GUILayoutUtility.GetRect(18, 18, "TextField");

        switch (GetImageQualityString(imageQuality))
        {
            case "low":
                EditorGUI.ProgressBar(rect, 0.2f, "Cost");
                break;

            case "medium":
                EditorGUI.ProgressBar(rect, 0.5f, "Cost");
                break;

            case "high":
                EditorGUI.ProgressBar(rect, 1f, "Cost");
                break;
        }
    }

    public static string GetImageQualityString(ImageQuality imageQuality)
    {
        switch (imageQuality)
        {
            case ImageQuality.Low:
                return "low";

            case ImageQuality.Medium:
                return "medium";

            case ImageQuality.High:
                return "high";

            default:
                return "low";
        }
    }

    public static string GetStylePromptSuffix(ImageStyle style)
    {
        switch (style)
        {
            case ImageStyle.Toon:
                return " Cartoon style, clean outlines, stylized shapes, vibrant colors.";

            case ImageStyle.Realistic:
                return " Photorealistic style, realistic lighting, natural textures, high detail.";

            case ImageStyle.ThreeD:
                return " 3D render style, volumetric lighting, detailed materials, cinematic look.";

            case ImageStyle.Japan:
                return " Studio Ghibli-inspired style, soft painterly backgrounds, warm atmosphere, charming anime aesthetic.";

            case ImageStyle.PixelArt:
                return " Pixel art style, low resolution, crisp pixels.";

            case ImageStyle.Anime:
                return " Anime style, high quality movie-like look.";

            case ImageStyle.General:
            default:
                return "";
        }
    }

    public static string GetBackgroundStyle(CharBackgroundStyle backgroundStyle)
    {
        switch (backgroundStyle)
        {
            case CharBackgroundStyle.transparent:
                return " Everything that is not the main subject must be transparent. Return a PNG image with transparent background.";

            case CharBackgroundStyle.landscape:
                return " The background must be a landscape composition in the same visual style as the main subject.";

            case CharBackgroundStyle.white:
                return " Everything that is not the main subject must be white. Return a PNG image with a white background.";

            case CharBackgroundStyle.General:
            default:
                return "";
        }
    }
}

public static class GameUIStyleExtensions
{
    public static string GetUIStyle(this MaterialAssignerWindow.GameUIStyle style)
    {
        switch (style)
        {
            case MaterialAssignerWindow.GameUIStyle.Flat:
                return "Flat minimalist game UI, simple shapes, solid colors, no gradients, clean modern interface";

            case MaterialAssignerWindow.GameUIStyle.Material:
                return "Material design game UI, layered panels, soft shadows, modern buttons, smooth rounded shapes";

            case MaterialAssignerWindow.GameUIStyle.Cartoon:
                return "Cartoon mobile game UI, colorful, thick outlines, rounded buttons, playful style similar to Clash Royale or Brawl Stars";

            case MaterialAssignerWindow.GameUIStyle.Fantasy:
                return "Fantasy RPG game UI, medieval ornaments, gold frames, stone or wood textures, magical runes";

            case MaterialAssignerWindow.GameUIStyle.SciFi:
                return "Sci-fi futuristic game UI, holographic panels, neon lines, tech interface similar to Halo or Mass Effect";

            case MaterialAssignerWindow.GameUIStyle.PixelArt:
                return "Pixel art retro game UI, low resolution pixel style, limited color palette, 8-bit or 16-bit aesthetic";

            case MaterialAssignerWindow.GameUIStyle.Glass:
                return "Glass or holographic UI, transparent panels, glowing edges, futuristic HUD elements";

            case MaterialAssignerWindow.GameUIStyle.DarkTech:
                return "Dark tech hacker style UI, black background, neon green or blue interface, terminal inspired design";

            case MaterialAssignerWindow.GameUIStyle.HandDrawn:
                return "Hand drawn illustrated game UI, sketch style lines, organic shapes, artistic illustrated interface";

            default:
                return "Game UI style";
        }
    }
}