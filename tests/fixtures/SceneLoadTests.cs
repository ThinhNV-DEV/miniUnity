using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// SceneManager.LoadScene / LoadSceneAsync / UnloadSceneAsync in play mode with the fixture Build Settings
// (tests/data/ProjectSettings/EditorBuildSettings.asset: 0 Assets/Scenes/Menu.unity, a disabled scene, 1 Assets/Scenes/Game.unity).
public class SceneLoadTests
{
    public class Probe : MonoBehaviour
    {
        public static readonly List<string> Log = new();
        void Awake() => Log.Add(name + ".Awake");
        void OnDestroy() => Log.Add(name + ".OnDestroy");
    }

    readonly List<string> events = new();
    void OnLoaded(Scene s, LoadSceneMode m) => events.Add($"loaded {s.name} {m}");
    void OnUnloaded(Scene s) => events.Add($"unloaded {s.name}");
    void OnActive(Scene a, Scene b) => events.Add($"active {b.name}");

    [SetUp] public void Hook() { SceneManager.sceneLoaded += OnLoaded; SceneManager.sceneUnloaded += OnUnloaded; SceneManager.activeSceneChanged += OnActive; Probe.Log.Clear(); }
    [TearDown] public void Unhook() { SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneUnloaded -= OnUnloaded; SceneManager.activeSceneChanged -= OnActive; }

    [Test] public void BuildSettingsListTheEnabledScenes()
    {
        Assert.AreEqual(2, SceneManager.sceneCountInBuildSettings);
        Assert.AreEqual("Assets/Scenes/Game.unity", SceneUtility.GetScenePathByBuildIndex(1));
        Assert.AreEqual(0, SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Menu.unity"));
        Assert.AreEqual(-1, SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Disabled.unity"));
    }

    [UnityTest] public IEnumerator MenuSwitchesToGame()
    {
        SceneManager.LoadScene("Menu");
        Assert.IsNull(GameObject.Find("MenuRoot"), "the load completes in the next frame");
        yield return null;
        Assert.IsNotNull(GameObject.Find("MenuRoot"));
        var menu = SceneManager.GetActiveScene();
        Assert.AreEqual("Menu", menu.name);
        Assert.AreEqual(0, menu.buildIndex);
        Assert.IsTrue(menu.isLoaded);
        Assert.AreEqual(1, SceneManager.sceneCount, "single mode closed the test scene");

        var keep = new GameObject("Keep").AddComponent<Probe>();
        Object.DontDestroyOnLoad(keep.gameObject);
        Assert.AreEqual("DontDestroyOnLoad", keep.gameObject.scene.name);
        new GameObject("Temp").AddComponent<Probe>();
        events.Clear();

        SceneManager.LoadScene(1);   // Game, by build index
        yield return null;
        Assert.IsNull(GameObject.Find("MenuRoot"));
        Assert.IsNotNull(GameObject.Find("Player"));
        Assert.IsTrue(keep != null && GameObject.Find("Keep") != null, "DontDestroyOnLoad objects survive");
        CollectionAssert.Contains(Probe.Log, "Temp.OnDestroy");
        CollectionAssert.DoesNotContain(Probe.Log, "Keep.OnDestroy");
        Assert.AreEqual("Game", SceneManager.GetActiveScene().name);
        CollectionAssert.AreEqual(new[] { "unloaded Menu", "active Game", "loaded Game Single" }, events);
        Assert.IsFalse(menu.isLoaded);
    }

    [UnityTest] public IEnumerator AsyncAdditiveWaitsForActivationThenUnloads()
    {
        SceneManager.LoadScene("Scenes/Game");
        yield return null;
        var op = SceneManager.LoadSceneAsync("Assets/Scenes/Menu.unity", LoadSceneMode.Additive);
        op.allowSceneActivation = false;
        yield return null; yield return null;
        Assert.IsFalse(op.isDone);
        Assert.AreEqual(0.9f, op.progress, 1e-6);
        Assert.IsNull(GameObject.Find("MenuRoot"));
        bool completed = false;
        op.completed += _ => completed = true;
        op.allowSceneActivation = true;
        yield return op;
        Assert.IsTrue(completed);
        Assert.AreEqual(2, SceneManager.sceneCount);
        Assert.AreEqual("Game", SceneManager.GetActiveScene().name, "additive keeps the active scene");
        Assert.IsNotNull(GameObject.Find("MenuRoot"));
        Assert.IsNotNull(GameObject.Find("Player"));
        Assert.AreEqual("Menu", GameObject.Find("MenuRoot").scene.name);

        yield return SceneManager.UnloadSceneAsync("Menu");
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsNull(GameObject.Find("MenuRoot"));
        CollectionAssert.Contains(events, "unloaded Menu");
    }

    [UnityTest] public IEnumerator UnknownScenesLogUnitysError()
    {
        LogAssert.Expect(LogType.Error, new Regex("Scene 'Nope' couldn't be loaded because it has not been added to the build settings"));
        SceneManager.LoadScene("Nope");
        LogAssert.Expect(LogType.Error, new Regex("build index: 7 couldn't be loaded"));
        SceneManager.LoadScene(7);
        LogAssert.Expect(LogType.Error, new Regex("Scene 'Disabled' couldn't be loaded"));
        SceneManager.LoadScene("Disabled");
        yield return null;
    }
}
