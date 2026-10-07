using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordAgents
{
    /// <summary>
    /// The campaign side of the bridge. For now it writes one line when a campaign session starts (the size of
    /// the world it found) and one line per in-game day with the real seconds that day took, so the log shows
    /// the module is alive and how fast the campaign runs - which sets how long a 60-day run will take.
    /// </summary>
    public class BridgeBehavior : CampaignBehaviorBase
    {
        private DateTime _lastDayAt = DateTime.Now;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                _lastDayAt = DateTime.Now;
                Log.Line("session launched at " + CampaignTime.Now + ": " + Kingdom.All.Count + " kingdoms, "
                         + MobileParty.All.Count + " parties, " + Settlement.All.Count + " settlements");
            }
            catch (Exception e) { Log.Line("session report failed: " + e.Message); }
        }

        private void OnDailyTick()
        {
            try
            {
                DateTime now = DateTime.Now;
                double seconds = (now - _lastDayAt).TotalSeconds;
                _lastDayAt = now;
                Log.Line("day " + (int)CampaignTime.Now.ToDays + ": " + seconds.ToString("F1") + " s since the previous day");
            }
            catch { }
        }
    }
}
