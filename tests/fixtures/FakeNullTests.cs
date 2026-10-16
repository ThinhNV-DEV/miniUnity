using NUnit.Framework;
using UnityEngine;

// Unity Editor's "fake null": an unassigned serialized Object field compares equal to null but throws UnassignedReferenceException
// (with the field and script names) when used; a destroyed object throws MissingReferenceException.
public class FakeNullTests
{
    class Holder : MonoBehaviour
    {
        public Transform target;
        public GameObject prefab;
        public Rigidbody body;
        [SerializeField] Renderer hidden;
        public Renderer Hidden => hidden;
        public Transform notSerialized { get; set; }
    }

    [Test] public void UnassignedFieldsAreFakeNull()
    {
        var h = new GameObject("H").AddComponent<Holder>();
        Assert.IsTrue(h.target == null, "== null");
        Assert.IsFalse((bool)h.target, "implicit bool");
        Assert.IsTrue(h.target.Equals(null));
        Assert.IsFalse(ReferenceEquals(h.target, null), "a placeholder object, as in the Unity Editor");
        var e = Assert.Throws<UnassignedReferenceException>(() => { var _ = h.target.position; });
        StringAssert.StartsWith("The variable target of Holder has not been assigned.", e.Message);
        Assert.Throws<UnassignedReferenceException>(() => h.prefab.SetActive(true));
        Assert.Throws<UnassignedReferenceException>(() => h.prefab.GetComponent<Collider>());
        Assert.Throws<UnassignedReferenceException>(() => { var _ = h.body.mass; });
        Assert.Throws<UnassignedReferenceException>(() => { var _ = h.Hidden.enabled; }, "[SerializeField] private too");
        Assert.Throws<UnassignedReferenceException>(() => { var _ = h.target.name; });
        Assert.IsNull(h.notSerialized, "non-serialized members stay real null");
        h.target = h.transform;
        Assert.AreSame(h.transform, h.target);
    }

    [Test] public void DestroyedObjectsThrowMissingReference()
    {
        var go = new GameObject("Gone");
        Object.DestroyImmediate(go);
        Assert.IsTrue(go == null);
        var e = Assert.Throws<MissingReferenceException>(() => { var _ = go.transform; });
        StringAssert.Contains("has been destroyed but you are still trying to access it", e.Message);
    }

    [Test] public void InstantiateKeepsFakeNulls()
    {
        var h = new GameObject("H").AddComponent<Holder>();
        var copy = Object.Instantiate(h);
        Assert.IsTrue(copy.target == null);
        Assert.Throws<UnassignedReferenceException>(() => { var _ = copy.target.position; });
    }
}
