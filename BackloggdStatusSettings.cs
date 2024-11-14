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
        public List<BackloggdURLBinder> BackloggdURLs { get; set; } = new List<BackloggdURLBinder>();
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
            return true;
        }

    }

    public class BackloggdURLBinder : ObservableObject
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private Guid gameId;
        public Guid GameId
        {
            get => gameId;
            set => SetValue(ref gameId, value);
        }

        private bool urlSet = false;
        public bool URLSet
        {
            get => urlSet;
            set => SetValue(ref urlSet, value);
        }

        private bool wishlist = false;
        public bool Wishlist
        {
            get => wishlist;
            set => SetValue(ref wishlist, value);
        }

        private bool backlog = false;
        public bool Backlog
        {
            get => backlog;
            set => SetValue(ref backlog, value);
        }

        private bool playing = false;
        public bool Playing
        {
            get => playing;
            set => SetValue(ref playing, value);
        }

        public enum PlayedStatus
        {
            Played,
            Completed,
            Retired,
            Shelved,
            Abandoned
        };

        private PlayedStatus? played = null;
        public PlayedStatus? Played
        {
            get => played;
            set => SetValue(ref played, value);
        }


        private string backloggdName = "Game not set";
        public string BackloggdName
        {
            get => backloggdName;
            set => SetValue(ref backloggdName, value);
        }

        [DontSerialize]
        private readonly BackloggdClient backloggdClient = new BackloggdClient();


        public void RefreshStatus()
        {
            Game game = PlayniteApiProvider.Api.Database.Games.Get(GameId);
            // TODO: There was an exception here. Look into it.
            Link gameURL = game.Links.FirstOrDefault(link => link.Url.Contains("https://www.backloggd.com/games"));
            if (gameURL == null)
            {
                URLSet = false;
                return;
            }

            logger.Debug("Call RefreshStatus in BackloggdURLBinder");
            BackloggdName = backloggdClient.GetBackloggdName(gameURL.Url);
            
            var status = backloggdClient.GetGameStatus(gameURL.Url);

            SetStatus(status);
        }

        private void SetStatus(List<string> status)
        {
            Wishlist = false;
            Backlog = false;
            Playing = false;
            Played = null;

            foreach (string s in status)
            {
                switch (s)
                {
                    case "wishlist":
                        Wishlist = true;
                        break;
                    case "backlog":
                        Backlog = true;
                        break;
                    case "playing":
                        Playing = true;
                        break;
                    case "played":
                        Played = PlayedStatus.Played;
                        break;
                    case "completed":
                        Played = PlayedStatus.Completed;
                        break;
                    case "retired":
                        Played = PlayedStatus.Retired;
                        break;
                    case "shelved":
                        Played = PlayedStatus.Shelved;
                        break;
                    case "abandoned":
                        Played = PlayedStatus.Abandoned;
                        break;
                    default:
                        logger.Error("Unrecognized Status");
                        throw new Exception($"Attempted to save unrecognized status. { s }");
                        break;
                }
            }
        }
    }
}