﻿using System.IO;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif // UNITY_EDITOR

namespace TensorFlowLite
{
    public class FilePopupAttribute : PropertyAttribute
    {
        public string regex;

        public FilePopupAttribute(string searchPattern)
        {
            this.regex = searchPattern;
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(FilePopupAttribute))]
    public class FilePopupDrawer : PropertyDrawer
    {
        string[] displayNames = null;
        int selectedIndex = -1;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                Debug.LogError($"type: {property.propertyType} is not supported.");
                EditorGUI.LabelField(position, label.text, "Use FilePopup with string.");
                return;
            }

            if (displayNames == null)
            {
                string regex = (attribute as FilePopupAttribute).regex;
                InitDisplayNames(regex);
            }
            if (displayNames.Length == 0)
            {
                EditorGUI.LabelField(position, label.text, "No files found.");
                return;
            }

            if (selectedIndex < 0)
            {
                selectedIndex = FindSelectedIndex(property.stringValue);
            }

            EditorGUI.BeginProperty(position, label, property);

            selectedIndex = EditorGUI.Popup(position, label.text, selectedIndex, displayNames);
            property.stringValue = displayNames[selectedIndex];

            EditorGUI.EndProperty();
        }

        private void InitDisplayNames(string regex)
        {
            // Create the directory if it doesn't exist
            if (!Directory.Exists(Application.streamingAssetsPath))
            {
                Directory.CreateDirectory(Application.streamingAssetsPath);
            }

            string[] fullPaths = Directory.GetFiles(Application.streamingAssetsPath, regex, SearchOption.AllDirectories);

            displayNames = fullPaths.Select(f =>
            {
                string path = f.Replace(Application.streamingAssetsPath, "")
                               .Replace('\\', '/');
                if (path.StartsWith("/"))
                {
                    path = path.Substring(1);
                }
                return path;
            }).ToArray();
        }

        private int FindSelectedIndex(string value)
        {
            for (int i = 0; i < displayNames.Length; i++)
            {
                if (displayNames[i] == value)
                {
                    return i;
                }
            }
            return 0;
        }
    }
#endif // UNITY_EDITOR

}
