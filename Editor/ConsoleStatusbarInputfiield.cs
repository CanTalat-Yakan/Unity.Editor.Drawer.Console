#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace UnityEssentials
{
    [InitializeOnLoad]
    internal static class ConsoleStatusbarInputfiield
    {
        private const string InputTextId = "##console_statusbar_input";

        private static string _input = string.Empty;
        private static string _currentSuggestion = string.Empty;
        private static GUIStyle _ghostSuffixStyle;

        static ConsoleStatusbarInputfiield() =>
            StatusbarHook.RightStatusbarGUI.Add(OnStatusbarGUI);

        private static void OnStatusbarGUI()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            Color previousContentColor = GUI.contentColor;
            GUI.contentColor = proSkin ? new Color(0.9f, 0.9f, 0.9f, 1f) : new Color(0.15f, 0.15f, 0.15f, 1f);

            GUI.SetNextControlName(InputTextId);
            _input = GUILayout.TextField(_input ?? string.Empty,
                EditorStyles.toolbarTextField, GUILayout.Width(320f));

            var inputRect = GUILayoutUtility.GetLastRect();

            _currentSuggestion = FindBestSuggestion(_input);
            DrawGhostSuggestion(inputRect);
            HandleInputKeys(Event.current);

            GUI.contentColor = previousContentColor;
            GUILayout.Space(5);
        }

        private static void HandleInputKeys(Event evt)
        {
            if(evt.keyCode == KeyCode.None || evt.type == EventType.Layout)
                return;
            
            if (evt.keyCode == KeyCode.Tab)
                if (TryAcceptCurrentSuggestion())
                    evt.Use();

            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                SubmitInput();
                evt.Use();
            }
        }

        private static void SubmitInput()
        {
            var line = (_input ?? string.Empty).Trim();
            _input = string.Empty;
            _currentSuggestion = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
                return;

            if (!Application.isPlaying && !CanExecuteInEditMode(line))
                return;

            ConsoleHost.TryExecuteLine(line);
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

        private static string FindBestSuggestion(string input)
        {
            var query = ConsoleUtilities.GetCommandQuery(input);
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            var commands = ConsoleHost.Commands.SortedCommands;

            for (var i = 0; i < commands.Count; i++)
            {
                var name = commands[i].Name;
                if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = ConsoleUtilities.MatchCommandQuery(name, query);
                if (match.IsPrefixMatch)
                    return name;
            }

            for (var i = 0; i < commands.Count; i++)
            {
                var name = commands[i].Name;
                if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = ConsoleUtilities.MatchCommandQuery(name, query);
                if (match.IsTokenMatch)
                    return name;
            }

            return string.Empty;
        }

        private static bool TryAcceptCurrentSuggestion()
        {
            var suggestion = _currentSuggestion;
            if (string.IsNullOrWhiteSpace(suggestion))
                return false;

            _input = ConsoleUtilities.ReplaceCommandToken(_input, suggestion);
            EditorGUI.FocusTextInControl(InputTextId);
            SetCaretToInputEnd();
            _currentSuggestion = FindBestSuggestion(_input);
            return true;
        }

        private static void SetCaretToInputEnd()
        {
            var textEditor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (textEditor == null)
                return;

            var caretIndex = (_input ?? string.Empty).Length;
            textEditor.cursorIndex = caretIndex;
            textEditor.selectIndex = caretIndex;
        }

        private static void DrawGhostSuggestion(Rect inputRect)
        {
            if (!string.Equals(GUI.GetNameOfFocusedControl(), InputTextId, StringComparison.Ordinal))
                return;

            if (!TryGetGhostSuffix(_input, _currentSuggestion, out var prefix, out var suffix))
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