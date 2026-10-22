using System.Collections.Generic;
using Pigeon.Movement;
using UnityEngine;

namespace PullYourselfTogether
{
    // What a crawling torso looks like: the dead player's own robot (a copy of their third-person model, their skin and all) with
    // its legs folded away, lying on its front where their torso is, pulling itself along: the arms reach ahead and drag back in
    // turn (an army crawl), the head up looking where it's going, the body rolling a little with each pull. It's animated here,
    // bone by bone, from how fast the torso is really going, on every machine with the mod (the others see the game's own torso
    // piece slide along). The game's torso piece is hidden while it shows; when someone picks the torso up, it's the piece again.
    internal sealed class Puppet : MonoBehaviour
    {
        private static readonly Dictionary<DeadPlayerPart, Puppet> all = new Dictionary<DeadPlayerPart, Puppet>();

        private DeadPlayerPart torso;
        private Transform root, chest, neck, shoulderL, shoulderR, elbowL, elbowR;
        private readonly List<Renderer> pieces = new List<Renderer>();   // (the game's torso piece, hidden)
        private Vector3 lastCentre, heading = Vector3.forward;
        private float phase, speed;
        private bool showing;

        // every frame: a puppet for each dead player's torso, gone with it
        public static void Tick()
        {
            if (!Plugin.Enabled.Value) { Clear(); return; }
            foreach (Player player in GameManager.players)
            {
                if (player == null || player.IsAlive) continue;
                DeadPlayerPart torso = Parts.Torso(player);
                if (torso != null && !all.ContainsKey(torso)) Make(player, torso);
            }
            if (all.Count == 0) return;
            List<DeadPlayerPart> gone = null;
            foreach (KeyValuePair<DeadPlayerPart, Puppet> kv in all)
                if (kv.Key == null || !kv.Key.IsSpawned || kv.Key.ParentPlayer == null || kv.Key.ParentPlayer.IsAlive)
                    (gone ??= new List<DeadPlayerPart>()).Add(kv.Key);
            if (gone == null) return;
            foreach (DeadPlayerPart part in gone)
            {
                if (all.TryGetValue(part, out Puppet puppet) && puppet != null) Destroy(puppet.gameObject);
                all.Remove(part);
            }
        }

        public static void Clear()
        {
            foreach (Puppet puppet in all.Values) if (puppet != null) Destroy(puppet.gameObject);
            all.Clear();
        }

        // A copy of the player's rig, made under a switched-off parent so none of the game's scripts on it wake up, then
        // stripped down to bones and skin.
        private static void Make(Player player, DeadPlayerPart torso)
        {
            ThirdPersonRig rig = player.Animator != null ? (player.Animator.ThirdPersonRig ?? player.Animator.LocalThirdPersonRig) : null;
            if (rig == null) return;
            var holder = new GameObject("PullYourselfTogether puppet");
            holder.SetActive(false);
            GameObject copy = Instantiate(rig.gameObject, holder.transform);
            foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is SkinnedMeshRenderer) && !(c is MeshFilter) && !(c is MeshRenderer)) DestroyImmediate(c);
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;   // (a local player's own body may sit on a layer their camera skips)
            foreach (SkinnedMeshRenderer smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.enabled = true;
                smr.updateWhenOffscreen = true;
            }
            copy.SetActive(true);
            var puppet = holder.AddComponent<Puppet>();
            puppet.torso = torso;
            puppet.root = copy.transform;
            Transform Bone(string name)
            {
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
                return null;
            }
            // the rest pose: everything straight, arms hanging
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("joint_") || t.name.Contains("Rotator")) t.localRotation = Quaternion.identity;
            puppet.chest = Bone("joint_Spine03");
            puppet.neck = Bone("joint_Neck");
            puppet.shoulderL = Bone("joint_Shoulder_L"); puppet.shoulderR = Bone("joint_Shoulder_R");
            puppet.elbowL = Bone("joint_Elbow_L"); puppet.elbowR = Bone("joint_Elbow_R");
            foreach (string leg in new[] { "joint_Thigh_L", "joint_Thigh_R" }) { Transform t = Bone(leg); if (t != null) t.localScale = Vector3.zero; }
            foreach (DeadPlayerPart.PartSectionData section in torso.sections)
                foreach (TargetMesh mesh in section.meshes) if (mesh != null && mesh.Renderer != null) puppet.pieces.Add(mesh.Renderer);
            puppet.lastCentre = Parts.Centre(torso);
            holder.SetActive(true);
            all[torso] = puppet;
            if (puppet.chest == null || puppet.shoulderL == null || puppet.shoulderR == null) { Plugin.Log.LogWarning("Pull Yourself Together: this robot's rig isn't the usual one: no crawling puppet"); Destroy(holder); all[torso] = null; }
        }

        private void LateUpdate()
        {
            if (torso == null) return;
            Vector3 centre = Parts.Centre(torso);
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 moved = centre - lastCentre;
            lastCentre = centre;
            moved.y = 0f;
            float now = moved.magnitude / dt;
            speed = Mathf.Lerp(speed, now > 20f ? 0f : now, 1f - Mathf.Exp(-10f * dt));   // (a teleport isn't a crawl)
            if (moved.sqrMagnitude > 1e-6f && now > 0.15f) heading = Vector3.Slerp(heading, moved.normalized, 1f - Mathf.Exp(-8f * dt));
            else if (Crawl.IsMine(torso)) heading = Vector3.Slerp(heading, Crawl.Facing, 1f - Mathf.Exp(-4f * dt));

            // carried (or flying): the game's piece shows instead
            bool show = !torso.IsHeldByAnyPlayer && !torso.Active;
            if (show != showing)
            {
                showing = show;
                root.gameObject.SetActive(show);
            }
            foreach (Renderer r in pieces) if (r != null) r.enabled = !show;
            if (!show) return;

            // a stroke a metre or so; resting, the arms ease to a reach
            phase += speed / 0.9f * dt;
            float roll = Mathf.Sin(phase * Mathf.PI * 2f) * 7f * Mathf.Clamp01(speed);

            // lying on its front, head the way it's going: the body's up (head) along the heading, its front to the ground
            Quaternion body = Quaternion.LookRotation(Vector3.down, heading) * Quaternion.AngleAxis(roll, Vector3.up);
            root.rotation = body;
            Arm(shoulderL, elbowL, phase, -1f);
            Arm(shoulderR, elbowR, phase + 0.5f, 1f);
            if (neck != null) neck.localRotation = Quaternion.AngleAxis(-55f, Vector3.right);   // (face up, looking ahead)
            // the chest where the torso is, a little off the ground
            root.position += centre + Vector3.up * 0.05f - chest.position;
        }

        // one arm through an army-crawl stroke: reach ahead, hand down, drag back to the hip; then elbow up and out, back to the
        // front. In the shoulder's own terms the arm hangs down -Y: turning it about X swings it toward the head (-180) through
        // the ground side (-90, the body's front), and about Z swings it out to the side (`side` -1 left, +1 right).
        private static void Arm(Transform shoulder, Transform elbow, float t, float side)
        {
            t -= Mathf.Floor(t);
            float swing, outward, bend;
            if (t < 0.55f)
            {
                float s = Ease(t / 0.55f);
                swing = Mathf.Lerp(-155f, -40f, s);
                outward = Mathf.Lerp(28f, 12f, s);
                bend = Mathf.Lerp(30f, 10f, s);
            }
            else
            {
                float s = (t - 0.55f) / 0.45f, arc = Mathf.Sin(s * Mathf.PI);
                swing = Mathf.Lerp(-40f, -155f, Ease(s));
                outward = Mathf.Lerp(12f, 28f, s) + 35f * arc;
                bend = Mathf.Lerp(10f, 30f, s) + 70f * arc;
            }
            shoulder.localRotation = Quaternion.AngleAxis(swing, Vector3.right) * Quaternion.AngleAxis(outward * side, Vector3.forward);
            if (elbow != null) elbow.localRotation = Quaternion.AngleAxis(-bend, Vector3.right);
        }

        private static float Ease(float x) => x * x * (3f - 2f * x);

        private void OnDestroy()
        {
            foreach (Renderer r in pieces) if (r != null) r.enabled = true;
        }
    }
}
