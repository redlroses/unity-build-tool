using System;
using System.IO;
using System.Linq;
using BuildTool.Editor.DirectoryBuilders;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildTool.Editor
{
    [CustomPropertyDrawer(typeof(BuildToolSettings.ProfileConfig))]
    public class ProfileConfigDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var builderProp = property.FindPropertyRelative("PathBuilder");
            if (builderProp.managedReferenceValue == null)
            {
                builderProp.managedReferenceValue = new PatternLocationPathBuilder();
            }

            var h = 12f;
            h += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            h += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            h += EditorGUIUtility.singleLineHeight;

            var isPatternBuilder = builderProp.managedReferenceValue is PatternLocationPathBuilder;
            var useCustomProp = isPatternBuilder ? builderProp.FindPropertyRelative("_useCustomPattern") : null;
            if (useCustomProp != null && useCustomProp.boolValue)
            {
                h += EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight;
            }

            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var profileProp = property.FindPropertyRelative("Profile");
            var builderProp = property.FindPropertyRelative("PathBuilder");

            if (builderProp.managedReferenceValue == null)
            {
                builderProp.managedReferenceValue = new PatternLocationPathBuilder();
            }

            var isPatternBuilder = builderProp.managedReferenceValue is PatternLocationPathBuilder;
            var useCustomProp = isPatternBuilder ? builderProp.FindPropertyRelative("_useCustomPattern") : null;
            var patternProp = isPatternBuilder ? builderProp.FindPropertyRelative("_pattern") : null;

            GUI.Box(position, GUIContent.none, EditorStyles.helpBox);

            var rect = new Rect(position.x + 8, position.y + 6, position.width - 16, EditorGUIUtility.singleLineHeight);

            var profile = profileProp.objectReferenceValue as BuildProfile;
            EditorGUI.LabelField(rect, profile ? profile.name : "Missing Profile", EditorStyles.boldLabel);

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            var sampleBuildPath = BuildTool.GetRootBuildPath();
            var previewPath = "N/A";
            if (builderProp.managedReferenceValue is ILocationPathNameBuilder builder && profile != null)
            {
                try
                {
                    previewPath = builder.GetLocationPathName(profile, sampleBuildPath)?.Replace('\\', '/');
                }
                catch
                {
                    previewPath = "Unable to compute preview path";
                }
            }

            var textRect = new Rect(rect.x, rect.y, rect.width - 32, rect.height);
            var copyRect = new Rect(rect.x + rect.width - 28, rect.y, 28, rect.height);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUI.TextField(textRect, new GUIContent("Path Preview", previewPath), previewPath);
            EditorGUI.EndDisabledGroup();

            var copyIcon = EditorGUIUtility.IconContent("TreeEditor.Duplicate");
            if (GUI.Button(copyRect, new GUIContent(copyIcon?.image, "Copy Path Preview")))
            {
                EditorGUIUtility.systemCopyBuffer = previewPath;
            }

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            if (useCustomProp != null)
            {
                EditorGUI.BeginChangeCheck();
                var isCustom = EditorGUI.ToggleLeft(
                    rect,
                    new GUIContent(
                        "Customize Path Pattern", "Check to override standard build path template for this profile"),
                    useCustomProp.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    useCustomProp.boolValue = isCustom;
                    property.serializedObject.ApplyModifiedProperties();
                }

                if (useCustomProp.boolValue && patternProp != null)
                {
                    rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

                    var patternFieldRect = new Rect(rect.x, rect.y, rect.width - 76, rect.height);
                    var tokensBtnRect = new Rect(rect.x + rect.width - 72, rect.y, 72, rect.height);

                    EditorGUI.BeginChangeCheck();
                    var newPattern = EditorGUI.TextField(
                        patternFieldRect,
                        new GUIContent("Pattern", "Custom relative path format for this profile"),
                        patternProp.stringValue);
                    if (EditorGUI.EndChangeCheck())
                    {
                        patternProp.stringValue = newPattern;
                        property.serializedObject.ApplyModifiedProperties();
                    }

                    if (GUI.Button(
                        tokensBtnRect,
                        new GUIContent("Tokens ▾", "Insert supported token into pattern"),
                        EditorStyles.miniPullDown))
                    {
                        ShowTokenMenu(patternProp, property.serializedObject);
                    }
                }
            }
        }

        private static void ShowTokenMenu(SerializedProperty patternProp, SerializedObject serializedObject)
        {
            var menu = new GenericMenu();
            var tokens = PatternLocationPathBuilder.GetAvailableTokens();

            foreach (var kvp in tokens)
            {
                var tokenTag = $"{{{kvp.Key}}}";
                var label = $"{tokenTag}  —  {kvp.Value}";
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    var current = patternProp.stringValue ?? "";
                    if (!current.EndsWith("/") && !current.EndsWith("_") &&
                        !current.EndsWith(".") && current.Length > 0)
                    {
                        current += "/";
                    }
                    patternProp.stringValue = current + tokenTag;
                    serializedObject.ApplyModifiedProperties();
                });
            }

            menu.ShowAsContext();
        }
    }
}
