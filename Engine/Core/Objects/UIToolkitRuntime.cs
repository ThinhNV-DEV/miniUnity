// The part of UI Toolkit's runtime that uGUI's EventSystem reaches when PACKAGE_UITOOLKIT is defined (as in every project with
// the built-in UIElements module): event-system registration and the list of runtime panels. MiniUnity has no runtime UI Toolkit
// panels, so the list is empty and no panel is ever created. Signatures match UnityEngine.UIElementsModule (uGUI binds to them).
using System;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    internal static class UIElementsRuntimeUtility
    {
        static readonly List<Panel> s_Panels = new();

        internal static Object activeEventSystem { get; private set; }
        internal static bool useDefaultEventSystem => activeEventSystem == null;

        public static event Action<BaseRuntimePanel> onCreatePanel;

        public static void RegisterEventSystem(Object eventSystem)
        {
            if (activeEventSystem != null && activeEventSystem != eventSystem && eventSystem != null && eventSystem.GetType().Name == "EventSystem")
                Debug.LogWarning("There can be only one active Event System.");
            activeEventSystem = eventSystem;
        }

        public static void UnregisterEventSystem(Object eventSystem)
        {
            if (activeEventSystem == eventSystem) activeEventSystem = null;
        }

        internal static List<Panel> GetSortedPlayerPanels() => s_Panels;

        public static EventBase CreateEvent(Event systemEvent) => null;

        internal static void Reset() { s_Panels.Clear(); activeEventSystem = null; onCreatePanel = null; }
    }

    internal class Panel
    {
        internal string name { get; set; }
    }

    internal abstract class BaseRuntimePanel : Panel
    {
        public GameObject selectableGameObject { get; set; }
        public event Action destroyed;
        internal void OnDestroyed() => destroyed?.Invoke();
    }
}

namespace UnityEngine
{
    /// <summary>Cursor state of the (headless) game window: kept for scripts and uGUI's EventSystem, nothing is drawn.</summary>
    public partial class Cursor
    {
        static bool s_Visible = true;
        static CursorLockMode s_Lock = CursorLockMode.None;
        public static bool visible { get => s_Visible; set => s_Visible = value; }
        public static CursorLockMode lockState { get => s_Lock; set => s_Lock = value; }
        public static void SetCursor(Texture2D texture, Vector2 hotspot, CursorMode cursorMode) { }
        internal static void Reset() { s_Visible = true; s_Lock = CursorLockMode.None; }
    }
}

namespace UnityEngine
{
    /// <summary>One display the size of the game view (Screen). RelativeMouseAt reports no multi-display mapping (Vector3.zero), as
    /// on platforms without multi-display support, so uGUI uses the event position as it is.</summary>
    public partial class Display
    {
        internal Display() { }
        static Display s_Main;
        public static Display main => s_Main ??= new Display();
        public static Display[] displays = { main };
        public int renderingWidth => (int)MiniUnity.Screen.Width;
        public int renderingHeight => (int)MiniUnity.Screen.Height;
        public int systemWidth => (int)MiniUnity.Screen.Width;
        public int systemHeight => (int)MiniUnity.Screen.Height;
        public bool active => this == main;
        public bool requiresBlitToBackbuffer => false;
        public bool requiresSrgbBlitToBackbuffer => false;
        public static int activeEditorGameViewTarget => 0;
        public void Activate() { }
        public void Activate(int width, int height, RefreshRate refreshRate) { }
        public void Activate(int width, int height, int refreshRate) { }
        public void SetParams(int width, int height, int x, int y) { }
        public void SetRenderingResolution(int w, int h) { }
        public static bool MultiDisplayLicense() => true;
        public static Vector3 RelativeMouseAt(Vector3 inputMouseCoordinates) => Vector3.zero;
    }
}
