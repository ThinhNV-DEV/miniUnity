// The IMGUI types around the controls (Imgui.cs): GUI / EditorGUI state, GUIContent, Event, GUILayoutUtility rects, the GUI.Scope
// family (DisabledScope / ChangeCheckScope / IndentLevelScope do what they say; layout scopes do nothing), EditorGUIUtility sizes
// and EditorWindow (GetWindow / CreateWindow / Show… / Close / Repaint; `ua window` drives OnGUI).
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;
using UnityEngine;

namespace UnityEngine
{
    public partial class GUI
    {
        static bool s_Enabled = true, s_Changed;
        static Color s_Color = Color.white, s_Bg = Color.white, s_Content = Color.white;
        public static bool enabled { get => s_Enabled; set => s_Enabled = value; }
        public static bool changed { get => s_Changed; set => s_Changed = value; }
        public static Color color { get => s_Color; set => s_Color = value; }
        public static Color backgroundColor { get => s_Bg; set => s_Bg = value; }
        public static Color contentColor { get => s_Content; set => s_Content = value; }
        public static int depth { get; set; }
        public static string tooltip { get; set; } = "";

        public abstract partial class Scope
        {
            bool m_Disposed;
            protected Scope() { }
            protected abstract void CloseScope();
            internal virtual void Dispose(bool disposing) { if (m_Disposed) return; m_Disposed = true; if (disposing) CloseScope(); }
            public void Dispose() => Dispose(true);
        }

        public partial class GroupScope
        {
            public GroupScope(Rect position) { }
            public GroupScope(Rect position, string text) { }
            public GroupScope(Rect position, Texture image) { }
            public GroupScope(Rect position, GUIContent content) { }
            public GroupScope(Rect position, GUIStyle style) { }
            public GroupScope(Rect position, string text, GUIStyle style) { }
            public GroupScope(Rect position, Texture image, GUIStyle style) { }
            protected override void CloseScope() { }
        }

        public partial class ScrollViewScope
        {
            public ScrollViewScope(Rect position, Vector2 scrollPosition, Rect viewRect) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Rect position, Vector2 scrollPosition, Rect viewRect, bool alwaysShowHorizontal, bool alwaysShowVertical) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Rect position, Vector2 scrollPosition, Rect viewRect, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Rect position, Vector2 scrollPosition, Rect viewRect, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar) { this.scrollPosition = scrollPosition; }
            protected override void CloseScope() { }
            public Vector2 scrollPosition { get; private set; }
            public bool handleScrollWheel { get; set; } = true;
        }

        public partial class ClipScope
        {
            public ClipScope(Rect position) { }
            protected override void CloseScope() { }
        }
    }

    public partial class GUILayout
    {
        public partial class HorizontalScope
        {
            public HorizontalScope(params GUILayoutOption[] options) { }
            public HorizontalScope(GUIStyle style, params GUILayoutOption[] options) { }
            public HorizontalScope(string text, GUIStyle style, params GUILayoutOption[] options) { }
            public HorizontalScope(Texture image, GUIStyle style, params GUILayoutOption[] options) { }
            public HorizontalScope(GUIContent content, GUIStyle style, params GUILayoutOption[] options) { }
            protected override void CloseScope() { }
        }
        public partial class VerticalScope
        {
            public VerticalScope(params GUILayoutOption[] options) { }
            public VerticalScope(GUIStyle style, params GUILayoutOption[] options) { }
            public VerticalScope(string text, GUIStyle style, params GUILayoutOption[] options) { }
            public VerticalScope(Texture image, GUIStyle style, params GUILayoutOption[] options) { }
            public VerticalScope(GUIContent content, GUIStyle style, params GUILayoutOption[] options) { }
            protected override void CloseScope() { }
        }
        public partial class AreaScope
        {
            public AreaScope(Rect screenRect) { }
            public AreaScope(Rect screenRect, string text) { }
            public AreaScope(Rect screenRect, Texture image) { }
            public AreaScope(Rect screenRect, GUIContent content) { }
            public AreaScope(Rect screenRect, string text, GUIStyle style) { }
            public AreaScope(Rect screenRect, Texture image, GUIStyle style) { }
            public AreaScope(Rect screenRect, GUIContent content, GUIStyle style) { }
            protected override void CloseScope() { }
        }
        public partial class ScrollViewScope
        {
            public ScrollViewScope(Vector2 scrollPosition, params GUILayoutOption[] options) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, params GUILayoutOption[] options) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Vector2 scrollPosition, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Vector2 scrollPosition, GUIStyle style, params GUILayoutOption[] options) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, GUIStyle background, params GUILayoutOption[] options) { this.scrollPosition = scrollPosition; }
            protected override void CloseScope() { }
            public Vector2 scrollPosition { get; private set; }
            public bool handleScrollWheel { get; set; } = true;
        }
    }

    public partial class GUILayoutUtility
    {
        public static Rect GetRect(GUIContent content, GUIStyle style) => Imgui.NextRect();
        public static Rect GetRect(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => Imgui.NextRect();
        public static Rect GetRect(float width, float height) => new Rect(0, Imgui.NextRect().y, width, height);
        public static Rect GetRect(float width, float height, GUIStyle style) => GetRect(width, height);
        public static Rect GetRect(float width, float height, params GUILayoutOption[] options) => GetRect(width, height);
        public static Rect GetRect(float width, float height, GUIStyle style, params GUILayoutOption[] options) => GetRect(width, height);
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight) => GetRect(maxWidth, minHeight);
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight, GUIStyle style) => GetRect(maxWidth, minHeight);
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight, params GUILayoutOption[] options) => GetRect(maxWidth, minHeight);
        public static Rect GetRect(float minWidth, float maxWidth, float minHeight, float maxHeight, GUIStyle style, params GUILayoutOption[] options) => GetRect(maxWidth, minHeight);
        public static Rect GetLastRect() => Imgui.NextRect();
        public static Rect GetAspectRect(float aspect) => Imgui.NextRect();
        public static Rect GetAspectRect(float aspect, GUIStyle style) => Imgui.NextRect();
        public static Rect GetAspectRect(float aspect, params GUILayoutOption[] options) => Imgui.NextRect();
        public static Rect GetAspectRect(float aspect, GUIStyle style, params GUILayoutOption[] options) => Imgui.NextRect();
    }

    public partial class GUIContent
    {
        string m_Text = ""; Texture m_Image; string m_Tooltip = "";
        public static GUIContent none = new GUIContent("");
        public GUIContent() { }
        public GUIContent(string text) { m_Text = text ?? ""; }
        public GUIContent(Texture image) { m_Image = image; }
        public GUIContent(string text, Texture image) { m_Text = text ?? ""; m_Image = image; }
        public GUIContent(string text, string tooltip) { m_Text = text ?? ""; m_Tooltip = tooltip ?? ""; }
        public GUIContent(Texture image, string tooltip) { m_Image = image; m_Tooltip = tooltip ?? ""; }
        public GUIContent(string text, Texture image, string tooltip) { m_Text = text ?? ""; m_Image = image; m_Tooltip = tooltip ?? ""; }
        public GUIContent(GUIContent src) { if (src != null) { m_Text = src.m_Text; m_Image = src.m_Image; m_Tooltip = src.m_Tooltip; } }
        public string text { get => m_Text; set => m_Text = value ?? ""; }
        public Texture image { get => m_Image; set => m_Image = value; }
        public string tooltip { get => m_Tooltip; set => m_Tooltip = value ?? ""; }
        public override string ToString() => m_Text;
    }

    public partial class Event
    {
        static Event s_Current;
        EventType m_Type = EventType.Layout;
        public static Event current { get => s_Current; set => s_Current = value; }
        public EventType type { get => m_Type; set => m_Type = value; }
        public EventType rawType => m_Type;
        public Vector2 mousePosition { get; set; }
        public Vector2 delta { get; set; }
        public int button { get; set; }
        public EventModifiers modifiers { get; set; }
        public int clickCount { get; set; }
        public char character { get; set; }
        public KeyCode keyCode { get; set; }
        public string commandName { get; set; } = "";
        public bool shift { get => (modifiers & EventModifiers.Shift) != 0; set => modifiers = value ? modifiers | EventModifiers.Shift : modifiers & ~EventModifiers.Shift; }
        public bool control { get => (modifiers & EventModifiers.Control) != 0; set => modifiers = value ? modifiers | EventModifiers.Control : modifiers & ~EventModifiers.Control; }
        public bool alt { get => (modifiers & EventModifiers.Alt) != 0; set => modifiers = value ? modifiers | EventModifiers.Alt : modifiers & ~EventModifiers.Alt; }
        public bool command { get => (modifiers & EventModifiers.Command) != 0; set => modifiers = value ? modifiers | EventModifiers.Command : modifiers & ~EventModifiers.Command; }
        public bool isKey => m_Type is EventType.KeyDown or EventType.KeyUp;
        public bool isMouse => m_Type is EventType.MouseDown or EventType.MouseUp or EventType.MouseDrag or EventType.MouseMove;
        public bool isScrollWheel => m_Type == EventType.ScrollWheel;
        public void Use() { if (m_Type is not (EventType.Repaint or EventType.Layout)) m_Type = EventType.Used; }
    }
}

namespace UnityEditor
{
    public partial class EditorGUI
    {
        public static int indentLevel { get; set; }
        public static bool showMixedValue { get; set; }
        public static bool actionKey => false;

        public partial struct DisabledScope
        {
            bool m_Open;
            public DisabledScope(bool disabled) { m_Open = true; Imgui.BeginDisabled(disabled); }
            public void Dispose() { if (m_Open) { m_Open = false; Imgui.EndDisabled(); } }
        }
        public partial class DisabledGroupScope
        {
            public DisabledGroupScope(bool disabled) { Imgui.BeginDisabled(disabled); }
            protected override void CloseScope() => Imgui.EndDisabled();
        }
        public partial class ChangeCheckScope
        {
            bool m_Checked, m_Changed;
            public ChangeCheckScope() { Imgui.BeginChangeCheck(); }
            public bool changed { get { if (!m_Checked) { m_Checked = true; m_Changed = Imgui.EndChangeCheck(); } return m_Changed; } }
            protected override void CloseScope() { if (!m_Checked) { m_Checked = true; m_Changed = Imgui.EndChangeCheck(); } }
        }
        public partial class IndentLevelScope
        {
            readonly int m_Increment;
            public IndentLevelScope() : this(1) { }
            public IndentLevelScope(int increment) { m_Increment = increment; indentLevel += increment; }
            protected override void CloseScope() => indentLevel -= m_Increment;
        }
        public partial class PropertyScope
        {
            public PropertyScope(Rect totalPosition, GUIContent label, SerializedProperty property) { content = label; }
            public GUIContent content { get; private set; }
            protected override void CloseScope() { }
        }
    }

    public partial class EditorGUILayout
    {
        public partial class HorizontalScope
        {
            public HorizontalScope(params GUILayoutOption[] options) { }
            public HorizontalScope(GUIStyle style, params GUILayoutOption[] options) { }
            public Rect rect => Imgui.NextRect();
            protected override void CloseScope() { }
        }
        public partial class VerticalScope
        {
            public VerticalScope(params GUILayoutOption[] options) { }
            public VerticalScope(GUIStyle style, params GUILayoutOption[] options) { }
            public Rect rect => Imgui.NextRect();
            protected override void CloseScope() { }
        }
        public partial class ScrollViewScope
        {
            public ScrollViewScope(Vector2 scrollPosition, params GUILayoutOption[] options) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, params GUILayoutOption[] options) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Vector2 scrollPosition, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Vector2 scrollPosition, GUIStyle style, params GUILayoutOption[] options) { this.scrollPosition = scrollPosition; }
            public ScrollViewScope(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, GUIStyle background, params GUILayoutOption[] options) { this.scrollPosition = scrollPosition; }
            public Vector2 scrollPosition { get; private set; }
            public bool handleScrollWheel { get; set; } = true;
            protected override void CloseScope() { }
        }
        public partial class ToggleGroupScope
        {
            public ToggleGroupScope(string label, bool toggle) { enabled = Imgui.Field("EditorGUILayout.BeginToggleGroup", label, toggle); Imgui.BeginDisabled(!enabled); }
            public ToggleGroupScope(GUIContent label, bool toggle) : this(Imgui.Text(label), toggle) { }
            public bool enabled { get; private set; }
            protected override void CloseScope() => Imgui.EndDisabled();
        }
        public partial class FadeGroupScope
        {
            public FadeGroupScope(float value) { visible = value > 0; }
            public bool visible { get; private set; }
            protected override void CloseScope() { }
        }
    }

    public partial class EditorGUIUtility
    {
        static float s_LabelWidth;
        public static float labelWidth { get => s_LabelWidth > 0 ? s_LabelWidth : Mathf.Max(currentViewWidth * 0.45f - 40, 120); set => s_LabelWidth = value; }
        public static float fieldWidth { get; set; } = 50;
        public static float currentViewWidth => 400;
        public static float singleLineHeight => 18;
        public static float standardVerticalSpacing => 2;
        public static bool isProSkin => true;
        public static bool wideMode { get; set; } = true;
        public static bool hierarchyMode { get; set; }
    }

    public partial class EditorWindow
    {
        internal static readonly List<EditorWindow> s_Open = new();
        Rect m_Position = new Rect(100, 100, 400, 300);
        GUIContent m_TitleContent;
        bool m_Shown;

        static EditorWindow Open(Type t, string title, bool create = false)
        {
            if (!create) { var w = s_Open.FirstOrDefault(x => x != null && t.IsInstanceOfType(x)); if (w != null) return w; }
            var win = (EditorWindow)ScriptableObject.CreateInstance(t);
            if (title != null) win.titleContent = new GUIContent(title);
            win.Show();
            return win;
        }

        public static EditorWindow GetWindow(Type windowType, bool utility, string title, bool focus) => Open(windowType, title);
        public static EditorWindow GetWindow(Type windowType, bool utility, string title) => Open(windowType, title);
        public static EditorWindow GetWindow(Type windowType, bool utility) => Open(windowType, null);
        public static EditorWindow GetWindow(Type windowType) => Open(windowType, null);
        public static EditorWindow GetWindowWithRect(Type windowType, Rect rect, bool utility, string title) { var w = Open(windowType, title); w.position = rect; return w; }
        public static EditorWindow GetWindowWithRect(Type windowType, Rect rect, bool utility) => GetWindowWithRect(windowType, rect, utility, null);
        public static EditorWindow GetWindowWithRect(Type windowType, Rect rect) => GetWindowWithRect(windowType, rect, false, null);
        public static T GetWindow<T>() where T : EditorWindow => (T)Open(typeof(T), null);
        public static T GetWindow<T>(bool utility) where T : EditorWindow => (T)Open(typeof(T), null);
        public static T GetWindow<T>(bool utility, string title) where T : EditorWindow => (T)Open(typeof(T), title);
        public static T GetWindow<T>(string title) where T : EditorWindow => (T)Open(typeof(T), title);
        public static T GetWindow<T>(string title, bool focus) where T : EditorWindow => (T)Open(typeof(T), title);
        public static T GetWindow<T>(bool utility, string title, bool focus) where T : EditorWindow => (T)Open(typeof(T), title);
        public static T GetWindow<T>(params Type[] desiredDockNextTo) where T : EditorWindow => (T)Open(typeof(T), null);
        public static T GetWindow<T>(string title, params Type[] desiredDockNextTo) where T : EditorWindow => (T)Open(typeof(T), title);
        public static T GetWindow<T>(string title, bool focus, params Type[] desiredDockNextTo) where T : EditorWindow => (T)Open(typeof(T), title);
        public static T CreateWindow<T>(params Type[] desiredDockNextTo) where T : EditorWindow => (T)Open(typeof(T), null, true);
        public static T CreateWindow<T>(string title, params Type[] desiredDockNextTo) where T : EditorWindow => (T)Open(typeof(T), title, true);
        public static bool HasOpenInstances<T>() where T : EditorWindow => s_Open.Any(w => w is T);
        public static void FocusWindowIfItsOpen(Type t) { }
        public static void FocusWindowIfItsOpen<T>() where T : EditorWindow { }
        public static T GetWindowWithRect<T>(Rect rect) where T : EditorWindow => (T)GetWindowWithRect(typeof(T), rect);
        public static T GetWindowWithRect<T>(Rect rect, bool utility) where T : EditorWindow => (T)GetWindowWithRect(typeof(T), rect);
        public static T GetWindowWithRect<T>(Rect rect, bool utility, string title) where T : EditorWindow => (T)GetWindowWithRect(typeof(T), rect, utility, title);
        public static T GetWindowWithRect<T>(Rect rect, bool utility, string title, bool focus) where T : EditorWindow => (T)GetWindowWithRect(typeof(T), rect, utility, title);

        public void Show() { if (m_Shown) return; m_Shown = true; s_Open.Add(this); }
        public void Show(bool immediateDisplay) => Show();
        public void ShowUtility() => Show();
        public void ShowPopup() => Show();
        public void ShowModalUtility() => Show();
        public void ShowAsDropDown(Rect buttonRect, Vector2 windowSize) => Show();
        public void ShowAuxWindow() => Show();
        public void ShowModal() => Show();
        public void ShowTab() => Show();
        public void Focus() { }
        public void Close()
        {
            if (!s_Open.Remove(this)) return;
            MiniUnity.Lifecycle.Invoke(this, "OnDisable"); MiniUnity.Lifecycle.Invoke(this, "OnDestroy");
            UnityEngine.Object.DestroyImmediate(this);
        }
        public void Repaint() { }
        public void BeginWindows() { }
        public void EndWindows() { }
        public void ShowNotification(GUIContent notification) => Debug.Log("[EditorWindow] " + notification?.text);
        public void ShowNotification(GUIContent notification, double fadeoutWait) => ShowNotification(notification);
        public void RemoveNotification() { }
        public bool SendEvent(Event e) => false;

        public GUIContent titleContent { get => m_TitleContent ??= new GUIContent(GetType().Name); set => m_TitleContent = value; }
        public string title { get => titleContent.text; set => titleContent = new GUIContent(value); }
        public Rect position { get => m_Position; set => m_Position = value; }
        public Vector2 minSize { get; set; } = new Vector2(100, 100);
        public Vector2 maxSize { get; set; } = new Vector2(4000, 4000);
        public bool wantsMouseMove { get; set; }
        public bool wantsMouseEnterLeaveWindow { get; set; }
        public bool wantsLessLayoutEvents { get; set; }
        public bool autoRepaintOnSceneChange { get; set; }
        public bool maximized { get; set; }
        public bool hasFocus => true;
        public bool docked => false;
        public static EditorWindow focusedWindow => s_Open.LastOrDefault();
        public static EditorWindow mouseOverWindow => null;
        public bool hasUnsavedChanges { get; set; }
        public string saveChangesMessage { get; set; } = "";
        public virtual void SaveChanges() => hasUnsavedChanges = false;
        public virtual void DiscardChanges() => hasUnsavedChanges = false;
    }
}

namespace UnityEditor
{
    public sealed partial class SerializedProperty
    {
        /// <summary>The C# field behind this property (walking the path through nested serializable classes), or null.</summary>
        System.Reflection.FieldInfo Field()
        {
            var t = m_Owner?.targetObject?.GetType();
            System.Reflection.FieldInfo f = null;
            foreach (var part in m_Path.Split('.'))
            {
                if (t == null) return null;
                if (part == "Array") { t = t.IsArray ? t.GetElementType() : t.IsGenericType ? t.GetGenericArguments()[0] : null; continue; }
                if (part.StartsWith("data[")) continue;
                f = MiniUnity.Serializer.SerializedFields(t).FirstOrDefault(x => x.Name == part);
                t = f?.FieldType;
            }
            return f;
        }

        Type ValueType()
        {
            var t = Field()?.FieldType;
            if (t != null && m_Index >= 0) t = t.IsArray ? t.GetElementType() : t.IsGenericType ? t.GetGenericArguments()[0] : t;
            return t;
        }

        public string displayName => ObjectNames.NicifyVariableName(m_Key);
        public string tooltip => "";
        public SerializedPropertyType propertyType
        {
            get
            {
                var t = ValueType();
                var n = m_Parent[m_Key];
                if (t == null) return n is MiniUnity.Yaml.YRef ? SerializedPropertyType.ObjectReference : n is MiniUnity.Yaml.YScalar ? SerializedPropertyType.String : SerializedPropertyType.Generic;
                if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(sbyte) || t == typeof(ushort)) return SerializedPropertyType.Integer;
                if (t == typeof(bool)) return SerializedPropertyType.Boolean;
                if (t == typeof(float) || t == typeof(double)) return SerializedPropertyType.Float;
                if (t == typeof(string)) return SerializedPropertyType.String;
                if (t == typeof(Color) || t == typeof(Color32)) return SerializedPropertyType.Color;
                if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return SerializedPropertyType.ObjectReference;
                if (t == typeof(LayerMask)) return SerializedPropertyType.LayerMask;
                if (t.IsEnum) return SerializedPropertyType.Enum;
                if (t == typeof(Vector2)) return SerializedPropertyType.Vector2;
                if (t == typeof(Vector3)) return SerializedPropertyType.Vector3;
                if (t == typeof(Vector4)) return SerializedPropertyType.Vector4;
                if (t == typeof(Rect)) return SerializedPropertyType.Rect;
                if (t == typeof(AnimationCurve)) return SerializedPropertyType.AnimationCurve;
                if (t == typeof(Bounds)) return SerializedPropertyType.Bounds;
                if (t == typeof(Gradient)) return SerializedPropertyType.Gradient;
                if (t == typeof(Quaternion)) return SerializedPropertyType.Quaternion;
                if (t == typeof(Vector2Int)) return SerializedPropertyType.Vector2Int;
                if (t == typeof(Vector3Int)) return SerializedPropertyType.Vector3Int;
                return SerializedPropertyType.Generic;
            }
        }
        public string[] enumNames => ValueType() is Type t && t.IsEnum ? Enum.GetNames(t) : Array.Empty<string>();
        public string[] enumDisplayNames => enumNames.Select(ObjectNames.NicifyVariableName).ToArray();
        public string type => ValueType()?.Name ?? "";
    }

    public sealed partial class ObjectNames
    {
        /// <summary>Unity's inspector label: drops m_ / _ / k prefixes, capitalises, spaces before capitals and digits.</summary>
        public static string NicifyVariableName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (name.StartsWith("m_")) name = name.Substring(2);
            else if (name.StartsWith("_")) name = name.TrimStart('_');
            else if (name.Length > 1 && name[0] == 'k' && char.IsUpper(name[1])) name = name.Substring(1);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i == 0) { sb.Append(char.ToUpperInvariant(c)); continue; }
                char p = name[i - 1];
                bool boundary = char.IsUpper(c) && (char.IsLower(p) || i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(p))
                             || char.IsDigit(c) && !char.IsDigit(p);
                if (boundary && sb[^1] != ' ') sb.Append(' ');
                sb.Append(c == '_' ? ' ' : c);
            }
            return sb.ToString();
        }
    }
}
