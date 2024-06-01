using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;   
using System.Windows.Controls;

namespace BackloggdStatus
{
    public class BackloggdStatus : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private BackloggdStatusSettingsViewModel settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("228e1135-a326-4a8d-8ee9-edc1c61c0982");

        public BackloggdStatus(IPlayniteAPI api) : base(api)
        {
            settings = new BackloggdStatusSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            logger.Info("BackloggdStatus Initialized");

            
        }

        private readonly Dictionary<string, string> backloggdStatuses = new Dictionary<string, string>
        {
            { "Abandoned", "Abandoned" },
            { "Beaten", "Completed" },
            { "Completed", "Completed" },
            { "On Hold", "Shelved" },
            { "Not Played", "Backlog" },
            { "Plan to Play", "Backlog" },
            { "Played", "Played" },
            { "Playing", "Playing" }
        };

        private readonly Dictionary<string, string> playniteStatuses = new Dictionary<string, string>
        {
            { "Abandoned", "Abandoned" },
            { "Backlog", "Not Played" },
            { "Completed", "Beaten" },
            { "Playing", "Playing" },
            { "Played", "Played" },
            { "Shelved", "On Hold" },
            { "Wishlist", "Not Played"}
        };

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            // Add code to be executed when game is finished installing.
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // Add code to be executed when Playnite is initialized.

            var games = PlayniteApi.Database.Games;

            foreach (var game in games)
            {
                string  backloggdStatus = GetBackloggdStatus(game.Name, out bool exists);
                if (exists)
                {
                    SetPlayniteStatus(game, backloggdStatus);
                }
                else
                {
                    SetBackloggdStatus(game, backloggdStatuses[game.CompletionStatus.Name]);
                }
            }
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // Add code to be executed when library is updated.
        }

        private void SetPlayniteStatus(Game game, string backloggdStatus)
        {
            // TODO: Implement this method
            if (game.CompletionStatus.Name != "Completed")
            {
                game.CompletionStatus.Name = playniteStatuses[backloggdStatus];
            }

            throw new NotImplementedException();
        }

        private string GetBackloggdStatus(string gameName, out bool exists)
        {
            // TODO: Implement this method
            throw new NotImplementedException();
        }

        private void SetBackloggdStatus(Game game, string playniteStatus)
        {
            // TODO: Implement this method
            throw new NotImplementedException();
        }

        // public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        // {
        //     // Add code to be executed when Playnite is shutting down.
        // }
        //
        // public override void OnGameStarted(OnGameStartedEventArgs args)
        // {
        //     // Add code to be executed when game is started running.
        // }
        //
        // public override void OnGameStarting(OnGameStartingEventArgs args)
        // {
        //     // Add code to be executed when game is preparing to be started.
        // }
        //
        // public override void OnGameStopped(OnGameStoppedEventArgs args)
        // {
        //     // Add code to be executed when game is preparing to be started.
        // }
        //
        // public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        // {
        //     // Add code to be executed when game is uninstalled.
        // }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new BackloggdStatusSettingsView();
        }
    }
}