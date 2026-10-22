using System.Collections.Generic;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PullYourselfTogether
{
    // Crawling. A dead player whose camera is on their own torso drives it: WASD (or the left stick) pulls it along the ground
    // the way the camera faces, surging with each arm's pull; Space (or A) lunges it up and forward over a bump. Their camera
    // follows low behind it (the mouse or right stick turns it), and once the torso is within the game's 2 m of their legs,
    // the game's own reconnect brings them back. (Puppet animates it; Hud shows them the way to their legs.)
    //  - the dead player's machine reads the controls and turns the camera; the host (where the parts' physics runs) moves
    //    the torso: held still by physics while it crawls (no sliding box, no tumbling), it follows the ground, steps up
    //    small ledges, slides along walls, and only goes back to physics to lunge, to fall, or when it's left alone;
    //  - a host without the mod can't be asked to crawl it, but anyone may throw a part: so then the torso flops along in
    //    little throws instead (the game's own reconnect still catches it).
    internal static class Crawl
    {
        public struct Order { public Vector2 Move; public byte Lunges; public float Time; }

        private sealed class Body
        {
            public float Phase, NextLunge, AirUntil, RestHeight = 0.25f;
            public Vector3 Heading = Vector3.forward, Velocity;
            public byte Lunges;
            public bool Steering;   // (held by us, not by physics)
            public int Solid;       // (what it would hit by itself: its layer's collisions)
        }

        public static readonly Dictionary<ulong, Order> Inputs = new Dictionary<ulong, Order>();   // (host) each crawler's latest
        private static readonly Dictionary<DeadPlayerPart, Body> bodies = new Dictionary<DeadPlayerPart, Body>();

        // how the game's torso piece lies when it crawls, relative to facing the way it goes (for players without the mod)
        public static Quaternion Pose = Quaternion.Euler(Plugin.PoseEuler);

        private const float StepUp = 0.5f, Radius = 0.22f;

        // --- the host: moving the torsos ---

        public static void SetInput(ulong client, Vector2 move, byte lunges) =>
            Inputs[client] = new Order { Move = Vector2.ClampMagnitude(move, 1f), Lunges = lunges, Time = Time.time };

        public static void HostFixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            foreach (Player player in GameManager.players)
            {
                if (player == null || player.IsAlive) continue;
                DeadPlayerPart torso = Parts.Torso(player);
                if (torso == null || torso.rigidbody == null) continue;
                bodies.TryGetValue(torso, out Body body);
                bool fresh = Inputs.TryGetValue(player.OwnerClientId, out Order order) && Time.time - order.Time < 0.5f;
                bool free = !torso.Active && !torso.IsHeldByAnyPlayer;
                if (!fresh || !free || Time.time - torso.__spawnTime < 1.5f)
                {
                    // left alone (or picked up): physics has it again
                    if (body != null && body.Steering && free) Release(torso, body, Vector3.zero);
                    else if (body != null) body.Steering = false;
                    continue;
                }
                if (body == null) bodies[torso] = body = new Body { Lunges = order.Lunges, Solid = SolidFor(torso) };
                Move(torso, body, order, dt);
                // (the game only checks a resting part that's been thrown: a crawling one, we check)
                torso.HandleAutoConnect();
            }
            if (bodies.Count > 0 && Time.frameCount % 300 == 0)
            {
                var gone = new List<DeadPlayerPart>();
                foreach (DeadPlayerPart part in bodies.Keys) if (part == null || !part.IsSpawned) gone.Add(part);
                foreach (DeadPlayerPart part in gone) bodies.Remove(part);
            }
        }

        private static void Move(DeadPlayerPart torso, Body body, Order order, float dt)
        {
            Rigidbody rb = torso.rigidbody;
            Vector3 centre = Parts.Centre(torso);
            Vector3 dir = new Vector3(order.Move.x, 0f, order.Move.y);
            float amount = Mathf.Clamp01(dir.magnitude);
            if (amount > 0.1f) body.Heading = dir / amount;

            // a lunge: physics throws it, and we take it back once it lands
            if (order.Lunges != body.Lunges)
            {
                body.Lunges = order.Lunges;
                if (body.Steering && Time.time >= body.NextLunge)
                {
                    body.NextLunge = Time.time + 1.1f;
                    body.AirUntil = Time.time + 0.35f;
                    Release(torso, body, body.Heading * Plugin.CrawlSpeed.Value * 1.8f + Vector3.up * 5.5f);
                    return;
                }
            }
            if (!body.Steering)
            {
                if (Time.time < body.AirUntil || !Ground(centre, 0.6f, body.Solid, out float groundY) || rb.linearVelocity.y > 0.5f) return;
                // landed: ours again
                body.Steering = true;
                body.RestHeight = Mathf.Clamp(centre.y - groundY, 0.18f, 0.4f);
                body.Velocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                rb.isKinematic = true;
            }

            // each arm's pull a surge, the reach between them slower; averaging CrawlSpeed
            body.Phase += amount * (Plugin.CrawlSpeed.Value / 0.9f) * dt;
            float pull = Mathf.Max(0f, Mathf.Sin(body.Phase * Mathf.PI * 4f));   // (two arms: two pulls a cycle)
            Vector3 want = body.Heading * (amount * Plugin.CrawlSpeed.Value * (0.55f + 0.9f * pull));
            body.Velocity = Vector3.Lerp(body.Velocity, want, 1f - Mathf.Exp(-(amount > 0.1f ? 10f : 6f) * dt));

            Vector3 step = body.Velocity * dt;
            // walls: slide along them
            if (step.sqrMagnitude > 1e-8f && Wall(centre, step, body.Solid, out RaycastHit wall))
            {
                Vector3 n = new Vector3(wall.normal.x, 0f, wall.normal.z).normalized;
                step -= n * Vector3.Dot(step, n);
                body.Velocity -= n * Vector3.Dot(body.Velocity, n);
            }
            Vector3 next = centre + step;
            // the ground: stepping up what's low enough, down what's there; nothing under it: a fall (physics again)
            if (Ground(next, StepUp + body.RestHeight, body.Solid, out float floor))
                next.y = Mathf.MoveTowards(centre.y, floor + body.RestHeight, 4f * dt);
            else if (!Ground(next, StepUp + body.RestHeight + 1.5f, body.Solid, out _))
            {
                Release(torso, body, body.Velocity);
                return;
            }
            rb.MovePosition(rb.position + (next - centre));
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(body.Heading) * Pose, 1f - Mathf.Exp(-8f * dt)));
        }

        private static void Release(DeadPlayerPart torso, Body body, Vector3 velocity)
        {
            body.Steering = false;
            if (torso.Active || torso.IsHeldByAnyPlayer) return;
            torso.rigidbody.isKinematic = false;
            torso.rigidbody.linearVelocity = velocity;
            torso.rigidbody.angularVelocity = Vector3.zero;
        }

        // Everything the torso piece's own layer collides with (so it stands on whatever physics would land it on, whatever
        // layer a level puts its floors on).
        private static int SolidFor(DeadPlayerPart torso)
        {
            int layer = torso.sections[torso.initialActiveSection].mainCollider.gameObject.layer, mask = 0;
            for (int i = 0; i < 32; i++) if (!Physics.GetIgnoreLayerCollision(layer, i)) mask |= 1 << i;
            return mask;
        }

        // (not the ground or a wall: players, and any loose part or prop, crawled through rather than climbed)
        private static bool Ignored(RaycastHit hit) =>
            hit.collider.isTrigger || hit.rigidbody != null && !hit.rigidbody.isKinematic || hit.collider.GetComponentInParent<Player>() != null
            || hit.collider.GetComponentInParent<HoldableObject>() != null;

        private static readonly RaycastHit[] hits = new RaycastHit[16];

        // the ground under `at`: the highest solid, flat-enough surface from StepUp above it to `depth` below it
        private static bool Ground(Vector3 at, float depth, int solid, out float y)
        {
            y = float.NegativeInfinity;
            int n = Physics.RaycastNonAlloc(at + Vector3.up * StepUp, Vector3.down, hits, StepUp + depth, solid, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (Ignored(hits[i]) || hits[i].normal.y < 0.5f) continue;
                if (!found || hits[i].point.y > y) { y = hits[i].point.y; found = true; }
            }
            return found;
        }

        // the nearest wall in the way of this step
        private static bool Wall(Vector3 centre, Vector3 step, int solid, out RaycastHit wall)
        {
            wall = default;
            if (step.sqrMagnitude < 1e-8f) return false;
            int n = Physics.SphereCastNonAlloc(centre, Radius, step.normalized, hits, step.magnitude + 0.05f, solid, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (Ignored(hits[i]) || hits[i].normal.y >= 0.6f || hits[i].distance <= 0f) continue;
                if (hits[i].distance < best) { best = hits[i].distance; wall = hits[i]; }
            }
            return best < float.PositiveInfinity;
        }

        // --- the dead player's machine: controls and camera ---

        private static float yaw, pitch = 15f;
        private static bool wasDead;
        private static byte lunges;
        private static float nextSend, nextFlop;
        private static DeadPlayerPart aimedAt;   // (the torso the camera was last set up behind)
        private static Vector2 forcedMove;
        private static float forcedUntil;
        private static bool forcedLunge, forcedToLegs;

        public static Vector3 Facing { get; private set; } = Vector3.forward;   // (where the camera faces, flat)

        public static bool IsMine(DeadPlayerPart torso) => torso != null && torso == aimedAt;

        // (the test's "crawl that way for a while", for trying it without a keyboard)
        public static void Force(Vector2 move, float seconds, bool lungeAfter, bool toLegs = false)
        {
            forcedToLegs = toLegs;
            forcedMove = move;
            forcedUntil = Time.unscaledTime + seconds;
            forcedLunge = lungeAfter;
        }

        // the local player's torso, when they're dead and looking at it (and nobody's carrying it)
        public static DeadPlayerPart Crawling()
        {
            Player me = Player.LocalPlayer;
            if (me == null || me.IsAlive || !Plugin.Enabled.Value) return null;
            DeadPlayerPart torso = me.CurrentDeathPart;
            if (torso == null || !torso.IsSpawned || torso.initialActiveSection != 0 || torso.IsHeldByAnyPlayer) return null;
            Component watching = me.PlayerLook.SpectatingPlayer;
            if (watching == null || watching.GetComponentInParent<DeadPlayerPart>() != torso) return null;
            return torso;
        }

        public static void ClientUpdate()
        {
            Player me = Player.LocalPlayer;
            if (me != null && me.IsAlive == wasDead)
            {
                wasDead = !me.IsAlive;
                Plugin.Log.LogInfo(wasDead ? "Pull Yourself Together: down" : "Pull Yourself Together: back up");
            }
            DeadPlayerPart torso = Crawling();
            if (torso == null) { aimedAt = null; forcedUntil = 0f; forcedLunge = false; Hud.Restore(null); return; }
            if (aimedAt != torso)
            {
                // just started: the camera behind the torso, looking the way the player was
                aimedAt = torso;
                yaw = me.PlayerLook.transform.eulerAngles.y;
                pitch = 15f;
            }

            Vector2 look = Vector2.zero, move = Vector2.zero;
            bool lunge = false;
            if (Time.unscaledTime < forcedUntil)
            {
                move = forcedMove;
                DeadPlayerPart legs = forcedToLegs ? Parts.Legs(me) : null;
                if (legs != null)
                {
                    Vector3 way = Parts.Centre(legs) - Parts.Centre(torso);
                    yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(way.x, way.z) * Mathf.Rad2Deg, 0.1f);
                    move = new Vector2(0f, 1f);
                }
            }
            else if (forcedLunge) { forcedLunge = false; lunge = true; }
            if (Mouse.current != null) look += Mouse.current.delta.ReadValue() * Plugin.LookSensitivity.Value;
            if (Keyboard.current != null)
            {
                Keyboard k = Keyboard.current;
                move += new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
                lunge |= k.spaceKey.wasPressedThisFrame;
            }
            if (Gamepad.current != null)
            {
                Gamepad g = Gamepad.current;
                move += g.leftStick.ReadValue();
                look += g.rightStick.ReadValue() * (180f * Time.unscaledDeltaTime);
                lunge |= g.buttonSouth.wasPressedThisFrame;
            }
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -5f, 65f);
            move = Vector2.ClampMagnitude(move, 1f);
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            Facing = turn * Vector3.forward;
            Vector3 world = turn * new Vector3(move.x, 0f, move.y);
            if (lunge) lunges++;

            if (Net.HostHasMod)
            {
                if (lunge || Time.unscaledTime >= nextSend)
                {
                    nextSend = Time.unscaledTime + 0.05f;
                    Net.SendInput(new Vector2(world.x, world.z), lunges);
                }
            }
            else if ((move.sqrMagnitude > 0.04f || lunge) && Time.unscaledTime >= nextFlop)
            {
                // (a host without the mod: a little throw of itself at a time)
                nextFlop = Time.unscaledTime + (lunge ? 1.2f : 0.75f);
                Vector3 throwAt = world.normalized * Plugin.CrawlSpeed.Value * (lunge ? 2f : 1.2f) + Vector3.up * (lunge ? 5.5f : 3f);
                torso.SetVelocity_ServerRpc(throwAt);
            }
        }

        // the camera: low behind the torso, turned by the player, pulled in where a wall's in the way
        public static void PlaceCamera(PlayerLook look)
        {
            DeadPlayerPart torso = Crawling();
            if (torso == null) { Hud.Show(look, null); return; }
            Vector3 pivot = Parts.Centre(torso) + Vector3.up * 0.55f;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            float dist = 2.8f;
            int n = Physics.SphereCastNonAlloc(pivot, 0.25f, rot * Vector3.back, hits, dist, SolidFor(torso), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!Ignored(hits[i]) && hits[i].distance > 0f) dist = Mathf.Min(dist, Mathf.Max(0.4f, hits[i].distance - 0.15f));
            look.transform.SetPositionAndRotation(pivot + rot * Vector3.back * dist, rot);
            Hud.Show(look, torso);   // (after the camera's placed: the marker's worked out from where it looks)
        }
    }

    [HarmonyPatch(typeof(PlayerLook), nameof(PlayerLook.HandleSpectatingCamera))]
    internal static class CrawlCamera
    {
        private static void Postfix(PlayerLook __instance)
        {
            try { Crawl.PlaceCamera(__instance); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Pull Yourself Together: camera: " + e.Message); }
        }
    }
}
