using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Run with: ua test --filter InputTests --input "right@1-30,left@33-36,space@5,mouse=(0,0)-(100,50)@1-11,scroll=(0,1)@12,touch0=(10,10)-(50,10)@20-24,text=hi@3,Fire1@40"
public class InputTests
{
    [UnityTest] public IEnumerator LegacyInputFollowsInputManager()
    {
        yield return null;                                   // frame 1
        if (!Input.anyKey) Assert.Ignore("needs scripted input: run with the --input spec in the header comment");
        Assert.AreEqual(3f / 60f, Input.GetAxis("Horizontal"), 1e-4f, "sensitivity 3 units/s");
        Assert.AreEqual(1, Input.GetAxisRaw("Horizontal"));
        Assert.IsTrue(Input.GetKey(KeyCode.RightArrow)); Assert.IsTrue(Input.GetKey("right")); Assert.IsTrue(Input.GetKeyDown("right"));
        Assert.IsTrue(Input.GetButton("Horizontal"));
        Assert.Throws<ArgumentException>(() => Input.GetAxis("Horizontl"), "unknown axis throws like Unity");
        Assert.Throws<ArgumentException>(() => Input.GetKey("rightarrow"), "unknown key name throws");
        yield return null; yield return null;                // frame 3
        Assert.IsFalse(Input.GetKeyDown(KeyCode.RightArrow));
        Assert.AreEqual("hi", Input.inputString);
        yield return null; yield return null;                // frame 5
        Assert.IsTrue(Input.GetButtonDown("Jump")); Assert.IsTrue(Input.GetKeyDown(KeyCode.Space)); Assert.IsTrue(Input.anyKeyDown);
        yield return null;                                   // frame 6
        Assert.IsTrue(Input.GetButtonUp("Jump"));
        Assert.AreEqual(50, Input.mousePosition.x, 1e-3f, "mouse drag interpolates");
        Assert.AreEqual(10 * 0.1f, Input.GetAxis("Mouse X"), 1e-3f, "Mouse X = delta · sensitivity 0.1");
        for (int i = 0; i < 6; i++) yield return null;      // frame 12
        Assert.AreEqual(1, Input.mouseScrollDelta.y);
        for (int i = 0; i < 8; i++) yield return null;      // frame 20
        Assert.AreEqual(1, Input.touchCount); Assert.AreEqual(TouchPhase.Began, Input.GetTouch(0).phase);
        Assert.IsTrue(Input.GetMouseButtonDown(0), "touch 0 simulates mouse 0");
        yield return null;                                   // frame 21
        Assert.AreEqual(TouchPhase.Moved, Input.GetTouch(0).phase); Assert.AreEqual(10, Input.GetTouch(0).deltaPosition.x, 1e-3f);
        for (int i = 0; i < 4; i++) yield return null;      // frame 25
        Assert.AreEqual(TouchPhase.Ended, Input.GetTouch(0).phase);
        for (int i = 0; i < 5; i++) yield return null;      // frame 30
        Assert.AreEqual(1, Input.GetAxis("Horizontal"), 1e-4f, "saturates at 1");
        yield return null; yield return null;                // frame 32: released for 2 frames
        Assert.AreEqual(1 - 2 * 3f / 60f, Input.GetAxis("Horizontal"), 1e-4f, "gravity 3 units/s back to 0");
        yield return null;                                   // frame 33: opposite key, snap
        Assert.AreEqual(-3f / 60f, Input.GetAxis("Horizontal"), 1e-4f, "snap jumps through 0");
        for (int i = 0; i < 7; i++) yield return null;      // frame 40
        Assert.IsTrue(Input.GetButtonDown("Fire1"), "virtual button pressed by its positive button");
        Assert.IsTrue(Input.GetKey(KeyCode.LeftControl));
    }
}
