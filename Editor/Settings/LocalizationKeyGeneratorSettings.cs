using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using Sirenix.OdinInspector;
using Dino.LocalizationKeyGenerator.Ollama;

namespace Dino.LocalizationKeyGenerator.Editor.Settings {
    internal class LocalizationKeyGeneratorSettings : ScriptableObject {
        const string _tabGroupGeneral = "General";
        const string _tabGroupOllama = "Ollama";
        const string _tabGroupSnippets = "Snippets";
        [SerializeField,TabGroup(_tabGroupGeneral)] private LocaleIdentifier[] _previewLocales = Array.Empty<LocaleIdentifier>();
        [SerializeField,TabGroup(_tabGroupGeneral)] private string _defaultKeyStringFormat = "aa_bb";
        [SerializeField,TabGroup(_tabGroupGeneral)] private string _defaultCommentStringFormat = string.Empty;
        [SerializeField,TabGroup(_tabGroupGeneral)] private StringDictionaryContainer _parameters = new StringDictionaryContainer();
        [SerializeField,TabGroup(_tabGroupSnippets),ListDrawerSettings(ShowFoldout = false)] private List<SnippetsCollection> _snippetsCollection = new ();
        [SerializeField,HideLabel,TabGroup(_tabGroupOllama)] private OllamaSettings _ollamaSettings = new ();

        public IReadOnlyList<LocaleIdentifier> PreviewLocales => _previewLocales;
        public string DefaultKeyStringFormat => _defaultKeyStringFormat;
        public string DefaultCommentStringFormat => _defaultCommentStringFormat;
        public IReadOnlyDictionary<string, string> Parameters => _parameters.Dictionary;
        public OllamaSettings OllamaSettings => _ollamaSettings;
        public List<string> GetSnippetsCollection(string collectionName) {
            var collection = _snippetsCollection.FirstOrDefault(c => c.collectionName == collectionName);
            if (collection == null) {
                return null;
            }
            return collection.snippets;
        }
        
        public long Version { get; private set; }
        
        public event Action Changed;
        
        private static LocalizationKeyGeneratorSettings _instance;

        public static LocalizationKeyGeneratorSettings Instance {
            get {
                if (_instance != null) {
                    return _instance;
                }
                
                var guid = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(LocalizationKeyGeneratorSettings)}").FirstOrDefault();
                if (string.IsNullOrEmpty(guid) == false) {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<LocalizationKeyGeneratorSettings>(path);
                    return _instance;
                }

                _instance = CreateInstance<LocalizationKeyGeneratorSettings>();
                UnityEditor.AssetDatabase.CreateAsset(_instance, $"Assets/{nameof(LocalizationKeyGeneratorSettings)}.asset");
                return _instance;
            }
        }
        
        [UnityEditor.MenuItem("Window/Localization Key Generator/Settings")]
        private static void Open() {
            UnityEditor.Selection.activeObject = Instance;
        }

        private void Reset() {
            if (LocalizationSettings.AvailableLocales.Locales.Count == 0) {
                _previewLocales = Array.Empty<LocaleIdentifier>();
                return;
            }
            _previewLocales = new [] { LocalizationSettings.AvailableLocales.Locales[0].Identifier };
        }

        private void OnValidate() {
            _parameters.Refresh();
            Version++;
            Changed?.Invoke();
        }

        [Serializable]
        private class StringDictionaryContainer {
            [SerializeField] private Pair[] Values = Array.Empty<Pair>();
            private Dictionary<string, string> _dictionary = new Dictionary<string, string>();
            
            public IReadOnlyDictionary<string, string> Dictionary => _dictionary;
            
            public void Refresh() => _dictionary = Values
                .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(keySelector: p => p.Key, elementSelector: p => p.Last().Value);
            
            [Serializable]
            private class Pair {
                [SerializeField] private string _key = string.Empty;
                [SerializeField] private string _value = string.Empty;
                
                public string Key => _key;
                public string Value => _value;
            }
        }

        [Serializable]
        public class SnippetsCollection {
            public string collectionName = string.Empty;
            [ListDrawerSettings(ShowFoldout = false)] public List<string> snippets = new ();            
        }
    }
}