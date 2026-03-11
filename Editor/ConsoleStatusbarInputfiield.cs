#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityEssentials
{
    [InitializeOnLoad]
    internal static class ConsoleStatusbarInputfiield
    {
        private const string InputTextId = "##console_statusbar_input";
        private const int MaxHistoryEntries = 50;

        private static string _input = string.Empty;
        private static string _currentSuggestion = string.Empty;
        private static readonly List<string> _suggestions = new(16);
        private static int _suggestionIndex = -1;
        private static readonly List<string> _history = new(32);
        private static int _historyIndex = -1;
        private static GUIStyle _ghostSuffixStyle;

        private enum NavigationMode
        {
            Suggestions,
            History
        }

        static ConsoleStatusbarInputfiield() =>
            StatusbarHook.RightStatusbarGUI.Add(OnStatusbarGUI);

        private static void OnStatusbarGUI()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            Color previousContentColor = GUI.contentColor;
            GUI.contentColor = proSkin ? new Color(0.9f, 0.9f, 0.9f, 1f) : new Color(0.15f, 0.15f, 0.15f, 1f);

            GUI.SetNextControlName(InputTextId);
            var previousInput = _input ?? string.Empty;
            _input = GUILayout.TextField(previousInput,
                EditorStyles.toolbarTextField, GUILayout.Width(320f));

            // Any direct user edit exits history browsing mode.
            if (!string.Equals(previousInput, _input, StringComparison.Ordinal) && _historyIndex >= 0)
                _historyIndex = -1;

            UpdateSuggestions(_input);

            var inputRect = GUILayoutUtility.GetLastRect();

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

        private static void SubmitInput()
        {
            var line = (_input ?? string.Empty).Trim();
            _input = string.Empty;
            _currentSuggestion = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
                return;

            if (!Application.isPlaying && !CanExecuteInEditMode(line))
                return;

            PushHistory(line);
            _historyIndex = -1;
            _suggestions.Clear();
            _suggestionIndex = -1;
            _currentSuggestion = string.Empty;
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

        private static NavigationMode ResolveNavigationMode(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return NavigationMode.History;

            if (_suggestions.Count > 0 && _suggestionIndex >= 0)
                return NavigationMode.Suggestions;

            return NavigationMode.History;
        }

        private static void UpdateSuggestions(string input)
        {
            var query = ConsoleUtilities.GetCommandQuery(input);
            if (string.IsNullOrWhiteSpace(query) || _historyIndex >= 0)
            {
                _suggestions.Clear();
                _suggestionIndex = -1;
                _currentSuggestion = string.Empty;
                return;
            }

            var previouslySelected = (_suggestionIndex >= 0 && _suggestionIndex < _suggestions.Count)
                ? _suggestions[_suggestionIndex]
                : string.Empty;

            _suggestions.Clear();
            var commands = ConsoleHost.Commands.SortedCommands;
            var showAllSuggestions = ShouldShowAllSuggestions(query);

            if (showAllSuggestions)
            {
                for (var i = 0; i < commands.Count; i++)
                    _suggestions.Add(commands[i].Name);
            }
            else
            {
                for (var i = 0; i < commands.Count; i++)
                {
                    var name = commands[i].Name;
                    if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var match = ConsoleUtilities.MatchCommandQuery(name, query);
                    if (match.IsPrefixMatch)
                        _suggestions.Add(name);
                }

                for (var i = 0; i < commands.Count; i++)
                {
                    var name = commands[i].Name;
                    if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var match = ConsoleUtilities.MatchCommandQuery(name, query);
                    if (match.IsTokenMatch && !match.IsPrefixMatch)
                        _suggestions.Add(name);
                }
            }

            if (_suggestions.Count == 0)
            {
                _suggestionIndex = -1;
                _currentSuggestion = string.Empty;
                return;
            }

            _suggestionIndex = 0;
            if (!string.IsNullOrWhiteSpace(previouslySelected))
            {
                for (var i = 0; i < _suggestions.Count; i++)
                {
                    if (!string.Equals(_suggestions[i], previouslySelected, StringComparison.Ordinal))
                        continue;

                    _suggestionIndex = i;
                    break;
                }
            }

            SyncCurrentSuggestionFromSelection();
        }

        private static bool ShouldShowAllSuggestions(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;

            var first = query[0];
            var last = query[query.Length - 1];
            return ConsoleUtilities.IsCommandTokenSeparator(first)
                   || ConsoleUtilities.IsCommandTokenSeparator(last);
        }

        private static void SyncCurrentSuggestionFromSelection()
        {
            if (_historyIndex >= 0 || _suggestionIndex < 0 || _suggestionIndex >= _suggestions.Count)
            {
                _currentSuggestion = string.Empty;
                return;
            }

            _currentSuggestion = _suggestions[_suggestionIndex];
        }

        private static bool TryAcceptCurrentSuggestion()
        {
            UpdateSuggestions(_input);

            var suggestion = _currentSuggestion;
            if (string.IsNullOrWhiteSpace(suggestion))
                return false;

            _input = ConsoleUtilities.ReplaceCommandToken(_input, suggestion);
            _historyIndex = -1;
            EditorGUI.FocusTextInControl(InputTextId);
            SetCaretToInputEnd();
            UpdateSuggestions(_input);
            return true;
        }

        private static bool TryNavigateWithArrows(KeyCode keyCode)
        {
            var query = ConsoleUtilities.GetCommandQuery(_input);
            UpdateSuggestions(_input);

            var mode = ResolveNavigationMode(query);
            if (mode == NavigationMode.Suggestions)
            {
                if (_suggestions.Count == 0)
                    return false;

                if (_suggestionIndex < 0 || _suggestionIndex >= _suggestions.Count)
                    _suggestionIndex = 0;

                if (keyCode == KeyCode.UpArrow)
                    _suggestionIndex = (_suggestionIndex - 1 + _suggestions.Count) % _suggestions.Count;
                else if (keyCode == KeyCode.DownArrow)
                    _suggestionIndex = (_suggestionIndex + 1) % _suggestions.Count;
                else
                    return false;

                SyncCurrentSuggestionFromSelection();
                _historyIndex = -1;
                EditorGUI.FocusTextInControl(InputTextId);
                return true;
            }

            _suggestionIndex = -1;
            _currentSuggestion = string.Empty;

            if (_history.Count == 0)
                return false;

            if (keyCode == KeyCode.UpArrow)
            {
                if (_historyIndex < 0)
                    _historyIndex = _history.Count - 1;
                else
                    _historyIndex = Mathf.Max(0, _historyIndex - 1);

                if (_historyIndex >= 0 && _historyIndex < _history.Count)
                    _input = _history[_historyIndex];
            }
            else if (keyCode == KeyCode.DownArrow)
            {
                if (_historyIndex < 0)
                    return false;

                _historyIndex++;
                if (_historyIndex >= _history.Count)
                {
                    _historyIndex = -1;
                    _input = string.Empty;
                }
                else
                {
                    _input = _history[_historyIndex];
                }
            }
            else
            {
                return false;
            }

            _suggestions.Clear();
            _suggestionIndex = -1;
            _currentSuggestion = string.Empty;
            EditorGUI.FocusTextInControl(InputTextId);
            return true;
        }

        private static void PushHistory(string line)
        {
            if (_history.Count == 0 || !string.Equals(_history[^1], line, StringComparison.Ordinal))
                _history.Add(line);

            if (_history.Count > MaxHistoryEntries)
                _history.RemoveAt(0);
        }

        private static void SetCaretToInputEnd()
        {
            var textEditor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (textEditor == null)
                return;

            var text = _input ?? string.Empty;

            textEditor.text = text;
            textEditor.scrollOffset = Vector2.zero;

            var caretIndex = text.Length;
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