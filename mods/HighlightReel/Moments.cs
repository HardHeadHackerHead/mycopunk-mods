using System.Collections.Generic;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

namespace HighlightReel
{
    // Your kills, as the game counts them for you: a boss or an Abomination is a highlight on its own, so is a kill with your melee,
    // and a burst of kills packed close together is a multi-kill (one picture per burst, its caption going up as the burst does).
    // In a horde the kills never stop, so a burst needs them tight (MultiKillCount within MultiKillWindow), and there's a rest
    // between bursts.
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.OnLocalPlayerKillTarget))]
    internal static class Kills
    {
        private const float BurstRest = 25f, MeleeRest = 15f;
        private static readonly Queue<float> recent = new Queue<float>();
        private static Highlight burstShot;
        private static int burstKills;
        private static float lastKill = -999f, nextBurst, nextMelee;

        private static void Postfix(ref KillCallbackData data)
        {
            if (LevelData.IsHub || (data.target.Type & TargetType.Enemy) == 0) return;
            if (data.target is EnemyCore core && core.Brain != null && Plugin.Bosses.Value)
            {
                EnemyBrain brain = core.Brain;
                string name = brain.SourceName;
                if (brain.EnemyType == EnemyType.Boss)
                    Reel.Capture(Kind.Boss, "BOSS DOWN" + (string.IsNullOrEmpty(name) ? "" : ": " + name.ToUpperInvariant()), 100, 0.35f);
                else if (brain.EnemyType == EnemyType.Abomination)
                    Reel.Capture(Kind.Boss, "ABOMINATION DOWN", 80, 0.35f);
            }

            float now = Time.time;
            if (data.damageData.IsMelee && Plugin.MeleeKills.Value && now >= nextMelee)
            {
                nextMelee = now + MeleeRest;
                Reel.Capture(Kind.Melee, "MELEE KILL", 60, 0.12f);
            }

            if (!Plugin.MultiKills.Value) return;
            float window = Plugin.MultiKillWindow.Value;
            // a burst carries on while the kills keep coming that close together
            if (burstShot != null && now - lastKill <= window)
            {
                burstKills++;
                burstShot.Caption = Caption(burstKills);
            }
            else if (burstShot != null) { burstShot = null; nextBurst = now + BurstRest; }
            lastKill = now;

            recent.Enqueue(now);
            while (recent.Count > 0 && now - recent.Peek() > window) recent.Dequeue();
            if (burstShot == null && recent.Count >= Plugin.MultiKillCount.Value && now >= nextBurst)
            {
                burstKills = recent.Count;
                burstShot = Reel.Capture(Kind.MultiKill, Caption(burstKills), 40 + Mathf.Min(burstKills, 10), 0.15f);
            }
        }

        private static string Caption(int kills) =>
            kills <= 3 ? "TRIPLE KILL" : kills == 4 ? "QUAD KILL" : "RAMPAGE ×" + kills;

        public static void Reset() { recent.Clear(); burstShot = null; burstKills = 0; lastKill = -999f; nextBurst = 0f; nextMelee = 0f; }
    }

    // What happens to you, checked every frame: a close call (your health dips very low and you live through it) and being
    // brought back up.
    internal static class You
    {
        private static bool wasAlive = true, inCloseCall;
        private static float closeCallCooldownUntil;

        public static void Update()
        {
            if (LevelData.IsHub) { wasAlive = true; inCloseCall = false; return; }
            Player me = Player.LocalPlayer;
            if ((object)me == null || !me) return;
            bool alive = me.IsAlive;

            if (alive && !wasAlive && Plugin.Revives.Value)
                Reel.Capture(Kind.Revive, "BACK IN THE FIGHT", 30, 0.6f);
            wasAlive = alive;

            if (!Plugin.CloseCalls.Value || !alive) { if (!alive) inCloseCall = false; return; }
            float health = me.NormalizedHealth;
            if (!inCloseCall && health > 0f && health <= Plugin.CloseCallHealth.Value && Time.time >= closeCallCooldownUntil)
            {
                inCloseCall = true;
                closeCallCooldownUntil = Time.time + 30f;
                // (kept only if you're still up three seconds later)
                Reel.Capture(Kind.CloseCall, "CLOSE CALL", 50, 0f,
                    keepIf: () => (object)Player.LocalPlayer != null && Player.LocalPlayer && Player.LocalPlayer.IsAlive, decideAfter: 3f);
            }
            else if (inCloseCall && health > Plugin.CloseCallHealth.Value * 2f) inCloseCall = false;
        }

        public static void Reset() { wasAlive = true; inCloseCall = false; closeCallCooldownUntil = 0f; }
    }
}
