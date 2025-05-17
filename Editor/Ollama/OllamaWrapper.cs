using System;
using System.Collections.Generic;
using System.Net;
using Dino.LocalizationKeyGenerator.Editor.Settings;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.SocialPlatforms;

namespace Dino.LocalizationKeyGenerator.Ollama {
    public static class OllamaWrapper
    {
        [Serializable]
        class OllamaRequest {
            public string model;
            public string prompt;
            public bool stream;
        }

        [Serializable]
        class OllamaResponse {
            public string response;
        }

        [Serializable]
        class OllamaModelList {
            [Serializable]
            public class Model {
                public string name;
            }
            public List<Model> models;
        }

        public class RequestResult
        {
            public string result;
            public bool success = true;
        }

        static readonly List<string> modelList = new();
        static float lastModelListUpdateTime = 0f;

        public static List<string> GetModelList() {
            if (Time.realtimeSinceStartup - lastModelListUpdateTime > 15f) {
                UpdateModelList();
            }
            return modelList;
        }

        private static async void UpdateModelList() {
            using (var client = new System.Net.Http.HttpClient()) {
                try {
                    var response = await client.GetAsync($"{LocalizationKeyGeneratorSettings.Instance.OllamaSettings.ollamaServerUrl}/api/tags");

                    if (response.IsSuccessStatusCode) {
                        string result = await response.Content.ReadAsStringAsync();
                        var ollamaModelList = JsonUtility.FromJson<OllamaModelList>(result);
                        lastModelListUpdateTime = Time.realtimeSinceStartup;
                        modelList.Clear();
                        foreach (var model in ollamaModelList.models) {
                            if (!modelList.Contains(model.name)) {
                                modelList.Add(model.name);
                            }
                        }
                    }
                } catch (Exception) {
                    modelList.Clear();                   
                }
            }
        }
            
        
        public static void RequestTranslation(string text, LocaleIdentifier sourceLocale, LocaleIdentifier targetLocale, Action<RequestResult> onComplete) {
            string keywordsList = LocalizationKeyGeneratorSettings.Instance.OllamaSettings.BuildKeywordsList(sourceLocale, targetLocale);


            string prompt = LocalizationKeyGeneratorSettings.Instance.OllamaSettings.translationPrompt
                        .Replace("{{source_language}}", sourceLocale.CultureInfo.DisplayName)
                        .Replace("{{target_language}}", targetLocale.CultureInfo.DisplayName)
                        .Replace("{{keywords_list}}", keywordsList)
                        .Replace("{{text}}", text);

            SendOllamaRequest(prompt, onComplete);            
        }

        public static void RequestSpellCheck(string text, LocaleIdentifier sourceLocale, Action<RequestResult> onComplete) {
            string prompt = LocalizationKeyGeneratorSettings.Instance.OllamaSettings.spellCheckPrompt
                        .Replace("{{source_language}}", sourceLocale.CultureInfo.DisplayName)
                        .Replace("{{text}}", text);
                        
            SendOllamaRequest(prompt, onComplete);            
        }

        private static  async void SendOllamaRequest(string prompt, Action<RequestResult> onComplete) {
            using (var client = new System.Net.Http.HttpClient()) {                                
                var request = new OllamaRequest {
                    model = LocalizationKeyGeneratorSettings.Instance.OllamaSettings.ollamaModel,
                    prompt = prompt,
                    stream = false,
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
                        if (translatedText.StartsWith(">")) {
                            translatedText = translatedText.Substring(1).Trim();
                        }

                        onComplete?.Invoke(new() { result = translatedText});
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
                } catch (Exception ex) {
                    onComplete?.Invoke(new() { success = false, result = ex.Message});
                } 
            }
        }
    }
}