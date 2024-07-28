﻿using Playnite.SDK;
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
using NotImplementedException = System.NotImplementedException;

namespace BackloggdStatus
{
    public class BackloggdStatusSettings : ObservableObject
    {
        // Example settings
        private string option1 = string.Empty;
        private bool option2 = false;
        // private bool optionThatWontBeSaved = false;

        public string Option1
        {
            get => option1;
            set => SetValue(ref option1, value);
        }

        public bool Option2
        {
            get => option2;
            set => SetValue(ref option2, value);
        }
        // Playnite serializes settings object to a JSON object and saves it as text file.
        // If you want to exclude some property from being saved then use `JsonDontSerialize` ignore attribute.
        // [DontSerialize]
        // public bool OptionThatWontBeSaved { get => optionThatWontBeSaved; set => SetValue(ref optionThatWontBeSaved, value); }
        // // End Example settings


        public List<BackloggdURLBinder> BackloggdURLs { get; set; } = new List<BackloggdURLBinder>();
    }

    public class BackloggdStatusSettingsViewModel : ObservableObject, ISettings
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly BackloggdStatus plugin;
        private readonly IPlayniteAPI api;

        private BackloggdStatusSettings editingClone { get; set; }

        // public ICommand OpenWebViewCommand { get; }

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

        private string gameName;
        public string GameName
        {
            get => gameName;
            set => SetValue(ref gameName, value);
        }

        private string backloggdName;
        public string BackloggdName
        {
            get => backloggdName;
            set => SetValue(ref backloggdName, value);
        }

        private string url;
        public string URL
        {
            get => url;
            set => SetValue(ref url, value);
        }

        private List<string> statusList;
        public List<string> StatusList
        {
            get => statusList;
            set => SetValue(ref statusList, value);
        }

        private string statusString;
        public string StatusString
        {
            get => statusString;
            set => SetValue(ref statusString, value);
        }

        [DontSerialize]
        public ICommand OpenWebViewCommand { get; }

        [DontSerialize]
        public ICommand RefreshCommand { get; }

        [DontSerialize]
        private readonly BackloggdClient backloggdClient = new BackloggdClient();

        public BackloggdURLBinder()
        {
            OpenWebViewCommand = new RelayCommand(OpenWebView);
            RefreshCommand = new RelayCommand(RefreshStatus);
        }

        private void OpenWebView()
        {
            logger.Debug("Call OpenWebView in BackloggdURLBinder");

            URL = backloggdClient.SetBackloggdUrl(GameName);
            RefreshStatus();
        }

        public void RefreshStatus()
        {
            if (URL == BackloggdStatus.DefaultURL)
            {
                StatusList = new List<string> { "Status: Unknown" };
                BackloggdName = "Game has not been set";
                FlattenStatus();
                return;
            }

            logger.Debug("Call RefreshStatus in BackloggdURLBinder");
            
            StatusList = backloggdClient.GetGameStatus(URL);
            BackloggdName = backloggdClient.GetBackloggdName(URL);


            FlattenStatus();

        }


        private void FlattenStatus()
        {
            StatusString = String.Empty;

            foreach (string status in StatusList)
            {
                StatusString += status + ", ";
            }

        }
    }
}