using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public class UnhideAnimations : ScriptableObject
{
    [MenuItem("Tools/" + CanvasToolsStatics.CANVAS_TOOLS_MENU + "/Animation Utilities/Unhide Animation Clips", false, 99)]
    private static void UnhideFix()
    {
        UnityEditor.Animations.AnimatorController ac = Selection.activeObject as UnityEditor.Animations.AnimatorController;
        if (ac != null)
        {
            foreach (UnityEditor.Animations.AnimatorControllerLayer layer in ac.layers)
            {
                foreach (UnityEditor.Animations.ChildAnimatorState curState in layer.stateMachine.states)
                {
                    if (curState.state.hideFlags != 0)
                    {
                        curState.state.hideFlags = (HideFlags)1;
                    }
                    foreach (var trans in curState.state.transitions)
                    {
                        if (trans.hideFlags == (HideFlags)3) trans.hideFlags = (HideFlags)1;
                    }
                    if (curState.state.motion != null)
                    {
                        ProcessMotion(curState.state.motion);
                    }
                }
                foreach (var anyTrans in layer.stateMachine.anyStateTransitions)
                {
                    if (anyTrans.hideFlags != 0) anyTrans.hideFlags = (HideFlags)1;
                }
                foreach (var entryTrans in layer.stateMachine.entryTransitions)
                {
                    if (entryTrans.hideFlags != 0) entryTrans.hideFlags = (HideFlags)1;
                }
            }

            EditorUtility.SetDirty(ac);
            string msg = "Animation controller fixed: " + ac.name;
            UnityEngine.Debug.Log(msg);
            EditorUtility.DisplayDialog("Babylon Toolkit", msg, "OK");
        }
        else
        {
            string warning = "You select an animation controller to fix.";
            UnityEngine.Debug.LogWarning(warning);
            EditorUtility.DisplayDialog("Babylon Toolkit", warning, "OK");
        }
    }
    private static void ProcessMotion(Motion motion)
    {
        if (motion.hideFlags == (HideFlags)3)
        {
            motion.hideFlags = (HideFlags)1;
        }
        if (motion is BlendTree)
        {
            var tree = motion as BlendTree;
            foreach (var child in tree.children)
            {
                if (child.motion != null)
                {
                    ProcessMotion(child.motion);
                }
            }
        }
    }
}