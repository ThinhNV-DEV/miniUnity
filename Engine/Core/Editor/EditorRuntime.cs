// Editor APIs packages query while initializing under `ua play` / `ua test` (MiniUnity is one main editor process, no workers).
namespace UnityEditor.MPE
{
    public partial class ProcessService
    {
        public static ProcessLevel level => ProcessLevel.Main;
        public static bool IsChannelServiceStarted() => false;
        public static string ReadParameter(string paramName) => null;
        public static bool HasCapability(string capName) => false;
    }
}

namespace UnityEditor
{
    public sealed partial class AssetDatabase
    {
        public static bool IsAssetImportWorkerProcess() => false;
    }
}
