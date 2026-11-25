using DedicatedServer.Chat;
using DedicatedServer.Config;
using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DedicatedServer.HostAutomatorStages
{
    internal class AutomatedHost
    {
        private IModHelper helper;
        private BehaviorChain behaviorChain;
        private BehaviorState behaviorState;
        private bool enabled;

        public AutomatedHost(IModHelper helper, IMonitor monitor, ModConfig config, EventDrivenChatBox chatBox)
        {
            behaviorChain = new BehaviorChain(helper, monitor, config, chatBox);
            behaviorState = new BehaviorState(monitor, chatBox);
            this.helper = helper;
        }

        public void Enable()
        {
            if (enabled)
                return;
            enabled = true;
            helper.Events.GameLoop.UpdateTicked += OnUpdate;
            helper.Events.GameLoop.DayStarted += OnNewDay;
        }

        public void Disable()
        {
            enabled = false;
            helper.Events.GameLoop.UpdateTicked -= OnUpdate;
            helper.Events.GameLoop.DayStarted -= OnNewDay;
            behaviorState.DisableFestivalChatBox();
        }

        private void OnNewDay(object sender, StardewModdingAPI.Events.DayStartedEventArgs e)
        {
            behaviorState.NewDay();
        }

        private void OnUpdate(object sender, StardewModdingAPI.Events.UpdateTickedEventArgs e)
        {
            if (Context.IsWorldReady && Context.IsMainPlayer)
                behaviorChain.Process(behaviorState);
        }
    }
}
