// Unity 2022.3's runtime panel classes as uGUI 1.0's EventSystem binds to them (Unity 6: Versioned/6000/UIToolkitPanels.cs).
using System;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    internal static partial class UIElementsRuntimeUtility
    {
        static readonly List<Panel> s_Panels = new();
        internal static List<Panel> GetSortedPlayerPanels() => s_Panels;
        static void ClearPanels() => s_Panels.Clear();
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
