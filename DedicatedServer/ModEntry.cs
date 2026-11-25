using DedicatedServer.Config;
using DedicatedServer.HostAutomatorStages;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DedicatedServer
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {
        private WaitCondition titleMenuWaitCondition;
        private ModConfig config;
        private bool farmStageEnabled;
        private PlayerListFile playerList;
        private readonly HashSet<long> disconnectedPlayers = new();
        private bool hostingFarm;
        private bool playerListDirty;

        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            this.config = helper.ReadConfig<ModConfig>();
            this.playerList = new PlayerListFile(Path.Combine(Constants.GamePath, ".playerlist.json"),
                message => Monitor.Log(message, LogLevel.Error));
            this.playerList.Write(Array.Empty<string>());
            helper.Events.GameLoop.SaveLoaded += OnWorldReady;
            helper.Events.GameLoop.DayStarted += OnWorldReady;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            helper.Events.Multiplayer.PeerDisconnected += OnPeerDisconnected;

            // ensure that the game environment is in a stable state before the mod starts executing
            this.titleMenuWaitCondition = new WaitCondition(() => Game1.activeClickableMenu is StardewValley.Menus.TitleMenu, 5);
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        }

        /// <summary>
        /// Event handler to wait until a specific condition is met before executing.
        /// </summary>
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (this.playerListDirty || e.IsMultipleOf(60))
            {
                this.playerListDirty = false;
                UpdatePlayerList();
            }

            if (!this.farmStageEnabled && this.titleMenuWaitCondition.IsMet())
            {
                this.farmStageEnabled = true; // Set the flag to true once the condition is met.
                new StartFarmStage(this.Helper, Monitor, config).Enable();
            }
            // makes the host stamina and health infinite
            if (Context.IsWorldReady && Context.IsMainPlayer)
            {
                Game1.player.health = Game1.player.maxHealth;
                Game1.player.Stamina = Game1.player.MaxStamina;
            }
        }

        private void OnWorldReady(object sender, EventArgs e)
        {
            this.hostingFarm = Context.IsMainPlayer;
            UpdatePlayerList();
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            this.hostingFarm = false;
            this.disconnectedPlayers.Clear();
            this.playerList.Write(Array.Empty<string>());
        }

        private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
        {
            this.disconnectedPlayers.Remove(e.Peer.PlayerID);
            // Connection approval may precede creation of the farmer object.
            this.playerListDirty = true;
        }

        private void OnPeerDisconnected(object sender, PeerDisconnectedEventArgs e)
        {
            this.disconnectedPlayers.Add(e.Peer.PlayerID);
            UpdatePlayerList();
        }

        private void UpdatePlayerList()
        {
            // Keep names through day/save transitions; clear only when leaving the hosted world.
            string[] names = this.hostingFarm && Context.IsMainPlayer
                ? Game1.otherFarmers.Values
                    .Where(farmer => farmer != null && farmer != Game1.player
                        && !this.disconnectedPlayers.Contains(farmer.UniqueMultiplayerID)
                        && !Game1.Multiplayer.isDisconnecting(farmer))
                    .Select(farmer => farmer.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray()
                : Array.Empty<string>();
            this.playerList.Write(names);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                this.playerList?.Write(Array.Empty<string>());
            base.Dispose(disposing);
        }

        /// <summary>
        /// Represents wait condition.
        /// </summary>
        private class WaitCondition
        {
            private readonly System.Func<bool> condition;
            private int waitCounter;

            public WaitCondition(System.Func<bool> condition, int initialWait)
            {
                this.condition = condition;
                this.waitCounter = initialWait;
            }

            public bool IsMet()
            {
                if (this.waitCounter <= 0 && this.condition())
                {
                    return true;
                }

                this.waitCounter--;
                return false;
            }
        }
    }
}
