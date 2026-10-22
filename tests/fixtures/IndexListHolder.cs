using System.Collections.Generic;
using UnityEngine;

// A list of ints for PrefabArrayModificationEditorTests: Unity writes it as a hex string, empty when the list is.
public class IndexListHolder : MonoBehaviour
{
    public List<int> indices = new();
}
