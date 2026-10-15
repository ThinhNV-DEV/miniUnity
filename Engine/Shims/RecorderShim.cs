// Unity Recorder API surface used by VideoExport.cs. The Unity Recorder itself is not emulated: StartRecording reports that
// and returns false; MiniUnity records videos its own way (`record.sh` → Agent/Recorder.cs: Play mode + software renderer +
// ffmpeg). Everything else in VideoExport (SRT, YouTube text, thumbnail copy, ffmpeg re-encode) runs as is.
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.Recorder
{
    public enum FrameRatePlayback { Constant, Variable }
    public static class RecorderOptions { public static bool VerboseMode { get; set; } }

    public class RecorderSettings : ScriptableObject
    {
        public bool Enabled { get; set; }
        public string OutputFile { get; set; }
    }

    public class MovieRecorderSettings : RecorderSettings
    {
        public Encoder.IEncoderSettings EncoderSettings { get; set; }
        public bool CaptureAudio { get; set; }
        public Input.ImageInputSettings ImageInputSettings { get; set; }
    }

    public class RecorderControllerSettings : ScriptableObject
    {
        readonly List<RecorderSettings> m_Settings = new();
        public IEnumerable<RecorderSettings> RecorderSettings => m_Settings;
        public void AddRecorderSettings(RecorderSettings s) => m_Settings.Add(s);
        public void SetRecordModeToManual() { }
        public FrameRatePlayback FrameRatePlayback { get; set; }
        public float FrameRate { get; set; } = 30;
        public bool CapFrameRate { get; set; }
    }

    public class RecorderController
    {
        public RecorderController(RecorderControllerSettings settings) { Settings = settings; }
        public RecorderControllerSettings Settings { get; }
        public void PrepareRecording() { }
        public bool StartRecording()
        {
            Debug.LogError("[MiniUnity] Unity Recorder is not emulated – use MiniUnity/record.sh (or export the MP4 from Unity).");
            return false;
        }
        public bool IsRecording() => false;
        public void StopRecording() { }
    }
}

namespace UnityEditor.Recorder.Encoder
{
    public interface IEncoderSettings { }
    public class CoreEncoderSettings : IEncoderSettings
    {
        public enum OutputCodec { MP4, WEBM }
        public enum VideoEncodingQuality { Low, Medium, High, Custom }
        public enum H264EncodingProfile { Baseline, Main, High }
        public OutputCodec Codec { get; set; }
        public VideoEncodingQuality EncodingQuality { get; set; }
        public H264EncodingProfile EncodingProfile { get; set; }
        public float TargetBitRate { get; set; }
        public uint GopSize { get; set; }
        public uint NumConsecutiveBFrames { get; set; }
    }
}

namespace UnityEditor.Recorder.Input
{
    public class ImageInputSettings { public int OutputWidth { get; set; } public int OutputHeight { get; set; } }
    public class GameViewInputSettings : ImageInputSettings { }
}
