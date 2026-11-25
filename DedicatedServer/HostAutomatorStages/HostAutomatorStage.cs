using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace DedicatedServer.HostAutomatorStages
{
    internal abstract class HostAutomatorStage
    {
        protected IModHelper helper;
        private bool enabled;

        public HostAutomatorStage(IModHelper helper)
        {
            this.helper = helper;
        }

        private void execute(object sender, UpdateTickedEventArgs e)
        { 
            Execute(sender, e);
        }

        public void Enable()
        {
            if (enabled)
                return;
            enabled = true;
            helper.Events.GameLoop.UpdateTicked += execute;
        }

        public void Disable()
        {
            enabled = false;
            helper.Events.GameLoop.UpdateTicked -= execute;
        }

        public abstract void Execute(object sender, UpdateTickedEventArgs e);
    }
}
