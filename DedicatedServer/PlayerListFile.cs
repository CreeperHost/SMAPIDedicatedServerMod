using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DedicatedServer
{
    /// <summary>Publish a complete JSON snapshot without exposing a partially written file.</summary>
    internal sealed class PlayerListFile
    {
        private readonly string path;
        private readonly Action<string> reportError;
        private string lastWrittenJson;
        private bool reportedError;

        public PlayerListFile(string path, Action<string> reportError)
        {
            this.path = path;
            this.reportError = reportError;
        }

        public bool Write(IEnumerable<string> names)
        {
            string json = JsonSerializer.Serialize(new
            {
                players = names.OrderBy(name => name, StringComparer.Ordinal).ToArray()
            });
            if (json == lastWrittenJson && File.Exists(path))
                return true;

            try
            {
                // Keep the temporary file beside the target so replacement stays on one filesystem.
                string temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(temporaryPath, path, overwrite: true);
                lastWrittenJson = json;
                reportedError = false;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Retry on the next refresh, without flooding the log while a filesystem issue persists.
                if (!reportedError)
                {
                    reportError($"Could not update player list '{path}': {ex.Message}");
                    reportedError = true;
                }
                return false;
            }
        }
    }
}
