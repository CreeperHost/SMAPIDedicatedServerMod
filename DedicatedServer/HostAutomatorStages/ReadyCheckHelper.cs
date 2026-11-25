using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using System.Linq;

namespace DedicatedServer.HostAutomatorStages
{
    internal static class ReadyCheckHelper
    {
        public static void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
                return;

            // Reading the host's mail unlocks progression events for the farmhands.
            for (int i = 0; i < 10; i++)
                Game1.getFarm().mailbox();

            if (!Game1.player.eventsSeen.Contains("295672") && Game1.netWorldState.Value.MuseumPieces.Count() >= 60)
                Game1.player.eventsSeen.Add("295672");

            int targetLevel = Game1.getFarm().buildings
                .Select(building => building.GetIndoors())
                .OfType<Cabin>()
                .Select(cabin => cabin.upgradeLevel)
                .DefaultIfEmpty(0)
                .Max();
            if (targetLevel > Game1.player.HouseUpgradeLevel)
            {
                Game1.player.HouseUpgradeLevel = targetLevel;
                Game1.player.performRenovation("FarmHouse");
            }
        }

        // The local dialog identifies the ready check the host has joined. This follows
        // the active game instance without caching a previous save's network state.
        public static bool IsReady(string checkName, Farmer player)
        {
            return player == Game1.player
                && Game1.activeClickableMenu is ReadyCheckDialog dialog
                && dialog.checkName == checkName;
        }
    }
}
