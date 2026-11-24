using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerlordAgents
{
    /// <summary>
    /// Module 0.24.4 (Nov 24): the siege screen the player sees counts the food of the lords inside a besieged place. Under the siege food rule (SiegeFood) a
    /// besieged place draws on its lords' food once its own store would run out, so its days of food are the store and that food together; the game's own
    /// screen (EncounterMenuOverlayVM: the defenders' "days of food left" and the tooltip's "Settlement's Days Until Food Runs Out") divides the store alone.
    /// Two postfixes, found by name (the view-model library is not a reference of the module), only while siege_food is on and the place is besieged with
    /// lords inside carrying food. Game's thread (the UI's own refresh). Game-only: the smoke test never loads this class.
    /// </summary>
    internal static class SiegeFoodScreen
    {
        internal const string OverlayType = "TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay.EncounterMenuOverlayVM";
        private static FieldInfo _defender;
        private static PropertyInfo _defenderFood;

        /// <summary>Attaches the two postfixes; says what attached.</summary>
        internal static string Install(HarmonyLib.Harmony h)
        {
            Type t = HarmonyLib.AccessTools.TypeByName(OverlayType);
            if (t == null) return "the siege screen's food: its view model was not found, so the screen shows the store alone";
            _defender = HarmonyLib.AccessTools.Field(t, "_defenderLeadingParty");
            _defenderFood = HarmonyLib.AccessTools.Property(t, "DefenderPartyFood");
            MethodInfo update = HarmonyLib.AccessTools.Method(t, "UpdateProperties");
            MethodInfo tooltip = HarmonyLib.AccessTools.Method(t, "GetEncounterSideFoodTooltip");
            int n = 0;
            if (update != null && _defender != null && _defenderFood != null)
            {
                h.Patch(update, postfix: new HarmonyLib.HarmonyMethod(typeof(SiegeFoodScreen).GetMethod(nameof(UpdatePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                n++;
            }
            if (tooltip != null && _defender != null)
            {
                h.Patch(tooltip, postfix: new HarmonyLib.HarmonyMethod(typeof(SiegeFoodScreen).GetMethod(nameof(TooltipPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                n++;
            }
            return "the siege screen's food " + (n == 2 ? "attached" : n == 1 ? "half attached" : "did not attach") + (Settings.SiegeFood ? "" : " (siege_food is off, so it shows the store alone)");
        }

        /// <summary>The besieged place the screen shows and its days with the lords' food, or false when the screen's own number stands.</summary>
        private static bool Pooled(object overlay, out Settlement place, out float lordsFood, out int days)
        {
            place = null;
            lordsFood = 0;
            days = 0;
            if (!Settings.SiegeFood || overlay == null || _defender == null) return false;
            object lead = _defender.GetValue(overlay);
            place = lead?.GetType().GetProperty("Settlement")?.GetValue(lead) as Settlement;
            Town town = place?.Town;
            if (town == null || !place.IsUnderSiege) return false;
            SiegeFood.Inside(place, out lordsFood, out float lordsEat);
            float change = town.FoodChangeWithoutMarketStocks;
            if (lordsFood <= 0 || change >= 0) return false;
            (int stock, int market) = TownHelpers.GetTownFoodAndMarketStocks(town);
            double d = SiegeFoodRules.Days(stock + market, -change, lordsFood, lordsEat);
            if (double.IsInfinity(d) || double.IsNaN(d)) return false;
            days = Math.Max(0, (int)Math.Ceiling(d));
            return true;
        }

        private static void UpdatePostfix(object __instance)
        {
            try
            {
                if (!Pooled(__instance, out _, out _, out int days)) return;
                MBTextManager.SetTextVariable("DAY_NUM", days.ToString());
                MBTextManager.SetTextVariable("PLURAL", days > 1 ? 1 : 0);
                _defenderFood.SetValue(__instance, GameTexts.FindText("str_party_food_left").ToString());
            }
            catch (Exception e) { Diag.Swallowed("the siege screen's food", e); }
        }

        private static void TooltipPostfix(object __instance, BattleSideEnum side, object __result)
        {
            try
            {
                if (side != BattleSideEnum.Defender || !(__result is IList list) || list.Count == 0) return;
                if (!Pooled(__instance, out _, out float lordsFood, out int days)) return;
                string label = new TextObject("{=DNXD37JL}Settlement's Days Until Food Runs Out").ToString();
                Type item = list[0].GetType();
                PropertyInfo def = item.GetProperty("DefinitionLabel");
                ConstructorInfo make = item.GetConstructors().FirstOrDefault(c =>
                {
                    ParameterInfo[] ps = c.GetParameters();
                    return ps.Length >= 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string) && ps[2].ParameterType == typeof(int) && ps.Skip(3).All(q => q.HasDefaultValue);
                });
                if (def == null || make == null) return;
                object Line(string a, string b)
                {
                    ParameterInfo[] ps = make.GetParameters();
                    var args = new object[ps.Length];
                    args[0] = a;
                    args[1] = b;
                    args[2] = 0;
                    for (int i = 3; i < ps.Length; i++) args[i] = ps[i].DefaultValue;
                    return make.Invoke(args);
                }
                for (int i = 0; i < list.Count; i++)
                {
                    if (!string.Equals(def.GetValue(list[i]) as string, label, StringComparison.Ordinal)) continue;
                    list[i] = Line(label, days.ToString());
                    list.Insert(i, Line("Food Carried by the Lords Inside", ((int)lordsFood).ToString()));
                    break;
                }
            }
            catch (Exception e) { Diag.Swallowed("the siege screen's food tooltip", e); }
        }
    }
}
