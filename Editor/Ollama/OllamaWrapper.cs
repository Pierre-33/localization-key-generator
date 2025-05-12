using System;
using System.Net;
using Dino.LocalizationKeyGenerator.Editor.Settings;
using UnityEngine;

namespace Dino.LocalizationKeyGenerator.Ollama {
    public static class OllamaWrapper
    {
        [Serializable]
        class OllamaRequest {
            public string model;
            public string prompt;
            public bool stream;
            public string keep_alive;
        }

        [Serializable]
        class OllamaResponse {
            public string response;
        }

        public class RequestResult
        {
            public string result;
            public bool success = true;
        }
        
        public static  async void RequestTranslation(string text, string sourceLanguage, string targetLanguage, Action<RequestResult> onComplete) {
            using (var client = new System.Net.Http.HttpClient()) {
                
                string prompt = LocalizationKeyGeneratorSettings.Instance.OllamaSettings.translationPrompt
                        .Replace("{{source_language}}", sourceLanguage)
                        .Replace("{{target_language}}", targetLanguage)
                        .Replace("{{text}}", text);
                
                var request = new OllamaRequest {
                    model = LocalizationKeyGeneratorSettings.Instance.OllamaSettings.ollamaModel,
                    prompt = prompt,
                    stream = false,
                    keep_alive = "15m"
                };

                string jsonContent = JsonUtility.ToJson(request);
                var requestContent = new System.Net.Http.StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
                try {
                    var response = await client.PostAsync($"{LocalizationKeyGeneratorSettings.Instance.OllamaSettings.ollamaServerUrl}/api/generate", requestContent);

                    if (response.IsSuccessStatusCode) {
                        string result = await response.Content.ReadAsStringAsync();

                        // Deserialize the response into a C# object
                        var translationResponse = JsonUtility.FromJson<OllamaResponse>(result);

                        // Trim everything before </think>
                        string translatedText;
                        int thinkIndex = translationResponse.response.IndexOf("</think>", StringComparison.Ordinal);
                        if (thinkIndex >= 0) {
                            translatedText = translationResponse.response.Substring(thinkIndex + "</think>".Length).Trim();
                        } else {
                            translatedText = translationResponse.response.Trim();
                        }

                        // Remove "=>" if it starts with it
                        if (translatedText.StartsWith("=>")) {
                            translatedText = translatedText.Substring(2).Trim();
                        }

                        onComplete?.Invoke(new() { result = translatedText}); // Invoke the callback with the translated text
                    } else {
                        string errorMessage = response.StatusCode.ToString();
                        if (response.StatusCode == HttpStatusCode.NotFound)
                            errorMessage = $"Model {LocalizationKeyGeneratorSettings.Instance.OllamaSettings.ollamaModel} not found";

                        onComplete?.Invoke(new() { success = false, result = errorMessage});
                    }
                } catch (System.Net.Http.HttpRequestException ex) {
                    string errorMessage = ex.Message;
                    if (ex.InnerException is System.Net.Sockets.SocketException socketEx) {                        
                        switch (socketEx.SocketErrorCode) {
                            case System.Net.Sockets.SocketError.ConnectionRefused:
                                errorMessage = $"Connection refused to {LocalizationKeyGeneratorSettings.Instance.OllamaSettings.ollamaServerUrl}";
                                break;
                            default:
                                break;
                        }
                    }
                    
                    onComplete?.Invoke(new() { success = false, result = errorMessage});
                } 
                catch (Exception ex) {
                    onComplete?.Invoke(new() { success = false, result = ex.Message});
                } 
            }
        }
    }
}