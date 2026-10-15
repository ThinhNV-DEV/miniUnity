# Usage: python3 audioassets.py <unity project>  — writes Assets/AudioTest/tone440.wav (1 s, 440 Hz, 16-bit mono) used by AudioTests.cs.
import math, os, struct, sys
d = os.path.join(sys.argv[1], "Assets/AudioTest"); os.makedirs(d, exist_ok=True)
p = os.path.join(d, "tone440.wav")
rate = 44100
data = b"".join(struct.pack("<h", int(0.5 * 32767 * math.sin(2 * math.pi * 440 * i / rate))) for i in range(rate))
with open(p, "wb") as f:
    f.write(b"RIFF" + struct.pack("<I", 36 + len(data)) + b"WAVE" + b"fmt " + struct.pack("<IHHIIHH", 16, 1, 1, rate, rate * 2, 2, 16) + b"data" + struct.pack("<I", len(data)) + data)
open(p + ".meta", "w").write("fileFormatVersion: 2\nguid: d0000000000000000000000000000001\nAudioImporter:\n  serializedVersion: 6\n")

# compressed copies (Ogg Vorbis / MP3) of the same tone, encoded with ffmpeg
import shutil, subprocess
if shutil.which("ffmpeg"):
    for ext, codec, guid in ((".ogg", "libvorbis", "d0000000000000000000000000000002"), (".mp3", "libmp3lame", "d0000000000000000000000000000003")):
        out = os.path.join(d, "tone440" + ext)
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", p, "-c:a", codec, "-q:a", "4", out], check=True)
        open(out + ".meta", "w").write(f"fileFormatVersion: 2\nguid: {guid}\nAudioImporter:\n  serializedVersion: 6\n")

# a mixer: Master ← Music (-6 dB) / SFX (0 dB, muted in snapshot "Quiet" via -80 dB); Music volume exposed as "MusicVol"
mixer = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!241 &24100000
AudioMixerController:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: TestMixer
  m_OutputGroup: {fileID: 0}
  m_MasterGroup: {fileID: 24300002}
  m_Snapshots:
  - {fileID: 24500006}
  - {fileID: 24500008}
  m_StartSnapshot: {fileID: 24500006}
  m_SuspendThreshold: -80
  m_EnableSuspend: 1
  m_UpdateMode: 0
  m_ExposedParameters:
  - guid: a0000000000000000000000000000002
    name: MusicVol
  m_AudioMixerGroupViews: []
  m_CurrentViewIndex: 0
  m_TargetSnapshot: {fileID: 24500006}
--- !u!243 &24300002
AudioMixerGroupController:
  m_ObjectHideFlags: 0
  m_Name: Master
  m_AudioMixer: {fileID: 24100000}
  m_GroupID: b0000000000000000000000000000001
  m_Children:
  - {fileID: 24300004}
  - {fileID: 24300010}
  m_Volume: a0000000000000000000000000000001
  m_Pitch: c0000000000000000000000000000001
  m_Send: 00000000000000000000000000000000
  m_Effects: []
  m_UserColorIndex: 0
  m_Mute: 0
  m_Solo: 0
  m_BypassEffects: 0
--- !u!243 &24300004
AudioMixerGroupController:
  m_ObjectHideFlags: 0
  m_Name: Music
  m_AudioMixer: {fileID: 24100000}
  m_GroupID: b0000000000000000000000000000002
  m_Children: []
  m_Volume: a0000000000000000000000000000002
  m_Pitch: c0000000000000000000000000000002
  m_Send: 00000000000000000000000000000000
  m_Effects: []
  m_UserColorIndex: 0
  m_Mute: 0
  m_Solo: 0
  m_BypassEffects: 0
--- !u!243 &24300010
AudioMixerGroupController:
  m_ObjectHideFlags: 0
  m_Name: SFX
  m_AudioMixer: {fileID: 24100000}
  m_GroupID: b0000000000000000000000000000003
  m_Children: []
  m_Volume: a0000000000000000000000000000003
  m_Pitch: c0000000000000000000000000000003
  m_Send: 00000000000000000000000000000000
  m_Effects: []
  m_UserColorIndex: 0
  m_Mute: 0
  m_Solo: 0
  m_BypassEffects: 0
--- !u!245 &24500006
AudioMixerSnapshotController:
  m_ObjectHideFlags: 0
  m_Name: Snapshot
  m_AudioMixer: {fileID: 24100000}
  m_SnapshotID: e0000000000000000000000000000001
  m_FloatValues:
    a0000000000000000000000000000002: -6
  m_TransitionOverrides: {}
--- !u!245 &24500008
AudioMixerSnapshotController:
  m_ObjectHideFlags: 0
  m_Name: Quiet
  m_AudioMixer: {fileID: 24100000}
  m_SnapshotID: e0000000000000000000000000000002
  m_FloatValues:
    a0000000000000000000000000000002: -6
    a0000000000000000000000000000003: -80
  m_TransitionOverrides: {}
"""
mp = os.path.join(d, "TestMixer.mixer")
open(mp, "w").write(mixer)
open(mp + ".meta", "w").write("fileFormatVersion: 2\nguid: d0000000000000000000000000000004\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 24100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")

# the same mixer with SFX soloed (AudioEffectsTests)
solo = mixer.replace("m_Name: TestMixer", "m_Name: SoloMixer")
i = solo.index("m_Name: SFX"); j = solo.index("m_Solo: 0", i)
solo = solo[:j] + "m_Solo: 1" + solo[j + len("m_Solo: 0"):]
sp = os.path.join(d, "SoloMixer.mixer")
open(sp, "w").write(solo)
open(sp + ".meta", "w").write("fileFormatVersion: 2\nguid: d0000000000000000000000000000005\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 24100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
