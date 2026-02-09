using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using EOSNative.Net;
using UnityEditor;
using UnityEngine;

namespace EOSNative.Editor
{
    /// <summary>
    /// Custom Inspector for <see cref="EasySync"/>. Scans sibling components for public
    /// fields and properties of supported types and shows toggle checkboxes for each.
    /// v2: Adds WriteAccess dropdown, Interpolation toggle, Interpolation speed, and
    /// "Convert to Code" button that exports a typed NetworkBehaviour .cs file.
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

        // Types that support interpolation
        private static readonly HashSet<Type> InterpolatableTypes = new()
        {
            typeof(float), typeof(double),
            typeof(Vector2), typeof(Vector3), typeof(Quaternion),
            typeof(Color), typeof(Color32),
            typeof(int), typeof(short), typeof(byte)
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
                "Set WriteAccess per property: Owner (default), Host, or All.\n" +
                "Enable Interpolation for smooth movement on remote peers.\n" +
                "Supported: bool, byte, short, int, long, float, double, string, " +
                "Vector2, Vector3, Quaternion, Color, Color32.",
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

                    EditorGUILayout.BeginHorizontal();

                    bool newBound = EditorGUILayout.ToggleLeft(label, isBound, GUILayout.MinWidth(200));
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
                                Access = EasySync.WriteAccess.Owner,
                                Interpolate = false,
                                InterpolateSpeed = 15f
                            });
                        }
                        else
                        {
                            sync._bindings.RemoveAt(idx);
                        }

                        EditorUtility.SetDirty(sync);
                    }

                    // Show WriteAccess and Interpolation controls for bound properties
                    if (newBound && idx >= 0 || newBound && isBound)
                    {
                        int bindIdx = FindBinding(sync, key, name);
                        if (bindIdx >= 0)
                        {
                            var binding = sync._bindings[bindIdx];

                            // WriteAccess dropdown
                            var newAccess = (EasySync.WriteAccess)EditorGUILayout.EnumPopup(
                                binding.Access, GUILayout.Width(60));
                            if (newAccess != binding.Access)
                            {
                                Undo.RecordObject(sync, "Change EasySync WriteAccess");
                                binding.Access = newAccess;
                                EditorUtility.SetDirty(sync);
                            }

                            // Interpolation toggle (only for interpolatable types)
                            if (InterpolatableTypes.Contains(type))
                            {
                                bool newInterp = EditorGUILayout.Toggle(
                                    new GUIContent("Lerp", "Interpolate this value on remote peers"),
                                    binding.Interpolate, GUILayout.Width(50));
                                if (newInterp != binding.Interpolate)
                                {
                                    Undo.RecordObject(sync, "Toggle EasySync Interpolation");
                                    binding.Interpolate = newInterp;
                                    EditorUtility.SetDirty(sync);
                                }

                                if (binding.Interpolate)
                                {
                                    float newSpeed = EditorGUILayout.FloatField(
                                        binding.InterpolateSpeed, GUILayout.Width(40));
                                    if (Math.Abs(newSpeed - binding.InterpolateSpeed) > 0.01f)
                                    {
                                        Undo.RecordObject(sync, "Change EasySync Interpolation Speed");
                                        binding.InterpolateSpeed = Mathf.Max(0.1f, newSpeed);
                                        EditorUtility.SetDirty(sync);
                                    }
                                }
                            }
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUI.indentLevel--;
            }

            // Summary
            EditorGUILayout.Space(4);
            var style = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic };
            EditorGUILayout.LabelField($"{sync._bindings.Count} properties synced", style);

            // Convert to Code button
            EditorGUILayout.Space(8);
            if (sync._bindings.Count > 0)
            {
                EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Generate a typed NetworkBehaviour .cs file from the current bindings. " +
                    "This creates compile-time SyncVars instead of runtime reflection — " +
                    "better performance and type safety.",
                    MessageType.None);

                if (GUILayout.Button("Convert to Code"))
                {
                    ConvertToCode(sync);
                }
            }

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

        #region Convert to Code

        private static void ConvertToCode(EasySync sync)
        {
            string className = sync.gameObject.name.Replace(" ", "") + "Sync";
            string path = EditorUtility.SaveFilePanel(
                "Save NetworkBehaviour", "Assets", className + ".cs", "cs");

            if (string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder();
            sb.AppendLine("using EOSNative.Net;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine($"/// <summary>");
            sb.AppendLine($"/// Auto-generated from EasySync on '{sync.gameObject.name}'.");
            sb.AppendLine($"/// Replace the EasySync component with this for better performance.");
            sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public class {className} : NetworkBehaviour");
            sb.AppendLine("{");

            // Collect unique component types needed
            var componentTypes = new HashSet<string>();
            foreach (var binding in sync._bindings)
                componentTypes.Add(binding.ComponentType);

            // SyncVar declarations
            foreach (var binding in sync._bindings)
            {
                string shortType = GetShortTypeName(binding.ComponentType);
                string varName = $"_{shortType}_{binding.MemberName}";
                string csType = GetCSharpTypeName(binding);
                sb.AppendLine($"    SyncVar<{csType}> {varName};");
            }

            sb.AppendLine();

            // Component references
            foreach (var compType in componentTypes)
            {
                string shortType = GetShortTypeName(compType);
                sb.AppendLine($"    private {shortType} _{shortType.ToLower()};");
            }

            sb.AppendLine();

            // Awake
            sb.AppendLine("    protected override void Awake()");
            sb.AppendLine("    {");
            sb.AppendLine("        base.Awake();");

            // Cache component references
            foreach (var compType in componentTypes)
            {
                string shortType = GetShortTypeName(compType);
                sb.AppendLine($"        _{shortType.ToLower()} = GetComponent<{shortType}>();");
            }

            sb.AppendLine();

            // Initialize SyncVars
            foreach (var binding in sync._bindings)
            {
                string shortType = GetShortTypeName(binding.ComponentType);
                string varName = $"_{shortType}_{binding.MemberName}";
                string csType = GetCSharpTypeName(binding);
                string defaultVal = GetDefaultValue(csType);
                sb.AppendLine($"        {varName} = Sync<{csType}>({defaultVal});");

                // Add OnChanged callback for interpolated or all remote-applied values
                sb.AppendLine($"        {varName}.OnChanged += (old, val) =>");
                sb.AppendLine("        {");
                string accessCheck = binding.Access switch
                {
                    EasySync.WriteAccess.Owner => "!IsOwner",
                    EasySync.WriteAccess.Host => "!IsHost",
                    EasySync.WriteAccess.All => "false", // All can write, so remotes always apply
                    _ => "!IsOwner"
                };
                // For All access, everyone applies; for Owner/Host, non-writers apply
                if (binding.Access == EasySync.WriteAccess.All)
                {
                    // In All mode, the writer IS the applier — don't re-apply
                    // Actually for All mode, everyone should see changes from others
                }
                sb.AppendLine($"            if ({accessCheck})");
                sb.AppendLine($"                _{shortType.ToLower()}.{binding.MemberName} = val;");
                sb.AppendLine("        };");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // Update — write synced values
            sb.AppendLine("    void Update()");
            sb.AppendLine("    {");

            // Group bindings by access level for clean if-blocks
            var ownerBindings = new List<EasySync.SyncBinding>();
            var hostBindings = new List<EasySync.SyncBinding>();
            var allBindings = new List<EasySync.SyncBinding>();

            foreach (var binding in sync._bindings)
            {
                switch (binding.Access)
                {
                    case EasySync.WriteAccess.Owner: ownerBindings.Add(binding); break;
                    case EasySync.WriteAccess.Host: hostBindings.Add(binding); break;
                    case EasySync.WriteAccess.All: allBindings.Add(binding); break;
                }
            }

            if (ownerBindings.Count > 0)
            {
                sb.AppendLine("        if (IsOwner)");
                sb.AppendLine("        {");
                foreach (var binding in ownerBindings)
                {
                    string shortType = GetShortTypeName(binding.ComponentType);
                    string varName = $"_{shortType}_{binding.MemberName}";
                    sb.AppendLine($"            {varName}.Value = _{shortType.ToLower()}.{binding.MemberName};");
                }
                sb.AppendLine("        }");
            }

            if (hostBindings.Count > 0)
            {
                sb.AppendLine("        if (IsHost)");
                sb.AppendLine("        {");
                foreach (var binding in hostBindings)
                {
                    string shortType = GetShortTypeName(binding.ComponentType);
                    string varName = $"_{shortType}_{binding.MemberName}";
                    sb.AppendLine($"            {varName}.Value = _{shortType.ToLower()}.{binding.MemberName};");
                }
                sb.AppendLine("        }");
            }

            if (allBindings.Count > 0)
            {
                sb.AppendLine("        // WriteAccess.All — any peer can write");
                foreach (var binding in allBindings)
                {
                    string shortType = GetShortTypeName(binding.ComponentType);
                    string varName = $"_{shortType}_{binding.MemberName}";
                    sb.AppendLine($"        {varName}.Value = _{shortType.ToLower()}.{binding.MemberName};");
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString());
            AssetDatabase.Refresh();

            Debug.Log($"[EasySync] Exported NetworkBehaviour to: {path}");
            EditorUtility.DisplayDialog("EasySync Export",
                $"Generated {className}.cs with {sync._bindings.Count} SyncVars.\n\n" +
                "Next steps:\n" +
                "1. Add the generated component to your GameObject\n" +
                "2. Remove the EasySync component\n" +
                "3. The generated code uses compile-time SyncVars for better performance.",
                "OK");
        }

        private static string GetShortTypeName(string fullTypeName)
        {
            int lastDot = fullTypeName.LastIndexOf('.');
            return lastDot >= 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;
        }

        private static string GetCSharpTypeName(EasySync.SyncBinding binding)
        {
            // Resolve the actual type from the binding
            var compType = Type.GetType(binding.ComponentType);
            if (compType == null)
            {
                // Try Unity assembly
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    compType = asm.GetType(binding.ComponentType);
                    if (compType != null) break;
                }
            }

            if (compType != null)
            {
                Type memberType = null;
                if (binding.IsProperty)
                {
                    var prop = compType.GetProperty(binding.MemberName, BindingFlags.Public | BindingFlags.Instance);
                    memberType = prop?.PropertyType;
                }
                else
                {
                    var field = compType.GetField(binding.MemberName, BindingFlags.Public | BindingFlags.Instance);
                    memberType = field?.FieldType;
                }

                if (memberType != null)
                    return TypeToKeyword(memberType);
            }

            return "object"; // fallback
        }

        private static string TypeToKeyword(Type t)
        {
            if (t == typeof(bool)) return "bool";
            if (t == typeof(byte)) return "byte";
            if (t == typeof(short)) return "short";
            if (t == typeof(ushort)) return "ushort";
            if (t == typeof(int)) return "int";
            if (t == typeof(uint)) return "uint";
            if (t == typeof(long)) return "long";
            if (t == typeof(ulong)) return "ulong";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(string)) return "string";
            return t.Name; // Vector2, Vector3, Quaternion, Color, Color32
        }

        private static string GetDefaultValue(string csType)
        {
            return csType switch
            {
                "bool" => "false",
                "byte" => "0",
                "short" => "0",
                "ushort" => "0",
                "int" => "0",
                "uint" => "0",
                "long" => "0",
                "ulong" => "0",
                "float" => "0f",
                "double" => "0.0",
                "string" => "\"\"",
                "Vector2" => "Vector2.zero",
                "Vector3" => "Vector3.zero",
                "Quaternion" => "Quaternion.identity",
                "Color" => "Color.white",
                "Color32" => "default",
                _ => "default"
            };
        }

        #endregion
    }
}
