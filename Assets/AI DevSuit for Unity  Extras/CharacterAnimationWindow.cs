using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class CharacterAnimationWindow : EditorWindow
{
    private enum AnimationType
    {
        WalkLeft,
        WalkRight,
        Jump,
        RunLeft,
        RunRight,
        ShotRight,
        ShotLeft,
        Die
    }

    private Texture2D sourceCharacterTexture;
    private Texture2D animationResultTexture;

    private bool isGeneratingAnimation = false;
    private string animationErrorMessage = "";
    private string animationTextureName = "";
    private AnimationType animationType = AnimationType.WalkLeft;

    private Vector2 scrollPos;

    [MenuItem("Tools/AI DevSuite/Character Animation")]
    public static void OpenWindow()
    {
        var window = GetWindow<CharacterAnimationWindow>("Character Animation");
        window.minSize = new Vector2(420, 500);
        window.Show();
    }

    public static void ShowWindow(Texture2D sourceTexture)
    {
        var window = GetWindow<CharacterAnimationWindow>("Character Animation");

        window.minSize = new Vector2(420, 500);

        window.sourceCharacterTexture = sourceTexture;
        window.animationResultTexture = null;
        window.animationErrorMessage = "";
        window.animationTextureName = "";
        window.isGeneratingAnimation = false;

        window.Show();
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("🎞 Character Animation Generator", EditorStyles.boldLabel);
        GUILayout.Space(10);

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

        GUILayout.Space(10);

        GUI.enabled = !isGeneratingAnimation;

        animationType = (AnimationType)EditorGUILayout.EnumPopup("Animation Type", animationType);

        GUILayout.Space(6);

        if (GUILayout.Button("Generate Animation", GUILayout.Height(35)))
        {
            animationErrorMessage = "";
            _ = GenerateAnimationAsync();
        }

        GUI.enabled = true;

        GUILayout.Space(6);

        if (!string.IsNullOrEmpty(animationErrorMessage))
        {
            EditorGUILayout.HelpBox(animationErrorMessage, MessageType.Error);
            GUILayout.Space(6);
        }

        if (isGeneratingAnimation)
        {
            Rect progressRect = EditorGUILayout.GetControlRect(false, 20);
            float prog = Mathf.PingPong((float)EditorApplication.timeSinceStartup, 1f);
            EditorGUI.ProgressBar(progressRect, prog, "Generating Animation...");
            GUILayout.Space(10);
            Repaint();
        }

        if (!isGeneratingAnimation && animationResultTexture != null)
        {
            GUILayout.Space(15);
            GUILayout.Label("Animation Result:", EditorStyles.boldLabel);

            DrawTextureFit(animationResultTexture, Mathf.Max(180f, position.height * 0.55f));

            GUILayout.Space(10);

            EditorGUILayout.LabelField("Animation File Name:");
            animationTextureName = EditorGUILayout.TextField(animationTextureName);

            GUI.enabled = animationResultTexture != null && !string.IsNullOrEmpty(animationTextureName.Trim());

            if (GUILayout.Button("Save Animation", GUILayout.Height(30)))
                SaveAnimationTextureOnly();

            GUI.enabled = true;
        }

        EditorGUILayout.EndScrollView();
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

        Rect rect = GUILayoutUtility.GetRect(drawWidth, drawHeight, GUILayout.Width(drawWidth), GUILayout.Height(drawHeight));
        GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private async Task GenerateAnimationAsync()
    {
        if (sourceCharacterTexture == null)
        {
            animationErrorMessage = "Request error: there is no base character image to use as reference.";
            Repaint();
            return;
        }

        isGeneratingAnimation = true;
        animationErrorMessage = "";
        animationResultTexture = null;
        Repaint();

        try
        {
            var manager = AIManager.EnsureManagerInstanceForEditor();

            string animationPrompt =
                "Create a single 2D side-view spritesheet of the SAME character from the reference image. "
                + GetAnimationType() + " "
                + "This must look like a real game-ready sprite sheet, not a poster, collage, concept art or illustration sheet. "
                + "Arrange all frames in ONE horizontal row. "
                + "Every frame must have the same camera angle, same zoom, same scale, same proportions, and same ground level. "
                + "The character must preserve exactly the same identity, face, hairstyle, colors, outfit, and visual style in all frames. "
                + "Only the pose must change to create motion. "
                + "Each frame must be clearly different from the previous one. "
                + "Use strong pose variation and clear movement progression. "
                + "Arms and legs must visibly change position in every frame. "
                + "Feet must not remain static. "
                + "Do not duplicate frames. "
                + "Do not mirror-copy frames. "
                + "Do not keep the legs in the same position. "
                + "Do not keep the arms in the same position. "
                + "The full body character must be fully visible in every frame, with no cropping. "
                + "Each sprite should stay centered within its frame area. "
                + "The character drawings should occupy about 60 percent of the total image area, leaving transparent empty space around. "
                + "IMPORTANT: final image must be a PNG with REAL transparent background using alpha channel RGBA. "
                + "Everything that is not the character must be fully transparent. "
                + "No floor, no shadows, no background, no scenery, no gradient, no pattern, no decoration, no text, no UI elements.";

            Texture2D generatedAnimation = await manager.GenerateImageFromPromptAndReference(
                animationPrompt,
                sourceCharacterTexture,
                "1024x1024"
            );

            if (generatedAnimation == null)
            {
                animationResultTexture = null;
                animationErrorMessage = "Request error: the animation generation failed.";
            }
            else
            {
                animationResultTexture = generatedAnimation;
            }
        }
        catch (System.Exception ex)
        {
            animationResultTexture = null;
            animationErrorMessage = "Request error: " + ex.Message;
            Debug.LogError(ex);
        }
        finally
        {
            isGeneratingAnimation = false;
            Repaint();
        }
    }

    private void SaveAnimationTextureOnly()
    {
        if (animationResultTexture == null)
        {
            Debug.LogWarning("There is no animation result to save.");
            return;
        }

        const string animationsFolder = "Assets/2dCharacterAnimation";

        if (!AssetDatabase.IsValidFolder(animationsFolder))
            AssetDatabase.CreateFolder("Assets", "2dCharacterAnimation");

        string fileName = animationTextureName.Trim();

        if (string.IsNullOrEmpty(fileName))
        {
            Debug.LogWarning("Enter a name to save the animation.");
            return;
        }

        string filePath = Path.Combine(animationsFolder, fileName + ".png");

        byte[] pngData = animationResultTexture.EncodeToPNG();
        File.WriteAllBytes(filePath, pngData);

        AssetDatabase.ImportAsset(filePath);
        AssetDatabase.Refresh();

        Debug.Log($"Animation saved in {filePath}");
    }

    private string GetAnimationType()
    {
        switch (animationType)
        {
            case AnimationType.WalkLeft:
                return "Create a 4-frame side-view walk cycle facing left. "
                     + "Frame 1: contact pose, left leg forward and right leg back. "
                     + "Frame 2: passing pose, left leg moving back under body and right leg moving forward under body. "
                     + "Frame 3: opposite contact pose, right leg forward and left leg back. "
                     + "Frame 4: passing pose again before looping. "
                     + "Arms must swing opposite to the legs. "
                     + "Every frame must show a clearly different foot position.";

            case AnimationType.WalkRight:
                return "Create a 4-frame side-view walk cycle facing right. "
                     + "Frame 1: contact pose, right leg forward and left leg back. "
                     + "Frame 2: passing pose, right leg moving back under body and left leg moving forward under body. "
                     + "Frame 3: opposite contact pose, left leg forward and right leg back. "
                     + "Frame 4: passing pose again before looping. "
                     + "Arms must swing opposite to the legs. "
                     + "Every frame must show a clearly different foot position.";

            case AnimationType.RunLeft:
                return "Create a 4-frame side-view run cycle facing left. "
                     + "Use stronger and more exaggerated action than a walk cycle. "
                     + "Frame 1: strong push/contact pose, left leg forward and right leg stretched back. "
                     + "Frame 2: airborne passing pose with both legs moving under the body. "
                     + "Frame 3: opposite strong push/contact pose, right leg forward and left leg stretched back. "
                     + "Frame 4: airborne passing pose again before looping. "
                     + "Arms must swing opposite to the legs with strong movement. "
                     + "Feet and knees must change position visibly in every frame.";

            case AnimationType.RunRight:
                return "Create a 4-frame side-view run cycle facing right. "
                     + "Use stronger and more exaggerated action than a walk cycle. "
                     + "Frame 1: strong push/contact pose, right leg forward and left leg stretched back. "
                     + "Frame 2: airborne passing pose with both legs moving under the body. "
                     + "Frame 3: opposite strong push/contact pose, left leg forward and right leg stretched back. "
                     + "Frame 4: airborne passing pose again before looping. "
                     + "Arms must swing opposite to the legs with strong movement. "
                     + "Feet and knees must change position visibly in every frame.";

            case AnimationType.Jump:
                return "Create a 5-frame side-view jump animation. "
                     + "Frame 1: anticipation pose, character crouching before jump. "
                     + "Frame 2: takeoff pose, legs pushing off ground and body rising. "
                     + "Frame 3: highest point in air, airborne pose. "
                     + "Frame 4: falling pose, body descending. "
                     + "Frame 5: landing pose, knees bent on impact. "
                     + "Every frame must be clearly different and show believable jump progression.";

            case AnimationType.Die:
                return "Create a 5-frame side-view death animation. "
                     + "Frame 1: initial hit or shock pose. "
                     + "Frame 2: losing balance. "
                     + "Frame 3: collapsing downward. "
                     + "Frame 4: almost on the ground. "
                     + "Frame 5: final dead pose on the ground or fully collapsed. "
                     + "Make the progression clear and dramatic. "
                     + "Do not repeat standing poses.";

            case AnimationType.ShotLeft:
                return "Create a 4-frame side-view shooting animation facing left. "
                     + "Frame 1: idle aiming pose. "
                     + "Frame 2: anticipation pose before firing. "
                     + "Frame 3: firing pose with strong recoil. "
                     + "Frame 4: recovery pose returning toward idle. "
                     + "Hands, arms, torso and weapon direction must visibly change between frames.";

            case AnimationType.ShotRight:
                return "Create a 4-frame side-view shooting animation facing right. "
                     + "Frame 1: idle aiming pose. "
                     + "Frame 2: anticipation pose before firing. "
                     + "Frame 3: firing pose with strong recoil. "
                     + "Frame 4: recovery pose returning toward idle. "
                     + "Hands, arms, torso and weapon direction must visibly change between frames.";

            default:
                return "Create a 4-frame side-view animation with clear pose changes in every frame.";
        }
    }
}