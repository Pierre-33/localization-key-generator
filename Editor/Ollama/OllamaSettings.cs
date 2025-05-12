using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Dino.LocalizationKeyGenerator.Ollama {
    [Serializable]
    class OllamaSettings {
        const string _defaultTranslationPrompt = 
            "Pretend you are a translator for a Unity game. I need the accurate translation in {{target_language}} for the sentences in {{source_language}} after the next => , please be very carreful with the Smart String format that need to be preserved (they are based on SmartFormat library). Preserving the smart string formating must be your top priority. You should not change the structure of the sentence, or add any line break, quotes or any other special formating, the translation must be accurate and very close from the original. I don't want any introduction or explanation, just the translated result. =>{{text}}";
        public string ollamaServerUrl = "http://localhost:11434";
        public string ollamaModel = "gemma3:4B";
        [TextArea(1, 20)] public string translationPrompt = _defaultTranslationPrompt;

        [Button,EnableIf("@translationPrompt != _defaultTranslationPrompt")]
        void RestoreDefaultPrompt() => translationPrompt = _defaultTranslationPrompt;

    }
    
}