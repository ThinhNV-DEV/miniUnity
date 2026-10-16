// Agent commands that run the project's editor UI code: `menu` invokes a [MenuItem], `window` opens an EditorWindow and drives its
// OnGUI headless (Editor/Imgui.cs): a Layout pass, an event pass that applies every --set, one event pass per --click, then a
// Repaint pass whose controls are returned (kind, label, value, enabled), with the logs / exceptions of the run.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using UnityEngine;

namespace MiniUnity.Agent
{
    public static partial class UaCommands
    {
        static void RegisterEditorWindows()
        {
            Reg("menu", "menu \"<Menu/Path>\"   run a [MenuItem] of the project's editor code (like clicking it in Unity); logs and exceptions of the call", a =>
            {
                var path = a.P(0, "menu path");
                var asms = LoadEditorCode(a);
                var m = MenuMethod(asms, path) ?? throw new UaError($"no [MenuItem(\"{path}\")] in the project (menu items: {string.Join(", ", MenuItems(asms).Take(30))})");
                Play.Reset();
                Invoke(m, null, "menu " + path);
                var res = PlaySummary(a.I("max-logs", 60));
                res["menu"] = path; res["method"] = m.DeclaringType?.FullName + "." + m.Name;
                if (s_CompileErrors > 0) res["compileErrors"] = s_CompileErrors;
                res["windows"] = Arr(UnityEditor.EditorWindow.s_Open, w => J(w.GetType().FullName));
                return res;
            });
            Reg("window", "window <WindowType | \"Menu/Path\"> [--set \"Label=value;Other=…\"] [--click \"Button;Other\"] [--max-logs n]   open an EditorWindow (by type name, or through the [MenuItem] that opens it), run its OnGUI headless: --set edits the fields with those labels, --click presses buttons, in that order; returns the controls drawn afterwards", a =>
            {
                var what = a.P(0, "window type or menu path");
                var asms = LoadEditorCode(a);
                Play.Reset();
                UnityEditor.EditorWindow win;
                var menu = what.Contains('/') ? MenuMethod(asms, what) : null;
                if (menu != null)
                {
                    int before = UnityEditor.EditorWindow.s_Open.Count;
                    Invoke(menu, null, "menu " + what);
                    win = UnityEditor.EditorWindow.s_Open.Skip(before).LastOrDefault() ?? UnityEditor.EditorWindow.s_Open.LastOrDefault()
                          ?? throw new UaError($"menu item '{what}' opened no EditorWindow");
                }
                else
                {
                    var types = asms.SelectMany(SafeTypes).Where(t => typeof(UnityEditor.EditorWindow).IsAssignableFrom(t) && !t.IsAbstract).ToList();
                    var t = types.FirstOrDefault(x => x.FullName == what) ?? types.FirstOrDefault(x => x.Name == what)
                            ?? throw new UaError($"no EditorWindow '{what}' in the project (windows: {string.Join(", ", types.Select(x => x.FullName).Take(30))})");
                    win = UnityEditor.EditorWindow.GetWindow(t);
                }
                var onGui = FindOnGui(win.GetType()) ?? throw new UaError($"{win.GetType().FullName} has no OnGUI (UI Toolkit windows are not supported)");
                var steps = new JsonArray();
                void Pass(EventType type, string label)
                {
                    Imgui.BeginPass(type);
                    Invoke(onGui, win, $"{win.GetType().Name}.OnGUI ({label})");
                    steps.Add(JsonValue.Create(label));
                }
                Imgui.Applied.Clear(); Imgui.ClearPending();
                Pass(EventType.Layout, "layout");
                var sets = Split(a.O("set"));
                if (sets.Count > 0)
                {
                    foreach (var s in sets)
                    {
                        int eq = s.IndexOf('=');
                        if (eq <= 0) throw new UaError($"--set: expected Label=value, got '{s}'");
                        Imgui.Set(s[..eq], s[(eq + 1)..]);
                    }
                    Pass(EventType.Used, "set");
                    var missed = Imgui.Unmatched().ToList();
                    Imgui.ClearPending();
                    if (missed.Count > 0) throw new UaError($"no enabled control matched: {string.Join(", ", missed)} (controls: {string.Join(", ", Imgui.Controls.Select(c => $"{c.Kind}:'{c.Label}'"))})");
                }
                foreach (var c in Split(a.O("click")))
                {
                    Imgui.Click(c);
                    Pass(EventType.MouseUp, "click " + c);
                    var missed = Imgui.Unmatched().ToList();
                    Imgui.ClearPending();
                    if (missed.Count > 0) throw new UaError($"no enabled button '{c}' (buttons: {string.Join(", ", Imgui.Controls.Where(x => x.Kind.EndsWith("Button")).Select(x => $"'{x.Label}'{(x.Enabled ? "" : " (disabled)")}"))})");
                }
                Pass(EventType.Repaint, "repaint");
                var res = PlaySummary(a.I("max-logs", 60));
                res["window"] = win.GetType().FullName; res["title"] = win.titleContent?.text;
                if (s_CompileErrors > 0) res["compileErrors"] = s_CompileErrors;
                res["passes"] = steps; res["applied"] = Arr(Imgui.Applied, J);
                res["controls"] = Arr(Imgui.Controls, c =>
                {
                    var o = new JsonObject { ["kind"] = c.Kind, ["label"] = c.Label };
                    if (c.Value != null) o["value"] = c.Value;
                    if (!c.Enabled) o["enabled"] = false;
                    if (c.Indent > 0) o["indent"] = c.Indent;
                    if (c.Options != null) o["options"] = Arr(c.Options, J);
                    return o;
                });
                foreach (var w in UnityEditor.EditorWindow.s_Open.ToList()) w.Close();
                return res;
            });
        }

        static List<Assembly> LoadEditorCode(UaArgs a)
        {
            // assemblies that failed to compile are left out (their dependents too); the result says how many errors there were
            var (m, build, asms) = RuntimeHost.CompileAndLoad(a, x => true);
            s_CompileErrors = build.Errors;
            ScriptRegistry.ExtraScriptDirs.AddRange(m.PackageDirs.Values);
            ScriptRegistry.Reset();
            Play.CaptureLogs = true;
            return asms;
        }

        static int s_CompileErrors;

        static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        static IEnumerable<(MethodInfo m, UnityEditor.MenuItem attr)> MenuEntries(IEnumerable<Assembly> asms) =>
            asms.SelectMany(SafeTypes).SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .SelectMany(m => { try { return m.GetCustomAttributes<UnityEditor.MenuItem>(false).Select(at => (m, at)).ToList(); } catch (Exception) { return new List<(MethodInfo, UnityEditor.MenuItem)>(); } });

        static IEnumerable<string> MenuItems(IEnumerable<Assembly> asms) => MenuEntries(asms).Where(e => !e.attr.validate).Select(e => e.attr.menuItem).Distinct();

        /// <summary>The menu item's method (its validate function, if any, must return true; shortcuts after the last space are ignored).</summary>
        static MethodInfo MenuMethod(IEnumerable<Assembly> asms, string path)
        {
            static string Clean(string p) { var i = p.LastIndexOf(' '); return i > 0 && p[(i + 1)..].IndexOfAny(new[] { '%', '#', '&', '_' }) == 0 ? p[..i] : p; }
            var entries = MenuEntries(asms).ToList();
            var hit = entries.FirstOrDefault(e => !e.attr.validate && (e.attr.menuItem == path || Clean(e.attr.menuItem) == path));
            if (hit.m == null) return null;
            var validate = entries.FirstOrDefault(e => e.attr.validate && Clean(e.attr.menuItem) == Clean(hit.attr.menuItem));
            if (validate.m != null && validate.m.GetParameters().Length == 0 && validate.m.Invoke(null, null) is false)
                throw new UaError($"menu item '{path}' is disabled (its validate function returned false)");
            return hit.m;
        }

        static MethodInfo FindOnGui(Type t)
        {
            for (var c = t; c != null && c != typeof(UnityEditor.EditorWindow); c = c.BaseType)
            {
                var m = c.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m != null) return m;
            }
            return null;
        }

        static void Invoke(MethodInfo m, object target, string where)
        {
            try { m.Invoke(target, m.GetParameters().Length == 0 ? null : new object[m.GetParameters().Length]); }
            catch (TargetInvocationException e) { Play.Report(e.InnerException ?? e, where); }
        }

        static List<string> Split(string s) => (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
    }
}
