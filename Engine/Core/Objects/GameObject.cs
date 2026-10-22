using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MiniUnity;

namespace UnityEngine
{
    public sealed partial class GameObject
    {
        internal readonly List<Component> m_Components = new();
        internal bool m_IsActive = true;
        internal int m_Layer;
        internal string m_Tag = "Untagged";
        internal uint m_StaticEditorFlags;
        internal SceneData m_Scene;
        internal Transform m_Transform;

        public GameObject() : this("New Game Object") { }
        public GameObject(string name) : this(name, Type.EmptyTypes) { }
        public GameObject(string name, params Type[] components)
        {
            m_Name = name ?? "";
            if (AssetLoader.s_BareGO) return;
            bool rect = components != null && components.Contains(typeof(RectTransform));
            var t = (Transform)Lifecycle.CreateComponentRaw(this, rect ? typeof(RectTransform) : typeof(Transform));
            m_Transform = t;
            m_Scene = SceneData.Active;
            m_Scene?.AddRoot(this);
            if (components != null)
                foreach (var c in components) if (c != typeof(RectTransform) && c != typeof(Transform)) AddComponent(c);
        }

        public Transform transform { get { ThrowIfDestroyed(); return m_Transform; } }
        public GameObject gameObject => this;
        public int layer { get { ThrowIfFakeNull(); return m_Layer; } set { ThrowIfFakeNull(); m_Layer = value; s_SceneEdits++; } }
        public string tag { get { ThrowIfFakeNull(); return m_Tag; } set { ThrowIfFakeNull(); m_Tag = value ?? "Untagged"; } }
        public bool CompareTag(string tag) { ThrowIfFakeNull(); return m_Tag == tag; }
        public bool activeSelf { get { ThrowIfFakeNull(); return m_IsActive; } }
        public bool activeInHierarchy
        {
            get
            {
                ThrowIfFakeNull();
                if (m_Asset != null || m_InPrefabAsset) return false;
                for (var t = m_Transform; t != null; t = t.m_Parent) if (!t.gameObject.m_IsActive) return false;
                return true;
            }
        }
        internal bool m_InPrefabAsset;
        public bool active { get => activeInHierarchy; set => SetActive(value); }
        public bool isStatic { get => m_StaticEditorFlags != 0; set => m_StaticEditorFlags = value ? 0xFFFFFFFFu : 0; }
        public UnityEngine.SceneManagement.Scene scene => SceneData.ToScene(RootScene());
        internal SceneData RootScene() { var t = m_Transform; while (t.m_Parent != null) t = t.m_Parent; return t.gameObject.m_Scene; }
        public ulong sceneCullingMask => 0;

        public void SetActive(bool value)
        {
            ThrowIfFakeNull();
            if (m_IsActive == value) return;
            s_SceneEdits++;
            bool wasActive = activeInHierarchy;
            m_IsActive = value;
            bool nowActive = activeInHierarchy;
            if (wasActive != nowActive) Lifecycle.ActiveChanged(this, nowActive);
        }

        // ---------------- components ----------------
        public Component AddComponent(Type componentType) { ThrowIfFakeNull(); return Lifecycle.AddComponent(this, componentType); }
        public T AddComponent<T>() where T : Component => (T)Lifecycle.AddComponent(this, typeof(T));

        public Component GetComponent(Type type)
        {
            ThrowIfFakeNull();
            foreach (var c in m_Components) if (!c.m_Destroyed && type.IsInstanceOfType(c)) return c;
            return null;
        }
        public T GetComponent<T>() => (T)(object)GetComponent(typeof(T));
        public Component GetComponent(string type) => m_Components.FirstOrDefault(c => c.GetType().Name == type);
        public bool TryGetComponent<T>(out T component) { component = GetComponent<T>(); return component != null && !(component is Object o && !o); }
        public bool TryGetComponent(Type type, out Component component) { component = GetComponent(type); return component != null; }
        public Component[] GetComponents(Type type) => m_Components.Where(c => !c.m_Destroyed && type.IsInstanceOfType(c)).ToArray();
        public T[] GetComponents<T>() => m_Components.Where(c => !c.m_Destroyed && c is T).Cast<T>().ToArray();
        public void GetComponents(Type type, List<Component> results) { results.Clear(); results.AddRange(GetComponents(type)); }
        public void GetComponents<T>(List<T> results) { results.Clear(); results.AddRange(GetComponents<T>()); }

        public Component GetComponentInChildren(Type type, bool includeInactive)
        {
            foreach (var go in Hier.Walk(this))
            {
                if (!includeInactive && !go.activeInHierarchy && !(m_InPrefabAsset)) continue;
                var c = go.GetComponent(type);
                if (c != null) return c;
            }
            return null;
        }
        public Component GetComponentInChildren(Type type) => GetComponentInChildren(type, false);
        public T GetComponentInChildren<T>() => (T)(object)GetComponentInChildren(typeof(T), false);
        public T GetComponentInChildren<T>(bool includeInactive) => (T)(object)GetComponentInChildren(typeof(T), includeInactive);
        public Component[] GetComponentsInChildren(Type type, bool includeInactive = false)
            => Hier.Walk(this).Where(g => includeInactive || g.activeInHierarchy || m_InPrefabAsset).SelectMany(g => g.GetComponents(type)).ToArray();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Hier.Walk(this).Where(g => includeInactive || g.activeInHierarchy || m_InPrefabAsset).SelectMany(g => g.GetComponents<T>()).ToArray();
        public T[] GetComponentsInChildren<T>() => GetComponentsInChildren<T>(false);
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> results) { results.Clear(); results.AddRange(GetComponentsInChildren<T>(includeInactive)); }
        public void GetComponentsInChildren<T>(List<T> results) => GetComponentsInChildren(false, results);

        public Component GetComponentInParent(Type type, bool includeInactive)
        {
            for (var t = m_Transform; t != null; t = t.m_Parent)
            {
                if (!includeInactive && !t.gameObject.activeInHierarchy && !m_InPrefabAsset) continue;
                var c = t.gameObject.GetComponent(type);
                if (c != null) return c;
            }
            return null;
        }
        public Component GetComponentInParent(Type type) => GetComponentInParent(type, false);
        public T GetComponentInParent<T>() => (T)(object)GetComponentInParent(typeof(T), false);
        public T GetComponentInParent<T>(bool includeInactive) => (T)(object)GetComponentInParent(typeof(T), includeInactive);
        public Component[] GetComponentsInParent(Type type, bool includeInactive = false)
        {
            var r = new List<Component>();
            for (var t = m_Transform; t != null; t = t.m_Parent)
                if (includeInactive || t.gameObject.activeInHierarchy || m_InPrefabAsset) r.AddRange(t.gameObject.GetComponents(type));
            return r.ToArray();
        }
        public T[] GetComponentsInParent<T>(bool includeInactive) => GetComponentsInParent(typeof(T), includeInactive).Cast<T>().ToArray();
        public T[] GetComponentsInParent<T>() => GetComponentsInParent<T>(false);
        public void GetComponentsInParent<T>(bool includeInactive, List<T> results) { results.Clear(); results.AddRange(GetComponentsInParent<T>(includeInactive)); }

        public int GetComponentCount() => m_Components.Count;
        public Component GetComponentAtIndex(int index) => m_Components[index];
        public int GetComponentIndex(Component component) => m_Components.IndexOf(component);

        public void SendMessage(string methodName, object value = null, SendMessageOptions options = SendMessageOptions.RequireReceiver) => Lifecycle.Send(this, methodName, value);
        public void SendMessage(string methodName) => SendMessage(methodName, null);
        public void BroadcastMessage(string methodName, object parameter = null, SendMessageOptions options = SendMessageOptions.RequireReceiver)
        { foreach (var g in Hier.Walk(this)) Lifecycle.Send(g, methodName, parameter); }

        public static GameObject Find(string name) => ObjectRegistry.SceneObjects(typeof(GameObject), false).Cast<GameObject>().FirstOrDefault(g => g.name == name);
        public static GameObject FindWithTag(string tag) => ObjectRegistry.SceneObjects(typeof(GameObject), false).Cast<GameObject>().FirstOrDefault(g => g.m_Tag == tag);
        public static GameObject FindGameObjectWithTag(string tag) => FindWithTag(tag);
        public static GameObject[] FindGameObjectsWithTag(string tag) => ObjectRegistry.SceneObjects(typeof(GameObject), false).Cast<GameObject>().Where(g => g.m_Tag == tag).ToArray();
    }

    public partial class Component
    {
        internal GameObject m_GameObject;
        public GameObject gameObject { get { ThrowIfDestroyed(); return m_GameObject; } }
        public Transform transform { get { ThrowIfDestroyed(); return m_GameObject.m_Transform; } }
        public string tag { get => gameObject.tag; set => gameObject.tag = value; }
        public bool CompareTag(string tag) => gameObject.CompareTag(tag);
        public Component GetComponent(Type type) => gameObject.GetComponent(type);
        public T GetComponent<T>() => gameObject.GetComponent<T>();
        public Component GetComponent(string type) => gameObject.GetComponent(type);
        public bool TryGetComponent<T>(out T component) => gameObject.TryGetComponent(out component);
        public bool TryGetComponent(Type type, out Component component) => gameObject.TryGetComponent(type, out component);
        public Component[] GetComponents(Type type) => gameObject.GetComponents(type);
        public T[] GetComponents<T>() => gameObject.GetComponents<T>();
        public void GetComponents(Type type, List<Component> results) => gameObject.GetComponents(type, results);
        public void GetComponents<T>(List<T> results) => gameObject.GetComponents(results);
        public Component GetComponentInChildren(Type t, bool includeInactive) => gameObject.GetComponentInChildren(t, includeInactive);
        public Component GetComponentInChildren(Type t) => gameObject.GetComponentInChildren(t);
        public T GetComponentInChildren<T>() => gameObject.GetComponentInChildren<T>();
        public T GetComponentInChildren<T>(bool includeInactive) => gameObject.GetComponentInChildren<T>(includeInactive);
        public Component[] GetComponentsInChildren(Type t, bool includeInactive = false) => gameObject.GetComponentsInChildren(t, includeInactive);
        public T[] GetComponentsInChildren<T>(bool includeInactive) => gameObject.GetComponentsInChildren<T>(includeInactive);
        public T[] GetComponentsInChildren<T>() => gameObject.GetComponentsInChildren<T>();
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> result) => gameObject.GetComponentsInChildren(includeInactive, result);
        public void GetComponentsInChildren<T>(List<T> result) => gameObject.GetComponentsInChildren(result);
        public Component GetComponentInParent(Type t, bool includeInactive) => gameObject.GetComponentInParent(t, includeInactive);
        public Component GetComponentInParent(Type t) => gameObject.GetComponentInParent(t);
        public T GetComponentInParent<T>() => gameObject.GetComponentInParent<T>();
        public T GetComponentInParent<T>(bool includeInactive) => gameObject.GetComponentInParent<T>(includeInactive);
        public Component[] GetComponentsInParent(Type t, bool includeInactive = false) => gameObject.GetComponentsInParent(t, includeInactive);
        public T[] GetComponentsInParent<T>(bool includeInactive) => gameObject.GetComponentsInParent<T>(includeInactive);
        public T[] GetComponentsInParent<T>() => gameObject.GetComponentsInParent<T>();
        public void GetComponentsInParent<T>(bool includeInactive, List<T> results) => gameObject.GetComponentsInParent(includeInactive, results);
        public int GetComponentIndex() => gameObject.GetComponentIndex(this);
        public void SendMessage(string methodName, object value = null, SendMessageOptions options = SendMessageOptions.RequireReceiver) => gameObject.SendMessage(methodName, value, options);
        public void SendMessage(string methodName) => gameObject.SendMessage(methodName);
        public void BroadcastMessage(string methodName, object parameter = null, SendMessageOptions options = SendMessageOptions.RequireReceiver) => gameObject.BroadcastMessage(methodName, parameter, options);
    }

    public partial class Behaviour
    {
        internal bool m_Enabled = true;
        public bool enabled
        {
            get { ThrowIfFakeNull(); return m_Enabled; }
            set
            {
                ThrowIfFakeNull();
                if (m_Enabled == value) return;
                s_SceneEdits++;
                bool was = isActiveAndEnabled;
                m_Enabled = value;
                Lifecycle.EnabledChanged(this, was, isActiveAndEnabled);
            }
        }
        public bool isActiveAndEnabled => m_Enabled && m_GameObject != null && m_GameObject.activeInHierarchy;
    }

    public partial class MonoBehaviour
    {
        public bool useGUILayout { get; set; } = true;
        public bool runInEditMode { get; set; }
        public bool didStart => MiniUnity.Play.IsPlaying && m_Awoken;
        public bool didAwake => m_Awoken;
        internal bool m_Awoken;
        internal bool m_EnableSent;   // OnEnable received and not yet followed by OnDisable
        public System.Threading.CancellationToken destroyCancellationToken => default;
        public Coroutine StartCoroutine(System.Collections.IEnumerator routine)
        {
            if (!MiniUnity.Play.IsPlaying) { Debug.LogWarning($"[MiniUnity] StartCoroutine ignored on {GetType().Name} (edit mode)"); return null; }
            return MiniUnity.Play.StartRoutine(this, routine);
        }
        public Coroutine StartCoroutine(string methodName) => StartCoroutine(methodName, null);
        public Coroutine StartCoroutine(string methodName, object value)
        {
            var m = GetType().GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (m == null) { Debug.LogError($"Coroutine '{methodName}' couldn't be started!"); return null; }
            var e = (System.Collections.IEnumerator)m.Invoke(this, m.GetParameters().Length == 0 ? null : new[] { value });
            return MiniUnity.Play.IsPlaying ? MiniUnity.Play.StartRoutine(this, e, methodName) : null;
        }
        public void StopCoroutine(System.Collections.IEnumerator routine) => MiniUnity.Play.StopRoutine(this, (e, h, n) => ReferenceEquals(e, routine));
        public void StopCoroutine(Coroutine routine) => MiniUnity.Play.StopRoutine(this, (e, h, n) => ReferenceEquals(h, routine));
        public void StopCoroutine(string methodName) => MiniUnity.Play.StopRoutine(this, (e, h, n) => n == methodName);
        public void StopAllCoroutines() => MiniUnity.Play.StopRoutine(this, (e, h, n) => true);
        public void Invoke(string methodName, float time) { if (MiniUnity.Play.IsPlaying) MiniUnity.Play.AddInvoke(this, methodName, time, 0, false); }
        public void InvokeRepeating(string methodName, float time, float repeatRate) { if (MiniUnity.Play.IsPlaying) MiniUnity.Play.AddInvoke(this, methodName, time, repeatRate, true); }
        public void CancelInvoke() => MiniUnity.Play.CancelInvoke(this, null);
        public void CancelInvoke(string methodName) => MiniUnity.Play.CancelInvoke(this, methodName);
        public bool IsInvoking(string methodName) => MiniUnity.Play.IsInvoking(this, methodName);
        public bool IsInvoking() => MiniUnity.Play.IsInvoking(this, null);
        public static void print(object message) => Debug.Log(message);
    }

    public partial class ScriptableObject
    {
        public static ScriptableObject CreateInstance(string className)
        {
            var t = TypeCache.FindType(className);
            return t == null ? null : CreateInstance(t);
        }
        public static ScriptableObject CreateInstance(Type type) => Lifecycle.CreateScriptableObject(type, true);
        public static T CreateInstance<T>() where T : ScriptableObject => (T)CreateInstance(typeof(T));
        public void SetDirty() { }
    }
}
