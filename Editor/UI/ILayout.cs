using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Dino.LocalizationKeyGenerator.Editor.UI {
    internal interface ILayout {
        void Draw(GUIContent label);
        void PopulateGenericMenu(InspectorProperty property, GenericMenu genericMenu);
    }
}