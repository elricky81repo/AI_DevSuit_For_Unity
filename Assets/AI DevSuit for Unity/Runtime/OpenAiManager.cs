using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UnityEditor;

public class OpenaiManager : MonoBehaviour
{
    [Header("OpenAI Credentials")]
    [SerializeField] private string apiKey = "TU_API_KEY_AQUI";
    [SerializeField] private string organizationKey = "TU_ORG_KEY_AQUI";

    public TTSVoice selectedVoice = TTSVoice.nova;
    [SerializeField] private Dropdown dropdownVoices;
    [SerializeField] private string selectedVoiceString;

    [SerializeField] private int maxCompletionTokens = 10000;
    [SerializeField] private float iaTemperatura = 0.3f;

    // Este sigue existiendo para no romper lo que ya usa el flujo actual.
    [SerializeField] public string openAiModel = "gpt-4o-mini";

    private const string TRANSCRIPTION_LANGUAGE_PREF = "GPTPlugin_TranscriptionLanguage";
    private const string DEFAULT_TRANSCRIPTION_LANGUAGE = "es";

    //MODELOS DE IMAGEN prueba con el primero si no anda con el otro  era para 2.0 pero no está andando bien
    //cuando ande volvemos a cambiar! por ahora no anda transparencia de fondo por ejemplo
    private const string PRIMARY_IMAGE_MODEL = "gpt-image-1.5";
    private const string FALLBACK_IMAGE_MODEL = "gpt-image-1.5";

    [SerializeField] private string transcriptionLanguage = DEFAULT_TRANSCRIPTION_LANGUAGE;

    [Serializable]
    public class ChatRequest
    {
        public string model;
        public List<ChatMessage> messages;
        public int max_completion_tokens;
        public float temperature;
    }

    [Serializable]
    public class ChatChoice
    {
        public ChatMessage message;
        public string finish_reason;
    }

    [Serializable]
    public class ChatResponse
    {
        public List<ChatChoice> choices;
    }

    [Serializable]
    public class OpenAIErrorResponse
    {
        public OpenAIError error;
    }

    [Serializable]
    public class OpenAIError
    {
        public string message;
        public string type;
        public string code;
        public string param;
    }

    public enum TTSVoice
    {
        alloy,
        ash,
        coral,
        echo,
        fable,
        onyx,
        nova,
        sage,
        shimmer
    }

    public TTSVoice TextToSpeechVoices;

    [Serializable]
    public class ResponsesRequest
    {
        public string model;
        public string instructions;
        public string input;
        public int max_output_tokens;
    }

    [Serializable]
    public class ResponseMessageContentItem
    {
        public string type;
        public string text;
    }

    [Serializable]
    public class ResponseOutputItem
    {
        public string type;
        public string role;
        public List<ResponseMessageContentItem> content;
    }

    [Serializable]
    public class ResponsesResponse
    {
        public string id;
        public List<ResponseOutputItem> output;
        public string output_text;
    }

    private void CargarOpciones()
    {
        if (dropdownVoices == null)
            return;

        dropdownVoices.ClearOptions();

        foreach (var voice in Enum.GetNames(typeof(TTSVoice)))
        {
            dropdownVoices.options.Add(new Dropdown.OptionData(voice));
        }

        dropdownVoices.value = 0;
        dropdownVoices.RefreshShownValue();
    }

    public void ChangeVoice(int arg)
    {
        if (dropdownVoices == null || dropdownVoices.options == null || dropdownVoices.options.Count == 0)
            return;

        string selectedOption = dropdownVoices.options[arg].text;
        selectedVoiceString = selectedOption;
    }

    public string GetTranscriptionLanguage()
    {
#if UNITY_EDITOR
        string saved = EditorPrefs.GetString(
            TRANSCRIPTION_LANGUAGE_PREF,
            DEFAULT_TRANSCRIPTION_LANGUAGE
        );

        if (string.IsNullOrWhiteSpace(saved))
            saved = DEFAULT_TRANSCRIPTION_LANGUAGE;

        transcriptionLanguage = saved.Trim().ToLower();
        return transcriptionLanguage;
#else
        if (string.IsNullOrWhiteSpace(transcriptionLanguage))
            transcriptionLanguage = DEFAULT_TRANSCRIPTION_LANGUAGE;

        return transcriptionLanguage.Trim().ToLower();
#endif
    }

    public void SetTranscriptionLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            language = DEFAULT_TRANSCRIPTION_LANGUAGE;

        transcriptionLanguage = language.Trim().ToLower();

#if UNITY_EDITOR
        EditorPrefs.SetString(TRANSCRIPTION_LANGUAGE_PREF, transcriptionLanguage);
#endif
    }

    private async void Start()
    {
        LoadCredentialsIfNeeded();

        CargarOpciones();
        selectedVoiceString = selectedVoice.ToString();
        transcriptionLanguage = GetTranscriptionLanguage();

        if (dropdownVoices != null)
        {
            dropdownVoices.onValueChanged.AddListener(ChangeVoice);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Flujo viejo / bajo nivel.
    /// Recibe la lista de mensajes ya armada desde afuera y solo hace la request.
    /// Lo dejamos para no romper otros scripts que ya lo usan.
    /// </summary>
    public async Task<List<ChatMessage>> GetCompletionResponse(List<ChatMessage> messages)
    {
        LoadCredentialsIfNeeded();

        string savedModel = GetSavedEditorModelApiString();

        if (!string.IsNullOrEmpty(savedModel))
        {
            openAiModel = savedModel;
        }
        else
        {
            Debug.LogWarning("[OpenaiManager] No saved model found, keeping current model: " + openAiModel);
        }

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set. Configure it in Setup.");
            return null;
        }

        if (IsResponsesOnlyModel(openAiModel))
        {
            Debug.LogError(
                "[OpenaiManager] El modelo '" + openAiModel + "' no es compatible con /v1/chat/completions. " +
                "Necesita otro flujo/endpoint distinto."
            );
            return null;
        }

        ChatRequest chatRequest = new ChatRequest
        {
            model = openAiModel,
            messages = messages,
            max_completion_tokens = maxCompletionTokens,
            temperature = GetSafeTemperature(openAiModel, iaTemperatura)
        };

        string jsonBody = JsonUtility.ToJson(chatRequest);

        using (UnityWebRequest request = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            if (!string.IsNullOrEmpty(organizationKey))
            {
                request.SetRequestHeader("OpenAI-Organization", organizationKey);
            }

            await SendRequestAsync(request);

            string rawResponse = request.downloadHandler != null ? request.downloadHandler.text : "";

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("[OpenaiManager] Error HTTP: " + request.error);

                if (!string.IsNullOrWhiteSpace(rawResponse))
                {
                    var errorObj = JsonUtility.FromJson<OpenAIErrorResponse>(rawResponse);
                    if (errorObj != null && errorObj.error != null && !string.IsNullOrEmpty(errorObj.error.message))
                    {
                        Debug.LogError("[OpenaiManager] OpenAI Error: " + errorObj.error.message);
                        EditorUtility.DisplayDialog("ERROR", errorObj.error.message, "OK");
                    }
                    else
                    {
                        Debug.LogError("[OpenaiManager] Error Body: " + rawResponse);
                    }
                }

                return null;
            }

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                Debug.LogWarning("[OpenaiManager] No se pudo extraer texto de Responses.");
                return null;
            }

            ChatResponse chatResponse = null;

            try
            {
                chatResponse = JsonUtility.FromJson<ChatResponse>(rawResponse);
            }
            catch (Exception ex)
            {
                Debug.LogError("[OpenaiManager] Error parseando JSON de chat: " + ex.Message);
                return null;
            }

            if (chatResponse == null || chatResponse.choices == null || chatResponse.choices.Count == 0)
            {
                Debug.LogWarning("[OpenaiManager] No se recibió una estructura válida de choices.");
                return null;
            }

            ChatMessage botMessage = chatResponse.choices[0].message;
            string finishReason = chatResponse.choices[0].finish_reason;

            if (finishReason == "length")
            {
                Debug.LogError("[OpenaiManager] La respuesta fue truncada por límite de tokens (finish_reason = length).");
                EditorUtility.DisplayDialog(
                    "Response Truncated",
                    "The AI response was cut off before finishing the code. Try again with fewer scripts or a smaller request.",
                    "OK"
                );
                return null;
            }

            if (botMessage == null)
            {
                Debug.LogWarning("[OpenaiManager] choices[0].message vino null.");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(botMessage.content))
            {
                botMessage.content = botMessage.content.Trim();
            }

            messages.Add(botMessage);
            return messages;
        }
    }

    /// <summary>
    /// Nueva función simple: recibe solo un prompt y devuelve texto.
    /// Ideal para desacoplar UI de estructura de mensajes.
    /// </summary>
    public async Task<string> GetSimpleCompletion(string prompt)
    {
        LoadCredentialsIfNeeded();

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set. Configure it in Setup.");
            return "";
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            Debug.LogWarning("[OpenaiManager] GetSimpleCompletion received an empty prompt.");
            return "";
        }

        string savedModel = GetSavedEditorModelApiString();

        if (!string.IsNullOrEmpty(savedModel))
        {
            openAiModel = savedModel;
        }
        else if (string.IsNullOrWhiteSpace(openAiModel))
        {
            openAiModel = "gpt-4o-mini";
        }

        if (IsResponsesOnlyModel(openAiModel))
        {
            var response = await GetResponseUsingResponsesAPI(
                prompt,
                "You are a helpful and precise assistant."
            );

            if (response == null || response.Count == 0)
                return "";

            for (int i = response.Count - 1; i >= 0; i--)
            {
                ChatMessage msg = response[i];

                if (msg != null &&
                    msg.role == "assistant" &&
                    !string.IsNullOrWhiteSpace(msg.content))
                {
                    return msg.content.Trim();
                }
            }

            return "";
        }

        var messages = new List<ChatMessage>
        {
            new ChatMessage
            {
                role = "user",
                content = prompt
            }
        };

        var legacyResponse = await GetCompletionResponse(messages);

        if (legacyResponse == null || legacyResponse.Count == 0)
            return "";

        for (int i = legacyResponse.Count - 1; i >= 0; i--)
        {
            ChatMessage msg = legacyResponse[i];

            if (msg != null &&
                msg.role == "assistant" &&
                !string.IsNullOrWhiteSpace(msg.content))
            {
                return msg.content.Trim();
            }
        }

        return "";
    }

    /// <summary>
    /// Esta función está pensada para que otros scripts del editor no tengan que:
    /// - elegir el modelo manualmente
    /// - decidir si el rol es "system" o "developer"
    /// - armar toda la lista de mensajes
    /// </summary>
    public async Task<List<ChatMessage>> GetConfiguredEditorCompletionResponse(
        string userContent,
        string instructionContent = null)
    {
        LoadCredentialsIfNeeded();

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set. Configure it in Setup.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(userContent))
        {
            Debug.LogError("[OpenaiManager] userContent vino vacío.");
            return null;
        }

        string savedModel = GetSavedEditorModelApiString();
        openAiModel = savedModel;

        if (string.IsNullOrWhiteSpace(instructionContent))
        {
            instructionContent = "Eres un asistente útil y preciso.";
        }

        if (IsResponsesOnlyModel(openAiModel))
        {
            return await GetResponseUsingResponsesAPI(userContent, instructionContent);
        }

        string instructionRole = UsesDeveloperRoleForModel(openAiModel) ? "developer" : "system";

        var messages = new List<ChatMessage>
        {
            new ChatMessage
            {
                role = instructionRole,
                content = instructionContent
            },
            new ChatMessage
            {
                role = "user",
                content = userContent
            }
        };

        return await GetCompletionResponse(messages);
    }

    private async Task<List<ChatMessage>> GetResponseUsingResponsesAPI(string userContent, string instructionContent)
    {
        LoadCredentialsIfNeeded();

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set. Configure it in Setup.");
            return null;
        }

        const string url = "https://api.openai.com/v1/responses";

        var requestBody = new ResponsesRequest
        {
            model = openAiModel,
            instructions = instructionContent,
            input = userContent,
            max_output_tokens = maxCompletionTokens
        };

        string jsonBody = JsonUtility.ToJson(requestBody);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            if (!string.IsNullOrEmpty(organizationKey))
            {
                request.SetRequestHeader("OpenAI-Organization", organizationKey);
            }

            await SendRequestAsync(request);

            string rawResponse = request.downloadHandler != null ? request.downloadHandler.text : "";

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("[OpenaiManager] Error HTTP en Responses: " + request.error);

                if (!string.IsNullOrWhiteSpace(rawResponse))
                {
                    var errorObj = JsonUtility.FromJson<OpenAIErrorResponse>(rawResponse);
                    if (errorObj != null && errorObj.error != null && !string.IsNullOrEmpty(errorObj.error.message))
                    {
                        Debug.LogError("[OpenaiManager] OpenAI Error: " + errorObj.error.message);
                        EditorUtility.DisplayDialog("ERROR", errorObj.error.message, "OK");
                    }
                    else
                    {
                        Debug.LogError("[OpenaiManager] Error Body: " + rawResponse);
                    }
                }

                return null;
            }

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                Debug.LogError("[OpenaiManager] La respuesta de Responses vino vacía.");
                return null;
            }

            string extractedText = ExtractTextFromResponses(rawResponse);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                Debug.LogWarning("[OpenaiManager] No se pudo extraer texto de Responses.");
                return null;
            }

            var result = new List<ChatMessage>
            {
                new ChatMessage
                {
                    role = "developer",
                    content = instructionContent
                },
                new ChatMessage
                {
                    role = "user",
                    content = userContent
                },
                new ChatMessage
                {
                    role = "assistant",
                    content = extractedText.Trim()
                }
            };

            return result;
        }
    }

    private string ExtractTextFromResponses(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";

        try
        {
            var parsed = JsonUtility.FromJson<ResponsesResponse>(json);

            if (parsed != null)
            {
                if (!string.IsNullOrWhiteSpace(parsed.output_text))
                    return parsed.output_text;

                if (parsed.output != null)
                {
                    StringBuilder sb = new StringBuilder();

                    for (int i = 0; i < parsed.output.Count; i++)
                    {
                        var outputItem = parsed.output[i];
                        if (outputItem == null || outputItem.content == null)
                            continue;

                        for (int j = 0; j < outputItem.content.Count; j++)
                        {
                            var contentItem = outputItem.content[j];
                            if (contentItem == null)
                                continue;

                            if (!string.IsNullOrWhiteSpace(contentItem.text))
                            {
                                if (sb.Length > 0)
                                    sb.AppendLine();

                                sb.Append(contentItem.text);
                            }
                        }
                    }

                    if (sb.Length > 0)
                        return sb.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[OpenaiManager] JsonUtility no pudo parsear Responses: " + ex.Message);
        }

        try
        {
            MatchCollection matches = Regex.Matches(
                json,
                "\"text\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.Singleline
            );

            if (matches != null && matches.Count > 0)
            {
                StringBuilder sb = new StringBuilder();

                foreach (Match match in matches)
                {
                    if (!match.Success || match.Groups.Count < 2)
                        continue;

                    string piece = match.Groups[1].Value;
                    piece = Regex.Unescape(piece);

                    if (string.IsNullOrWhiteSpace(piece))
                        continue;

                    if (sb.Length > 0)
                        sb.AppendLine();

                    sb.Append(piece);
                }

                return sb.ToString().Trim();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[OpenaiManager] Error parseando texto de Responses: " + ex.Message);
        }

        return "";
    }

    private Task<UnityWebRequest> SendRequestAsync(UnityWebRequest request)
    {
        var tcs = new TaskCompletionSource<UnityWebRequest>();
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        operation.completed += _ => { tcs.TrySetResult(request); };
        return tcs.Task;
    }

    [Serializable]
    private class TranscriptionResponse
    {
        public string text;
    }

    public async Task<string> CreateAudioTranscriptionAsync(byte[] audioData, string language = null)
    {
        LoadCredentialsIfNeeded();

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set. Configure it in Setup.");
            return "";
        }

        string previousModel = openAiModel;
        string audioModel = "gpt-4o-mini-transcribe";

        try
        {
            string resolvedLanguage = string.IsNullOrWhiteSpace(language)
                ? GetTranscriptionLanguage()
                : language.Trim().ToLower();

            string url = "https://api.openai.com/v1/audio/transcriptions";

            WWWForm form = new WWWForm();
            form.AddBinaryData("file", audioData, "audio.wav", "audio/wav");
            form.AddField("model", audioModel);

            if (!string.IsNullOrWhiteSpace(resolvedLanguage))
            {
                form.AddField("language", resolvedLanguage);
            }

            using (UnityWebRequest request = UnityWebRequest.Post(url, form))
            {
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);

                if (!string.IsNullOrEmpty(organizationKey))
                {
                    request.SetRequestHeader("OpenAI-Organization", organizationKey);
                }

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Delay(10);
                }

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError("[OpenaiManager] Transcription Error: " + request.error + "\n" + request.downloadHandler.text);
                    return "";
                }

                string responseText = request.downloadHandler.text;
                TranscriptionResponse response = JsonUtility.FromJson<TranscriptionResponse>(responseText);

                return response != null ? response.text : "";
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[OpenaiManager] Transcription exception: " + ex.Message);
            return "";
        }
        finally
        {
            openAiModel = previousModel;
        }
    }

    public string NormalizeText(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        string normalizedText = input
            .Replace('“', '\"')
            .Replace('”', '\"')
            .Replace('‘', '\'')
            .Replace('’', '\'');

        normalizedText = Regex.Replace(normalizedText, @"[^\w\s\-_]", "");
        normalizedText = Regex.Replace(normalizedText, @"\s+", " ").Trim();

        return normalizedText;
    }

    private void ProcessAndSendTranscription(string transcribedText)
    {
        if (transcribedText != "Subtítulos realizados por la comunidad de Amara.org")
        {
        }
        else
        {
        }
    }

    public async Task<byte[]> SynthesizeTextToAudio(string talkText)
    {
        LoadCredentialsIfNeeded();

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set. Configure it in Setup.");
            return null;
        }

        string url = "https://api.openai.com/v1/audio/speech";

        Debug.Log(talkText);

        talkText = NormalizeText(talkText);

        string voiceToUse = string.IsNullOrEmpty(selectedVoiceString)
            ? selectedVoice.ToString()
            : selectedVoiceString;

        string jsonPayload =
            "{\"input\": \"" + talkText.Replace("\"", "\\\"") + "\", " +
            "\"model\": \"tts-1\", " +
            "\"voice\": \"" + voiceToUse + "\"}";

        byte[] postData = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(postData);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            if (!string.IsNullOrEmpty(organizationKey))
            {
                request.SetRequestHeader("OpenAI-Organization", organizationKey);
            }

            Debug.Log("Envío pedido de audio");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error en TTS: " + request.error + "\n" + request.downloadHandler.text);
                return null;
            }

            byte[] audioBytes = request.downloadHandler.data;
            Debug.Log("Recibido " + audioBytes.Length + " bytes de audio.");
            return audioBytes;
        }
    }

    [Serializable]
    private class ImageRequest
    {
        public string model;
        public string prompt;
        public int n;
        public string size;
        public string response_format;
    }

    [Serializable]
    private class ImageGenerationResponse
    {
        public ImageData[] data;
    }

    [Serializable]
    private class ImageData
    {
        public string b64_json;
    }

    [Serializable]
    public class ImageRequestGpt
    {
        public string model;
        public string prompt;
        public int n;
        public string size;
        public string quality;
    }

    [Serializable]
    public class ModelInfo
    {
        public string id;
    }

    [Serializable]
    public class ModelsListResponse
    {
        public ModelInfo[] data;
        public string @object;
    }

    public async Task<Texture2D> GenerateImageFromPrompt(
        string prompt,
        string size = "1024x1024",
        int n = 1,
        string quality = "low")
    {
        LoadCredentialsIfNeeded();

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set.");
            return null;
        }

        Texture2D primaryResult = await GenerateImageFromPromptWithModel(
            PRIMARY_IMAGE_MODEL,
            prompt,
            size,
            n,
            quality
        );

        if (primaryResult != null)
            return primaryResult;

        Debug.LogWarning("[OpenaiManager] Fallback a " + FALLBACK_IMAGE_MODEL);

        return await GenerateImageFromPromptWithModel(
            FALLBACK_IMAGE_MODEL,
            prompt,
            size,
            n,
            quality
        );
    }

    private async Task<Texture2D> GenerateImageFromPromptWithModel(
        string model,
        string prompt,
        string size,
        int n,
        string quality)
    {
        const string url = "https://api.openai.com/v1/images/generations";
        Debug.Log("[OpenaiManager] Trying image generation with model: " + model);

        var req = new ImageRequestGpt
        {
            model = model,
            prompt = prompt,
            n = n,
            size = size,
            quality = quality
        };

        string jsonBody = JsonUtility.ToJson(req);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            if (!string.IsNullOrEmpty(organizationKey))
                request.SetRequestHeader("OpenAI-Organization", organizationKey);

            var op = request.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errorBody = request.downloadHandler != null ? request.downloadHandler.text : "";
                Debug.LogWarning($"[OpenaiManager] Error al generar imagen con {model}: {request.error}\n{errorBody}");
                return null;
            }

            var response = JsonUtility.FromJson<ImageGenerationResponse>(request.downloadHandler.text);

            if (response?.data != null && response.data.Length > 0 && !string.IsNullOrEmpty(response.data[0].b64_json))
            {
                byte[] imgBytes = Convert.FromBase64String(response.data[0].b64_json);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(imgBytes);

                Debug.Log("[OpenaiManager] Image generation OK with model: " + model);
                return tex;
            }

            Debug.LogWarning("[OpenaiManager] No se recibió imagen válida con el modelo " + model + ".");
            return null;
        }
    }

    public async Task<bool> CheckImageModelAvailabilityAsync(string modelId)
    {
        LoadCredentialsIfNeeded();

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set. Configure it in Setup.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            Debug.LogError("[OpenaiManager] modelId vacío en CheckImageModelAvailabilityAsync.");
            return false;
        }

        const string url = "https://api.openai.com/v1/models";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            if (!string.IsNullOrEmpty(organizationKey))
                request.SetRequestHeader("OpenAI-Organization", organizationKey);

            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[OpenaiManager] Error al listar modelos: {request.error}\n{request.downloadHandler.text}");
                return false;
            }

            string jsonResponse = request.downloadHandler.text;
            ModelsListResponse modelsList = JsonUtility.FromJson<ModelsListResponse>(jsonResponse);

            if (modelsList?.data == null)
            {
                Debug.LogError("[OpenaiManager] La respuesta de modelos no contiene datos válidos.");
                return false;
            }

            foreach (var modelInfo in modelsList.data)
            {
                if (modelInfo != null && modelInfo.id == modelId)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public async Task<Texture2D> GenerateImageFromPromptAndReference(
        string prompt,
        Texture2D referenceImage,
        string size = "1024x1024",
        int n = 1,
        string background = "transparent")
    {
        LoadCredentialsIfNeeded();

        Debug.Log("[OpenaiManager] Starting image generation with reference...");

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set. Configure it in Setup.");
            return null;
        }

        if (referenceImage == null)
        {
            Debug.LogError("GenerateImageFromPromptAndReference: referenceImage es null.");
            return null;
        }

        Texture2D readableReference = GetReadableTexture(referenceImage);
        bool destroyReadableCopy = readableReference != referenceImage;

        byte[] imageBytes = readableReference != null ? readableReference.EncodeToPNG() : null;

        if (destroyReadableCopy && readableReference != null)
        {
            DestroyImmediate(readableReference);
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            Debug.LogError("No se pudo convertir la imagen de referencia a PNG.");
            return null;
        }

        Texture2D primaryResult = await GenerateImageEditWithModel(
            PRIMARY_IMAGE_MODEL,
            prompt,
            imageBytes,
            size,
            n,
            background
        );

        if (primaryResult != null)
            return primaryResult;

        Debug.LogWarning("[OpenaiManager] Fallback a " + FALLBACK_IMAGE_MODEL + " para edición con referencia.");

        return await GenerateImageEditWithModel(
            FALLBACK_IMAGE_MODEL,
            prompt,
            imageBytes,
            size,
            n,
            background
        );
    }

    private async Task<Texture2D> GenerateImageEditWithModel(
        string model,
        string prompt,
        byte[] imageBytes,
        string size,
        int n,
        string background)
    {
        const string url = "https://api.openai.com/v1/images/edits";
        Debug.Log("[OpenaiManager] Trying image edit with model: " + model);

        var form = new List<IMultipartFormSection>
    {
        new MultipartFormFileSection("image", imageBytes, "reference.png", "image/png"),
        new MultipartFormDataSection("model", model),
        new MultipartFormDataSection("prompt", prompt),
        new MultipartFormDataSection("size", size),
        new MultipartFormDataSection("n", n.ToString()),
        new MultipartFormDataSection("background", background),
        new MultipartFormDataSection("output_format", "png")
    };

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            if (!string.IsNullOrEmpty(organizationKey))
                request.SetRequestHeader("OpenAI-Organization", organizationKey);

            var op = request.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errorBody = request.downloadHandler != null ? request.downloadHandler.text : "";
                Debug.LogWarning($"[OpenaiManager] Error al editar/generar imagen con referencia usando {model}: {request.error}\n{errorBody}");
                return null;
            }

            var response = JsonUtility.FromJson<ImageGenerationResponse>(request.downloadHandler.text);

            if (response?.data != null &&
                response.data.Length > 0 &&
                !string.IsNullOrEmpty(response.data[0].b64_json))
            {
                byte[] imgBytes = Convert.FromBase64String(response.data[0].b64_json);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(imgBytes);

                Debug.Log("[OpenaiManager] Image edit OK with model: " + model);
                return tex;
            }

            Debug.LogWarning("[OpenaiManager] No se recibió imagen válida en GenerateImageEditWithModel con " + model + ".");
            return null;
        }
    }

    /// <summary>
    /// Flujo nuevo para generación de imágenes desde el editor.
    /// </summary>
    public async Task<Texture2D> GenerateImageForEditor(
        string prompt,
        string size = "1024x1024",
        string quality = "low")
    {
        LoadCredentialsIfNeeded();

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set. Configure it in Setup.");
            return null;
        }

        openAiModel = PRIMARY_IMAGE_MODEL;
        return await GenerateImageFromPrompt(prompt, size: size, quality: quality);
    }

    /// <summary>
    /// Flujo nuevo para generar/editar imagen a partir de una imagen de referencia.
    /// </summary>
    public async Task<Texture2D> GenerateImageFromReferenceForEditor(
        string prompt,
        Texture2D referenceImage,
        string size = "1024x1024",
        string background = "transparent")
    {
        LoadCredentialsIfNeeded();

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set. Configure it in Setup.");
            return null;
        }

        if (referenceImage == null)
        {
            Debug.LogError("[OpenaiManager] GenerateImageFromReferenceForEditor: referenceImage es null.");
            return null;
        }

        openAiModel = PRIMARY_IMAGE_MODEL;

        return await GenerateImageFromPromptAndReference(
            prompt,
            referenceImage,
            size: size,
            background: background
        );
    }

    private float GetSafeTemperature(string model, float requestedTemperature)
    {
        switch (model)
        {
            case "o3":
            case "o4-mini":
            case "gpt-5":
            case "gpt-5-mini":
            case "gpt-5-nano":
            case "gpt-5.1-codex-mini":
            case "gpt-5.3-codex":
                return 1f;

            default:
                return requestedTemperature;
        }
    }

    private void LoadCredentialsIfNeeded()
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(apiKey) || apiKey == "TU_API_KEY_AQUI")
        {
            apiKey = UnityEditor.EditorPrefs.GetString("GPTPlugin_ApiKey", "").Trim();
        }

        if (string.IsNullOrEmpty(organizationKey) || organizationKey == "TU_ORG_KEY_AQUI")
        {
            organizationKey = UnityEditor.EditorPrefs.GetString("GPTPlugin_OrgKey", "").Trim();
        }
#else
        Debug.LogWarning("API Key not set. In build, you must assign it manually or via config.");
#endif
    }

    private bool HasValidApiKey()
    {
        return !string.IsNullOrEmpty(apiKey) && apiKey != "TU_API_KEY_AQUI";
    }

    private string GetSavedEditorModelApiString()
    {
#if UNITY_EDITOR
        string savedValue = UnityEditor.EditorPrefs.GetString("GPTPlugin_Model", "Gpt4oMini");

        if (Enum.TryParse(savedValue, out GptModel parsedModel))
        {
            return parsedModel.ToApiModelString();
        }

        switch (savedValue)
        {
            case "gpt_4o_mini":
            case "gpt-4o-mini":
                return "gpt-4o-mini";

            case "gpt_4o":
            case "gpt-4o":
                return "gpt-4o";

            case "o4_mini":
            case "o4-mini":
                return "o4-mini";

            case "o3":
                return "o3";

            case "gpt_5":
            case "gpt-5":
                return "gpt-5";

            case "gpt_5_mini":
            case "gpt-5-mini":
                return "gpt-5-mini";

            case "gpt_5_nano":
            case "gpt-5-nano":
                return "gpt-5-nano";

            case "gpt_5_1_codex_mini":
            case "gpt-5.1-codex-mini":
                return "gpt-5.1-codex-mini";

            case "gpt_5_3_codex":
            case "gpt-5.3-codex":
                return "gpt-5.3-codex";
        }
#endif

        return "gpt-4o-mini";
    }

    private bool UsesDeveloperRoleForModel(string model)
    {
        switch (model)
        {
            case "o3":
            case "o4-mini":
            case "gpt-5":
            case "gpt-5-mini":
            case "gpt-5-nano":
                return true;

            default:
                return false;
        }
    }

    private bool IsResponsesOnlyModel(string model)
    {
        switch (model)
        {
            case "gpt-5":
            case "gpt-5-mini":
            case "gpt-5-nano":
            case "gpt-5.1-codex-mini":
            case "gpt-5.3-codex":
                return true;

            default:
                return false;
        }
    }

    public async Task<string> AnalyzeUIImageQuality(Texture2D image, string prompt)
    {
        LoadCredentialsIfNeeded();

        if (!HasValidApiKey())
        {
            Debug.LogError("[OpenaiManager] API Key not set.");
            return "";
        }

        if (image == null)
        {
            Debug.LogError("[OpenaiManager] Imagen null en AnalyzeUIImageQuality.");
            return "";
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            Debug.LogError("[OpenaiManager] Prompt vacío en AnalyzeUIImageQuality.");
            return "";
        }

        byte[] imageBytes = image.EncodeToPNG();
        if (imageBytes == null || imageBytes.Length == 0)
        {
            Debug.LogError("[OpenaiManager] No se pudo convertir la imagen a PNG.");
            return "";
        }

        string base64Image = Convert.ToBase64String(imageBytes);
        string dataUrl = "data:image/png;base64," + base64Image;

        const string url = "https://api.openai.com/v1/responses";

        string safePrompt = prompt
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");

        string safeDataUrl = dataUrl
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");

        string jsonPayload =
            "{"
            + "\"model\":\"gpt-5\","
            + "\"input\":["
                + "{"
                    + "\"role\":\"user\","
                    + "\"content\":["
                        + "{"
                            + "\"type\":\"input_text\","
                            + "\"text\":\"" + safePrompt + "\""
                        + "},"
                        + "{"
                            + "\"type\":\"input_image\","
                            + "\"image_url\":\"" + safeDataUrl + "\","
                            + "\"detail\":\"high\""
                        + "}"
                    + "]"
                + "}"
            + "]"
            + "}";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            if (!string.IsNullOrEmpty(organizationKey))
            {
                request.SetRequestHeader("OpenAI-Organization", organizationKey);
            }

            await SendRequestAsync(request);

            string rawResponse = request.downloadHandler != null ? request.downloadHandler.text : "";

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[OpenaiManager] Error AnalyzeUIImageQuality: " + request.error);

                if (!string.IsNullOrWhiteSpace(rawResponse))
                {
                    var errorObj = JsonUtility.FromJson<OpenAIErrorResponse>(rawResponse);
                    if (errorObj != null && errorObj.error != null && !string.IsNullOrEmpty(errorObj.error.message))
                    {
                        Debug.LogError("[OpenaiManager] OpenAI Error: " + errorObj.error.message);
                    }
                    else
                    {
                        Debug.LogError("[OpenaiManager] Error Body: " + rawResponse);
                    }
                }

                return "";
            }

            string extracted = ExtractTextFromResponses(rawResponse);

            if (string.IsNullOrWhiteSpace(extracted))
            {
                Debug.LogWarning("[OpenaiManager] No se pudo extraer texto en AnalyzeUIImageQuality.");
                return "";
            }

            return extracted.Trim();
        }
    }

    private Texture2D GetReadableTexture(Texture2D source)
    {
        if (source == null)
            return null;

        try
        {
            source.GetPixel(0, 0);
            return source;
        }
        catch
        {
        }

        RenderTexture rt = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear
        );

        Graphics.Blit(source, rt);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D readableTexture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        readableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readableTexture.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return readableTexture;
    }

    public static void ClearCachedCredentials()
    {
#if UNITY_EDITOR
        var managers = Resources.FindObjectsOfTypeAll<OpenaiManager>();

        foreach (var m in managers)
        {
            if (m == null)
                continue;

            if (UnityEditor.EditorUtility.IsPersistent(m))
                continue;

            m.apiKey = "";
            m.organizationKey = "";
            m.openAiModel = "";
        }
#else
        var managers = UnityEngine.Object.FindObjectsByType<OpenaiManager>(FindObjectsSortMode.None);

        foreach (var m in managers)
        {
            if (m == null)
                continue;

            m.apiKey = "";
            m.organizationKey = "";
            m.openAiModel = "";
        }
#endif
    }
}