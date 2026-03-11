#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UnityEssentials
{
    [InitializeOnLoad]
    internal static class ConsoleStatusbarInputfiield
    {
        private static string _input = string.Empty;

        static ConsoleStatusbarInputfiield() =>
            StatusbarHook.RightStatusbarGUI.Add(OnStatusbarGUI);

        private static void OnStatusbarGUI()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            Color previousContentColor = GUI.contentColor;
            GUI.contentColor = proSkin ? new Color(0.9f, 0.9f, 0.9f, 1f) : new Color(0.15f, 0.15f, 0.15f, 1f);
            
            _input = GUILayout.TextField(_input ?? string.Empty,
                EditorStyles.toolbarTextField, GUILayout.Width(320f), GUILayout.Height(18f));

            GUI.contentColor = previousContentColor;
            GUILayout.Space(5);
        }
    }
}
#endif
