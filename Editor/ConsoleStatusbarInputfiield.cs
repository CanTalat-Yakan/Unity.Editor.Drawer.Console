#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace UnityEssentials
{
    [InitializeOnLoad]
    public static class ConsoleStatusbarInputfiield
    {
        private const string InputTextId = "##console_statusbar_input";
        private static readonly ConsoleInputState s_state = new();
        private static bool s_focusRequested;

        [Shortcut("Unity Essentials/Console/Focus Statusbar Input", KeyCode.Space, ShortcutModifiers.Alt)]
        private static void FocusConsoleInputShortcut() =>
            RequestFocusFromShortcut();

        static ConsoleStatusbarInputfiield() =>
            StatusbarHook.RightStatusbarGUI.Add(OnStatusbarGUI);

        private static void OnStatusbarGUI()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            Color previousContentColor = GUI.contentColor;
            GUI.contentColor = proSkin ? new Color(0.9f, 0.9f, 0.9f, 1f) : new Color(0.15f, 0.15f, 0.15f, 1f);

            GUI.SetNextControlName(InputTextId);
            var previousInput = s_state.Input ?? string.Empty;
            EnsureValidSelectionState(previousInput);
            s_state.Input = GUILayout.TextField(previousInput,
                EditorStyles.toolbarTextField, GUILayout.Width(320f));

            // Any direct user edit exits history browsing mode.
            if (!string.Equals(previousInput, s_state.Input, StringComparison.Ordinal) && s_state.HistoryIndex >= 0)
                s_state.HistoryIndex = -1;

            UpdateSuggestions(s_state.Input);

            var inputRect = GUILayoutUtility.GetLastRect();

            DrawGhostSuggestion(inputRect);

            if (s_focusRequested)
            {
                s_focusRequested = false;
                FocusInputField();
            }

            HandleInputKeys(Event.current);

            GUI.contentColor = previousContentColor;
            GUILayout.Space(5);
        }

        private static void HandleInputKeys(Event evt)
        {
            if(evt.keyCode == KeyCode.None || evt.type == EventType.Layout)
                return;

            if (!string.Equals(GUI.GetNameOfFocusedControl(), InputTextId, StringComparison.Ordinal))
                return;
            
            if (evt.keyCode == KeyCode.Tab)
                if (TryAcceptCurrentSuggestion())
                    evt.Use();

            if (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow)
            {
                if(evt.type == EventType.KeyUp)
                    if (TryNavigateWithArrows(evt.keyCode))
                        evt.Use();
                SetCaretToInputEnd();
            }

            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                SubmitInput();
                evt.Use();
            }
        }

        private static void FocusInputField()
        {
            FocusInputTextControl();
            SetCaretToInputEnd();
        }

        private static void FocusInputTextControl()
        {
            StatusbarFocusController.FocusRightDockContainer();
            GUI.FocusControl(InputTextId);
            EditorGUI.FocusTextInControl(InputTextId);
        }

        public static void RequestFocusFromShortcut()
        {
            s_focusRequested = true;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private static void SubmitInput()
        {
            var line = (s_state.Input ?? string.Empty).Trim();
            s_state.Input = string.Empty;
            s_state.CurrentSuggestion = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
                return;

            if (!Application.isPlaying && !CanExecuteInEditMode(line))
                return;

            ConsoleInputShared.PushHistory(s_state.History, line);
            s_state.HistoryIndex = -1;
            s_state.Suggestions.Clear();
            s_state.SuggestionIndex = -1;
            s_state.CurrentSuggestion = string.Empty;
            ConsoleHost.TryExecuteLine(line, logToDebug: true);
        }

        private static bool CanExecuteInEditMode(string line)
        {
            var commandName = ExtractCommandName(line);
            if (string.IsNullOrWhiteSpace(commandName))
                return true;

            if (!ConsoleHost.Commands.TryGet(commandName, out var command))
                return true;

            if (command.Method == null || command.Method.IsStatic)
                return true;

            return false;
        }

        private static string ExtractCommandName(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            var trimmed = line.Trim();
            var space = trimmed.IndexOf(' ');
            return space < 0 ? trimmed : trimmed.Substring(0, space);
        }

        private static void UpdateSuggestions(string input)
        {
            var query = ConsoleUtilities.GetCommandQuery(input);
            if (string.IsNullOrWhiteSpace(query) || s_state.HistoryIndex >= 0)
            {
                s_state.Suggestions.Clear();
                s_state.SuggestionIndex = -1;
                s_state.CurrentSuggestion = string.Empty;
                return;
            }

            var previouslySelected = (s_state.SuggestionIndex >= 0 && s_state.SuggestionIndex < s_state.Suggestions.Count)
                ? s_state.Suggestions[s_state.SuggestionIndex].Name
                : string.Empty;

            ConsoleInputShared.RebuildSuggestions(ConsoleHost.Commands.SortedCommands, query, s_state.Suggestions);

            if (s_state.Suggestions.Count == 0)
            {
                s_state.SuggestionIndex = -1;
                s_state.CurrentSuggestion = string.Empty;
                return;
            }

            s_state.SuggestionIndex = 0;
            if (!string.IsNullOrWhiteSpace(previouslySelected))
            {
                for (var i = 0; i < s_state.Suggestions.Count; i++)
                {
                    if (!string.Equals(s_state.Suggestions[i].Name, previouslySelected, StringComparison.Ordinal))
                        continue;

                    s_state.SuggestionIndex = i;
                    break;
                }
            }

            SyncCurrentSuggestionFromSelection();
        }

        private static void SyncCurrentSuggestionFromSelection()
        {
            if (s_state.HistoryIndex >= 0 || s_state.SuggestionIndex < 0 || s_state.SuggestionIndex >= s_state.Suggestions.Count)
            {
                s_state.CurrentSuggestion = string.Empty;
                return;
            }

            s_state.CurrentSuggestion = s_state.Suggestions[s_state.SuggestionIndex].Name;
        }

        private static bool TryAcceptCurrentSuggestion()
        {
            UpdateSuggestions(s_state.Input);

            var suggestion = s_state.CurrentSuggestion;
            if (string.IsNullOrWhiteSpace(suggestion))
                return false;

            s_state.Input = ConsoleUtilities.ReplaceCommandToken(s_state.Input, suggestion);
            s_state.HistoryIndex = -1;
            FocusInputTextControl();
            SetCaretToInputEnd();
            UpdateSuggestions(s_state.Input);
            return true;
        }

        private static bool TryNavigateWithArrows(KeyCode keyCode)
        {
            var query = ConsoleUtilities.GetCommandQuery(s_state.Input);
            UpdateSuggestions(s_state.Input);

            var mode = ConsoleInputShared.ResolveNavigationMode(query, s_state.Suggestions.Count, s_state.SuggestionIndex);
            if (mode == ConsoleInputNavigationMode.Suggestions)
            {
                if (s_state.Suggestions.Count == 0)
                    return false;

                if (s_state.SuggestionIndex < 0 || s_state.SuggestionIndex >= s_state.Suggestions.Count)
                    s_state.SuggestionIndex = 0;

                if (keyCode == KeyCode.UpArrow)
                    s_state.SuggestionIndex = (s_state.SuggestionIndex - 1 + s_state.Suggestions.Count) % s_state.Suggestions.Count;
                else if (keyCode == KeyCode.DownArrow)
                    s_state.SuggestionIndex = (s_state.SuggestionIndex + 1) % s_state.Suggestions.Count;
                else
                    return false;

                SyncCurrentSuggestionFromSelection();
                s_state.HistoryIndex = -1;
                FocusInputTextControl();
                return true;
            }

            s_state.SuggestionIndex = -1;
            s_state.CurrentSuggestion = string.Empty;

            if (s_state.History.Count == 0)
                return false;

            if (keyCode == KeyCode.UpArrow)
            {
                if (s_state.HistoryIndex < 0)
                    s_state.HistoryIndex = s_state.History.Count - 1;
                else
                    s_state.HistoryIndex = Mathf.Max(0, s_state.HistoryIndex - 1);

                if (s_state.HistoryIndex >= 0 && s_state.HistoryIndex < s_state.History.Count)
                    s_state.Input = s_state.History[s_state.HistoryIndex];
            }
            else if (keyCode == KeyCode.DownArrow)
            {
                if (s_state.HistoryIndex < 0)
                    return false;

                s_state.HistoryIndex++;
                if (s_state.HistoryIndex >= s_state.History.Count)
                {
                    s_state.HistoryIndex = -1;
                    s_state.Input = string.Empty;
                }
                else
                {
                    s_state.Input = s_state.History[s_state.HistoryIndex];
                }
            }
            else
            {
                return false;
            }

            s_state.Suggestions.Clear();
            s_state.SuggestionIndex = -1;
            s_state.CurrentSuggestion = string.Empty;
            FocusInputTextControl();
            return true;
        }

        private static void SetCaretToInputEnd()
        {
            if (!TryGetFocusedInputTextEditor(out var textEditor))
                return;

            var text = s_state.Input ?? string.Empty;
            textEditor.scrollOffset = Vector2.zero;

            var caretIndex = text.Length;
            textEditor.cursorIndex = caretIndex;
            textEditor.selectIndex = caretIndex;
        }

        private static void EnsureValidSelectionState(string text)
        {
            if (!TryGetFocusedInputTextEditor(out var textEditor))
                return;

            var safeText = text ?? string.Empty;
            var maxIndex = safeText.Length;

            if (textEditor.cursorIndex < 0 || textEditor.cursorIndex > maxIndex)
                textEditor.cursorIndex = Mathf.Clamp(textEditor.cursorIndex, 0, maxIndex);

            if (textEditor.selectIndex < 0 || textEditor.selectIndex > maxIndex)
                textEditor.selectIndex = Mathf.Clamp(textEditor.selectIndex, 0, maxIndex);
        }

        private static bool TryGetFocusedInputTextEditor(out TextEditor textEditor)
        {
            textEditor = null;

            if (!string.Equals(GUI.GetNameOfFocusedControl(), InputTextId, StringComparison.Ordinal))
                return false;

            var keyboardControl = GUIUtility.keyboardControl;
            if (keyboardControl <= 0)
                return false;

            textEditor = GUIUtility.GetStateObject(typeof(TextEditor), keyboardControl) as TextEditor;
            return textEditor != null;
        }

        private static GUIStyle _ghostSuffixStyle;
        private static void DrawGhostSuggestion(Rect inputRect)
        {
            if (!string.Equals(GUI.GetNameOfFocusedControl(), InputTextId, StringComparison.Ordinal))
                return;

            if (!TryGetGhostSuffix(s_state.Input, s_state.CurrentSuggestion, out var prefix, out var suffix))
                return;

            _ghostSuffixStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                richText = false
            };

            var style = EditorStyles.toolbarTextField;
            _ghostSuffixStyle.font = style.font;
            _ghostSuffixStyle.fontSize = style.fontSize;
            _ghostSuffixStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.7f, 0.7f, 0.7f, 0.85f)
                : new Color(0.35f, 0.35f, 0.35f, 0.8f);

            var leftPadding = style.padding.left + 1f;
            var rightPadding = style.padding.right + 1f;
            var prefixWidth = style.CalcSize(new GUIContent(prefix)).x;

            var ghostRect = new Rect(
                inputRect.x + leftPadding + prefixWidth - 8f,
                inputRect.y - 0.6f,
                Mathf.Max(0f, inputRect.width - leftPadding - rightPadding - prefixWidth),
                inputRect.height);

            if (ghostRect.width <= 1f)
                return;

            GUI.Label(ghostRect, suffix, _ghostSuffixStyle);
        }

        private static bool TryGetGhostSuffix(string input, string suggestion, out string prefix, out string suffix)
        {
            prefix = string.Empty;
            suffix = string.Empty;

            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(suggestion))
                return false;

            input ??= string.Empty;
            var leading = input.Length - input.TrimStart().Length;
            var trimmed = input.TrimStart();
            var space = trimmed.IndexOf(' ');

            // Only render inline completion while the user is still editing the command token.
            if (space >= 0)
                return false;

            var query = trimmed;
            if (string.IsNullOrWhiteSpace(query))
                return false;

            if (!suggestion.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return false;

            if (suggestion.Length <= query.Length)
                return false;

            prefix = new string(' ', leading) + query;
            suffix = suggestion.Substring(query.Length);
            return true;
        }


    }
}
#endif