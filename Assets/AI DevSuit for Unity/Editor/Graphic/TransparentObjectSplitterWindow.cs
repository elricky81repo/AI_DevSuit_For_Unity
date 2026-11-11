using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class TransparentObjectSplitterWindow : EditorWindow
{
    private Texture2D sourceTexture;
    private string outputFolder = "Assets/SplitOutput";
    private int alphaThreshold = 10;
    private int minObjectPixels = 20;
    private int padding = 2;
    private bool sortTopToBottom = true;

    private Vector2 resultsScroll;
    private List<TransparentObjectSplitterCore.SplitResult> lastResults = new List<TransparentObjectSplitterCore.SplitResult>();

    [MenuItem("Tools/AI DevSuite/Utils/Transparent Object Splitter")]
    public static void ShowWindow()
    {
        TransparentObjectSplitterWindow window = GetWindow<TransparentObjectSplitterWindow>("Object Splitter");
        window.minSize = new Vector2(420f, 500f);
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField("Split PNG by Transparency", EditorStyles.boldLabel);
        GUILayout.Space(4);
        EditorGUILayout.HelpBox("Detects separate visible objects in a transparent PNG and exports each one as an individual PNG sprite.", MessageType.Info);

        GUILayout.Space(8);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Source PNG", sourceTexture, typeof(Texture2D), false);
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

            GUILayout.Space(6);

            alphaThreshold = EditorGUILayout.IntSlider("Alpha Threshold", alphaThreshold, 0, 255);
            minObjectPixels = EditorGUILayout.IntField("Min Object Pixels", minObjectPixels);
            padding = EditorGUILayout.IntSlider("Padding", padding, 0, 32);
            sortTopToBottom = EditorGUILayout.Toggle("Sort Top To Bottom", sortTopToBottom);
        }

        GUILayout.Space(10);

        using (new EditorGUI.DisabledScope(sourceTexture == null))
        {
            if (GUILayout.Button("Split Objects", GUILayout.Height(36)))
            {
                SplitSelectedTexture();
            }
        }

        GUILayout.Space(12);
        DrawResultsSection();
    }

    private void SplitSelectedTexture()
    {
        if (sourceTexture == null)
        {
            EditorUtility.DisplayDialog("Error", "Please assign a source PNG.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            EditorUtility.DisplayDialog("Error", "Please enter a valid output folder inside Assets.", "OK");
            return;
        }

        if (!outputFolder.StartsWith("Assets"))
        {
            EditorUtility.DisplayDialog("Error", "Output folder must be inside Assets.", "OK");
            return;
        }

        TransparentObjectSplitterCore.SplitSettings settings = new TransparentObjectSplitterCore.SplitSettings
        {
            alphaThreshold = alphaThreshold,
            minObjectPixels = Mathf.Max(1, minObjectPixels),
            padding = Mathf.Max(0, padding),
            sortTopToBottom = sortTopToBottom
        };

        lastResults = TransparentObjectSplitterCore.SplitTexture(sourceTexture, outputFolder, settings);

        if (lastResults == null || lastResults.Count == 0)
        {
            EditorUtility.DisplayDialog("Result", "No separated objects were found.", "OK");
            return;
        }

        Repaint();

        EditorUtility.DisplayDialog(
            "Done",
            $"Generated {lastResults.Count} PNG files in:\n{outputFolder}",
            "OK"
        );
    }

    private void DrawResultsSection()
    {
        EditorGUILayout.LabelField("Last Results", EditorStyles.boldLabel);
        GUILayout.Space(4);

        if (lastResults == null || lastResults.Count == 0)
        {
            EditorGUILayout.HelpBox("No generated objects yet.", MessageType.None);
            return;
        }

        EditorGUILayout.LabelField("Generated Objects: " + lastResults.Count);
        GUILayout.Space(6);

        resultsScroll = EditorGUILayout.BeginScrollView(resultsScroll);

        for (int i = 0; i < lastResults.Count; i++)
        {
            DrawResultItem(lastResults[i], i);
        }

        EditorGUILayout.EndScrollView();

        GUILayout.Space(8);

        if (GUILayout.Button("Ping Output Folder"))
        {
            Object folderObj = AssetDatabase.LoadAssetAtPath<Object>(outputFolder);
            if (folderObj != null)
            {
                EditorGUIUtility.PingObject(folderObj);
                Selection.activeObject = folderObj;
            }
            else
            {
                Debug.LogWarning("Could not load output folder: " + outputFolder);
            }
        }
    }

    private void DrawResultItem(TransparentObjectSplitterCore.SplitResult result, int index)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField($"Object {(index + 1).ToString("D2")}", EditorStyles.boldLabel);

            if (result.texture != null)
            {
                Rect previewRect = GUILayoutUtility.GetRect(80, 80, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(previewRect, result.texture, null, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.LabelField("File", result.fileName);
            EditorGUILayout.LabelField("Path", result.assetPath);
            EditorGUILayout.LabelField("Rect", $"X:{result.rect.x} Y:{result.rect.y} W:{result.rect.width} H:{result.rect.height}");

            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ping"))
                {
                    Object obj = AssetDatabase.LoadAssetAtPath<Object>(result.assetPath);
                    if (obj != null)
                    {
                        EditorGUIUtility.PingObject(obj);
                        Selection.activeObject = obj;
                    }
                }

                if (GUILayout.Button("Select"))
                {
                    Object obj = AssetDatabase.LoadAssetAtPath<Object>(result.assetPath);
                    if (obj != null)
                    {
                        Selection.activeObject = obj;
                    }
                }
            }
        }
    }
}