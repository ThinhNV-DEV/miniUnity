// Editor-side members that runtime package code and project scripts touch while playing in the Editor (TextMeshPro
// Awake, editor singletons). MiniUnity plays as the Editor would, never while building a player.
using UnityEngine;

namespace UnityEditor
{
    public partial class BuildPipeline
    {
        public static bool isBuildingPlayer => false;
    }

    public partial class ScriptableSingleton<T>
    {
        static T s_Instance;
        /// <summary>One instance per type, created on first use (Unity also loads it from its FilePath asset when there is one).</summary>
        public static T instance => s_Instance != null ? s_Instance : s_Instance = ScriptableObject.CreateInstance<T>();
    }
}

