using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace VRChatQuickJoin
{
    internal class Configuration
    {
        private FileInfo file;
        private string LoadedJson;
        internal AppConfig App = new AppConfig();
        internal enum LaunchMode
        {
            Unknown = 0,
            Uri,
            Launcher,
            Steam,
            SelfInvite
        }
        public class AppConfig
        {
            //public bool Skip2FA { get; set; } = false;
            public bool WaitOnExit { get; set; } = false;
            public bool FetchDetails { get; set; } = false;
            public bool OverwriteComments { get; set; } = true;
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string TOTPSecret { get; set; } = "";
            public string GameArguments { get; set; } = "";
            public List<List<string>> RunAdditional { get; set; } = new List<List<string>>();
            public LaunchMode LaunchMode { get; set; } = LaunchMode.Uri;
            public Dictionary<string, string> Ids { get; set; } = new Dictionary<string, string>();
            public string UserAgent { get; set; } = "VRCQuickJoin/1.0";
            public string _AuthCookie { get; set; } = "";
            public string _TwoFactorAuthCookie { get; set; } = "";
        }

        public Configuration(FileInfo file)
        {
            this.file = file;
        }
        internal AppConfig LoadConfiguration() => LoadConfiguration(file);
        private AppConfig LoadConfiguration(FileInfo file)
        {
            try
            {
                Console.WriteLine($"Loading {file.FullName}");

                if (!file.Exists)
                {
                    Console.WriteLine($"Configuration file not found. Creating default configuration file at\n\"{file.FullName}\"");
                    var defaultConfig = new AppConfig { };
                    SaveConfiguration(file, defaultConfig, null);

                    Console.WriteLine("Default configuration file created. Please edit it with your credentials and run the application again.");
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    Environment.Exit(0);
                }
                LoadedJson = file.ReadAllText();
                App = JsonConvert.DeserializeObject<AppConfig>(LoadedJson);

                if (App == null)
                {
                    throw new InvalidOperationException("Failed to deserialize configuration file.");
                }

                Console.WriteLine("Configuration loaded successfully.");
                return App;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(1);
                return null;
            }
        }
        internal bool SaveConfiguration() => SaveConfiguration(file, App, LoadedJson);
        internal bool SaveConfiguration(AppConfig appConfig) => SaveConfiguration(file, appConfig, LoadedJson);
        private static bool SaveConfiguration(FileInfo file, AppConfig appConfig, string loadedJson = null)
        {
            try
            {
                var json = JsonConvert.SerializeObject(appConfig, Formatting.Indented);
                if (string.IsNullOrWhiteSpace(loadedJson) || json != loadedJson) {
                    Console.WriteLine($"Saving configuration to \"{file.FullName}\"");
                    file.WriteAllText(json);
                    Console.WriteLine("Configuration saved successfully.");
                    loadedJson = json;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving configuration: {ex.Message}");
                return false;
            }
        }

    }
}
