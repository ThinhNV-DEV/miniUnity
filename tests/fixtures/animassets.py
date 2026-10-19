#!/usr/bin/env python3
# Usage: python3 animassets.py <unity project>  — writes Assets/AnimTest (Hero.controller + clips) used by AnimatorTests.cs.
# AnimatorTests.SpriteSwapControllerFromUnitySample also needs the RoboHero sample (com.unity.2d.pixel-perfect Samples~/…/RoboHero/{Animations,Textures}) copied to Assets/RoboHero.
# Writes hand-authored Unity-format animation assets (controller + clips + behaviour script meta) for the Animator tests.
import os, sys
root = sys.argv[1]
d = os.path.join(root, "Assets/AnimTest"); os.makedirs(d, exist_ok=True)
def meta(path, guid, main=None):
    s = f"fileFormatVersion: 2\nguid: {guid}\n"
    if main is not None: s += f"NativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: {main}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    open(path + ".meta", "w").write(s)
HDR = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"
def key(t, v, i=0, o=0): return f"      - serializedVersion: 3\n        time: {t}\n        value: {v}\n        inSlope: {i}\n        outSlope: {o}\n        tangentMode: 0\n        weightedMode: 0\n        inWeight: 0.33333334\n        outWeight: 0.33333334\n"
def vkey(t, v, i=(0,0,0), o=(0,0,0)):
    f = lambda x: "{x: %s, y: %s, z: %s}" % x
    return f"      - serializedVersion: 3\n        time: {t}\n        value: {f(v)}\n        inSlope: {f(i)}\n        outSlope: {f(o)}\n        tangentMode: 0\n        weightedMode: 0\n        inWeight: {{x: 0.33333334, y: 0.33333334, z: 0.33333334}}\n        outWeight: {{x: 0.33333334, y: 0.33333334, z: 0.33333334}}\n"
def clip(name, guid, pos=None, floats=(), stop=1.0, loop=1, events=()):
    s = HDR + "--- !u!74 &7400000\nAnimationClip:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n"
    s += f"  m_Name: {name}\n  serializedVersion: 7\n  m_Legacy: 0\n  m_Compressed: 0\n  m_UseHighQualityCurve: 1\n  m_RotationCurves: []\n  m_CompressedRotationCurves: []\n  m_EulerCurves: []\n"
    if pos:
        path, keys = pos
        s += "  m_PositionCurves:\n  - curve:\n      serializedVersion: 2\n      m_Curve:\n" + "".join(vkey(*k) for k in keys) + "      m_PreInfinity: 2\n      m_PostInfinity: 2\n      m_RotationOrder: 4\n    path: " + path + "\n"
    else: s += "  m_PositionCurves: []\n"
    s += "  m_ScaleCurves: []\n"
    if floats:
        s += "  m_FloatCurves:\n"
        for (path, cls, attr, keys) in floats:
            s += "  - curve:\n      serializedVersion: 2\n      m_Curve:\n" + "".join(key(*k) for k in keys) + "      m_PreInfinity: 2\n      m_PostInfinity: 2\n      m_RotationOrder: 4\n"
            s += f"    attribute: {attr}\n    path: {path}\n    classID: {cls}\n    script: {{fileID: 0}}\n"
    else: s += "  m_FloatCurves: []\n"
    s += "  m_PPtrCurves: []\n  m_SampleRate: 60\n  m_WrapMode: 0\n  m_Bounds:\n    m_Center: {x: 0, y: 0, z: 0}\n    m_Extent: {x: 0, y: 0, z: 0}\n"
    s += f"  m_AnimationClipSettings:\n    serializedVersion: 2\n    m_AdditiveReferencePoseClip: {{fileID: 0}}\n    m_AdditiveReferencePoseTime: 0\n    m_StartTime: 0\n    m_StopTime: {stop}\n    m_OrientationOffsetY: 0\n    m_Level: 0\n    m_CycleOffset: 0\n    m_HasAdditiveReferencePose: 0\n    m_LoopTime: {loop}\n    m_LoopBlend: 0\n"
    s += "  m_EditorCurves: []\n  m_EulerEditorCurves: []\n  m_HasGenericRootTransform: 0\n  m_HasMotionFloatCurves: 0\n"
    if events:
        s += "  m_Events:\n"
        for (t, fn, fp, ip, data) in events:
            s += f"  - time: {t}\n    functionName: {fn}\n    data: {data}\n    objectReferenceParameter: {{fileID: 0}}\n    floatParameter: {fp}\n    intParameter: {ip}\n    messageOptions: 0\n"
    else: s += "  m_Events: []\n"
    p = os.path.join(d, name + ".anim"); open(p, "w").write(s); meta(p, guid, 7400000)

clip("Idle", "a0000000000000000000000000000001", pos=("Body", [(0, (0,0,0), (1,0,0), (1,0,0)), (1, (1,0,0), (1,0,0), (1,0,0))]), stop=1, loop=1)
clip("Walk", "a0000000000000000000000000000002", floats=[("Body", 4, "m_LocalScale.x", [(0, 1), (1, 1)])], stop=1, loop=1)
clip("Run", "a0000000000000000000000000000003", floats=[("Body", 4, "m_LocalScale.x", [(0, 3), (1, 3)])], stop=1, loop=1)
clip("Jump", "a0000000000000000000000000000004", pos=("Body", [(0, (0,0,0), (0,8,0), (0,8,0)), (0.25, (0,2,0)), (0.5, (0,0,0), (0,-8,0), (0,-8,0))]),
     floats=[("Body", 1, "m_IsActive", [(0, 1), (0.5, 1)])], stop=0.5, loop=0, events=[(0.25, "OnApex", 7.5, 3, "apex")])
clip("Wave", "a0000000000000000000000000000005", floats=[("Arm", 4, "localEulerAnglesRaw.z", [(0, 0, 90, 90), (1, 90, 90, 90)])], stop=1, loop=1)

BEH_GUID = "b0000000000000000000000000000001"
ctrl = HDR + """--- !u!91 &9100000
AnimatorController:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: Hero
  serializedVersion: 5
  m_AnimatorParameters:
  - m_Name: Speed
    m_Type: 1
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
  - m_Name: Jump
    m_Type: 9
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
  - m_Name: Mode
    m_Type: 3
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
  m_AnimatorLayers:
  - serializedVersion: 5
    m_Name: Base Layer
    m_StateMachine: {fileID: 1107000000000000001}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}
  - serializedVersion: 5
    m_Name: Upper
    m_StateMachine: {fileID: 1107000000000000002}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}
"""
def sm(fid, name, states, default, anys=(), entries=(), machines=(), behaviours=()):
    s = f"--- !u!1107 &{fid}\nAnimatorStateMachine:\n  serializedVersion: 6\n  m_ObjectHideFlags: 1\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_Name: {name}\n  m_ChildStates:\n"
    for st in states: s += f"  - serializedVersion: 1\n    m_State: {{fileID: {st}}}\n    m_Position: {{x: 0, y: 0, z: 0}}\n"
    s += "  m_ChildStateMachines:" + ("".join(f"\n  - serializedVersion: 1\n    m_StateMachine: {{fileID: {m}}}\n    m_Position: {{x: 0, y: 0, z: 0}}" for m in machines) if machines else " []") + "\n"
    s += "  m_AnyStateTransitions:" + ("".join(f"\n  - {{fileID: {t}}}" for t in anys) if anys else " []") + "\n"
    s += "  m_EntryTransitions:" + ("".join(f"\n  - {{fileID: {t}}}" for t in entries) if entries else " []") + "\n"
    s += "  m_StateMachineTransitions: {}\n"
    s += "  m_StateMachineBehaviours:" + ("".join(f"\n  - {{fileID: {b}}}" for b in behaviours) if behaviours else " []") + "\n"
    s += f"  m_AnyStatePosition: {{x: 0, y: 0, z: 0}}\n  m_EntryPosition: {{x: 0, y: 0, z: 0}}\n  m_ExitPosition: {{x: 0, y: 0, z: 0}}\n  m_ParentStateMachinePosition: {{x: 800, y: 20, z: 0}}\n  m_DefaultState: {{fileID: {default}}}\n"
    return s
def state(fid, name, motion, transitions=(), behaviours=(), speed=1, tag="", wd=1, motion_file=None):
    mref = f"{{fileID: 7400000, guid: {motion}, type: 2}}" if motion and len(str(motion)) == 32 else (f"{{fileID: {motion}}}" if motion else "{fileID: 0}")
    s = f"--- !u!1102 &{fid}\nAnimatorState:\n  serializedVersion: 6\n  m_ObjectHideFlags: 1\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_Name: {name}\n  m_Speed: {speed}\n  m_CycleOffset: 0\n"
    s += "  m_Transitions:" + ("".join(f"\n  - {{fileID: {t}}}" for t in transitions) if transitions else " []") + "\n"
    s += "  m_StateMachineBehaviours:" + ("".join(f"\n  - {{fileID: {b}}}" for b in behaviours) if behaviours else " []") + "\n"
    s += f"  m_Position: {{x: 50, y: 50, z: 0}}\n  m_IKOnFeet: 0\n  m_WriteDefaultValues: {wd}\n  m_Mirror: 0\n  m_SpeedParameterActive: 0\n  m_MirrorParameterActive: 0\n  m_CycleOffsetParameterActive: 0\n  m_TimeParameterActive: 0\n  m_Motion: {mref}\n  m_Tag: {tag}\n  m_SpeedParameter: \n  m_MirrorParameter: \n  m_CycleOffsetParameter: \n  m_TimeParameter: \n"
    return s
def trans(fid, dst, conds=(), exit_time=0.75, has_exit=0, dur=0.25, fixed=1, offset=0, interrupt=0, self_=1, is_exit=0):
    s = f"--- !u!1101 &{fid}\nAnimatorStateTransition:\n  m_ObjectHideFlags: 1\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_Name: \n  m_Conditions:"
    s += "".join(f"\n  - m_ConditionMode: {m}\n    m_ConditionEvent: {p}\n    m_EventTreshold: {t}" for (m, p, t) in conds) if conds else " []"
    s += f"\n  m_DstStateMachine: {{fileID: 0}}\n  m_DstState: {{fileID: {dst}}}\n  m_Solo: 0\n  m_Mute: 0\n  m_IsExit: {is_exit}\n  serializedVersion: 3\n  m_TransitionDuration: {dur}\n  m_TransitionOffset: {offset}\n  m_ExitTime: {exit_time}\n  m_HasExitTime: {has_exit}\n  m_HasFixedDuration: {fixed}\n  m_InterruptionSource: {interrupt}\n  m_OrderedInterruption: 1\n  m_CanTransitionToSelf: {self_}\n"
    return s
IDLE, RUN, JUMP, WAVE, NONE = 1102000000000000001, 1102000000000000002, 1102000000000000003, 1102000000000000004, 1102000000000000005
TREE = 20600000000000001
ctrl += sm(1107000000000000001, "Base Layer", [IDLE, RUN, JUMP], IDLE, anys=[1101000000000000005])
ctrl += sm(1107000000000000002, "Upper", [NONE, WAVE], NONE, anys=[1101000000000000006, 1101000000000000007])
ctrl += state(IDLE, "Idle", "a0000000000000000000000000000001", transitions=[1101000000000000001], tag="Ground")
ctrl += state(RUN, "Run", TREE, transitions=[1101000000000000002], tag="Ground")
ctrl += state(JUMP, "Jump", "a0000000000000000000000000000004", transitions=[1101000000000000003], behaviours=[11400000000000001])
ctrl += state(NONE, "None", None)
ctrl += state(WAVE, "Wave", "a0000000000000000000000000000005")
ctrl += trans(1101000000000000001, RUN, conds=[(3, "Speed", 0.1)], dur=0.25)
ctrl += trans(1101000000000000002, IDLE, conds=[(4, "Speed", 0.1)], dur=0.25)
ctrl += trans(1101000000000000003, IDLE, has_exit=1, exit_time=1, dur=0.1)
ctrl += trans(1101000000000000005, JUMP, conds=[(1, "Jump", 0)], dur=0, self_=0)
ctrl += trans(1101000000000000006, WAVE, conds=[(6, "Mode", 1)], dur=0, self_=0)
ctrl += trans(1101000000000000007, NONE, conds=[(6, "Mode", 0)], dur=0, self_=0)
ctrl += f"""--- !u!206 &{TREE}
BlendTree:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Locomotion
  m_Childs:
  - serializedVersion: 2
    m_Motion: {{fileID: 7400000, guid: a0000000000000000000000000000002, type: 2}}
    m_Threshold: 0
    m_Position: {{x: 0, y: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: Speed
    m_Mirror: 0
  - serializedVersion: 2
    m_Motion: {{fileID: 7400000, guid: a0000000000000000000000000000003, type: 2}}
    m_Threshold: 1
    m_Position: {{x: 0, y: 0}}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: Speed
    m_Mirror: 0
  m_BlendParameter: Speed
  m_BlendParameterY: Speed
  m_MinThreshold: 0
  m_MaxThreshold: 1
  m_UseAutomaticThresholds: 0
  m_NormalizedBlendValues: 0
  m_BlendType: 0
--- !u!114 &11400000000000001
MonoBehaviour:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {BEH_GUID}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
  label: jump
"""
p = os.path.join(d, "Hero.controller"); open(p, "w").write(ctrl); meta(p, "c0000000000000000000000000000001", 9100000)
open(os.path.join(root, "Assets/PlayTests/AnimTestBehaviour.cs.meta"), "w").write(f"fileFormatVersion: 2\nguid: {BEH_GUID}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData: \n")
print("ok")
