// Headless IMGUI: OnGUI code (EditorWindow, custom inspectors, runtime OnGUI) runs without a screen. Every control call is recorded
// (kind, label, value, enabled) and returns its value unchanged, except where a pending edit says otherwise: `ua window` runs a
// Layout pass, then one event pass per --set / --click (the control whose label matches returns the new value / true and sets
// GUI.changed, like a user edit), then a Repaint pass whose controls are reported. Layout calls (Begin/End, Space, options, rects)
// do nothing; drawing calls are recorded as labels. The control overloads are generated (ImguiControls.cs, tools/imgui_gen.py).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    public static class Imgui
    {
        public sealed class Control
        {
            public string Kind, Label, Value; public bool Enabled; public int Indent; public string[] Options;
        }
        public static readonly List<Control> Controls = new();
        static readonly List<(string label, string value)> pendingSets = new();
        static string pendingClick;
        public static bool Changed;
        static readonly Stack<bool> disabled = new();
        static readonly Stack<bool> changeChecks = new();
        static float y;
        public static readonly List<string> Applied = new();

        public static void BeginPass(EventType type)
        {
            Controls.Clear(); disabled.Clear(); changeChecks.Clear(); y = 0; Changed = false;
            Event.current = new Event { type = type };
        }

        /// <summary>The next pass applies `label=value` to the matching control (by label, else by kind) / clicks the matching button.</summary>
        public static void Set(string label, string value) => pendingSets.Add((label, value));
        public static void Click(string label) => pendingClick = label;
        public static void ClearPending() { pendingSets.Clear(); pendingClick = null; }
        public static IEnumerable<string> Unmatched() => pendingSets.Select(p => "set " + p.label).Concat(pendingClick != null ? new[] { "click " + pendingClick } : Array.Empty<string>());

        static bool Enabled => GUI.enabled;
        static bool Match(string want, string kind, string label) =>
            string.Equals(want.Trim(), (label ?? "").Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(want, kind, StringComparison.OrdinalIgnoreCase)
            || (label ?? "").Length > 0 && want.Trim().TrimEnd(':').Equals(label.Trim().TrimEnd(':'), StringComparison.OrdinalIgnoreCase);

        static Control Record(string kind, string label, string value, string[] options = null)
        {
            var c = new Control { Kind = kind, Label = label ?? "", Value = value, Enabled = Enabled, Indent = UnityEditor.EditorGUI.indentLevel, Options = options };
            Controls.Add(c);
            y += 20;
            return c;
        }

        static bool Take(string kind, string label, out string value)
        {
            value = null;
            if (!Enabled) return false;
            for (int i = 0; i < pendingSets.Count; i++)
                if (Match(pendingSets[i].label, kind, label)) { value = pendingSets[i].value; Applied.Add($"set {pendingSets[i].label}={value}"); pendingSets.RemoveAt(i); return true; }
            return false;
        }

        static void Edited()
        {
            Changed = true; GUI.changed = true;
            if (changeChecks.Count > 0) { changeChecks.Pop(); changeChecks.Push(true); }
        }

        public static string Text(GUIContent c) => c?.text ?? "";
        public static string[] Texts(string[] s) => s ?? Array.Empty<string>();
        public static string[] Texts(GUIContent[] s) => s?.Select(Text).ToArray() ?? Array.Empty<string>();
        public static string[] Texts(Texture[] s) => s?.Select(t => t != null ? t.name : "").ToArray() ?? Array.Empty<string>();
        public static Rect NextRect() => new Rect(0, y, Math.Max(100, UnityEditor.EditorGUIUtility.currentViewWidth), 18);
        public static GUILayoutOption Option() => new GUILayoutOption(GUILayoutOption.Type.fixedWidth, 0f);

        public static string Show(object v) => v switch
        {
            null => "null",
            Object o => o == null ? "null" : $"{o.name} ({o.GetType().Name})",
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            IFormattable fm => fm.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString(),
        };

        public static bool Button(string kind, string label)
        {
            Record(kind, label, null);
            if (pendingClick != null && Enabled && Match(pendingClick, kind, label)) { Applied.Add("click " + pendingClick); pendingClick = null; Edited(); return true; }
            return false;
        }

        public static void Label(string kind, string label, string text) => Record(kind, label, text);

        public static T Field<T>(string kind, string label, T value)
        {
            var c = Record(kind, label, Show(value));
            if (!Take(kind, label, out var s)) return value;
            object nv = Parse(s, value != null ? value.GetType() : typeof(T));
            if (nv is T t)
            {
                if (!Equals(t, value)) Edited();
                c.Value = Show(t);
                return t;
            }
            throw new ArgumentException($"{kind} '{label}': cannot use '{s}' as {typeof(T).Name}");
        }

        public static float Slider(string kind, string label, float value, float lo, float hi) => Mathf.Clamp(Field(kind, label, value), Math.Min(lo, hi), Math.Max(lo, hi));
        public static int Slider(string kind, string label, int value, int lo, int hi) => Math.Clamp(Field(kind, label, value), Math.Min(lo, hi), Math.Max(lo, hi));

        public static int Popup(string kind, string label, int index, string[] options, int[] values)
        {
            var c = Record(kind, label, values == null ? (index >= 0 && index < options.Length ? options[index] : index.ToString()) : Show(index), options);
            if (!Take(kind, label, out var s)) return index;
            int pick = Array.FindIndex(options, o => string.Equals(o, s, StringComparison.OrdinalIgnoreCase));
            if (pick < 0 && !int.TryParse(s, out pick)) throw new ArgumentException($"{kind} '{label}': no option '{s}' (options: {string.Join(", ", options)})");
            int result = values != null ? (pick >= 0 && pick < values.Length && options.Any(o => string.Equals(o, s, StringComparison.OrdinalIgnoreCase)) ? values[pick] : pick) : pick;
            if (result != index) Edited();
            c.Value = s;
            return result;
        }

        public static Object ObjectField(string kind, string label, Object obj, Type type)
        {
            var c = Record(kind, label, Show(obj));
            if (!Take(kind, label, out var s)) return obj;
            Object nv = s is "" or "null" or "None" ? null : UnityEditor.AssetDatabase.LoadAssetAtPath(s, type ?? typeof(Object));
            if (nv == null && s is not ("" or "null" or "None")) throw new ArgumentException($"{kind} '{label}': no {type?.Name ?? "asset"} at '{s}'");
            if (nv != obj) Edited();
            c.Value = Show(nv);
            return nv;
        }

        public static void Property(string kind, string label, UnityEditor.SerializedProperty p)
        {
            string name = label.Length > 0 ? label : p?.displayName ?? "";
            var c = Record(kind, name, p == null ? "null" : PropValue(p));
            if (p == null || !Take(kind, name, out var s) && !Take(kind, p.name, out s) && !Take(kind, p.propertyPath, out s)) return;
            SetProp(p, s);
            Edited();
            c.Value = PropValue(p);
        }

        static string PropValue(UnityEditor.SerializedProperty p) => p.propertyType switch
        {
            UnityEditor.SerializedPropertyType.Integer => p.intValue.ToString(CultureInfo.InvariantCulture),
            UnityEditor.SerializedPropertyType.Boolean => p.boolValue ? "true" : "false",
            UnityEditor.SerializedPropertyType.Float => p.floatValue.ToString("R", CultureInfo.InvariantCulture),
            UnityEditor.SerializedPropertyType.String => p.stringValue,
            UnityEditor.SerializedPropertyType.ObjectReference => Show(p.objectReferenceValue),
            UnityEditor.SerializedPropertyType.Enum => p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length ? p.enumDisplayNames[p.enumValueIndex] : p.enumValueIndex.ToString(),
            _ => p.propertyType.ToString(),
        };

        static void SetProp(UnityEditor.SerializedProperty p, string s)
        {
            switch (p.propertyType)
            {
                case UnityEditor.SerializedPropertyType.Integer: p.intValue = int.Parse(s, CultureInfo.InvariantCulture); break;
                case UnityEditor.SerializedPropertyType.Boolean: p.boolValue = ParseBool(s); break;
                case UnityEditor.SerializedPropertyType.Float: p.floatValue = float.Parse(s, CultureInfo.InvariantCulture); break;
                case UnityEditor.SerializedPropertyType.String: p.stringValue = s; break;
                case UnityEditor.SerializedPropertyType.ObjectReference: p.objectReferenceValue = s is "" or "null" ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(s); break;
                case UnityEditor.SerializedPropertyType.Enum:
                {
                    int i = Array.FindIndex(p.enumDisplayNames, n => string.Equals(n, s, StringComparison.OrdinalIgnoreCase));
                    p.enumValueIndex = i >= 0 ? i : int.Parse(s, CultureInfo.InvariantCulture);
                    break;
                }
                default: throw new ArgumentException($"PropertyField '{p.propertyPath}': editing {p.propertyType} is not supported");
            }
        }

        static bool ParseBool(string s) => s.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";

        static object Parse(string s, Type t)
        {
            var ci = CultureInfo.InvariantCulture;
            if (t == typeof(string)) return s;
            if (t == typeof(int)) return int.Parse(s, ci);
            if (t == typeof(long)) return long.Parse(s, ci);
            if (t == typeof(float)) return float.Parse(s, ci);
            if (t == typeof(double)) return double.Parse(s, ci);
            if (t == typeof(bool)) return ParseBool(s);
            if (t.IsEnum) return Enum.Parse(t, s.Replace(" ", ""), true);
            float[] F() => s.Trim('(', ')', ' ').Split(',').Select(x => float.Parse(x.Trim(), ci)).ToArray();
            if (t == typeof(Vector2)) { var f = F(); return new Vector2(f[0], f[1]); }
            if (t == typeof(Vector3)) { var f = F(); return new Vector3(f[0], f[1], f[2]); }
            if (t == typeof(Vector4)) { var f = F(); return new Vector4(f[0], f[1], f[2], f[3]); }
            if (t == typeof(Vector2Int)) { var f = F(); return new Vector2Int((int)f[0], (int)f[1]); }
            if (t == typeof(Vector3Int)) { var f = F(); return new Vector3Int((int)f[0], (int)f[1], (int)f[2]); }
            if (t == typeof(Color)) { if (ColorUtility.TryParseHtmlString(s, out var c)) return c; var f = F(); return new Color(f[0], f[1], f[2], f.Length > 3 ? f[3] : 1); }
            if (t == typeof(Rect)) { var f = F(); return new Rect(f[0], f[1], f[2], f[3]); }
            return Convert.ChangeType(s, t, ci);
        }

        public static void BeginChangeCheck() => changeChecks.Push(false);
        public static bool EndChangeCheck()
        {
            bool c = changeChecks.Count > 0 && changeChecks.Pop();
            if (c && changeChecks.Count > 0) { changeChecks.Pop(); changeChecks.Push(true); }   // a nested change is a change of the outer block too
            return c;
        }
        public static void BeginDisabled(bool d) { disabled.Push(GUI.enabled); GUI.enabled = GUI.enabled && !d; }
        public static void EndDisabled() { if (disabled.Count > 0) GUI.enabled = disabled.Pop(); }

        public static Rect GetRect() => NextRect();
    }
}
