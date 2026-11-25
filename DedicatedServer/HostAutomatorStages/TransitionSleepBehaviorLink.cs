using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using System;

namespace DedicatedServer.HostAutomatorStages
{
    internal class TransitionSleepBehaviorLink : BehaviorLink
    {
        public TransitionSleepBehaviorLink(BehaviorLink next = null) : base(next)
        {
        }

        public override void Process(BehaviorState state)
        {
            bool shouldSleep = Utils.Sleeping.ShouldSleep(state.GetNumOtherPlayers());
            bool isSleeping = Utils.Sleeping.IsSleeping();
            if (shouldSleep && !isSleeping)
            {
                if (state.HasBetweenTransitionSleepWaitTicks())
                    state.DecrementBetweenTransitionSleepWaitTicks();
                else if (Game1.currentLocation is FarmHouse)
                {
                    Game1.player.isInBed.Value = true;
                    Game1.player.sleptInTemporaryBed.Value = true;
                    // Let the game create and complete its own ready check, including
                    // the current sleep/save logic and cancellation callback.
                    Game1.currentLocation.answerDialogueAction("Sleep_Yes", Array.Empty<string>());
                    state.Sleep();
                }
                else
                {
                    var farmHouse = Game1.getFarm().GetMainFarmHouse()?.GetIndoors() as FarmHouse
                        ?? Game1.getLocationFromName("FarmHouse") as FarmHouse;
                    if (farmHouse == null)
                        return;
                    var entry = farmHouse.getEntryLocation();
                    Game1.warpFarmer(farmHouse.NameOrUniqueName, entry.X, entry.Y, 2);
                    state.WarpToSleep();
                }
            }
            else if (!shouldSleep && isSleeping)
            {
                if (state.HasBetweenTransitionSleepWaitTicks())
                    state.DecrementBetweenTransitionSleepWaitTicks();
                else if (Game1.activeClickableMenu is ReadyCheckDialog dialog && dialog.isCancelable())
                {
                    dialog.closeDialog(Game1.player);
                    Game1.netReady.SetLocalReady("sleep", false);
                    Game1.player.isInBed.Value = false;
                    Game1.player.sleptInTemporaryBed.Value = false;
                    Game1.player.timeWentToBed.Value = 0;
                    state.CancelSleep();
                }
            }
            else
            {
                state.ClearBetweenTransitionSleepWaitTicks();
                processNext(state);
            }
        }
    }
}
