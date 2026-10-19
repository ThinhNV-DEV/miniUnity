using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MiniUnity;

namespace UnityEngine
{
    internal static class MathfInternal
    {
        public static volatile float FloatMinNormal = 1.17549435E-38f;
        public static volatile float FloatMinDenormal = float.Epsilon;
        public static bool IsFlushToZeroEnabled = FloatMinDenormal == 0;
    }

    internal static class DrivenPropertyManagerInternal
    {
        public static bool IsDriven(Object target, string propertyPath) => false;
    }

    public sealed partial class Debug
    {
        public static int WarningCount, ErrorCount;
        public static readonly List<string> Messages = new();
        static void Emit(string level, object message, Object context = null)
        {
            var s = $"[{level}] {message}";
            Messages.Add(s);
            string stack = message is Exception ex ? ex.StackTrace ?? "" : (MiniUnity.Play.IsPlaying ? new System.Diagnostics.StackTrace(2, true).ToString() : "");
            MiniUnity.Play.OnLog(level, message is Exception ex2 ? $"{ex2.GetType().Name}: {ex2.Message}" : message?.ToString(), stack);
            if (level == "Warning") WarningCount++;
            if (level == "Error" || level == "Exception") ErrorCount++;
            if (level != "Log" || MiniUnity.Lifecycle.Trace || Environment.GetEnvironmentVariable("MINIUNITY_LOG") == "1" || EchoedLog(message?.ToString()))
                Console.Error.WriteLine(s);
        }
        /// <summary>Debug.Log messages starting with one of these prefixes are echoed to stderr (warnings and errors always are);
        /// a host adds its own, e.g. "[VideoRecreate]".</summary>
        public static readonly List<string> EchoPrefixes = new() { "[MiniUnity]" };
        static bool EchoedLog(string m) { if (m == null) return false; foreach (var p in EchoPrefixes) if (m.StartsWith(p, StringComparison.Ordinal)) return true; return false; }
        public static void Log(object message) => Emit("Log", message);
        public static void Log(object message, Object context) => Emit("Log", message, context);
        public static void LogFormat(string format, params object[] args) => Emit("Log", string.Format(format, args));
        public static void LogFormat(Object context, string format, params object[] args) => Emit("Log", string.Format(format, args));
        public static void LogWarning(object message) => Emit("Warning", message);
        public static void LogWarning(object message, Object context) => Emit("Warning", message, context);
        public static void LogWarningFormat(string format, params object[] args) => Emit("Warning", string.Format(format, args));
        public static void LogWarningFormat(Object context, string format, params object[] args) => Emit("Warning", string.Format(format, args));
        public static void LogError(object message) => Emit("Error", message);
        public static void LogError(object message, Object context) => Emit("Error", message, context);
        public static void LogErrorFormat(string format, params object[] args) => Emit("Error", string.Format(format, args));
        public static void LogErrorFormat(Object context, string format, params object[] args) => Emit("Error", string.Format(format, args));
        public static void LogException(Exception exception) => Emit("Exception", exception);
        public static void LogException(Exception exception, Object context) => Emit("Exception", exception);
        public static void LogAssertion(object message) => Emit("Error", message);
        public static void Assert(bool condition) { if (!condition) Emit("Error", "Assertion failed"); }
        public static void Assert(bool condition, string message) { if (!condition) Emit("Error", "Assertion failed: " + message); }
        public static void Assert(bool condition, object message) { if (!condition) Emit("Error", "Assertion failed: " + message); }
        public static void Assert(bool condition, Object context) { if (!condition) Emit("Error", "Assertion failed"); }
        public static void Assert(bool condition, object message, Object context) { if (!condition) Emit("Error", "Assertion failed: " + message); }
        public static void DrawLine(Vector3 start, Vector3 end) { }
        public static void DrawLine(Vector3 start, Vector3 end, Color color) { }
        public static void DrawLine(Vector3 start, Vector3 end, Color color, float duration) { }
        public static void DrawRay(Vector3 start, Vector3 dir) { }
        public static void DrawRay(Vector3 start, Vector3 dir, Color color) { }
        public static void Break() { }
        public static void ClearDeveloperConsole() { }
        public static bool isDebugBuild => true;
        public static bool developerConsoleVisible { get => false; set { } }
        public static ILogger unityLogger => null;
    }

    public partial class Application
    {
        public static bool isPlaying => MiniUnity.Play.IsPlaying;
        public static bool isEditor => true;
        public static bool isBatchMode => true;
        public static bool isFocused => MiniUnity.Play.IsPlaying;   // the (virtual) Game view has focus while playing
        public static string dataPath => Path.Combine(AssetIndex.Root, "Assets").Replace('\\', '/');
        public static string persistentDataPath => Path.Combine(AssetIndex.Root, "Library", "persistent").Replace('\\', '/');
        public static string temporaryCachePath => Path.Combine(AssetIndex.Root, "Temp").Replace('\\', '/');
        public static string streamingAssetsPath => dataPath + "/StreamingAssets";
        public static RuntimePlatform platform => RuntimePlatform.LinuxEditor;
        public static string unityVersion => "2022.3.62f2";
        public static string version => "1.0";
        public static string productName => "code-bits-video";
        public static string companyName => "Percas";
        public static string identifier => "com.percas.codebits";
        public static int targetFrameRate { get => -1; set { } }
        public static bool runInBackground { get => true; set { } }
        public static SystemLanguage systemLanguage => SystemLanguage.English;
        public static bool isMobilePlatform => false;
        public static bool isConsolePlatform => false;
        public static NetworkReachability internetReachability => NetworkReachability.NotReachable;
        public static event Application.LogCallback logMessageReceived { add { MiniUnity.Play.LogHandlers += value; } remove { MiniUnity.Play.LogHandlers -= value; } }
        public static event Application.LogCallback logMessageReceivedThreaded { add { MiniUnity.Play.LogHandlers += value; } remove { MiniUnity.Play.LogHandlers -= value; } }
        public static event Action quitting { add { } remove { } }
        public static event Action<bool> focusChanged { add { } remove { } }
        public static event Func<bool> wantsToQuit { add { } remove { } }
        public static event Action onBeforeRender { add { } remove { } }
        public static void OpenURL(string url) { }
        public static void Quit() { }
        public static void Quit(int exitCode) { }
        public static bool IsPlaying(Object obj) => MiniUnity.Play.IsPlaying && obj != null && obj.m_Asset == null;
    }

    public partial class Time
    {
        // edit mode: a fixed 30 fps "frame" (builders); play mode: the simulated player loop (MiniUnity.Play)
        static bool P => MiniUnity.Play.IsPlaying;
        public static float time => P ? (float)MiniUnity.Play.Time : 0;
        public static double timeAsDouble => P ? MiniUnity.Play.Time : 0;
        public static float deltaTime => P ? (MiniUnity.Play.InFixed ? MiniUnity.Play.FixedDeltaTime : MiniUnity.Play.DeltaTime) : 1f / 30f;
        public static float unscaledDeltaTime => P ? MiniUnity.Play.UnscaledDeltaTime : 1f / 30f;
        public static float unscaledTime => P ? (float)MiniUnity.Play.UnscaledTime : 0;
        public static double unscaledTimeAsDouble => P ? MiniUnity.Play.UnscaledTime : 0;
        public static float realtimeSinceStartup => P ? (float)MiniUnity.Play.UnscaledTime : (float)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds;
        public static double realtimeSinceStartupAsDouble => realtimeSinceStartup;
        public static float timeScale { get => MiniUnity.Play.TimeScale; set => MiniUnity.Play.TimeScale = Mathf.Max(0, value); }
        public static float fixedDeltaTime { get => MiniUnity.Play.FixedDeltaTime; set => MiniUnity.Play.FixedDeltaTime = Mathf.Max(0.0001f, value); }
        public static float fixedUnscaledDeltaTime => MiniUnity.Play.FixedDeltaTime;
        public static float maximumDeltaTime { get => MiniUnity.Play.MaximumDeltaTime; set => MiniUnity.Play.MaximumDeltaTime = value; }
        public static int frameCount => P ? MiniUnity.Play.Frame : 0;
        public static int captureFramerate { get => 0; set { } }
        public static float smoothDeltaTime => deltaTime;
        public static float fixedTime => P ? (float)MiniUnity.Play.FixedTime : 0;
        public static double fixedTimeAsDouble => P ? MiniUnity.Play.FixedTime : 0;
        public static bool inFixedTimeStep => MiniUnity.Play.InFixed;
        public static float timeSinceLevelLoad => time;
        public static double timeSinceLevelLoadAsDouble => timeAsDouble;
        public static int renderedFrameCount => frameCount;
        public static float fixedUnscaledTime => fixedTime;
        public static double fixedUnscaledTimeAsDouble => fixedTimeAsDouble;
        public static float captureDeltaTime { get => 0; set { } }
        public static float maximumParticleDeltaTime { get => 0.03f; set { } }
    }

    public sealed partial class Resources
    {
        public static T Load<T>(string path) where T : Object => (T)Load(path, typeof(T));
        public static Object Load(string path) => Load(path, typeof(Object));
        public static Object Load(string path, Type systemTypeInstance)
        {
            foreach (var p in AssetIndex.AllPaths())
            {
                int i = p.IndexOf("/Resources/", StringComparison.Ordinal);
                if (i < 0) continue;
                var rel = p.Substring(i + "/Resources/".Length);
                var noExt = Path.ChangeExtension(rel, null)?.Replace('\\', '/');
                if (!string.Equals(noExt, path, StringComparison.Ordinal)) continue;
                if (Directory.Exists(AssetIndex.Abs(p))) continue;
                var o = AssetLoader.LoadAssetAtPath(p, systemTypeInstance);
                if (o != null) return o;
            }
            return null;
        }
        public static T[] LoadAll<T>(string path) where T : Object => LoadAll(path, typeof(T)).Cast<T>().ToArray();
        public static Object[] LoadAll(string path) => LoadAll(path, typeof(Object));
        public static Object[] LoadAll(string path, Type systemTypeInstance)
        {
            var res = new List<Object>();
            foreach (var p in AssetIndex.AllPaths())
            {
                int i = p.IndexOf("/Resources/", StringComparison.Ordinal);
                if (i < 0) continue;
                var rel = p.Substring(i + "/Resources/".Length);
                if (!(rel.StartsWith(path.TrimEnd('/') + "/") || path.Length == 0)) continue;
                if (Directory.Exists(AssetIndex.Abs(p))) continue;
                var o = AssetLoader.LoadAssetAtPath(p, systemTypeInstance);
                if (o != null) res.Add(o);
            }
            return res.ToArray();
        }
        public static T GetBuiltinResource<T>(string path) where T : Object => (T)GetBuiltinResource(typeof(T), path);
        public static Object GetBuiltinResource(Type type, string path) => type == typeof(Font) ? MiniUnity.LegacyFontFace.Builtin : null;
        public static void UnloadAsset(Object assetToUnload) { }
        public static AsyncOperation UnloadUnusedAssets() => null;
        public static T[] FindObjectsOfTypeAll<T>() where T : Object => ObjectRegistry.All(typeof(T)).Cast<T>().ToArray();
        public static Object[] FindObjectsOfTypeAll(Type type) => ObjectRegistry.All(type).ToArray();
    }

    public partial class ColorUtility
    {
        public static bool TryParseHtmlString(string htmlString, out Color color)
        {
            color = new Color(0, 0, 0, 1);
            if (string.IsNullOrEmpty(htmlString)) return false;
            var s = htmlString.Trim();
            if (s.StartsWith("#"))
            {
                var h = s.Substring(1);
                if (h.Length == 3 || h.Length == 4) h = string.Concat(h.Select(c => new string(c, 2)));
                if (h.Length != 6 && h.Length != 8) return false;
                if (!uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
                byte r, g, b, a = 255;
                if (h.Length == 6) { r = (byte)(v >> 16); g = (byte)(v >> 8); b = (byte)v; }
                else { r = (byte)(v >> 24); g = (byte)(v >> 16); b = (byte)(v >> 8); a = (byte)v; }
                color = new Color32(r, g, b, a);
                return true;
            }
            var named = new Dictionary<string, Color32>(StringComparer.OrdinalIgnoreCase)
            {
                ["red"] = new(255, 0, 0, 255), ["cyan"] = new(0, 255, 255, 255), ["blue"] = new(0, 0, 255, 255), ["darkblue"] = new(0, 0, 160, 255),
                ["lightblue"] = new(173, 216, 230, 255), ["purple"] = new(128, 0, 128, 255), ["yellow"] = new(255, 255, 0, 255), ["lime"] = new(0, 255, 0, 255),
                ["fuchsia"] = new(255, 0, 255, 255), ["white"] = new(255, 255, 255, 255), ["silver"] = new(192, 192, 192, 255), ["grey"] = new(128, 128, 128, 255),
                ["black"] = new(0, 0, 0, 255), ["orange"] = new(255, 165, 0, 255), ["brown"] = new(165, 42, 42, 255), ["maroon"] = new(128, 0, 0, 255),
                ["green"] = new(0, 128, 0, 255), ["olive"] = new(128, 128, 0, 255), ["navy"] = new(0, 0, 128, 255), ["teal"] = new(0, 128, 128, 255),
                ["aqua"] = new(0, 255, 255, 255), ["magenta"] = new(255, 0, 255, 255),
            };
            if (named.TryGetValue(s, out var c32)) { color = c32; return true; }
            return false;
        }
        public static string ToHtmlStringRGB(Color color) { Color32 c = color; return $"{c.r:X2}{c.g:X2}{c.b:X2}"; }
        public static string ToHtmlStringRGBA(Color color) { Color32 c = color; return $"{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}"; }
    }

    public partial class JsonUtility
    {
        public static string ToJson(object obj) => ToJson(obj, false);
        public static string ToJson(object obj, bool prettyPrint)
        {
            if (obj == null) return "";
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = prettyPrint, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                WriteObj(w, obj, obj.GetType());
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        static void WriteObj(Utf8JsonWriter w, object o, Type t)
        {
            w.WriteStartObject();
            foreach (var f in Serializer.SerializedFields(t)) { w.WritePropertyName(f.Name); WriteVal(w, f.GetValue(o), f.FieldType); }
            w.WriteEndObject();
        }
        static void WriteVal(Utf8JsonWriter w, object v, Type t)
        {
            if (t == typeof(string)) { w.WriteStringValue((string)v ?? ""); return; }
            if (t == typeof(bool)) { w.WriteBooleanValue((bool)v); return; }
            if (t.IsEnum) { w.WriteNumberValue(Convert.ToInt64(v)); return; }
            if (t == typeof(float)) { w.WriteNumberValue((float)v); return; }
            if (t == typeof(double)) { w.WriteNumberValue((double)v); return; }
            if (t.IsPrimitive) { w.WriteNumberValue(Convert.ToDecimal(v)); return; }
            if (typeof(Object).IsAssignableFrom(t)) { w.WriteStartObject(); w.WriteNumber("instanceID", (v as Object)?.GetInstanceID() ?? 0); w.WriteEndObject(); return; }
            if (t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)))
            {
                var et = t.IsArray ? t.GetElementType() : t.GetGenericArguments()[0];
                w.WriteStartArray();
                if (v is System.Collections.IList l) foreach (var it in l) WriteVal(w, it, et);
                w.WriteEndArray();
                return;
            }
            if (t == typeof(Vector2)) { var p = (Vector2)v; w.WriteStartObject(); w.WriteNumber("x", p.x); w.WriteNumber("y", p.y); w.WriteEndObject(); return; }
            if (t == typeof(Vector3)) { var p = (Vector3)v; w.WriteStartObject(); w.WriteNumber("x", p.x); w.WriteNumber("y", p.y); w.WriteNumber("z", p.z); w.WriteEndObject(); return; }
            if (t == typeof(Color)) { var c = (Color)v; w.WriteStartObject(); w.WriteNumber("r", c.r); w.WriteNumber("g", c.g); w.WriteNumber("b", c.b); w.WriteNumber("a", c.a); w.WriteEndObject(); return; }
            WriteObj(w, v ?? Activator.CreateInstance(t, true), t);
        }
        public static T FromJson<T>(string json) => (T)FromJson(json, typeof(T));
        public static object FromJson(string json, Type type)
        {
            var o = Activator.CreateInstance(type, true);
            FromJsonOverwrite(json, o);
            return o;
        }
        public static void FromJsonOverwrite(string json, object objectToOverwrite)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            JsonDocument doc;
            // Unity reports malformed JSON as ArgumentException ("JSON parse error: …"), not as a parser-specific exception type
            try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true }); }
            catch (JsonException e) { throw new ArgumentException("JSON parse error: " + e.Message, e); }
            using var _ = doc;
            ReadObj(doc.RootElement, objectToOverwrite, objectToOverwrite.GetType());
        }
        static void ReadObj(JsonElement e, object o, Type t)
        {
            if (e.ValueKind != JsonValueKind.Object) return;
            foreach (var f in Serializer.SerializedFields(t))
                if (e.TryGetProperty(f.Name, out var v)) f.SetValue(o, ReadVal(v, f.FieldType, f.GetValue(o)));
        }
        static object ReadVal(JsonElement v, Type t, object cur)
        {
            if (t == typeof(string)) return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
            if (t == typeof(bool)) return v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0);
            if (t.IsEnum) return Enum.ToObject(t, v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0);
            if (t == typeof(float)) return (float)v.GetDouble();
            if (t == typeof(double)) return v.GetDouble();
            if (t.IsPrimitive) return Convert.ChangeType(v.GetDouble(), t, CultureInfo.InvariantCulture);
            if (typeof(Object).IsAssignableFrom(t)) return null;
            if (t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)))
            {
                var et = t.IsArray ? t.GetElementType() : t.GetGenericArguments()[0];
                var items = v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => ReadVal(x, et, null)).ToList() : new List<object>();
                if (t.IsArray) { var a = Array.CreateInstance(et, items.Count); for (int i = 0; i < items.Count; i++) a.SetValue(items[i], i); return a; }
                var l = (System.Collections.IList)Activator.CreateInstance(t); foreach (var it in items) l.Add(it); return l;
            }
            if (t == typeof(Vector2)) return new Vector2(G(v, "x"), G(v, "y"));
            if (t == typeof(Vector3)) return new Vector3(G(v, "x"), G(v, "y"), G(v, "z"));
            if (t == typeof(Color)) return new Color(G(v, "r"), G(v, "g"), G(v, "b"), G(v, "a"));
            var o = cur ?? Activator.CreateInstance(t, true);
            ReadObj(v, o, t);
            return o;
        }
        static float G(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? (float)p.GetDouble() : 0;
    }

    public sealed partial class QualitySettings
    {
        /// <summary>PlayerSettings.colorSpace of the project (ProjectSettings.asset m_ActiveColorSpace: 0 gamma, 1 linear).</summary>
        public static ColorSpace activeColorSpace => s_ColorSpace ??= ReadColorSpace();
        public static ColorSpace desiredColorSpace => activeColorSpace;
        static ColorSpace? s_ColorSpace;
        internal static void ResetColorSpace() => s_ColorSpace = null;
        static ColorSpace ReadColorSpace()
        {
            try
            {
                var f = System.IO.Path.Combine(MiniUnity.AssetIndex.Root ?? "", "ProjectSettings", "ProjectSettings.asset");
                if (MiniUnity.AssetIndex.Root != null && System.IO.File.Exists(f))
                    foreach (var line in System.IO.File.ReadLines(f))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("m_ActiveColorSpace:", StringComparison.Ordinal)) return t.EndsWith("1") ? ColorSpace.Linear : ColorSpace.Gamma;
                    }
            }
            catch (System.IO.IOException) { }
            return ColorSpace.Gamma;
        }
        public static int vSyncCount { get => 0; set { } }
        /// <summary>MSAA sample count of the active quality level (0 = off); a camera rendering through an MSAA intermediate uses it.</summary>
        public static int antiAliasing
        {
            get { if (s_AA is int v) return v; MiniUnity.WorldRenderer.ShadowQuality.Load(); return MiniUnity.WorldRenderer.ShadowQuality.AntiAliasing; }
            set => s_AA = value <= 1 ? 0 : value >= 8 ? 8 : value >= 4 ? 4 : 2;
        }
        static int? s_AA;
    }

    public sealed partial class Screen
    {
        public static int width => (int)MiniUnity.Screen.Width;
        public static int height => (int)MiniUnity.Screen.Height;
        public static float dpi => 96;
        public static bool fullScreen { get => false; set { } }
    }
}

namespace MiniUnity
{
    public static class Layers
    {
        static readonly string[] builtin = { "Default", "TransparentFX", "Ignore Raycast", "", "Water", "UI" };
        public static string LayerToName(int layer) => layer >= 0 && layer < builtin.Length ? builtin[layer] : "";
        public static int NameToLayer(string name) => Array.IndexOf(builtin, name);
    }
}
