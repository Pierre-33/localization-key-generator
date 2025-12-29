using System;
using System.Collections.Generic;
using Dino.LocalizationKeyGenerator.Editor.Settings;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Localization;

namespace Dino.LocalizationKeyGenerator.Ollama {
    [Serializable]
    class OllamaSettings {
        const string _tabGroupTranslationPrompt = "Translation Prompt";
        const string _tabGroupTranslationKeywords = "Translation Keywords";
        const string _tabGroupSpellCheckPrompt = "Spell Check Prompt";
        const string _defaultTranslationPrompt = 
            "Pretend you are a translator for a Unity game. I need the accurate translation in {{target_language}} for the sentences in {{source_language}} after the next => , please be very carreful with the Smart String format that need to be preserved (they are based on SmartFormat library). Preserving the smart string formating must be your top priority. Please transfert also potential rich text balise. Here is a list of keywords that you should respect in your translation : {{keywords_list}}.You should not change the structure of the sentence, or add any line break, quotes or any other special formating, the translation must be accurate and very close from the original. I don't want any introduction or explanation, just the translated result. =>{{text}}";
        const string _defaultSpellCheckingPrompt = 
            "Please check for typo or grammatical error the sentence writing in {{source_language}} after the next =>. Be very carreful with the Unity Smart String format that need to be preserved (they are based on SmartFormat library). In case of a plural form driven by the value of a token between brace, I would like you to handle it with the proper Unity plural formatter : {token:p:singularform|pluralform}. For example \"you gain {token} point\" should become \"you gain {token} point{token:p:|s}\". I don't want any introduction or explanation, just the corrected result. =>{{text}}";
        [InfoBox("Can't reach Ollama Server. Information on how to run Ollama and pull a model can be found on ollama.com", InfoMessageType.Warning,"@OllamaWrapper.GetModelList().Count == 0")]
        public string ollamaServerUrl = "http://localhost:11434";
        [InfoBox("Recommanded model is gemma3:12b if your computer can handle it. gemma3:4b is way faster but less reliable, gemma2:9b is a good compromise.")]
        [CustomValueDrawer("CustomOllamaModelDrawer")] public string ollamaModel = "gemma3:4B";
        [SerializeField,TabGroup(_tabGroupTranslationKeywords),ListDrawerSettings(CustomAddFunction = "CustomAddKeyword")] List<Keyword> translationKeywords = new List<Keyword>();
        [TextArea(1, 40),HideLabel,TabGroup(_tabGroupTranslationPrompt)] public string translationPrompt = _defaultTranslationPrompt;        

        [Button("Restore Default Prompt"),EnableIf("@translationPrompt != _defaultTranslationPrompt"),TabGroup(_tabGroupTranslationPrompt)]
        void RestoreTranslationDefaultPrompt() => translationPrompt = _defaultTranslationPrompt;

        [TextArea(1, 40),HideLabel,TabGroup(_tabGroupSpellCheckPrompt)] public string spellCheckPrompt = _defaultSpellCheckingPrompt;

        [Button("Restore Default Prompt"),EnableIf("@spellCheckPrompt != _defaultSpellCheckingPrompt"),TabGroup(_tabGroupSpellCheckPrompt)]
        void RestoreSpellCheckPrompt() => spellCheckPrompt = _defaultSpellCheckingPrompt;

        #if UNITY_EDITOR
        private static string CustomOllamaModelDrawer(string value, GUIContent label) {
            var modelList = OllamaWrapper.GetModelList();
            if (modelList.Count == 0) {
                UnityEditor.EditorGUILayout.LabelField(label, new GUIContent("No model found."));
                return value;
            }
            else {
                int selectedIndex = modelList.IndexOf(value);
                selectedIndex = UnityEditor.EditorGUILayout.Popup(label,selectedIndex, modelList.ToArray()); 
                return modelList[selectedIndex];
            }
        }

        private Keyword CustomAddKeyword()
        {
            var newKeyword = new Keyword();
            foreach (var locale in LocalizationKeyGeneratorSettings.Instance.PreviewLocales)
            {
                var translatedKeyword = new TranslatedKeyword();
                translatedKeyword.locale = locale;
                translatedKeyword.keyword = string.Empty;
                newKeyword.translatedKeywords ??= new();
                newKeyword.translatedKeywords.Add(translatedKeyword);
            }
            return newKeyword;
        }

        #endif

        [Serializable]
        class TranslatedKeyword
        {
            public LocaleIdentifier locale;
            public string keyword;
        }

        [Serializable]
        class Keyword
        {
            [CustomValueDrawer("CustomTranslatedKeywordDrawer"),ListDrawerSettings(ShowFoldout = false),LabelText("@GetLabel()")]
            public List<TranslatedKeyword> translatedKeywords;

            string GetLabel()
            {
                if (translatedKeywords != null && translatedKeywords.Count > 0) {
                    if (translatedKeywords[0].keyword != string.Empty) {
                        return translatedKeywords[0].keyword;
                    }                    
                }
                return "New Keyword";
            }

            private static TranslatedKeyword CustomTranslatedKeywordDrawer(TranslatedKeyword value, GUIContent label) {
                UnityEditor.EditorGUILayout.BeginHorizontal();
                
                // Get all available locales to create a dropdown
                var locales = UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales;
                if (locales != null && locales.Locales.Count > 0)
                {
                    int selectedIndex = -1;
                    string[] localeOptions = new string[locales.Locales.Count];
                    
                    // Populate dropdown options and find current selection
                    for (int i = 0; i < locales.Locales.Count; i++)
                    {
                        localeOptions[i] = locales.Locales[i].name;
                        if (locales.Locales[i].Identifier == value.locale)
                        {
                            selectedIndex = i;
                        }
                    }
                    
                    // If locale not found, default to first option
                    if (selectedIndex == -1) selectedIndex = 0;
                    
                    // Draw the dropdown
                    int newSelectedIndex = UnityEditor.EditorGUILayout.Popup(selectedIndex, localeOptions);
                    if (newSelectedIndex != selectedIndex)
                    {
                        value.locale = locales.Locales[newSelectedIndex].Identifier;
                    }
                }
                else
                {
                    // Fallback if no locales are available
                    UnityEditor.EditorGUILayout.LabelField("No locales available");
                }
                
                value.keyword = UnityEditor.EditorGUILayout.TextField(value.keyword);
                UnityEditor.EditorGUILayout.EndHorizontal();

                return value;
            }
        }

        public string BuildKeywordsList(LocaleIdentifier sourceLocale, LocaleIdentifier targetLocale)
        {
            var stringBuilder = new System.Text.StringBuilder();
            foreach (var keyword in translationKeywords)
            {
                var sourceKeyword = keyword.translatedKeywords.Find(k => k.locale == sourceLocale);
                if (sourceKeyword == null)
                    continue;
                var targetKeyword = keyword.translatedKeywords.Find(k => k.locale == targetLocale);
                if (targetKeyword == null)
                    continue;
                if (stringBuilder.Length > 0)
                    stringBuilder.Append(",");
                stringBuilder.Append($"\"{sourceKeyword.keyword}\"/\"{targetKeyword.keyword}\"");
            }
            return stringBuilder.ToString();
        }
    }
    
}