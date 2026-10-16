// Oracle tests: the same measurements run in the real Unity Editor and in MiniUnity. Unity's values are committed
// (tests/oracle/expected.json, made with run_oracle.py --make-unity-project / --import); run_oracle.py compares MiniUnity's.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public static class Oracle
{
    static readonly SortedDictionary<string, (double value, double tol)> values = new();

    /// <summary>Record one measurement; <paramref name="tolerance"/> is how far MiniUnity may be from Unity.</summary>
    public static void Report(string key, double value, double tolerance)
    {
        values[key] = (value, tolerance);
        Save();
    }

    public static void Report(string key, Vector3 v, double tolerance)
    {
        Report(key + ".x", v.x, tolerance); Report(key + ".y", v.y, tolerance); Report(key + ".z", v.z, tolerance);
    }

    static void Save()
    {
        var path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "OracleResults.json");
        bool mini = typeof(Object).Assembly.GetName().Name == "MiniUnity";
        var lines = values.Select(kv => $"    \"{kv.Key}\": {{\"value\": {kv.Value.value.ToString("R", CultureInfo.InvariantCulture)}, \"tolerance\": {kv.Value.tol.ToString("R", CultureInfo.InvariantCulture)}}}");
        File.WriteAllText(path, "{\n  \"engine\": \"" + (mini ? "MiniUnity" : "Unity " + Application.unityVersion) + "\",\n  \"values\": {\n" + string.Join(",\n", lines) + "\n  }\n}\n");
    }
}
