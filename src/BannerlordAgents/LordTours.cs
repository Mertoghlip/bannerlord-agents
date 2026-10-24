using System;
using System.Collections;
using System.Reflection;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordAgents
{
    /// <summary>
    /// TFAOMFixes' recruiting trip for computer lords (its LordRecruiting, 0.11.3; found after run C1, Oct 23): a lord of no
    /// player's clan and not managed by Party AI Controls who falls below half his ranks is taken on a trip from place to place
    /// until 80%, the game's AI switched off while it steers him (its Steer). It never touches a lord while an order of ours
    /// holds him (it waits while another switched him off), but it takes him the hour after our order ends: all 28 of run C1's
    /// "still held after release" alarms were these trips, not a lord left frozen. And it gives a lord up only when our order's
    /// place differs from the trip's: with the same place, the trip stays and later switches the game's AI on under our order
    /// (its End) or steers him on. So: the after-release check names the trip instead of raising the alarm, and any order of
    /// ours takes the lord off it (Drop). Its members are private: reached by name, read and dropped only (TFAOMFixes is not
    /// changed). Game's thread.
    /// </summary>
    internal static class LordTours
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static bool _looked;
        private static IDictionary _tours;
        private static FieldInfo _target;

        private static bool Resolve()
        {
            if (_looked) return _tours != null;
            _looked = true;
            try
            {
                Type t = HarmonyLib.AccessTools.TypeByName("TFAOMFixes.LordRecruiting");
                _tours = t?.GetField("_tours", Any)?.GetValue(null) as IDictionary;
                Type tour = t?.GetNestedType("Tour", Any);
                _target = tour?.GetField("Target", Any);
            }
            catch (Exception e) { Diag.Swallowed("finding TFAOMFixes' recruiting trips", e); }
            Log.Line("lord tours: " + (_tours == null ? "TFAOMFixes' recruiting trips not found (it is not loaded, or changed)" : "TFAOMFixes' recruiting trips found"));
            return _tours != null;
        }

        /// <summary>Where TFAOMFixes' recruiting trip takes this party now, or null when it is on none.</summary>
        public static Settlement TargetOf(MobileParty p)
        {
            if (p == null || !Resolve()) return null;
            try { return _tours.Contains(p) ? _target?.GetValue(_tours[p]) as Settlement : null; }
            catch (Exception e) { Diag.Swallowed("reading a recruiting trip", e); return null; }
        }

        /// <summary>One of our orders takes this lord: off TFAOMFixes' recruiting trip, as it does itself when another order takes him.</summary>
        public static void Drop(MobileParty p)
        {
            if (p == null || !Resolve()) return;
            try
            {
                if (!_tours.Contains(p)) return;
                _tours.Remove(p);
                Diag.Count("order:took a lord off a recruiting trip");
                Diag.Event("order", "off a recruiting trip", new { party = p.StringId });
            }
            catch (Exception e) { Diag.Swallowed("taking a lord off a recruiting trip", e); }
        }

        public static void Reset() => _looked = false;
    }
}
