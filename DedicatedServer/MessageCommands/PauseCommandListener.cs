using DedicatedServer.Chat;
using StardewValley;
using System;

namespace DedicatedServer.MessageCommands
{
    internal class PauseCommandListener
    {
        private EventDrivenChatBox chatBox;

        public PauseCommandListener(EventDrivenChatBox chatBox)
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

        private void chatReceived(object sender, ChatEventArgs e)
        {
            var tokens = e.Message.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return;
            }
            // Private message chatKind is 3
            if (e.ChatKind == 3 && tokens[0] == "pause")
            {

                Game1.netWorldState.Value.IsPaused = !Game1.netWorldState.Value.IsPaused;
                if (Game1.netWorldState.Value.IsPaused)
                {
                    chatBox.globalInfoMessage("Paused");
                    return;
                }
                chatBox.globalInfoMessage("Resumed");
            }
        }
    }
}
