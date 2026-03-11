#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace UnityEssentials
{
    [InitializeOnLoad]
    internal static class ConsoleStatusbarInputfiield
    {
        private const string InputControlName = "UnityEssentials.ConsoleStatusbarInput";
        private const float InputWidth = 320f;
        private const float InputHeight = 18f;

        private static string _input = string.Empty;

        static ConsoleStatusbarInputfiield() =>
            StatusbarHook.RightStatusbarGUI.Add(OnStatusbarGUI);

        private static void OnStatusbarGUI()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            Color previousContentColor = GUI.contentColor;
            GUI.contentColor = proSkin ? new Color(0.9f, 0.9f, 0.9f, 1f) : new Color(0.15f, 0.15f, 0.15f, 1f);

            GUI.SetNextControlName(InputControlName);
            _input = GUILayout.TextField(_input ?? string.Empty,
                EditorStyles.toolbarTextField, GUILayout.Width(InputWidth), GUILayout.Height(InputHeight));

            HandleSubmitKey(Event.current);

            GUI.contentColor = previousContentColor;
            GUILayout.Space(5);
        }

        private static void HandleSubmitKey(Event evt)
        {
            if (evt == null || evt.type != EventType.KeyDown)
                return;

            if (!string.Equals(GUI.GetNameOfFocusedControl(), InputControlName, StringComparison.Ordinal))
                return;

            SubmitInput();
            evt.Use();
        }

        private static void SubmitInput()
        {
            var line = (_input ?? string.Empty).Trim();
            _input = string.Empty;

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
    }
}
#endif