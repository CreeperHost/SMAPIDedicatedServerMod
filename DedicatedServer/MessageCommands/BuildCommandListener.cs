using DedicatedServer.Chat;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DedicatedServer.MessageCommands
{
    internal class BuildCommandListener
    {
        private static Dictionary<string, Action<EventDrivenChatBox, Farmer>> buildingActions = new Dictionary<string, Action<EventDrivenChatBox, Farmer>>
        {
            {"stone_cabin", genBuildCabin("Stone Cabin")},
            {"plank_cabin", genBuildCabin("Plank Cabin")},
            {"log_cabin", genBuildCabin("Log Cabin")},
        };
        private static readonly string validBuildingNamesList = genValidBuildingNamesList();

        private EventDrivenChatBox chatBox;

        public BuildCommandListener(EventDrivenChatBox chatBox)
        {
            this.chatBox = chatBox;
        }

        private static Action<EventDrivenChatBox, Farmer> genBuildCabin(string cabinBlueprintName)
        {
            void buildCabin(EventDrivenChatBox chatBox, Farmer farmer)
            {
                var point = farmer.Tile;
                // Since 1.6, cabin appearances are skins of the same building type.
                if (!Building.TryGetData("Cabin", out var data))
                {
                    chatBox.textBoxEnter("/message " + farmer.Name + " Error: Cabin building data is unavailable.");
                    return;
                }
                switch (farmer.facingDirection.Value)
                {
                    case 1: // Right
                        point.X++;
                        point.Y -= (data.Size.Y / 2);
                        break;
                    case 2: // Down
                        point.X -= (data.Size.X / 2);
                        point.Y++;
                        break;
                    case 3: // Left
                        point.X -= data.Size.X;
                        point.Y -= (data.Size.Y / 2);
                        break;
                    default: // 0 = Up
                        point.X -= (data.Size.X / 2);
                        point.Y -= data.Size.Y;
                        break;
                }
                Game1.player.team.buildLock.RequestLock(delegate
                {
                    var game = Game1.game1;
                    var hostLocation = game.instanceGameLocation;
                    try
                    {
                        if (Game1.locationRequest != null || farmer.currentLocation is not Farm farm)
                        {
                            return;
                        }

                        // The game's placement checks read Game1.currentLocation for map
                        // properties even when called on another location. Borrow the farm
                        // context for this synchronous operation without warping the bot or
                        // raising location-change events, then restore it in finally.
                        game.instanceGameLocation = farm;
                        if (farm.buildStructure("Cabin", data, point, Game1.player, out var building))
                        {
                            building.skinId.Value = cabinBlueprintName == "Stone Cabin" ? null : cabinBlueprintName;
                            chatBox.textBoxEnter(farmer.Name + " just built a " + cabinBlueprintName);
                        }
                        else
                        {
                            chatBox.textBoxEnter("/message " + farmer.Name + " Error: " + Game1.content.LoadString("Strings\\UI:Carpenter_CantBuild"));
                        }
                    }
                    finally
                    {
                        game.instanceGameLocation = hostLocation;
                        Game1.player.team.buildLock.ReleaseLock();
                    }
                }, delegate
                {
                    chatBox.textBoxEnter("/message " + farmer.Name + " Error: Another building operation is in progress. Please try again.");
                });
            }
            return buildCabin;
        }

        private static string genValidBuildingNamesList()
        {
            string str = "";
            var buildingActionsEnumerable = buildingActions.Keys.ToArray();
            for (int i = 0; i < buildingActionsEnumerable.Length; i++)
            {
                str += "\"" + buildingActionsEnumerable[i] + "\"";
                if (i + 1 < buildingActionsEnumerable.Length)
                {
                    str += ", ";
                }
                if (i + 1 == buildingActionsEnumerable.Length - 1)
                {
                    str += "and ";
                }
            }
            return str;
        }

        private void pmValidBuildingNames(Farmer farmer)
        {
            var str = "/message " + farmer.Name + " Valid building names include " + validBuildingNamesList;
            chatBox.textBoxEnter(str);
        }

        public void Enable()
        {
            chatBox.ChatReceived += chatReceived;
        }

        public void Disable()
        {
            chatBox.ChatReceived -= chatReceived;
        }

        private void chatReceived(object sender, ChatEventArgs e)
        {
            // Private message chatKind is 3
            var tokens = e.Message.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return;
            }
            if (e.ChatKind == 3 && tokens[0] == "build")
            {
                // Find the farmer it came from and determine their location
                foreach (var farmer in Game1.otherFarmers.Values)
                {
                    if (farmer.UniqueMultiplayerID == e.SourceFarmerId)
                    {
                        if (tokens.Length != 2)
                        {
                            chatBox.textBoxEnter("/message " + farmer.Name + " Error: Invalid command usage.");
                            chatBox.textBoxEnter("/message " + farmer.Name + " Usage: build [building_name]");
                            pmValidBuildingNames(farmer);
                            return;
                        }
                        var buildingName = tokens[1];
                        if (buildingActions.TryGetValue(buildingName, out var action))
                        {
                            var location = farmer.currentLocation;
                            if (location is Farm f)
                            {
                                action(chatBox, farmer);
                            }
                            else
                            {
                                chatBox.textBoxEnter("/message " + farmer.Name + " Error: You cannot place buildings outside of the farm!");
                            }
                        }
                        else
                        {
                            chatBox.textBoxEnter("/message " + farmer.Name + " Error: Unrecognized building name \"" + buildingName + "\"");
                            pmValidBuildingNames(farmer);
                        }
                        break;
                    }
                }
            }
        }
    }
}
