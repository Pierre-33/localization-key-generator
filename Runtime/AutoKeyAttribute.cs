using UnityEngine;

namespace Dino.LocalizationKeyGenerator {
    public class AutoKeyAttribute : PropertyAttribute {
        public readonly string Format;
        public readonly bool IsDefaultTabAuto;
        public readonly string[] SnippetsCollections;
        
        public AutoKeyAttribute(string format, bool isDefaultTabAuto = true) {
            Format = format;
            IsDefaultTabAuto = isDefaultTabAuto;
            SnippetsCollections = null;
        }

        public AutoKeyAttribute(string format, bool isDefaultTabAuto, params string[] snippetsCollections) {
            Format = format;
            IsDefaultTabAuto = isDefaultTabAuto;
            SnippetsCollections = snippetsCollections;
        }
        
        public AutoKeyAttribute(string format, params string[] snippetsCollections) {
            Format = format;
            IsDefaultTabAuto = true;
            SnippetsCollections = snippetsCollections;
        }
    }
}