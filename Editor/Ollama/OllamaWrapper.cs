using System;
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
        
        public static  async void RequestTranslation(string text, string targetLanguage, Action<RequestResult> onComplete) {
            using (var client = new System.Net.Http.HttpClient()) {
                
                string prompt = LocalizationKeyGeneratorSettings.Instance.OllamaSettings.translationPrompt
                        .Replace("{{language}}", targetLanguage)
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
                        onComplete?.Invoke(new() { success = false, result = response.StatusCode.ToString()}); // Invoke the callback with null to indicate failure
                    }
                } catch (Exception ex) {
                    onComplete?.Invoke(new() { success = false, result = ex.Message}); // Invoke the callback with null to indicate failure
                }
            }
        }
    }
}