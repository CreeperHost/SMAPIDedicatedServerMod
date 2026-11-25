using DedicatedServer.Chat;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Objects;
using System;
using System.Collections.Generic;

namespace DedicatedServer.MessageCommands
{
    internal class DemolishCommandListener
    {
        private EventDrivenChatBox chatBox;

        public DemolishCommandListener(EventDrivenChatBox chatBox)
        {
            this.chatBox = chatBox;
        }

        public void Enable()
        {
            chatBox.ChatReceived += chatReceived;
        }

        public void Disable()
        {
            chatBox.ChatReceived -= chatReceived;
        }

        private bool canDemolish(Building building, Farm farm, string farmerName)
        {
            var error = GetDemolitionError(building, farm);
            if (error == null)
            {
                return true;
            }

            chatBox.textBoxEnter("/message " + farmerName + " Error: " + error);
            return false;
        }

        /// <summary>Check permanent structures and the minimum buildings required by the farm.</summary>
        internal static string GetDemolitionError(Building building, Farm farm)
        {
            var data = building.GetData();
            var type = building.buildingType.Value;
            if (data == null || data.BuildCost < 0
                || (type == "Farmhouse" && building.HasIndoorsName("FarmHouse"))
                || (type == "Greenhouse" && building.HasIndoorsName("Greenhouse")))
            {
                return "This building can't be demolished.";
            }

            // 1.6 also requires the farm to retain at least one pet bowl.
            if ((type == "Shipping Bin" || type == "Pet Bowl") && !farm.HasMinBuildings(type, 2))
            {
                return "Can't demolish the last " + type.ToLowerInvariant() + ".";
            }

            return null;
        }

        private void destroyCabin(string farmerName, Building building, Farm f)
        {
            Action buildingLockFailed = delegate
            {
                chatBox.textBoxEnter("/message " + farmerName + " Error: " + Game1.content.LoadString("Strings\\UI:Carpenter_CantDemolish_LockFailed"));
            };
            Action continueDemolish = delegate
            {
                // A confirmation or network lock can complete after another player removed the building.
                if (!f.buildings.Contains(building))
                {
                    chatBox.textBoxEnter("/message " + farmerName + " Error: This building no longer exists.");
                    return;
                }
                if (!canDemolish(building, f, farmerName))
                {
                    return;
                }
                var interior = building.GetIndoors();
                if (building.daysOfConstructionLeft.Value > 0 || building.daysUntilUpgrade.Value > 0)
                {
                    chatBox.textBoxEnter("/message " + farmerName + " Error: " + Game1.content.LoadString("Strings\\UI:Carpenter_CantDemolish_DuringConstruction"));
                }
                else if (interior is AnimalHouse animalHouse && animalHouse.animalsThatLiveHere.Count > 0)
                {
                    chatBox.textBoxEnter("/message " + farmerName + " Error: " + Game1.content.LoadString("Strings\\UI:Carpenter_CantDemolish_AnimalsHere"));
                }
                else if (interior != null && interior.farmers.Any())
                {
                    chatBox.textBoxEnter("/message " + farmerName + " Error: " + Game1.content.LoadString("Strings\\UI:Carpenter_CantDemolish_PlayerHere"));
                }
                else
                {
                    if (interior is Cabin cabin)
                    {
                        foreach (Farmer allFarmer in Game1.getAllFarmers())
                        {
                            if (allFarmer.currentLocation != null && allFarmer.currentLocation.Name == cabin.GetCellarName())
                            {
                                chatBox.textBoxEnter("/message " + farmerName + " Error: " + Game1.content.LoadString("Strings\\UI:Carpenter_CantDemolish_PlayerHere"));
                                return;
                            }
                        }
                    }

                    if (interior is Cabin occupiedCabin && occupiedCabin.IsOwnerActivated)
                    {
                        chatBox.textBoxEnter("/message " + farmerName + " Error: " + Game1.content.LoadString("Strings\\UI:Carpenter_CantDemolish_FarmhandOnline"));
                    }
                    else
                    {
                        building.BeforeDemolish();
                        Chest chest = null;
                        if (interior is Cabin demolishedCabin)
                        {
                            List<Item> list = demolishedCabin.demolish();
                            if (list.Count > 0)
                            {
                                chest = new Chest(playerChest: true);
                                chest.fixLidFrame();
                                chest.Items.OverwriteWith(list);
                            }
                        }

                        if (f.destroyStructure(building))
                        {
                            _ = building.tileY.Value;
                            _ = building.tilesHigh.Value;
                            Game1.flashAlpha = 1f;
                            building.showDestroyedAnimation(Game1.getFarm());
                            Utility.spreadAnimalsAround(building, f);
                            if (chest != null)
                            {
                                f.objects[new Vector2(building.tileX.Value + building.tilesWide.Value / 2, building.tileY.Value + building.tilesHigh.Value / 2)] = chest;
                            }
                        }
                    }
                }
            };

            Game1.player.team.demolishLock.RequestLock(continueDemolish, buildingLockFailed);
        }

        private Action genDestroyCabinAction(string farmerName, Building building)
        {
            void destroyCabinAction()
            {
                Farm f = Game1.getFarm();
                destroyCabin(farmerName, building, f);
            }

            return destroyCabinAction;
        }

        private Action genCancelDestroyCabinAction(string farmerName)
        {
            void cancelDestroyCabinAction()
            {
                chatBox.textBoxEnter("/message " + farmerName + " Action canceled.");
            }

            return cancelDestroyCabinAction;
        }

        private void chatReceived(object sender, ChatEventArgs e)
        {
            var tokens = e.Message.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return;
            }
            // Private message chatKind is 3
            if (e.ChatKind == 3 && tokens[0] == "demolish")
            {
                // Find the farmer it came from and determine their location
                foreach (var farmer in Game1.otherFarmers.Values)
                {
                    if (farmer.UniqueMultiplayerID == e.SourceFarmerId)
                    {
                        if (tokens.Length != 1)
                        {
                            chatBox.textBoxEnter("/message " + farmer.Name + " Error: Invalid command usage.");
                            chatBox.textBoxEnter("/message " + farmer.Name + " Usage: demolish");
                            return;
                        }
                        var location = farmer.currentLocation;
                        if (location is Farm f)
                        {
                            var tileLocation = farmer.Tile;
                            switch (farmer.facingDirection.Value)
                            {
                                case 1: // Right
                                    tileLocation.X++;
                                    break;
                                case 2: // Down
                                    tileLocation.Y++;
                                    break;
                                case 3: // Left
                                    tileLocation.X--;
                                    break;
                                default: // 0 = up
                                    tileLocation.Y--;
                                    break;
                            }
                            foreach (var building in f.buildings)
                            {
                                if (building.occupiesTile(tileLocation))
                                {
                                    // Determine if the building can be demolished
                                    if (!canDemolish(building, f, farmer.Name))
                                    {
                                        return;
                                    }

                                    if (building.GetIndoors() is Cabin cabin)
                                    {
                                        if (cabin.HasOwner && cabin.owner.isCustomized.Value)
                                        {
                                            // The cabin is owned by someone. Ask the player if they're certain; record in memory the action to destroy the building.
                                            var responseActions = new Dictionary<string, Action>();
                                            responseActions["yes"] = genDestroyCabinAction(farmer.Name, building);
                                            responseActions["no"] = genCancelDestroyCabinAction(farmer.Name);
                                            chatBox.RegisterFarmerResponseActionGroup(farmer.UniqueMultiplayerID, responseActions);
                                            chatBox.textBoxEnter("/message " + farmer.Name + " This cabin belongs to a player. Are you sure you want to remove it? Message me \"yes\" or \"no\".");
                                            return;
                                        }
                                    }

                                    // The cabin doesn't belong to anyone. Destroy it immediately without confirmation.
                                    destroyCabin(farmer.Name, building, f);
                                    return;
                                }
                            }

                            chatBox.textBoxEnter("/message " + farmer.Name + " Error: No building found. You must be standing next to a building and facing it.");
                        }
                        else
                        {
                            chatBox.textBoxEnter("/message " + farmer.Name + " Error: You cannot demolish buildings outside of the farm.");
                        }
                        break;
                    }
                }
            }
        }
    }
}
