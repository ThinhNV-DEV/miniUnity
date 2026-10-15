using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// MaterialPropertyBlock, Renderer.Set/GetPropertyBlock, RectTransformUtility, Cursor / Display, and components kept as serialized data.
public class RenderApiTests
{
    [Test] public void PropertyBlockStoresValuesByKind()
    {
        var b = new MaterialPropertyBlock();
        Assert.IsTrue(b.isEmpty);
        b.SetFloat("_Glossiness", 0.25f);
        b.SetColor("_Color", new Color(1, 0.5f, 0, 1));
        b.SetInteger("_Count", 3);
        b.SetMatrix("_M", Matrix4x4.Translate(Vector3.one));
        Assert.AreEqual(0.25f, b.GetFloat("_Glossiness"));
        Assert.AreEqual(new Color(1, 0.5f, 0, 1).linear, (Color)b.GetVector("_Color"), "colours and vectors share a slot (linear-space project: the shader value)");
        Assert.AreEqual(3, b.GetInteger(Shader.PropertyToID("_Count")));
        Assert.AreEqual(1f, b.GetMatrix("_M").m03);
        Assert.IsTrue(b.HasFloat("_Glossiness")); Assert.IsTrue(b.HasColor("_Color")); Assert.IsFalse(b.HasTexture("_Color"));
        Assert.AreEqual(0f, b.GetFloat("_Missing"));
        b.Clear();
        Assert.IsTrue(b.isEmpty);
    }

    [Test] public void PropertyBlockArraysKeepTheirFirstLength()
    {
        var b = new MaterialPropertyBlock();
        b.SetVectorArray("_Colors", new[] { Vector4.one, Vector4.zero, Vector4.one });
        b.SetVectorArray("_Colors", new List<Vector4> { new Vector4(2, 2, 2, 2) });
        var got = b.GetVectorArray("_Colors");
        Assert.AreEqual(3, got.Length);
        Assert.AreEqual(new Vector4(2, 2, 2, 2), got[0], "a shorter array overwrites the start");
        Assert.AreEqual(Vector4.zero, got[1], "and keeps the rest");
        UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("exceeds previous array size"));
        b.SetFloatArray("_F", new float[2]);
        b.SetFloatArray("_F", new float[] { 1, 2, 3, 4 });
        Assert.AreEqual(2, b.GetFloatArray("_F").Length, "a longer array is capped");
        var list = new List<float>();
        b.GetFloatArray("_F", list);
        CollectionAssert.AreEqual(new[] { 1f, 2f }, list);
        Assert.IsNull(b.GetFloatArray("_None"));
    }

    [Test] public void RendererCopiesTheBlock()
    {
        var r = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<Renderer>();
        Assert.IsFalse(r.HasPropertyBlock());
        var b = new MaterialPropertyBlock();
        b.SetColor("_Color", Color.red);
        r.SetPropertyBlock(b);
        b.SetColor("_Color", Color.blue);   // not applied until set again
        var read = new MaterialPropertyBlock();
        r.GetPropertyBlock(read);
        Assert.AreEqual(Color.red, read.GetColor("_Color"));
        Assert.IsTrue(r.HasPropertyBlock());
        r.SetPropertyBlock(null);
        Assert.IsFalse(r.HasPropertyBlock());
        r.SetPropertyBlock(b, 0);
        r.GetPropertyBlock(read, 0);
        Assert.AreEqual(Color.blue, read.GetColor("_Color"));
    }

    static RectTransform OverlayRect(Vector2 size)
    {
        var canvas = new GameObject("Canvas", typeof(RectTransform)).AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var child = new GameObject("Box", typeof(RectTransform)).GetComponent<RectTransform>();
        child.SetParent(canvas.transform, false);
        child.sizeDelta = size;
        return child;
    }

    [Test] public void RectangleContainsScreenPointOnAnOverlayCanvas()
    {
        var rt = OverlayRect(new Vector2(100, 100));
        var centre = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Assert.IsTrue(RectTransformUtility.RectangleContainsScreenPoint(rt, centre));
        Assert.IsTrue(RectTransformUtility.RectangleContainsScreenPoint(rt, centre + new Vector2(49, -49)));
        Assert.IsFalse(RectTransformUtility.RectangleContainsScreenPoint(rt, centre + new Vector2(51, 0)));
        Assert.IsFalse(RectTransformUtility.RectangleContainsScreenPoint(rt, centre + new Vector2(45, 0), null, new Vector4(0, 0, 10, 0)), "padding (left, bottom, right, top) shrinks the area");
        Assert.IsTrue(RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, centre + new Vector2(10, 20), null, out var local));
        Assert.AreEqual(10f, local.x, 1e-3f); Assert.AreEqual(20f, local.y, 1e-3f);
        Assert.AreEqual(new Vector2(3, 4), RectTransformUtility.WorldToScreenPoint(null, new Vector3(3, 4, 5)));
    }

    [Test] public void FlipLayoutOnAxisMirrorsPivotAnchorsAndPosition()
    {
        var rt = OverlayRect(new Vector2(10, 10));
        rt.anchorMin = new Vector2(0.1f, 0.2f); rt.anchorMax = new Vector2(0.3f, 0.4f); rt.pivot = new Vector2(0.25f, 0.5f); rt.anchoredPosition = new Vector2(5, 6);
        RectTransformUtility.FlipLayoutOnAxis(rt, 0, false, false);
        Assert.AreEqual(0.75f, rt.pivot.x, 1e-6f);
        Assert.AreEqual(0.7f, rt.anchorMin.x, 1e-6f); Assert.AreEqual(0.9f, rt.anchorMax.x, 1e-6f);
        Assert.AreEqual(-5f, rt.anchoredPosition.x);
        var fixedSize = OverlayRect(new Vector2(10, 12));
        var b = RectTransformUtility.CalculateRelativeRectTransformBounds(fixedSize);
        Assert.AreEqual(new Vector3(10, 12, 0), b.size);
    }

    [Test] public void CursorAndDisplay()
    {
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState); Assert.IsFalse(Cursor.visible);
        Assert.AreEqual(Screen.width, Display.main.systemWidth);
        Assert.AreEqual(1, Display.displays.Length);
        Assert.AreEqual(Vector3.zero, Display.RelativeMouseAt(new Vector3(10, 10)));
    }

    [Test] public void ParticleAndLineRenderersAreComponents()
    {
        var go = new GameObject("Fx");
        var ps = go.AddComponent<ParticleSystem>();
        var lr = new GameObject("Line").AddComponent<LineRenderer>();
        Assert.AreSame(ps, go.GetComponent<ParticleSystem>());
        Assert.IsNotNull(go.GetComponent<ParticleSystemRenderer>(), "ParticleSystem requires its renderer");
        Assert.AreSame(lr, lr.gameObject.GetComponent<LineRenderer>());
    }
}
