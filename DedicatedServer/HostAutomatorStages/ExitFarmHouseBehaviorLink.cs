using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DedicatedServer.HostAutomatorStages
{
    internal class ExitFarmHouseBehaviorLink : BehaviorLink
    {
        public ExitFarmHouseBehaviorLink(BehaviorLink next = null) : base(next)
        {
        }

        public override void Process(BehaviorState state)
        {
            if (!state.ExitedFarmhouse() && Game1.currentLocation != null && Game1.currentLocation is FarmHouse)
            {
                state.WarpToFarm();
                state.ExitFarmhouse(); // Mark as exited
                state.SetWaitTicks(60); // Set up wait ticks to wait for possible event
            }
            else
            {
                processNext(state);
            }
        }
    }
}
