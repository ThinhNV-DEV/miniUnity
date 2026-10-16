using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

// Timeline AnimationTrack through the animation playables: the director's graph (TimelinePlayable → AnimationTrack mixer →
// clip playables → AnimationPlayableOutput bound to an Animator) poses the rig when the director is evaluated.
public class TimelineAnimationTests
{
    static AnimationClip Clip(string name)
    {
#if UNITY_EDITOR
        var c = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/AnimTest/{name}.anim");
#else
        AnimationClip c = null;
#endif
        Assert.IsNotNull(c, name);
        return c;
    }

    [Test] public void AnimationTrackPosesTheBoundAnimator()
    {
        var go = new GameObject("Rig");
        var body = new GameObject("Body").transform; body.SetParent(go.transform, false);
        var animator = go.AddComponent<Animator>();
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        var track = timeline.CreateTrack<AnimationTrack>(null, "Anim");
        var idle = track.CreateClip(Clip("Idle"));
        idle.start = 1; idle.duration = 1;
        var run = track.CreateClip(Clip("Run"));
        run.start = 3; run.duration = 1;
        var director = go.AddComponent<PlayableDirector>();
        director.playableAsset = timeline;
        director.SetGenericBinding(track, animator);

        director.time = 1.25; director.Evaluate();
        Assert.AreEqual(0.25f, body.localPosition.x, 1e-3f, "Idle clip at local time 0.25");
        director.time = 1.75; director.Evaluate();
        Assert.AreEqual(0.75f, body.localPosition.x, 1e-3f);
        director.time = 3.5; director.Evaluate();
        Assert.AreEqual(3f, body.localScale.x, 1e-3f, "Run clip sets scale.x");
    }
}
