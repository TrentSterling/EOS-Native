using System;
using System.Collections.Generic;
using System.Reflection;
using EOSNative.Net;
using UnityEditor;
using UnityEngine;

namespace EOSNative.Editor
{
    /// <summary>
    /// Custom Inspector for <see cref="EasySync"/>. Scans sibling components for public
    /// fields and properties of supported types and shows toggle checkboxes for each.
    /// </summary>
    [CustomEditor(typeof(EasySync))]
    public class EasySyncEditor : UnityEditor.Editor
    {
        private static readonly HashSet<Type> Supported = new()
        {
            typeof(bool), typeof(byte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(string),
            typeof(Vector2), typeof(Vector3), typeof(Quaternion),
            typeof(Color), typeof(Color32)
        };

        // Components to exclude from the scan
        private static readonly HashSet<Type> Excluded = new()
        {
            typeof(Transform), typeof(NetworkObject), typeof(EasySync)
        };

        // Base types whose declared properties we skip (tag, name, gameObject, etc.)
        private static readonly HashSet<Type> BaseTypes = new()
        {
            typeof(MonoBehaviour), typeof(Behaviour),
            typeof(Component), typeof(UnityEngine.Object)
        };

        private readonly Dictionary<string, bool> _foldouts = new();

        public override void OnInspectorGUI()
        {
            var sync = (EasySync)target;

            serializedObject.Update();

            // Sync interval
            var intervalProp = serializedObject.FindProperty("_syncInterval");
            EditorGUILayout.PropertyField(intervalProp, new GUIContent("Sync Interval (sec)"));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Synced Properties", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Check fields and properties to sync across the network.\n" +
                "Only the object owner can write synced values. Supported: bool, byte, short, int, long, " +
                "float, double, string, Vector2, Vector3, Quaternion, Color, Color32.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            // Scan sibling components
            var components = sync.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                var compType = comp.GetType();
                if (Excluded.Contains(compType)) continue;
                if (typeof(NetworkBehaviour).IsAssignableFrom(compType)) continue;

                // Collect syncable members
                var members = new List<(string name, Type type, bool isProperty)>();

                // Public instance fields
                foreach (var field in compType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (Supported.Contains(field.FieldType))
                        members.Add((field.Name, field.FieldType, false));
                }

                // Public instance properties (must have getter + setter, skip base Unity props)
                foreach (var prop in compType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!prop.CanRead || !prop.CanWrite) continue;
                    if (prop.GetIndexParameters().Length > 0) continue;
                    if (!Supported.Contains(prop.PropertyType)) continue;
                    if (BaseTypes.Contains(prop.DeclaringType)) continue;
                    if (prop.GetCustomAttribute<ObsoleteAttribute>() != null) continue;
                    members.Add((prop.Name, prop.PropertyType, true));
                }

                if (members.Count == 0) continue;

                // Foldout per component
                string key = compType.FullName ?? compType.Name;
                if (!_foldouts.ContainsKey(key)) _foldouts[key] = true;
                _foldouts[key] = EditorGUILayout.Foldout(_foldouts[key], compType.Name, true);
                if (!_foldouts[key]) continue;

                EditorGUI.indentLevel++;

                foreach (var (name, type, isProperty) in members)
                {
                    int idx = FindBinding(sync, key, name);
                    bool isBound = idx >= 0;
                    string label = isProperty ? $"{name}  ({type.Name})" : $"{name}  ({type.Name})  [field]";

                    bool newBound = EditorGUILayout.ToggleLeft(label, isBound);
                    if (newBound != isBound)
                    {
                        Undo.RecordObject(sync, newBound ? "Add EasySync Binding" : "Remove EasySync Binding");

                        if (newBound)
                        {
                            sync._bindings.Add(new EasySync.SyncBinding
                            {
                                ComponentType = key,
                                MemberName = name,
                                IsProperty = isProperty,
                                Access = EasySync.WriteAccess.Owner
                            });
                        }
                        else
                        {
                            sync._bindings.RemoveAt(idx);
                        }

                        EditorUtility.SetDirty(sync);
                    }
                }

                EditorGUI.indentLevel--;
            }

            // Summary
            EditorGUILayout.Space(4);
            var style = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic };
            EditorGUILayout.LabelField($"{sync._bindings.Count} properties synced", style);

            serializedObject.ApplyModifiedProperties();
        }

        private static int FindBinding(EasySync sync, string compType, string memberName)
        {
            for (int i = 0; i < sync._bindings.Count; i++)
            {
                if (sync._bindings[i].ComponentType == compType &&
                    sync._bindings[i].MemberName == memberName)
                    return i;
            }
            return -1;
        }
    }
}
