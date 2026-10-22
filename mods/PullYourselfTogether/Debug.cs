using System.Text;
using Pigeon.Movement;
using UnityEngine;

namespace PullYourselfTogether
{
    // (for working out the robot's rig: its bones, as they sit, into the log)
    internal static class Debug
    {
        public static void DumpRig()
        {
            Player me = Player.LocalPlayer;
            if (me == null || me.Animator == null) return;
            foreach (ThirdPersonRig rig in new[] { me.Animator.ThirdPersonRig, me.Animator.LocalThirdPersonRig })
            {
                if (rig == null) continue;
                var sb = new StringBuilder();
                sb.Append("rig ").Append(rig.name).Append(" active ").Append(rig.gameObject.activeInHierarchy).Append('\n');
                Dump(rig.transform, 0, sb);
                foreach (SkinnedMeshRenderer smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    sb.Append("skinned ").Append(smr.name).Append(": ").Append(smr.bones.Length).Append(" bones, root ")
                      .Append(smr.rootBone != null ? smr.rootBone.name : "-").Append(", mesh ").Append(smr.sharedMesh != null ? smr.sharedMesh.name : "-").Append('\n');
                Plugin.Log.LogInfo(sb.ToString());
            }
        }

        private static void Dump(Transform t, int depth, StringBuilder sb)
        {
            sb.Append(' ', depth * 2).Append(t.name).Append(' ').Append(t.localPosition.ToString("F2")).Append(' ').Append(t.localEulerAngles.ToString("F0"));
            foreach (Component c in t.GetComponents<Component>()) if (!(c is Transform)) sb.Append(" [").Append(c.GetType().Name).Append(']');
            sb.Append('\n');
            foreach (Transform child in t) Dump(child, depth + 1, sb);
        }
    }
}
