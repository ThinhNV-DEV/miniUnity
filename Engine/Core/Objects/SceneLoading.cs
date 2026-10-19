// Runtime scene loading (SceneManager.LoadScene / LoadSceneAsync / UnloadSceneAsync) in play mode, as Unity does it: scenes
// named by build index, name or path from the Build Settings list (ProjectSettings/EditorBuildSettings.asset, enabled
// scenes); the load happens at the start of the next frame (async: once allowSceneActivation is set); a single-mode load
// unloads every other scene (OnDisable / OnDestroy, sceneUnloaded) except DontDestroyOnLoad objects, then the new scene's
// objects get Awake / OnEnable, activeSceneChanged and sceneLoaded fire, and Start runs in that frame's update.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEngine.SceneManagement
{
    public partial class SceneManager
    {
        public static event UnityAction<Scene, LoadSceneMode> sceneLoaded;
        public static event UnityAction<Scene> sceneUnloaded;
        public static event UnityAction<Scene, Scene> activeSceneChanged;
        internal static void RaiseLoaded(Scene s, LoadSceneMode m) => Raise(() => sceneLoaded?.Invoke(s, m));
        internal static void RaiseUnloaded(Scene s) => Raise(() => sceneUnloaded?.Invoke(s));
        internal static void RaiseActiveChanged(Scene from, Scene to) => Raise(() => activeSceneChanged?.Invoke(from, to));
        static void Raise(Action a) { try { a(); } catch (Exception e) { Play.Report(e, "SceneManager event"); } }

        public static int sceneCountInBuildSettings => SceneLoader.BuildScenes().Count;
        public static Scene GetSceneByBuildIndex(int buildIndex)
        {
            var p = SceneLoader.PathOfIndex(buildIndex);
            return p == null ? default : GetSceneByPath(p);
        }
        public static Scene[] GetAllScenes() => SceneData.LoadedScenes.Select(SceneData.ToScene).ToArray();
        public static Scene CreateScene(string sceneName) => CreateScene(sceneName, default);
        public static Scene CreateScene(string sceneName, CreateSceneParameters parameters)
        {
            if (string.IsNullOrEmpty(sceneName)) throw new ArgumentException("Scene name is empty");
            if (SceneData.LoadedScenes.Any(s => s.Name == sceneName)) throw new ArgumentException($"A scene with the name '{sceneName}' already exists");
            return SceneData.ToScene(new SceneData { Name = sceneName });
        }
        public static void MergeScenes(Scene sourceScene, Scene destinationScene)
        {
            var from = SceneData.ByHandle(sceneHandle(sourceScene)); var to = SceneData.ByHandle(sceneHandle(destinationScene));
            if (from == null || to == null || !from.Loaded || !to.Loaded) throw new ArgumentException("MergeScenes: both scenes must be valid and loaded");
            foreach (var r in from.Roots.ToList()) MoveGameObjectToScene(r, destinationScene);
            SceneLoader.Unload(from);
        }
        static int sceneHandle(Scene s) => s.m_Handle;

        public static void LoadScene(string sceneName) => LoadScene(sceneName, new LoadSceneParameters(LoadSceneMode.Single));
        public static void LoadScene(string sceneName, LoadSceneMode mode) => LoadScene(sceneName, new LoadSceneParameters(mode));
        public static Scene LoadScene(string sceneName, LoadSceneParameters parameters) => SceneLoader.Queue(SceneLoader.PathOfName(sceneName), sceneName, parameters.loadSceneMode, false, out _);
        public static void LoadScene(int sceneBuildIndex) => LoadScene(sceneBuildIndex, new LoadSceneParameters(LoadSceneMode.Single));
        public static void LoadScene(int sceneBuildIndex, LoadSceneMode mode) => LoadScene(sceneBuildIndex, new LoadSceneParameters(mode));
        public static Scene LoadScene(int sceneBuildIndex, LoadSceneParameters parameters) => SceneLoader.Queue(SceneLoader.PathOfIndex(sceneBuildIndex), "#" + sceneBuildIndex, parameters.loadSceneMode, false, out _);

        public static AsyncOperation LoadSceneAsync(string sceneName) => LoadSceneAsync(sceneName, new LoadSceneParameters(LoadSceneMode.Single));
        public static AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode) => LoadSceneAsync(sceneName, new LoadSceneParameters(mode));
        public static AsyncOperation LoadSceneAsync(string sceneName, LoadSceneParameters parameters) { SceneLoader.Queue(SceneLoader.PathOfName(sceneName), sceneName, parameters.loadSceneMode, true, out var op); return op; }
        public static AsyncOperation LoadSceneAsync(int sceneBuildIndex) => LoadSceneAsync(sceneBuildIndex, new LoadSceneParameters(LoadSceneMode.Single));
        public static AsyncOperation LoadSceneAsync(int sceneBuildIndex, LoadSceneMode mode) => LoadSceneAsync(sceneBuildIndex, new LoadSceneParameters(mode));
        public static AsyncOperation LoadSceneAsync(int sceneBuildIndex, LoadSceneParameters parameters) { SceneLoader.Queue(SceneLoader.PathOfIndex(sceneBuildIndex), "#" + sceneBuildIndex, parameters.loadSceneMode, true, out var op); return op; }

        public static AsyncOperation UnloadSceneAsync(Scene scene) => UnloadSceneAsync(scene, UnloadSceneOptions.None);
        public static AsyncOperation UnloadSceneAsync(Scene scene, UnloadSceneOptions options) => SceneLoader.QueueUnload(SceneData.ByHandle(scene.m_Handle));
        public static AsyncOperation UnloadSceneAsync(string sceneName) => UnloadSceneAsync(sceneName, UnloadSceneOptions.None);
        public static AsyncOperation UnloadSceneAsync(string sceneName, UnloadSceneOptions options) => SceneLoader.QueueUnload(SceneLoader.LoadedByName(sceneName));
        public static AsyncOperation UnloadSceneAsync(int sceneBuildIndex) => UnloadSceneAsync(sceneBuildIndex, UnloadSceneOptions.None);
        public static AsyncOperation UnloadSceneAsync(int sceneBuildIndex, UnloadSceneOptions options) => SceneLoader.QueueUnload(SceneLoader.LoadedByIndex(sceneBuildIndex));
        public static bool UnloadScene(Scene scene) => SceneLoader.UnloadNow(SceneData.ByHandle(scene.m_Handle));
        public static bool UnloadScene(string sceneName) => SceneLoader.UnloadNow(SceneLoader.LoadedByName(sceneName));
        public static bool UnloadScene(int sceneBuildIndex) => SceneLoader.UnloadNow(SceneLoader.LoadedByIndex(sceneBuildIndex));
    }

    public static partial class SceneUtility
    {
        public static string GetScenePathByBuildIndex(int buildIndex) => SceneLoader.PathOfIndex(buildIndex) ?? "";
        public static int GetBuildIndexByScenePath(string scenePath) => SceneLoader.IndexOf(scenePath);
    }
}

namespace MiniUnity
{
    public static class SceneLoader
    {
        sealed class Pending { public string Path; public LoadSceneMode Mode; public AsyncOperation Op; public SceneData Unload; public int Frame; public SceneData Placeholder; }
        static readonly List<Pending> pending = new();

        public static void Reset() { pending.Clear(); SceneData.ResetDontDestroyOnLoad(); }

        /// <summary>Enabled scenes of the Build Settings, in build-index order.</summary>
        public static List<string> BuildScenes()
        {
            try { return UnityEditor.EditorBuildSettings.scenes.Where(s => s.enabled && !string.IsNullOrEmpty(s.path)).Select(s => AssetIndex.Norm(s.path)).ToList(); }
            catch { return new List<string>(); }
        }
        public static string PathOfIndex(int i) { var l = BuildScenes(); return i >= 0 && i < l.Count ? l[i] : null; }
        public static int IndexOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return -1;
            var p = AssetIndex.Norm(path);
            return BuildScenes().FindIndex(s => string.Equals(s, p, StringComparison.OrdinalIgnoreCase));
        }
        /// <summary>A scene name as Unity accepts it: the file name ("Game"), a path without "Assets/" and ".unity"
        /// ("Scenes/Game"), or the full path.</summary>
        public static string PathOfName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var n = name.Replace('\\', '/');
            if (n.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 6);
            if (n.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) n = n.Substring(7);
            foreach (var s in BuildScenes())
            {
                var rel = s.StartsWith("Assets/") ? s.Substring(7, s.Length - 13) : s;
                if (string.Equals(rel, n, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(s), n, StringComparison.OrdinalIgnoreCase) || rel.EndsWith("/" + n, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }
        public static SceneData LoadedByName(string name)
        {
            var p = PathOfName(name);
            return SceneData.LoadedScenes.FirstOrDefault(s => (p != null && s.Path == p) || s.Name == name || s.Path == name);
        }
        public static SceneData LoadedByIndex(int i) { var p = PathOfIndex(i); return p == null ? null : SceneData.LoadedScenes.FirstOrDefault(s => s.Path == p); }

        static void RequirePlayMode()
        {
            if (!Play.IsPlaying) throw new InvalidOperationException("This can only be used during play mode, please use EditorSceneManager.OpenScene() instead.");
        }

        /// <summary>Queue a load for the next frame. Returns the (not yet loaded) scene; unknown scenes log Unity's error.</summary>
        public static Scene Queue(string path, string what, LoadSceneMode mode, bool async, out AsyncOperation op)
        {
            RequirePlayMode();
            op = null;
            if (path == null)
            {
                Debug.LogError(what.StartsWith("#")
                    ? $"Scene with build index: {what.Substring(1)} couldn't be loaded because it has not been added to the build settings scenes list"
                    : $"Scene '{what}' couldn't be loaded because it has not been added to the build settings or the AssetBundle has not been loaded.\nTo add a scene to the build settings use the menu File->Build Settings...");
                return default;
            }
            op = new AsyncOperation { m_Progress = 0 };
            // the scene exists (invalid until loaded, like Unity's isLoaded == false) so the returned Scene can be kept
            var ph = new SceneData { Path = path, Name = Path.GetFileNameWithoutExtension(path), Loaded = false };
            pending.Add(new Pending { Path = path, Mode = mode, Op = op, Frame = Play.Frame, Placeholder = ph });
            return SceneData.ToScene(ph);
        }

        public static AsyncOperation QueueUnload(SceneData s)
        {
            RequirePlayMode();
            if (s == null || !s.Loaded) throw new ArgumentException("Scene to unload is invalid");
            if (SceneData.LoadedScenes.Count <= 1) { Debug.LogWarning("Unloading the last loaded scene " + s.Path + "(" + s.Name + "), is not supported. Please use SceneManager.LoadScene()/EditorSceneManager.OpenScene() to switch to another scene."); return null; }
            var op = new AsyncOperation();
            pending.Add(new Pending { Unload = s, Op = op, Frame = Play.Frame });
            return op;
        }

        /// <summary>Run the loads queued in earlier frames (start of a frame, before Start / Update).</summary>
        public static void Process()
        {
            if (pending.Count == 0) return;
            foreach (var p in pending.ToList())
            {
                if (p.Frame >= Play.Frame) continue;
                if (!p.Op.allowSceneActivation) { p.Op.m_Progress = 0.9f; continue; }
                pending.Remove(p);
                if (p.Unload != null) { if (p.Unload.Loaded) UnloadNow(p.Unload); }
                else Load(p);
                p.Op.InvokeCompletionEvent();
                // a load activates scenes queued before it (Unity loads in order)
            }
        }

        static void Load(Pending p)
        {
            var prevActive = SceneData.Active;
            if (p.Mode == LoadSceneMode.Single)
                foreach (var s in SceneData.LoadedScenes) UnloadObjects(s);
            var scene = MiniUnity.Agent.RuntimeHost.LoadScene(p.Path, p.Placeholder);
            if (p.Mode == LoadSceneMode.Single)
            {
                foreach (var s in SceneData.LoadedScenes.Where(s => s != scene).ToList()) { s.Loaded = false; UnityEngine.SceneManagement.SceneManager.RaiseUnloaded(SceneData.ToScene(s)); }
                scene.MakeOnly();
            }
            else SceneData.Active = prevActive != null && prevActive.Loaded ? prevActive : scene;
            Play.AwakeScene(scene);
            if (SceneData.Active != prevActive) UnityEngine.SceneManagement.SceneManager.RaiseActiveChanged(SceneData.ToScene(prevActive), SceneData.ToScene(SceneData.Active));
            UnityEngine.SceneManagement.SceneManager.RaiseLoaded(SceneData.ToScene(scene), p.Mode);
        }

        static void UnloadObjects(SceneData s)
        {
            foreach (var r in s.Roots.ToList()) if (r != null && !r.m_Destroyed) Object.DestroyImmediate(r);
            s.Roots.Clear(); Hier.Version++;
        }

        public static bool UnloadNow(SceneData s)
        {
            if (s == null || !s.Loaded) return false;
            UnloadObjects(s);
            s.Loaded = false;
            if (SceneData.Active == s)
            {
                var next = SceneData.LoadedScenes.FirstOrDefault();
                SceneData.Active = next;
                UnityEngine.SceneManagement.SceneManager.RaiseActiveChanged(SceneData.ToScene(s), SceneData.ToScene(next));
            }
            UnityEngine.SceneManagement.SceneManager.RaiseUnloaded(SceneData.ToScene(s));
            return true;
        }
        internal static void Unload(SceneData s) => UnloadNow(s);

        /// <summary>Object.DontDestroyOnLoad: the object's root moves to the DontDestroyOnLoad scene (play mode only).</summary>
        public static void DontDestroyOnLoad(Object target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target), "The Object you want to keep is null.");
            if (!Play.IsPlaying) { Debug.LogError("DontDestroyOnLoad only works in play mode."); return; }   // Unity: InvalidOperation in edit mode
            var go = target as GameObject ?? (target as Component)?.gameObject;
            if (go == null) return;
            if (go.transform.parent != null) { Debug.LogWarning("DontDestroyOnLoad only works for root GameObjects or components on root GameObjects."); return; }
            var ddol = SceneData.DontDestroyOnLoadScene;
            if (go.m_Scene == ddol) return;
            go.m_Scene?.RemoveRoot(go);
            ddol.AddRoot(go);
            foreach (var g in Hier.Walk(go)) g.m_Scene = ddol;
        }
    }
}
