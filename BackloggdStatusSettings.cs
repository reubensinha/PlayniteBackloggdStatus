using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using Playnite.SDK.Plugins;
using NotImplementedException = System.NotImplementedException;
using System.Security.Policy;

namespace BackloggdStatus
{
    public class BackloggdStatusSettings : ObservableObject
    {
        
        // Playnite serializes settings object to a JSON object and saves it as text file.
        // If you want to exclude some property from being saved then use `JsonDontSerialize` ignore attribute.
        public List<BackloggdGame> BackloggdGamesList { get; set; } = new List<BackloggdGame>();
    }

    public class BackloggdStatusSettingsViewModel : ObservableObject, ISettings
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly BackloggdStatus plugin;
        private readonly IPlayniteAPI api;

        private BackloggdStatusSettings editingClone { get; set; }


        private BackloggdStatusSettings settings;
        public BackloggdStatusSettings Settings
        {
            get => settings;
            set => SetValue(ref settings, value);
        }

        public BackloggdStatusSettingsViewModel(BackloggdStatus plugin, IPlayniteAPI api)
        {
            // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
            this.plugin = plugin;
            this.api = api;

            // OpenWebViewCommand = new RelayCommand<Game>(OpenWebView);

            // Load saved settings.
            var savedSettings = plugin.LoadPluginSettings<BackloggdStatusSettings>();

            logger.Debug("In ViewModel Constructor");

            // LoadPluginSettings returns null if no saved data is available.
            if (savedSettings != null)
            {
                Settings = savedSettings;

                logger.Debug("Settings loaded from json");
            }
            else
            {
                Settings = new BackloggdStatusSettings();

                logger.Debug("New Settings created");
            }
        }



        public void BeginEdit()
        {
            // Code executed when settings view is opened and user starts editing values.
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            Settings = editingClone;

        }

        public void EndEdit()
        {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            // This method should save settings made to Option1 and Option2.
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();

            if (Settings.BackloggdGamesList == null || !Settings.BackloggdGamesList.Any())
            {
                errors.Add("No Backloggd URLs have been configured.");
            }

            return !errors.Any();
        }

    }

    public class BackloggdGame : ObservableObject
    {
        [DontSerialize]
        private readonly IPlayniteAPI PlayniteApi = PlayniteApiProvider.Api;

        [DontSerialize]
        private static readonly ILogger logger = LogManager.GetLogger();

        [DontSerialize]
        public enum PlayedStatus
        {
            Played,
            Completed,
            Retired,
            Shelved,
            Abandoned
        };

        private Guid gameId;
        public Guid GameId
        {
            get => gameId;
            set => SetValue(ref gameId, value);
        }

        private string backloggdName = "Game not set";
        public string BackloggdName
        {
            get => backloggdName;
            private set => SetValue(ref backloggdName, value);
        }

        private string backloggdUrl;
        public string BackloggdUrl
        {
            get => backloggdUrl;
            private set => SetValue(ref backloggdUrl, value);
        }

        private bool playing;
        public bool Playing
        {
            get => playing;
            private set => SetValue(ref playing, value);
        }

        private bool backlog;
        public bool Backlog
        {
            get => backlog;
            private set => SetValue(ref backlog, value);
        }

        private bool wishlist;
        public bool Wishlist
        {
            get => wishlist;
            private set => SetValue(ref wishlist, value);
        }

        private PlayedStatus? played;
        public PlayedStatus? Played
        {
            get => played;
            private set => SetValue(ref played, value);
        }

    }
}