using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System;
using UnityEditor;

public class AIManager : MonoBehaviour
{
    public enum AIProviderType
    {
        OpenAI = 0,
        Anthropic = 1
    }

    [Header("AI Provider")]
    [SerializeField] private AIProviderType currentProvider = AIProviderType.OpenAI;

    private OpenaiManager _openaiManager;
    private AnthropicManager _anthropicManager;

    private void Awake()
    {
        LoadProviderFromPrefs();
    }

    private void EnsureProvider()
    {
        LoadProviderFromPrefs();

        switch (currentProvider)
        {
            case AIProviderType.OpenAI:
                EnsureOpenAIManager();
                break;

            case AIProviderType.Anthropic:
                EnsureAnthropicManager();
                break;

            default:
                Debug.LogError("[AIManager] Unsupported provider: " + currentProvider);
                break;
        }
    }

    private void EnsureOpenAIManager()
    {
        if (_openaiManager == null)
        {
            _openaiManager = GetComponent<OpenaiManager>();

            if (_openaiManager == null)
            {
                _openaiManager = gameObject.AddComponent<OpenaiManager>();
#if UNITY_EDITOR
                if (gameObject.hideFlags == HideFlags.HideAndDontSave)
                {
                    _openaiManager.hideFlags = HideFlags.HideAndDontSave;
                }
#endif
            }
        }
    }

    private void EnsureAnthropicManager()
    {
        if (_anthropicManager == null)
        {
            _anthropicManager = GetComponent<AnthropicManager>();

            if (_anthropicManager == null)
            {
                _anthropicManager = gameObject.AddComponent<AnthropicManager>();
#if UNITY_EDITOR
                if (gameObject.hideFlags == HideFlags.HideAndDontSave)
                {
                    _anthropicManager.hideFlags = HideFlags.HideAndDontSave;
                }
#endif
            }
        }
    }

    public void SetProvider(AIProviderType providerType)
    {
        currentProvider = providerType;
    }

    public AIProviderType GetCurrentProvider()
    {
        return currentProvider;
    }

    /// <summary>
    /// Esta función sigue existiendo para compatibilidad con código viejo.
    /// Solo afecta a OpenAI.
    /// </summary>
    public void SetOpenAIModel(string modelName)
    {
        EnsureOpenAIManager();

        if (_openaiManager != null)
        {
            _openaiManager.openAiModel = modelName;
        }
    }

    /// <summary>
    /// Flujo viejo basado en List&lt;ChatMessage&gt;.
    /// Por ahora queda solo para OpenAI, porque está más atado a su formato.
    /// </summary>
    public async Task<List<ChatMessage>> GetCompletionResponse(List<ChatMessage> messages)
    {
        EnsureProvider();

        switch (currentProvider)
        {
            case AIProviderType.OpenAI:
                return await _openaiManager.GetCompletionResponse(messages);

            case AIProviderType.Anthropic:
                Debug.LogError("[AIManager] GetCompletionResponse(List<ChatMessage>) todavía no está implementado para Anthropic. Usá GetConfiguredEditorCompletionResponse(...) o GetSimpleCompletion(...).");
                return null;

            default:
                Debug.LogError("[AIManager] No valid provider available for chat completion.");
                return messages;
        }
    }

    /// <summary>
    /// Nueva función simple y genérica.
    /// Ideal para prompts cortos donde no querés que la UI sepa nada del provider.
    /// </summary>
    public async Task<string> GetSimpleCompletion(string prompt)
    {
        EnsureProvider();

        switch (currentProvider)
        {
            case AIProviderType.OpenAI:
                return await _openaiManager.GetSimpleCompletion(prompt);

            case AIProviderType.Anthropic:
                return await _anthropicManager.GetSimpleCompletion(prompt);

            default:
                Debug.LogError("[AIManager] No valid provider for simple completion.");
                return "";
        }
    }

    public async Task<string> CreateAudioTranscriptionAsync(byte[] audioData, string language = null)
    {
        EnsureProvider();

        switch (currentProvider)
        {
            case AIProviderType.OpenAI:
                return await _openaiManager.CreateAudioTranscriptionAsync(audioData, language);

            case AIProviderType.Anthropic:
                EditorUtility.DisplayDialog(
                    "Error",
                    "Anthropic dont suport audio trascription. Please use Openai Engine",
                    "OK"
                );
                Debug.LogError("[AIManager] Anthropic no soporta este flujo de audio transcription en este manager.");
                return "";

            default:
                Debug.LogError("[AIManager] No valid provider available for audio transcription.");
                return "";
        }
    }

    public async Task<byte[]> SynthesizeTextToAudio(string talkText)
    {
        EnsureProvider();

        switch (currentProvider)
        {
            case AIProviderType.OpenAI:
                return await _openaiManager.SynthesizeTextToAudio(talkText);

            case AIProviderType.Anthropic:
                Debug.LogError("[AIManager] Anthropic no soporta este flujo de text-to-speech en este manager.");
                return null;

            default:
                Debug.LogError("[AIManager] No valid provider available for text-to-speech.");
                return null;
        }
    }

    public async Task<Texture2D> GenerateImageFromPrompt(
        string prompt,
        string size = "1024x1024",
        int n = 1,
        string quality = "low")
    {
        EnsureOpenAIManager();

        if (_openaiManager == null)
        {
            Debug.LogError("[AIManager] OpenaiManager no está disponible para generación de imágenes.");
            return null;
        }

        return await _openaiManager.GenerateImageFromPrompt(prompt, size, n, quality);
    }

    public async Task<Texture2D> GenerateImageFromPromptAndReference(
        string prompt,
        Texture2D referenceImage,
        string size = "1024x1024",
        int n = 1,
        string background = "transparent")
    {
        EnsureOpenAIManager();

        if (_openaiManager == null)
        {
            Debug.LogError("[AIManager] OpenaiManager no está disponible para edición/generación de imágenes con referencia.");
            return null;
        }

        return await _openaiManager.GenerateImageFromPromptAndReference(
            prompt,
            referenceImage,
            size,
            n,
            background
        );
    }

    public async Task<bool> CheckImageModelAvailabilityAsync()
    {
        EnsureOpenAIManager();

        if (_openaiManager == null)
        {
            Debug.LogError("[AIManager] OpenaiManager no está disponible para el chequeo de modelos de imagen.");
            return false;
        }

        // 🔥 usamos el nuevo método genérico
        return await _openaiManager.CheckImageModelAvailabilityAsync("gpt-image-2");
    }

    /// <summary>
    /// Esta es la función importante para tus UI scripts.
    /// La idea es que ellos llamen SIEMPRE a esta misma función,
    /// y AIManager decida si responde OpenAI o Anthropic.
    /// </summary>
    public async Task<List<ChatMessage>> GetConfiguredEditorCompletionResponse(
        string userContent,
        string instructionContent = null)
    {
        EnsureProvider();

        switch (currentProvider)
        {
            case AIProviderType.OpenAI:
                return await _openaiManager.GetConfiguredEditorCompletionResponse(userContent, instructionContent);

            case AIProviderType.Anthropic:
                return await _anthropicManager.GetConfiguredEditorCompletionResponse(userContent, instructionContent);

            default:
                Debug.LogError("[AIManager] No valid provider available for configured editor completion.");
                return null;
        }
    }

    /// <summary>
    /// Flujo nuevo de imágenes para editor.
    /// OpenAI-only.
    /// </summary>
    public async Task<Texture2D> GenerateImageForEditor(
        string prompt,
        string size = "1024x1024",
        string quality = "low")
    {
        EnsureOpenAIManager();

        if (_openaiManager == null)
        {
            Debug.LogError("[AIManager] OpenaiManager no está disponible para editor image generation.");
            return null;
        }

        return await _openaiManager.GenerateImageForEditor(prompt, size, quality);
    }

    /// <summary>
    /// Flujo nuevo de imágenes con referencia para editor.
    /// OpenAI-only.
    /// </summary>
    public async Task<Texture2D> GenerateImageFromReferenceForEditor(
        string prompt,
        Texture2D referenceImage,
        string size = "1024x1024",
        string background = "transparent")
    {
        EnsureOpenAIManager();

        if (_openaiManager == null)
        {
            Debug.LogError("[AIManager] OpenaiManager no está disponible para editor image generation with reference.");
            return null;
        }

        return await _openaiManager.GenerateImageFromReferenceForEditor(
            prompt,
            referenceImage,
            size,
            background
        );
    }

    /// <summary>
    /// API pública unificada para análisis de calidad de UI en una imagen.
    /// OpenAI-only.
    /// </summary>
    public async Task<string> AnalyzeUIImageQuality(Texture2D image, string prompt, string instructionOverride = null)
    {
        if (image == null)
        {
            throw new ArgumentNullException(nameof(image), "[AIManager] La textura de imagen no puede ser nula.");
        }
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("[AIManager] El prompt no puede estar vacío.", nameof(prompt));
        }

        EnsureOpenAIManager();

        if (_openaiManager == null)
        {
            throw new InvalidOperationException("[AIManager] OpenaiManager no está disponible.");
        }

        return await _openaiManager.AnalyzeUIImageQuality(image, prompt);
    }

#if UNITY_EDITOR
    private static AIManager FindExistingEditorManager()
    {
        AIManager manager = UnityEngine.Object.FindFirstObjectByType<AIManager>();
        if (manager != null)
            return manager;

        AIManager[] allManagers = Resources.FindObjectsOfTypeAll<AIManager>();
        foreach (AIManager candidate in allManagers)
        {
            if (candidate == null)
                continue;

            if (EditorUtility.IsPersistent(candidate))
                continue;

            return candidate;
        }

        return null;
    }

    private static void ApplyEditorOnlyHideFlags(GameObject go)
    {
        if (go == null)
            return;

        go.hideFlags = HideFlags.HideAndDontSave;

        AIManager aiManager = go.GetComponent<AIManager>();
        if (aiManager != null)
            aiManager.hideFlags = HideFlags.HideAndDontSave;

        OpenaiManager openai = go.GetComponent<OpenaiManager>();
        if (openai != null)
            openai.hideFlags = HideFlags.HideAndDontSave;

        AnthropicManager anthropic = go.GetComponent<AnthropicManager>();
        if (anthropic != null)
            anthropic.hideFlags = HideFlags.HideAndDontSave;
    }

    /// <summary>
    /// Helper estático para asegurar una instancia de AIManager en el Editor,
    /// creándola como GameObject oculto si no existe en escena.
    /// También asegura la instancia del proveedor configurado.
    /// </summary>
    public static AIManager EnsureManagerInstanceForEditor()
    {
        AIManager manager = FindExistingEditorManager();

        if (manager == null)
        {
            GameObject go = new GameObject("AIManager_Auto");
            ApplyEditorOnlyHideFlags(go);

            manager = go.AddComponent<AIManager>();
            manager.hideFlags = HideFlags.HideAndDontSave;

            manager.LoadProviderFromPrefs();
            manager.EnsureProvider();

            ApplyEditorOnlyHideFlags(go);
        }
        else
        {
            manager.LoadProviderFromPrefs();
            manager.EnsureProvider();

            if (manager.gameObject.hideFlags == HideFlags.HideAndDontSave)
            {
                ApplyEditorOnlyHideFlags(manager.gameObject);
            }
        }

        return manager;
    }
#endif

    private void LoadProviderFromPrefs()
    {
#if UNITY_EDITOR
        string provider = UnityEditor.EditorPrefs.GetString("AI_PROVIDER", "OpenAI");

        if (Enum.TryParse(provider, out AIProviderType parsed))
        {
            currentProvider = parsed;
        }
#endif
    }
}