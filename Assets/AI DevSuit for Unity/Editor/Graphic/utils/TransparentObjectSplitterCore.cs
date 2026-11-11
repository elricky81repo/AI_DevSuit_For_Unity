using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TransparentObjectSplitterCore
{
    public class SplitSettings
    {
        public int alphaThreshold = 10;
        public int minObjectPixels = 20;
        public int padding = 2;
        public bool sortTopToBottom = true;
    }

    public class SplitResult
    {
        public RectInt rect;
        public string assetPath;
        public Texture2D texture;
        public string fileName;
    }

    public static List<SplitResult> SplitTexture(Texture2D sourceTexture, string outputFolder, SplitSettings settings = null)
    {
        List<SplitResult> results = new List<SplitResult>();

        if (sourceTexture == null)
        {
            Debug.LogError("[TransparentObjectSplitterCore] Source texture is null.");
            return results;
        }

        if (settings == null)
            settings = new SplitSettings();

        string sourcePath = AssetDatabase.GetAssetPath(sourceTexture);
        if (string.IsNullOrEmpty(sourcePath))
        {
            Debug.LogError("[TransparentObjectSplitterCore] Could not get texture asset path. The texture must exist inside Assets.");
            return results;
        }

        EnsureTextureReadable(sourcePath);

        Texture2D readableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        if (readableTexture == null)
        {
            Debug.LogError("[TransparentObjectSplitterCore] Could not reload readable texture.");
            return results;
        }

        if (!AssetDatabase.IsValidFolder(outputFolder))
            CreateFolderRecursive(outputFolder);

        List<RectInt> objectRects = DetectObjects(
            readableTexture,
            settings.alphaThreshold,
            settings.minObjectPixels,
            settings.padding
        );

        if (objectRects.Count == 0)
        {
            Debug.LogWarning("[TransparentObjectSplitterCore] No separated objects were found.");
            return results;
        }

        SortRects(objectRects, settings.sortTopToBottom);

        string baseName = Path.GetFileNameWithoutExtension(sourcePath);

        for (int i = 0; i < objectRects.Count; i++)
        {
            RectInt rect = objectRects[i];
            Texture2D cutTexture = CropTexture(readableTexture, rect);

            string fileName = $"{baseName}_{(i + 1).ToString("D2")}.png";
            string savePath = Path.Combine(outputFolder, fileName).Replace("\\", "/");

            byte[] pngData = cutTexture.EncodeToPNG();
            File.WriteAllBytes(savePath, pngData);

            Object.DestroyImmediate(cutTexture);

            results.Add(new SplitResult
            {
                rect = rect,
                assetPath = savePath,
                texture = null,
                fileName = fileName
            });
        }

        AssetDatabase.Refresh();
        ConfigureGeneratedTextures(outputFolder);

        for (int i = 0; i < results.Count; i++)
        {
            results[i].texture = AssetDatabase.LoadAssetAtPath<Texture2D>(results[i].assetPath);
        }

        return results;
    }

    public static List<SplitResult> SplitTextureAtPath(string sourceAssetPath, string outputFolder, SplitSettings settings = null)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourceAssetPath);
        if (texture == null)
        {
            Debug.LogError("[TransparentObjectSplitterCore] Could not load texture at path: " + sourceAssetPath);
            return new List<SplitResult>();
        }

        return SplitTexture(texture, outputFolder, settings);
    }

    private static void EnsureTextureReadable(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        bool changed = false;

        if (!importer.isReadable)
        {
            importer.isReadable = true;
            changed = true;
        }

        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            changed = true;
        }

        if (!importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = true;
            changed = true;
        }

        if (changed)
            importer.SaveAndReimport();
    }

    private static void CreateFolderRecursive(string fullFolderPath)
    {
        string normalized = fullFolderPath.Replace("\\", "/");

        if (AssetDatabase.IsValidFolder(normalized))
            return;

        string[] parts = normalized.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
        {
            Debug.LogError("[TransparentObjectSplitterCore] Output folder must be inside Assets.");
            return;
        }

        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private static List<RectInt> DetectObjects(Texture2D texture, int alphaThreshold, int minObjectPixels, int padding)
    {
        int width = texture.width;
        int height = texture.height;

        Color32[] pixels = texture.GetPixels32();
        bool[] visited = new bool[width * height];
        List<RectInt> rects = new List<RectInt>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int startIndex = x + y * width;

                if (visited[startIndex])
                    continue;

                visited[startIndex] = true;

                if (pixels[startIndex].a <= alphaThreshold)
                    continue;

                RectInt? foundRect = FloodFillObject(
                    x, y,
                    width, height,
                    pixels,
                    visited,
                    alphaThreshold,
                    minObjectPixels,
                    padding
                );

                if (foundRect.HasValue)
                    rects.Add(foundRect.Value);
            }
        }

        return rects;
    }

    private static RectInt? FloodFillObject(
        int startX,
        int startY,
        int width,
        int height,
        Color32[] pixels,
        bool[] visited,
        int alphaThreshold,
        int minObjectPixels,
        int padding)
    {
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));

        int minX = startX;
        int minY = startY;
        int maxX = startX;
        int maxY = startY;
        int pixelCount = 0;

        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue();
            int x = p.x;
            int y = p.y;
            int index = x + y * width;

            if (pixels[index].a <= alphaThreshold)
                continue;

            pixelCount++;

            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;

            EnqueueIfValid(x - 1, y, width, height, visited, queue);
            EnqueueIfValid(x + 1, y, width, height, visited, queue);
            EnqueueIfValid(x, y - 1, width, height, visited, queue);
            EnqueueIfValid(x, y + 1, width, height, visited, queue);
        }

        if (pixelCount < minObjectPixels)
            return null;

        minX = Mathf.Max(0, minX - padding);
        minY = Mathf.Max(0, minY - padding);
        maxX = Mathf.Min(width - 1, maxX + padding);
        maxY = Mathf.Min(height - 1, maxY + padding);

        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static void EnqueueIfValid(int x, int y, int width, int height, bool[] visited, Queue<Vector2Int> queue)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;

        int index = x + y * width;
        if (visited[index])
            return;

        visited[index] = true;
        queue.Enqueue(new Vector2Int(x, y));
    }

    private static Texture2D CropTexture(Texture2D source, RectInt rect)
    {
        Texture2D result = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false);
        result.filterMode = FilterMode.Point;
        result.wrapMode = TextureWrapMode.Clamp;

        Color[] colors = source.GetPixels(rect.x, rect.y, rect.width, rect.height);
        result.SetPixels(colors);
        result.Apply();

        return result;
    }

    private static void SortRects(List<RectInt> rects, bool sortTopToBottom)
    {
        if (sortTopToBottom)
        {
            rects.Sort((a, b) =>
            {
                int yCompare = b.y.CompareTo(a.y);
                if (yCompare != 0) return yCompare;
                return a.x.CompareTo(b.x);
            });
        }
        else
        {
            rects.Sort((a, b) =>
            {
                int xCompare = a.x.CompareTo(b.x);
                if (xCompare != 0) return xCompare;
                return b.y.CompareTo(a.y);
            });
        }
    }

    private static void ConfigureGeneratedTextures(string folderPath)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                continue;

            bool changed = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (!importer.isReadable)
            {
                importer.isReadable = true;
                changed = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            if (changed)
                importer.SaveAndReimport();
        }
    }
}