using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

namespace HighlightReel
{
    // Your kills, as the game counts them for you: a boss or an Abomination is a highlight on its own, and a few kills close
    // together are a multi-kill (one picture, its caption raised as the streak grows).
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.OnLocalPlayerKillTarget))]
    internal static class Kills
    {
        private static float streakStart = -999f, lastKill = -999f;
        private static int streak;
        private static Highlight streakShot;

        private static void Postfix(ref KillCallbackData data)
        {
            if (LevelData.IsHub || (data.target.Type & TargetType.Enemy) == 0) return;
            if (data.target is EnemyCore core && core.Brain != null)
            {
                EnemyBrain brain = core.Brain;
                string name = brain.SourceName;
                if (brain.EnemyType == EnemyType.Boss && Plugin.Bosses.Value)
                    Reel.Capture("BOSS DOWN" + (string.IsNullOrEmpty(name) ? "" : ": " + name.ToUpperInvariant()), 100, 0.35f);
                else if (brain.EnemyType == EnemyType.Abomination && Plugin.Bosses.Value)
                    Reel.Capture("ABOMINATION DOWN", 80, 0.35f);
            }

            if (!Plugin.MultiKills.Value) return;
            float now = Time.time;
            if (now - lastKill > Plugin.MultiKillWindow.Value) { streak = 0; streakShot = null; streakStart = now; }
            lastKill = now;
            streak++;
            if (streak >= Plugin.MultiKillCount.Value)
            {
                string caption = streak == 3 ? "TRIPLE KILL" : streak == 4 ? "QUAD KILL" : "RAMPAGE ×" + streak;
                streakShot = Reel.Capture(caption, 40 + streak * 5, 0.15f, replaces: streakShot);
            }
        }

        public static void Reset() { streak = 0; streakShot = null; lastKill = -999f; }
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
                Reel.Capture("BACK IN THE FIGHT", 30, 0.6f);
            wasAlive = alive;

            if (!Plugin.CloseCalls.Value || !alive) { if (!alive) inCloseCall = false; return; }
            float health = me.NormalizedHealth;
            if (!inCloseCall && health > 0f && health <= Plugin.CloseCallHealth.Value && Time.time >= closeCallCooldownUntil)
            {
                inCloseCall = true;
                closeCallCooldownUntil = Time.time + 30f;
                // (kept only if you're still up three seconds later)
                Reel.Capture("CLOSE CALL", 50, 0f, keepIf: () => (object)Player.LocalPlayer != null && Player.LocalPlayer && Player.LocalPlayer.IsAlive, decideAfter: 3f);
            }
            else if (inCloseCall && health > Plugin.CloseCallHealth.Value * 2f) inCloseCall = false;
        }

        public static void Reset() { wasAlive = true; inCloseCall = false; closeCallCooldownUntil = 0f; }
    }
}
