using System.Collections.Generic;
using Pigeon.Movement;
using UnityEngine;

namespace PullYourselfTogether
{
    // A dead player's two halves, as the game keeps them: two DeadPlayerParts, the torso (made first, section 0) and the legs
    // (section 1), in DeadPlayerPart's own list of every dead player's parts.
    internal static class Parts
    {
        public static DeadPlayerPart Torso(Player player) => Find(player, 0);
        public static DeadPlayerPart Legs(Player player) => Find(player, 1);

        private static DeadPlayerPart Find(Player player, int section)
        {
            if (player == null || !DeadPlayerPart.allParts.TryGetValue(player, out List<DeadPlayerPart> parts)) return null;
            foreach (DeadPlayerPart part in parts)
                if (part != null && part.IsSpawned && part.initialActiveSection == section && !part.AreAllSectionsActive) return part;
            return null;
        }

        // the middle of the part's body, in the world (where the game measures its 2 m to the other half from)
        public static Vector3 Centre(DeadPlayerPart part)
        {
            BoxCollider box = part.sections[part.initialActiveSection].mainCollider;
            return box.transform.TransformPoint(box.center);
        }

        // within the game's 2 m of reconnecting
        public static bool Touching(DeadPlayerPart a, DeadPlayerPart b) => (Centre(a) - Centre(b)).sqrMagnitude < 4f;
    }
}
