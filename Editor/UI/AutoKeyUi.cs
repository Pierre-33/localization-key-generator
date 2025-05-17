using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Dino.LocalizationKeyGenerator.Editor.Settings;
using Dino.LocalizationKeyGenerator.Editor.Solvers;
using Dino.LocalizationKeyGenerator.Editor.Utility;
using Dino.LocalizationKeyGenerator.Ollama;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Localization.UI;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace Dino.LocalizationKeyGenerator.Editor.UI {
    internal class AutoKeyUi {
        private const string TextControlNamePrefix = "LocalizationText";

        private static string[] _tabLabels;

        private readonly KeySolver _keySolver;
        private readonly InspectorProperty _property;
        private readonly AutoKeyAttribute _attribute;
        private readonly Styles _styles;
        private readonly PropertyEditor _editor;

        private ReadOnlyCollection<StringTableCollection> _tableCollections;
        private string[] _collectionLabels;
        private long _settingsVersionOnPrevKeySolverRun = -1;        
        private AutoKeyUiMode _mode;
        private readonly Dictionary<string, LocaleControlData> _localeControlsData = new ();

        private class LocaleControlData
        {
            public enum State
            {
                None,
                Translating,
                SpellChecking
            }
            public LocaleControlData(LocaleIdentifier locale) {
                this.locale = locale;
            }
            public bool HasPendingRequest() => state != State.None;
            public string GetStateString() {
                return state switch {
                    State.Translating => "Translating...",
                    State.SpellChecking => "Spell Checking...",
                    _ => string.Empty
                };
            }
            public void ClearRequest() {
                state = State.None;
                requestResult = null;
            }
            public Rect controlRect;
            public OllamaWrapper.RequestResult requestResult;
            public State state;
            public LocaleIdentifier locale;
            public string snippetToInsert;
            public int instertIndex = -1;
        }

        #region Initialization

        public AutoKeyUi(InspectorProperty property, AutoKeyAttribute attr, PropertyEditor editor, Styles styles) {
            _keySolver = new KeySolver();
            _property = property;
            _attribute = attr;
            _editor = editor;
            _styles = styles;
            InitializeModeTabs();
            BindTableUpdates();
        }

        private void InitializeModeTabs() {
            _tabLabels = _tabLabels ?? Enum.GetNames(typeof(AutoKeyUiMode));
            _mode = _attribute.IsDefaultTabAuto ? AutoKeyUiMode.Auto : AutoKeyUiMode.Manual;
        }

        private void BindTableUpdates() {
            if (_tableCollections != null) {
                return;
            }
            UpdateTablesList();
            LocalizationEditorSettings.EditorEvents.CollectionAdded += _ => UpdateTablesList();
            LocalizationEditorSettings.EditorEvents.CollectionRemoved += _ => UpdateTablesList();
        }

        private void UpdateTablesList() {
            _tableCollections = LocalizationEditorSettings.GetStringTableCollections();
            _collectionLabels = _tableCollections.Select(c => c.TableCollectionName).Prepend("None").ToArray();
        }

        #endregion
        
        #region Draw

        public void DrawModeSelector(out AutoKeyUiMode mode) {
            BeginIndentedGroup();

            var selectedTab = (int) _mode;
            selectedTab = GUILayout.Toolbar(selectedTab, _tabLabels, EditorStyles.miniButton);
            _mode = (AutoKeyUiMode) selectedTab;
            mode = _mode;

            EndIndentedGroup();
        }

        public void DrawErrors() {
            var errors = _keySolver.GetErrors();
            if (string.IsNullOrEmpty(errors) == false) {
                EditorGUILayout.LabelField(new GUIContent(errors, _styles.WarningIcon), _styles.ErrorStyle);
            }
        }

        public void DrawKeySelector() {
            DrawTablePopup();
            DrawKey();
        }

        public void DrawText() {
            var sharedEntry = _editor.GetSharedEntry();
            
            foreach (var locale in LocalizationKeyGeneratorSettings.Instance.PreviewLocales) {
                if (locale == default || _editor.IsLocalizationTableAvailable(locale) == false) {
                    continue;
                }
                
                DrawLocale(locale, ref sharedEntry);
            }
        }

        private void DrawTablePopup() {
            var prevCollectionIndex = 0;
            var collection = _editor.GetTableCollection();
            if (collection != null) {
                for (var i = 0; i < _tableCollections.Count; i++) {
                    if (_tableCollections[i] != collection) continue;
                    prevCollectionIndex = i + 1;
                    break;
                }
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Table", _styles.LabelStyle, _styles.LabelOptions);
            
            BeginIgnoreIndent();
            var newCollectionIndex = EditorGUILayout.Popup(prevCollectionIndex, _collectionLabels, _styles.FlexibleContentOptions);
            EndIgnoreIndent();
            
            var newCollection = newCollectionIndex <= 0 ? default : _tableCollections[newCollectionIndex - 1];
            if (newCollectionIndex != prevCollectionIndex) {
                _editor.SetTableCollection(newCollection);
                GUIUtility.ExitGUI();
            }

            if (newCollection != null && GUILayout.Button(_styles.EditTableButton, _styles.ButtonStyle, _styles.SquareContentOptions)) {
                LocalizationTablesWindow.ShowWindow(newCollection);
            }
            EditorGUILayout.EndHorizontal();
        }

        private bool ArePreviewLocalesSmart()
        {
            foreach (var locale in LocalizationKeyGeneratorSettings.Instance.PreviewLocales)
            {
                var table = _editor.GetLocalizationTable(locale);
                var entry = _editor.GetSharedEntry() != null ? _editor.GetLocalizationTableEntry(table) : null;
                if (entry != null)
                {
                    if (!entry.IsSmart)
                        return false;
                }
            }
            return true;
        }

        private bool CheckForBrackets()
        {
            foreach (var locale in LocalizationKeyGeneratorSettings.Instance.PreviewLocales)
            {
                var table = _editor.GetLocalizationTable(locale);
                var entry = _editor.GetSharedEntry() != null ? _editor.GetLocalizationTableEntry(table) : null;
                if (entry?.Value != null)
                {
                    if (entry.Value.Contains("{") && entry.Value.Contains("}"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void SetPreviewLocalesSmart(bool isSmart)
        {
            foreach (var locale in LocalizationKeyGeneratorSettings.Instance.PreviewLocales)
            {
                var table = _editor.GetLocalizationTable(locale);
                var entry = _editor.GetSharedEntry() != null ? _editor.GetLocalizationTableEntry(table) : null;
                if (entry != null) {
                    entry.IsSmart = isSmart;
                }
                
            }
        }

        private void DrawKey() {
            var sharedData = _editor.GetSharedData();

            if (sharedData == null) {
                return;
            }
            var sharedEntry = _editor.GetSharedEntry();
            var hasEntry = sharedEntry != null;
            var keyText = hasEntry ? sharedEntry.Key : "None";

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent($"Key: {keyText}", tooltip: keyText), _styles.LabelStyle, _styles.LabelOptions);

            if (hasEntry == false && GUILayout.Button("Generate", _styles.ButtonStyle, _styles.FlexibleContentOptions)) {
                if (TryCreateUniqueLocalizationKey(sharedData, _attribute.Format, oldKey: null, out var key)) {
                    sharedEntry = _editor.CreateSharedEntry(key);
                }
            }

            var findButtonContent = new GUIContent("Find", "Find existing key in table");
            if (hasEntry == false && GUILayout.Button(findButtonContent, _styles.ButtonStyle, _styles.FlexibleContentOptions)) {
                if (TryCreateLocalizationKey(_attribute.Format, out var key)) {
                    if (sharedData.Contains(key)) {
                        sharedEntry = sharedData.GetEntry(key);
                        _editor.SetSharedEntryReference(sharedEntry);
                    }
                    else {
                        Debug.Log($"Unable to find an existing entry with the key '{key}'");
                    }
                }
            }

            if (hasEntry && GUILayout.Button("Regenerate", _styles.ButtonStyle, _styles.FlexibleContentOptions)) {
                if (TryCreateUniqueLocalizationKey(sharedData, _attribute.Format, sharedEntry.Key, out var key)) {
                    _editor.RenameSharedEntry(key);
                }
            }

            if (hasEntry) {
                bool isSmart = ArePreviewLocalesSmart();
                string smartButtonTooltip = "Toggle Smart String for all languages";
                Color previousColor = GUI.color;
                if (isSmart) {
                    GUI.color = Color.lightGray;
                }
                else if (CheckForBrackets()) {
                    GUI.color = Color.red;
                    smartButtonTooltip = "One of your locale contains brackets, and Smart is not active";
                }
                
                if (GUILayout.Button(new GUIContent("S", smartButtonTooltip), _styles.SquareContentOptions)) {                    
                    SetPreviewLocalesSmart(!isSmart);
                    GUIUtility.hotControl = 0;
                    GUIUtility.keyboardControl = 0;
                    GUIUtility.ExitGUI();
                }
                GUI.color = previousColor;

                if (GUILayout.Button(new GUIContent("❐", "Duplicate table entry"), _styles.SquareContentOptions)) {
                    if (TryCreateUniqueLocalizationKey(sharedData, _attribute.Format, sharedEntry.Key, out var key)) {
                        _editor.CreateSharedEntry(key);
                        _editor.CopySharedEntryValuesFrom(sharedEntry);
                    }
                    
                    GUIUtility.hotControl = 0;
                    GUIUtility.keyboardControl = 0;
                    GUIUtility.ExitGUI();
                }
                
                if (GUILayout.Button(new GUIContent("○", "Set reference empty"), _styles.SquareContentOptions)) {
                    _editor.SetSharedEntryReferenceEmpty();
                    GUIUtility.hotControl = 0;
                    GUIUtility.keyboardControl = 0;
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button(new GUIContent("✕", "Remove table entry"), _styles.SquareContentOptions)) {
                    _editor.RemoveSharedEntry();
                    GUIUtility.hotControl = 0;
                    GUIUtility.keyboardControl = 0;
                    GUIUtility.ExitGUI();
                }
            }
            else {
                SkipButtonControl();
                SkipButtonControl();
                SkipButtonControl();
            }

            EditorGUILayout.EndHorizontal();
        }

        string HightlightTextDifference(string oldText, string newText) 
        {
            // Compute the LCS table
            int[,] lcs = new int[oldText.Length + 1, newText.Length + 1];
            for (int i = 1; i <= oldText.Length; i++) {
                for (int j = 1; j <= newText.Length; j++) {
                    if (oldText[i - 1] == newText[j - 1]) {
                        lcs[i, j] = lcs[i - 1, j - 1] + 1;
                    } else {
                        lcs[i, j] = Mathf.Max(lcs[i - 1, j], lcs[i, j - 1]);
                    }
                }
            }

            // Create arrays to track which characters are part of the common subsequence
            bool[] inLcsOld = new bool[oldText.Length];
            bool[] inLcsNew = new bool[newText.Length];

            // Backtrack to find the LCS and mark common characters
            // We'll backtrack from the end to prefer keeping later characters 
            // (making the algorithm prefer marking earlier duplicate characters as deleted)
            int x = oldText.Length, y = newText.Length;
            
            // Create a copy of the LCS table for reverse traversal
            int[,] rlcs = new int[oldText.Length + 1, newText.Length + 1];
            for (int i = 0; i <= oldText.Length; i++) {
                for (int j = 0; j <= newText.Length; j++) {
                    rlcs[i, j] = lcs[i, j];
                }
            }
            
            // First pass - mark the optimal LCS elements from the end
            while (x > 0 && y > 0) {
                if (oldText[x - 1] == newText[y - 1]) {
                    inLcsOld[x - 1] = true;
                    inLcsNew[y - 1] = true;
                    x--;
                    y--;
                } else if (rlcs[x - 1, y] >= rlcs[x, y - 1]) {
                    x--;
                } else {
                    y--;
                }
            }
            
            // Build the result showing both additions and replacements
            var result = new System.Text.StringBuilder();
            int oldIndex = 0, newIndex = 0;
            
            // Diff algorithm that shows both additions and replacements
            while (oldIndex < oldText.Length || newIndex < newText.Length) {
                // Case 1: Characters match in both strings
                if (oldIndex < oldText.Length && newIndex < newText.Length && 
                    inLcsOld[oldIndex] && inLcsNew[newIndex]) {
                    result.Append(oldText[oldIndex]);
                    oldIndex++;
                    newIndex++;
                }
                // Case 2: Check for character replacement (one character removed and replaced by one or more characters)
                else if (oldIndex < oldText.Length && !inLcsOld[oldIndex] &&
                         newIndex < newText.Length && !inLcsNew[newIndex]) {
                    // This is a replacement - highlight new character(s) without striking out the old
                    // We'll collect all consecutive replacement characters
                    int startNewIndex = newIndex;
                    while (newIndex < newText.Length && !inLcsNew[newIndex]) {
                        newIndex++;
                    }
                    
                    result.Append($"<b><color=white>{newText.Substring(startNewIndex, newIndex - startNewIndex)}</color></b>");
                    oldIndex++; // Skip the old character
                }
                // Case 3: Character was deleted from old text with no replacement
                else if (oldIndex < oldText.Length && !inLcsOld[oldIndex]) {
                    result.Append($"<b><s><color=white>{oldText[oldIndex]}</color></s></b>");
                    oldIndex++;
                }
                // Case 4: Character was added in new text (not replacing anything)
                else if (newIndex < newText.Length && !inLcsNew[newIndex]) {
                    result.Append($"<b><color=white>{newText[newIndex]}</color></b>");
                    newIndex++;
                }
                // Skip past any other characters
                else {
                    if (oldIndex < oldText.Length) oldIndex++;
                    if (newIndex < newText.Length) newIndex++;
                }
            }

            return result.ToString();
        }

        private void DrawLocale(LocaleIdentifier locale, ref SharedTableData.SharedTableEntry sharedEntry) {
            var table = _editor.GetLocalizationTable(locale);
            var entry = sharedEntry != null ? _editor.GetLocalizationTableEntry(table) : null;

            
            if (table == null) {
                return;
            }

            var textControlName = GetTextControlName(locale);
            // Create a new control data if needed
            if (_localeControlsData.TryGetValue(textControlName, out LocaleControlData controlData) == false)
            {
                controlData = new LocaleControlData(locale);
                _localeControlsData.Add(textControlName, controlData);
            }

            EditorGUI.BeginChangeCheck();
            BeginVerticalContentSizeFitter();
            
            GUI.SetNextControlName(textControlName);

            var oldText = entry?.Value ?? string.Empty;

            bool forceApplyChange = false;
            if (controlData.HasPendingRequest()) {
                if (controlData.requestResult != null) {
                    if (controlData.requestResult.success && controlData.state == LocaleControlData.State.SpellChecking) {
                        // Highlight the difference between the old and new text
                        oldText = HightlightTextDifference(oldText, controlData.requestResult.result);
                    }   
                    else {
                        oldText = controlData.requestResult.result;                        
                    }                  
                }
                else {
                    oldText = controlData.GetStateString();
                }
            } else if (controlData.snippetToInsert != null) {
                //Insert snippet at cursor position
                oldText = oldText.Insert(controlData.instertIndex, controlData.snippetToInsert);                    
                forceApplyChange = true;                
            }
            controlData.snippetToInsert = null;
            controlData.instertIndex = -1;

            bool isReadOnly = controlData.HasPendingRequest();

            GUI.enabled = !isReadOnly;
            var newText = EditorGUILayout.TextArea(oldText, _styles.TextStyle, _styles.TextOptions);
            GUI.enabled = true;

            if (controlData.HasPendingRequest()) {
                GUILayout.BeginHorizontal(); 
                GUILayout.FlexibleSpace();           
                if (GUILayout.Button("Cancel",GUILayout.Width(75))) {
                    controlData.ClearRequest();                    
                }
                GUI.enabled = controlData.requestResult != null && controlData.requestResult.success;
                if (GUILayout.Button("Accept",GUILayout.Width(75))) {
                    newText = controlData.requestResult.result; // we need to reapply the result to remove potential formating introduced by HightlightTextDifference
                    controlData.ClearRequest();                     
                    forceApplyChange = true;
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();                
            }

            EndVerticalContentSizeFitter();

            var isTextSelected = GUI.GetNameOfFocusedControl() == textControlName;
            var textRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint) {
                controlData.controlRect = textRect;
            }
            EditorGUI.LabelField(textRect, locale.Code, isTextSelected ? _styles.SelectedLocaleMarkerStyle : _styles.NormalLocaleMarkerStyle);

            if ((isReadOnly || EditorGUI.EndChangeCheck() == false) && !forceApplyChange) {
                return;
            }

            if (entry != null) {
                _editor.SetLocalizationTableEntryValue(entry, newText);
                return;
            }

            if (sharedEntry != null) {
                entry = _editor.CreateLocalizationTableEntry(table, sharedEntry.Key);
            }
            else {
                if (TryCreateUniqueLocalizationKey(table.SharedData, _attribute.Format, oldKey: null, key: out var key) == false) {
                    return;
                }

                entry = _editor.CreateLocalizationTableEntry(table, key);
            }

            _editor.SetLocalizationTableEntryValue(entry, newText);
            
            GUIUtility.ExitGUI();
        }

        private string GetTextControlName(LocaleIdentifier locale) {
            return $"{TextControlNamePrefix}@{_property.Path}-{locale.Code}";
        }

        #endregion

        #region Context menu
        private void OnOllamaRequestComplete(string controlName, OllamaWrapper.RequestResult result) {
            _localeControlsData[controlName].requestResult = result;

            // Repaint all open Inspector windows, this a little overkill but i don't know how to find my window
            var inspectorWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();                
            foreach (var window in inspectorWindows) {
                window.Repaint();
            }
        }

        public void PopulateGenericMenu(InspectorProperty property, GenericMenu genericMenu)
        {
            var sharedEntry = _editor.GetSharedEntry();

            void TranslateAll(LocaleControlData controlData) {
                var table = _editor.GetLocalizationTable(controlData.locale);
                var entry = sharedEntry != null ? _editor.GetLocalizationTableEntry(table) : null;

                if (entry != null) {
                    foreach (var otherlocale in LocalizationKeyGeneratorSettings.Instance.PreviewLocales) {
                        if (otherlocale == default || _editor.IsLocalizationTableAvailable(otherlocale) == false) {
                            continue;
                        }
                        if (otherlocale == controlData.locale) {
                            continue;
                        }
                        var controlName = GetTextControlName(otherlocale);
                        _localeControlsData[controlName].state = LocaleControlData.State.Translating;
                        OllamaWrapper.RequestTranslation(entry.Value,controlData.locale,otherlocale,(result) => OnOllamaRequestComplete(controlName, result)); 
                    }
                }
            }

            void SpellCheck(LocaleControlData controlData) {
                var table = _editor.GetLocalizationTable(controlData.locale);
                var entry = sharedEntry != null ? _editor.GetLocalizationTableEntry(table) : null;

                if (entry != null) {
                    var controlName = GetTextControlName(controlData.locale);
                    controlData.state = LocaleControlData.State.SpellChecking;
                    OllamaWrapper.RequestSpellCheck(entry.Value,controlData.locale,(result) => OnOllamaRequestComplete(controlName, result)); 
                }
            }

            void InsertSnippet(LocaleControlData controlData, string snippet, int insertIndex){
                controlData.snippetToInsert = snippet;
                controlData.instertIndex = insertIndex;
            }

            var mousePosition = Event.current.mousePosition;

            foreach (var kvp in _localeControlsData) {
                if (kvp.Value.controlRect.Contains(mousePosition)) {
                    if (!kvp.Value.HasPendingRequest()) {
                        genericMenu.AddSeparator("");
                        genericMenu.AddItem(new GUIContent("Translate in other languages"), false, () => TranslateAll(kvp.Value));
                        genericMenu.AddItem(new GUIContent("Spell Check"), false, () => SpellCheck(kvp.Value));

                        if (_attribute.SnippetsCollections != null && _attribute.SnippetsCollections.Length > 0) {
                            // You can't get the TextEditor from the EditorGUILayout.TextArea with GUIUtility.GetStateObject like you would in GUILayout.TextArea
                            // So we need to use reflection to get the TextEditor from the EditorGUI
                            // https://discussions.unity.com/t/custom-editor-want-features-from-guilayout-textarea-and-editorguilayout-textarea/859550
                            var editor = typeof(EditorGUI).GetField("activeEditor", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as TextEditor;
                            if (editor != null && kvp.Value.controlRect.Contains(new Vector2(editor.position.x, editor.position.y))) {
                                if (_attribute.SnippetsCollections != null) {
                                    genericMenu.AddSeparator("");

                                    foreach (string snippetCollectionName in _attribute.SnippetsCollections) {
                                        var collection = LocalizationKeyGeneratorSettings.Instance.GetSnippetsCollection(snippetCollectionName);
                                        if (collection != null) {
                                            foreach (var snippet in collection) {
                                                genericMenu.AddItem(new GUIContent($"Paste: {snippet}"), false, () => InsertSnippet(kvp.Value,snippet, editor.cursorIndex));
                                            }
                                        }
                                        else {
                                            genericMenu.AddItem(new GUIContent($"Missing Snippets Collection: {snippetCollectionName}"), false, null);
                                        }
                                    }
                                }
                            }
                        }
                        
                    }
                    break;
                }
            }
        }
        #endregion
        
        #region Update

        public void Update() {
            CheckForErrors();
        }

        private void CheckForErrors() {
            if (_settingsVersionOnPrevKeySolverRun == LocalizationKeyGeneratorSettings.Instance.Version)
                return;

            _settingsVersionOnPrevKeySolverRun = LocalizationKeyGeneratorSettings.Instance.Version;
            _keySolver.CheckForErrors(_property, _attribute.Format);
        }
        
        #endregion
        
        #region Localization tools

        private bool TryCreateLocalizationKey(string keyFormat, out string key) {
            _settingsVersionOnPrevKeySolverRun = LocalizationKeyGeneratorSettings.Instance.Version;
            return _keySolver.TryCreateKey(_property, keyFormat, out key);
        }

        private bool TryCreateUniqueLocalizationKey(SharedTableData sharedData, string keyFormat, string oldKey, out string key) {
            if (sharedData == null) {
                key = null;
                return false;
            }
            
            _settingsVersionOnPrevKeySolverRun = LocalizationKeyGeneratorSettings.Instance.Version;
            return _keySolver.TryCreateUniqueKey(_property, keyFormat, sharedData, oldKey, out key);
        }

        #endregion

        #region Layout

        private void SkipButtonControl() {
            GUI.Button(new Rect(), GUIContent.none);
        }

        private void BeginIndentedGroup() {
            SirenixEditorGUI.BeginIndentedVertical();
        }

        private void EndIndentedGroup() {
            SirenixEditorGUI.EndIndentedVertical();
        }
        
        private void BeginVerticalContentSizeFitter() {
            GUILayout.BeginVertical(_styles.ContentSizeFitterOptions);
        }

        private void EndVerticalContentSizeFitter() {
            GUILayout.EndVertical();
        }

        private void BeginIgnoreIndent() {
            GUIHelper.PushIndentLevel(EditorGUI.indentLevel);
            EditorGUI.indentLevel = 0;
        }

        private void EndIgnoreIndent() {
            GUIHelper.PopIndentLevel();
        }
        
        #endregion
    }
}