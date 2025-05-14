using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Dino.LocalizationKeyGenerator.Ollama {
    [Serializable]
    class OllamaSettings {
        const string _tabGroupTranslationPrompt = "Translation Prompt";
        const string _tabGroupSpellCheckPrompt = "Spell Check Prompt";
        const string _defaultTranslationPrompt = 
            "Pretend you are a translator for a Unity game. I need the accurate translation in {{target_language}} for the sentences in {{source_language}} after the next => , please be very carreful with the Smart String format that need to be preserved (they are based on SmartFormat library). Preserving the smart string formating must be your top priority. You should not change the structure of the sentence, or add any line break, quotes or any other special formating, the translation must be accurate and very close from the original. I don't want any introduction or explanation, just the translated result. =>{{text}}";
        const string _defaultSpellCheckingPrompt = 
            "Please check for typo or grammatical error the sentence writing in {{source_language}} after the next =>. Be very carreful with the Unity Smart String format that need to be preserved (they are based on SmartFormat library). I don't want any introduction or explanation, just the corrected result. =>{{text}}";
        [InfoBox("Can't reach Ollama Server. Information on how to run Ollama and pull a model can be found on ollama.com", InfoMessageType.Warning,"@OllamaWrapper.GetModelList().Count == 0")]
        public string ollamaServerUrl = "http://localhost:11434";
        [InfoBox("Recommanded model is gemma3:12b if your computer can handle it. gemma3:4b is way faster but less reliable, gemma2:9b is a good compromise.")]
        [CustomValueDrawer("CustomOllamaModelDrawer")] public string ollamaModel = "gemma3:4B";
        [TextArea(1, 40),HideLabel,TabGroup(_tabGroupTranslationPrompt)] public string translationPrompt = _defaultTranslationPrompt;

        [Button("Restore Default Prompt"),EnableIf("@translationPrompt != _defaultTranslationPrompt"),TabGroup(_tabGroupTranslationPrompt)]
        void RestoreTranslationDefaultPrompt() => translationPrompt = _defaultTranslationPrompt;

        [TextArea(1, 40),HideLabel,TabGroup(_tabGroupSpellCheckPrompt)] public string spellCheckPrompt = _defaultSpellCheckingPrompt;

        [Button("Restore Default Prompt"),EnableIf("@spellCheckPrompt != _defaultSpellCheckingPrompt"),TabGroup(_tabGroupSpellCheckPrompt)]
        void RestoreSpellCheckPrompt() => spellCheckPrompt = _defaultSpellCheckingPrompt;

        #if UNITY_EDITOR
        private static string CustomOllamaModelDrawer(string value, GUIContent label)
        {
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
        #endif
    }
    
}