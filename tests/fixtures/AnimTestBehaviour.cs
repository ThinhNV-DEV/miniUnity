using System.Collections.Generic;
using UnityEngine;

public class AnimTestBehaviour : StateMachineBehaviour
{
    public string label;
    public static readonly List<string> Log = new List<string>();
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) => Log.Add($"enter:{label}:{stateInfo.IsName("Jump")}");
    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { if (!Log.Contains("update:" + label)) Log.Add("update:" + label); }
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) => Log.Add($"exit:{label}");
}
