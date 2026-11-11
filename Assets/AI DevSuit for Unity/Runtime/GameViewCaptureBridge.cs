using System;
using System.Collections;
using System.IO;
using UnityEngine;

public class GameViewCaptureBridge : MonoBehaviour
{
    public static GameViewCaptureBridge Instance { get; private set; }

    public static Texture2D LatestCapture => Instance != null ? Instance._latestCapture : null; // Captura manual
    public static Texture2D LatestPreview => Instance != null ? Instance._latestPreview : null; // Preview continuo
    public static bool IsCapturing => Instance != null && Instance._isCapturing;

    public static Vector2Int LatestCaptureSize => LatestCapture != null ? new Vector2Int(LatestCapture.width, LatestCapture.height) : Vector2Int.zero;
    public static Vector2Int LatestPreviewSize => LatestPreview != null ? new Vector2Int(LatestPreview.width, LatestPreview.height) : Vector2Int.zero;

    [SerializeField] private float autoCaptureInterval = 0.15f;
    [SerializeField] private bool autoCapture = false;

    private Texture2D _latestCapture;
    private Texture2D _latestPreview;
    private bool _isCapturing;
    private Coroutine _captureLoop;

    public event Action<Texture2D> OnCaptureUpdated;
    public event Action<Texture2D> OnPreviewUpdated;

    private static string PersistedCapturePath =>
        Path.Combine(Application.dataPath, "../Library/UnityVibeCodingPlugin_LastCapture.png");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null)
            return;

        GameObject go = new GameObject("[GameViewCaptureBridge]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<GameViewCaptureBridge>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            DestroyImmediate(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        if (autoCapture && Application.isPlaying)
            StartAutoCapture();
    }

    private void OnDisable()
    {
        StopAutoCapture();

        if (_latestPreview != null)
        {
            Destroy(_latestPreview);
            _latestPreview = null;
        }
    }

    public void StartAutoCapture()
    {
        if (!Application.isPlaying)
            return;

        if (_captureLoop != null)
            return;

        _captureLoop = StartCoroutine(AutoCaptureLoop());
    }

    public void StopAutoCapture()
    {
        if (_captureLoop != null)
        {
            StopCoroutine(_captureLoop);
            _captureLoop = null;
        }
    }

    public void SetAutoCapture(bool enabled)
    {
        autoCapture = enabled;

        if (enabled)
            StartAutoCapture();
        else
            StopAutoCapture();
    }

    public void SetInterval(float interval)
    {
        autoCaptureInterval = Mathf.Max(0.1f, interval);
    }

    public void EnsurePreviewRunning()
    {
        if (!Application.isPlaying)
            return;

        if (_captureLoop == null)
            StartAutoCapture();
    }

    public void RequestCaptureNow()
    {
        if (!_isCapturing && gameObject.activeInHierarchy)
            StartCoroutine(CaptureAtEndOfFrame(true));
    }

    private IEnumerator AutoCaptureLoop()
    {
        while (true)
        {
            yield return CaptureAtEndOfFrame(false);
            yield return new WaitForSecondsRealtime(autoCaptureInterval);
        }
    }

    private IEnumerator CaptureAtEndOfFrame(bool manualCapture)
    {
        if (_isCapturing)
            yield break;

        _isCapturing = true;

        yield return new WaitForEndOfFrame();

        Texture2D newPreview = ScreenCapture.CaptureScreenshotAsTexture();

        if (newPreview != null)
        {
            Texture2D oldPreview = _latestPreview;

            _latestPreview = newPreview;
            OnPreviewUpdated?.Invoke(_latestPreview);

            if (manualCapture)
            {
                Texture2D manualCopy = CloneTexture(newPreview);
                if (manualCopy != null)
                {
                    if (_latestCapture != null)
                        Destroy(_latestCapture);

                    _latestCapture = manualCopy;
                    SaveCaptureToDisk(_latestCapture);
                    OnCaptureUpdated?.Invoke(_latestCapture);
                }
            }

            if (oldPreview != null && oldPreview != _latestCapture)
                Destroy(oldPreview);
        }

        _isCapturing = false;
    }

    private Texture2D CloneTexture(Texture2D source)
    {
        if (source == null)
            return null;

        Texture2D clone = new Texture2D(source.width, source.height, source.format, false);
        clone.SetPixels32(source.GetPixels32());
        clone.Apply(false, false);
        return clone;
    }

    public static Texture2D LoadPersistedCapture()
    {
        try
        {
            if (!File.Exists(PersistedCapturePath))
                return null;

            byte[] bytes = File.ReadAllBytes(PersistedCapturePath);
            if (bytes == null || bytes.Length == 0)
                return null;

            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes, false))
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return null;
            }

            tex.name = "PersistedGameViewCapture";
            return tex;
        }
        catch
        {
            return null;
        }
    }

    private void SaveCaptureToDisk(Texture2D texture)
    {
        if (texture == null)
            return;

        try
        {
            string directory = Path.GetDirectoryName(PersistedCapturePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            byte[] png = texture.EncodeToPNG();
            if (png != null && png.Length > 0)
                File.WriteAllBytes(PersistedCapturePath, png);
        }
        catch
        {
        }
    }

    private void ClearCapture()
    {
        if (_latestCapture != null)
        {
            Destroy(_latestCapture);
            _latestCapture = null;
        }

        if (_latestPreview != null)
        {
            Destroy(_latestPreview);
            _latestPreview = null;
        }
    }

    public static Texture2D Downscale(Texture2D source, int maxSide)
    {
        if (source == null)
            return null;

        if (maxSide <= 0)
            return source;

        int width = source.width;
        int height = source.height;
        int maxDim = Mathf.Max(width, height);

        if (maxDim <= maxSide)
            return source;

        float scale = (float)maxSide / maxDim;
        int targetW = Mathf.Max(1, Mathf.RoundToInt(width * scale));
        int targetH = Mathf.Max(1, Mathf.RoundToInt(height * scale));

        RenderTexture rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            FilterMode prevFilter = source.filterMode;
            source.filterMode = FilterMode.Bilinear;

            Graphics.Blit(source, rt);

            RenderTexture.active = rt;

            Texture2D result = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0, false);
            result.Apply(false, false);

            source.filterMode = prevFilter;

            return result;
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}