// Unity 6's runtime panel classes as uGUI 2.0's UIToolkitInteroperabilityBridge binds to them: the sorted panel list is typed as
// BaseRuntimePanel, and disposed / isFlat are declared on the new BaseVisualElementPanel (Unity 2022.3: Versioned/2022). MiniUnity
// has no runtime UI Toolkit panels, so the list stays empty.
using System;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    internal static partial class UIElementsRuntimeUtility
    {
        static readonly List<BaseRuntimePanel> s_Panels = new();
        internal static List<BaseRuntimePanel> GetSortedPlayerPanels() => s_Panels;
        static void ClearPanels() => s_Panels.Clear();
    }

    internal abstract class BaseVisualElementPanel
    {
        internal bool disposed { get; private set; }
        public bool isFlat => true;
    }

    internal class Panel : BaseVisualElementPanel
    {
        internal string name { get; set; }
    }

    internal abstract class BaseRuntimePanel : Panel
    {
        public GameObject selectableGameObject { get; set; }
        public event Action destroyed;
        internal void OnDestroyed() => destroyed?.Invoke();
    }

    internal static class UIRUtility
    {
        // the bridge destroys the panel GameObjects it created with this (UIElementsModule's own rule)
        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
