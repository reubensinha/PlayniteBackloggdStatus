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

            // TODO: Change length check to equality check.
            // TODO: Instead of creating a new List, add and remove items from the existing List.
            // if (Settings.BackloggdURLs.Count != api.Database.Games.Count)
            // {
            //     Settings.BackloggdURLs = api.Database.Games.Select(game => new BackloggdURLBinder { Game = game, URL = "No URL Saved" }).ToList();
            // }
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

        private const int width = 880;
        private const int height = 530;

        public Game Game { get; set; }

        private string url;
        public string URL
        {
            get => url;
            set => SetValue(ref url, value);
        }

        [DontSerialize]
        public ICommand OpenWebViewCommand { get; }

        public BackloggdURLBinder()
        {
            OpenWebViewCommand = new RelayCommand(OpenWebView);
        }

        private void OpenWebView()
        {
            logger.Debug("Call OpenWebView in BackloggdURLBinder");

            using (var webView = PlayniteApiProvider.api.WebViews.CreateView(width, height))
            {

                // TODO: Make this faster.
                // TODO: Open directly to search result using game name.
                webView.LoadingChanged += (s, e) =>
                {
                    var currentAddress = webView.GetCurrentAddress();
                    if (!string.IsNullOrEmpty(currentAddress) && currentAddress.Contains("https://www.backloggd.com/games"))
                    {
                        URL = currentAddress;
                        webView.Close();
                    }
                };

                webView.Navigate("https://www.backloggd.com");
                webView.OpenDialog();

                if (webView.GetCurrentAddress().Contains("https://www.backloggd.com/games"))
                {
                    URL = webView.GetCurrentAddress();
                    webView.Close();
                }
            }
        }
    }
}