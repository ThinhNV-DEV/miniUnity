// ScriptableSingleton<T>: one instance per type, created on first access (editor tools and packages' [InitializeOnLoad] code use it).
// Nothing is persisted: Save writes no file.
namespace UnityEditor
{
    public partial class ScriptableSingleton<T>
    {
        static T s_Instance;
        public static T instance
        {
            get
            {
                if (s_Instance == null) { s_Instance = UnityEngine.ScriptableObject.CreateInstance<T>(); s_Instance.hideFlags = UnityEngine.HideFlags.HideAndDontSave; }
                return s_Instance;
            }
        }
    }
}
