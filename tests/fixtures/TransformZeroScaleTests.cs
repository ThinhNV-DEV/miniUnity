using NUnit.Framework;
using UnityEngine;

// A parent with a zero scale axis (2D art often has scale (1, 1, 0)) is not invertible; Unity inverts it level by level with
// InverseSafe scale, so setting a child's world position still places it on the other axes.
public class TransformZeroScaleTests
{
    [Test] public void PositionUnderZeroZScaleParentKeepsXY()
    {
        var parent = new GameObject("Board").transform;
        parent.position = new Vector3(-0.25f, -0.25f, 0); parent.localScale = new Vector3(1, 1, 0);
        var child = new GameObject("Cell").transform; child.SetParent(parent, false);
        child.position = new Vector3(3.5f, 2.5f, 0);
        Assert.AreEqual(3.5f, child.position.x, 1e-5f);
        Assert.AreEqual(2.5f, child.position.y, 1e-5f);
        Assert.AreEqual(new Vector3(3.75f, 2.75f, 0), child.localPosition);
        var local = parent.InverseTransformPoint(new Vector3(1, 1, 5));
        Assert.AreEqual(1.25f, local.x, 1e-5f); Assert.AreEqual(1.25f, local.y, 1e-5f); Assert.AreEqual(0, local.z, "1/0 → 0 on the flat axis");
    }
}
