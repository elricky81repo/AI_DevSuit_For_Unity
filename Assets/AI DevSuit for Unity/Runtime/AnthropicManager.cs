using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class AnthropicManager : MonoBehaviour
{
    [Header("Anthropic Credentials")]
    [SerializeField] private string apiKey = "YOUR_ANTHROPIC_API_KEY_HERE";

    [Header("Anthropic Settings")]
    [SerializeField] private string model = "claude-haiku-4-5-20251001";
    [SerializeField] private int maxTokens = 1024;

    #region DATA STRUCTURES

    [Serializable]
    private class AnthropicMessageContent
    {
        public string type = "text";
        public string text;
    }

    [Serializable]
    private class AnthropicMessage
    {
        public string role;
        public AnthropicMessageContent[] content;
    }

    [Serializable]
    private class AnthropicRequestBody
    {
        public string model;
        public int max_tokens;
        public string system;
        public AnthropicMessage[] messages;
    }

    [Serializable]
    private class AnthropicResponseContentItem
    {
        public string type;
        public string text;
    }

    [Serializable]
    private class AnthropicResponseBody
    {
        public string id;
        public string type;
        public string role;
        public AnthropicResponseContentItem[] content;
    }

    [Serializable]
    private class AnthropicErrorWrapper
    {
        public AnthropicError error;
    }

    [Serializable]
    private class AnthropicError
    {
        public string type;
        public string message;
    }

    #endregion

    #region INIT

    private void LoadApiKeyIfNeeded()
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_ANTHROPIC_API_KEY_HERE")
        {
            apiKey = EditorPrefs.GetString("ANTHROPIC_API_KEY", "").Trim();
        }
#endif
    }

    private void LoadModelFromPrefs()
    {
#if UNITY_EDITOR
        string saved = EditorPrefs.GetString(
            "ANTHROPIC_MODEL",
            AnthropicModel.ClaudeHaiku45.ToString()
        );

        var modelEnum = AnthropicModelMapper.FromString(saved);
        model = AnthropicModelMapper.ToApiString(modelEnum);
#endif
    }

    private bool HasValidApiKey()
    {
        return !string.IsNullOrEmpty(apiKey) && apiKey != "YOUR_ANTHROPIC_API_KEY_HERE";
    }

    #endregion

    #region PUBLIC API

    public async Task<string> GetSimpleCompletion(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return "";

        var response = await GetConfiguredEditorCompletionResponse(prompt, null);

        if (response == null || response.Count == 0)
            return "";

        return response[^1]?.content ?? "";
    }

    public async Task<List<ChatMessage>> GetConfiguredEditorCompletionResponse(
        string userContent,
        string instructionContent = null)
    {
        LoadApiKeyIfNeeded();
        LoadModelFromPrefs();

        if (!HasValidApiKey())
        {
            Debug.LogError("[AnthropicManager] API Key not configured.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(userContent))
        {
            Debug.LogError("[AnthropicManager] userContent is empty.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(instructionContent))
        {
            instructionContent = "You are a helpful assistant.";
        }

        var requestBody = new AnthropicRequestBody
        {
            model = model,
            max_tokens = maxTokens,
            system = instructionContent,
            messages = new[]
            {
                new AnthropicMessage
                {
                    role = "user",
                    content = new[]
                    {
                        new AnthropicMessageContent
                        {
                            text = userContent
                        }
                    }
                }
            }
        };

        string jsonBody = JsonUtility.ToJson(requestBody);

        using (UnityWebRequest request = new UnityWebRequest("https://api.anthropic.com/v1/messages", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("x-api-key", apiKey);
            request.SetRequestHeader("anthropic-version", "2023-06-01");

            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Task.Yield();
            }

            string rawResponse = request.downloadHandler?.text ?? "";

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[AnthropicManager] HTTP Error: " + request.error);

                string userMessage = "Anthropic request failed.";

                if (!string.IsNullOrWhiteSpace(rawResponse))
                {
                    try
                    {
                        var errorObj = JsonUtility.FromJson<AnthropicErrorWrapper>(rawResponse);
                        string apiError = errorObj?.error?.message;

                        if (!string.IsNullOrWhiteSpace(apiError))
                        {
                            Debug.LogError("[AnthropicManager] " + apiError);
                            userMessage = apiError;
                        }
                    }
                    catch
                    {
                        Debug.LogError("[AnthropicManager] Raw error: " + rawResponse);
                    }
                }

#if UNITY_EDITOR
                EditorApplication.delayCall += () =>
                {
                    EditorUtility.DisplayDialog(
                        "Anthropic Error",
                        userMessage,
                        "OK"
                    );
                };
#endif

                return null;
            }

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                Debug.LogError("[AnthropicManager] Empty response.");
                return null;
            }

            AnthropicResponseBody responseBody;

            try
            {
                responseBody = JsonUtility.FromJson<AnthropicResponseBody>(rawResponse);
            }
            catch (Exception ex)
            {
                Debug.LogError("[AnthropicManager] JSON parse error: " + ex.Message);
                return null;
            }

            if (responseBody?.content == null || responseBody.content.Length == 0)
            {
                Debug.LogWarning("[AnthropicManager] No valid content.");
                return null;
            }

            var sb = new StringBuilder();

            foreach (var item in responseBody.content)
            {
                if (item != null && item.type == "text" && !string.IsNullOrWhiteSpace(item.text))
                {
                    sb.AppendLine(item.text);
                }
            }

            string finalText = sb.ToString().Trim();

            if (string.IsNullOrWhiteSpace(finalText))
            {
                Debug.LogWarning("[AnthropicManager] Empty text.");
                return null;
            }

            return new List<ChatMessage>
            {
                new ChatMessage
                {
                    role = "assistant",
                    content = finalText
                }
            };
        }
    }

#if UNITY_EDITOR
    public static void ClearCachedCredentials()
    {
        AnthropicManager[] managers = Resources.FindObjectsOfTypeAll<AnthropicManager>();

        foreach (AnthropicManager manager in managers)
        {
            if (manager == null)
                continue;

            manager.apiKey = "";
        }
    }
#endif

    #endregion
}