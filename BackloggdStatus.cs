using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;   
using System.Windows.Controls;


namespace BackloggdStatus
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class BackloggdStatus : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private BackloggdStatusSettingsViewModel settings { get; set; }
        public override Guid Id { get; } = Guid.Parse("228e1135-a326-4a8d-8ee9-edc1c61c0982");

        private const bool debug = true;
        private const bool verbose = true;

        public static readonly string DefaultURL = "URL not Set";

        // TODO: Make Dictionaries configurable in settings.
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
            { "Wishlist", "Not Played" }
        };

        private readonly BackloggdClient backloggdClient;

        public BackloggdStatus(IPlayniteAPI api) : base(api)
        {
            if (verbose)
            {
                logger.Trace("BackloggdStatus Constructor Called");
            }

            PlayniteApiProvider.api = api;

            settings = new BackloggdStatusSettingsViewModel(this, api);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            logger.Info("BackloggdStatus Initialized");


            backloggdClient = new BackloggdClient();





            // var games = PlayniteApi.Database.Games;
            //
            // foreach (var game in games)
            // {
            //     string backloggdStatus = GetBackloggdStatus(game.Name, out bool exists);
            //     if (exists)
            //     {
            //         SetPlayniteStatus(game, backloggdStatus);
            //     }
            //     else
            //     {
            //         SetBackloggdStatus(game, backloggdStatuses[game.CompletionStatus.Name]);
            //     }
            // }


        }


        // To add new main menu items override GetMainMenuItems
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            if (verbose)
            {
                logger.Trace("GetMainMenuItems Called");
            }
            
            yield return new MainMenuItem
            {
                // Added into "Extensions -> BackloggdStatus" menu
                MenuSection = "@BackloggdStatus",
                Description = "Sign In",
                Action = (arg1) => backloggdClient.Login()
            };
            

            // TODO: Add setting window action
            yield return new MainMenuItem
            {
                // Added into "Extensions -> BackloggdStatus" menu
                MenuSection = "@BackloggdStatus",
                Description = "Configure Status",
                Action = (args1) => throw new NotImplementedException()
            };

            if (debug)
            {
                yield return new MainMenuItem
                {
                    // Added into "Extensions -> BackloggdStatus" menu
                    MenuSection = "@BackloggdStatus",
                    Description = "Open WebView",
                    Action = (args1) => backloggdClient.OpenWebView()
                };

                yield return new MainMenuItem
                {
                    // Added into "Extensions -> BackloggdStatus" menu
                    MenuSection = "@BackloggdStatus",
                    Description = "Sign Out",
                    Action = (arg1) => backloggdClient.Logout()
                };
            }
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            
            if (verbose)
            {
                logger.Trace("GetGameMenuItems Called");
            }

            BackloggdURLBinder game = settings.Settings.BackloggdURLs.Select(x => x).First(x => Equals(x.Game, args.Games[0]));

            List<string> statusList = game.StatusList;

            if (!backloggdClient.LoggedIn)
            {
                yield return new GameMenuItem
                {
                    // Added into game context menu
                    MenuSection = "BackloggdStatus",
                    Description = "Sign In",
                    Action = (arg1) => backloggdClient.Login()
                };
            }
            else
            {
                if (game.URL == DefaultURL)
                {
                    yield return new GameMenuItem
                    {
                        // TODO: Bring up dialog with Game, Backloggd URL, and Backloggd Status
                        MenuSection = "BackloggdStatus",
                        Description = DefaultURL,
                        Action = (arg1) => throw new NotImplementedException() //TODO: Action to set url in settings
                    };
                }
                else
                {
                    yield return new GameMenuItem
                    {
                        // TODO: Bring up dialog with Game, Backloggd URL, and Backloggd Status
                        MenuSection = "BackloggdStatus",
                        Description = "Refresh Status",
                        Action = (arg1) =>
                        {
                            game.GetStatus();
                            SavePluginSettings(settings.Settings);
                        }
                    };

                    yield return new GameMenuItem
                    {
                        // Added into game context menu
                        MenuSection = "BackloggdStatus",
                        Description = "-"
                    };

                    // TODO: Slows down context menu loading. Find a way to cache this.

                    if (statusList.Count == 0)
                    {
                        statusList.Add("Status: Not Set");
                    }

                    foreach (string status in statusList)
                    {
                        yield return new GameMenuItem
                        {
                            // TODO: Bring up dialog with Game, Backloggd URL, and Backloggd Status
                            MenuSection = "BackloggdStatus",
                            Description = status,
                            Action = (arg1) =>
                            {
                                backloggdClient.OpenWebView(game.URL);
                                game.GetStatus();
                                SavePluginSettings(settings.Settings);
                                // TODO: Update Game Status when closing WebView
                            }
                        };
                    }

                    yield return new GameMenuItem
                    {
                        // Added into game context menu
                        MenuSection = "BackloggdStatus",
                        Description = "-"
                    };

                    // TODO: Add Menu Item for each potential status on Backloggd.com
                    yield return new GameMenuItem
                    {
                        // Added into game context menu
                        MenuSection = "BackloggdStatus",
                        Description = "Set Status",
                        Action = (arg1) => throw new NotImplementedException()
                    };

                    yield return new GameMenuItem
                    {
                        // Added into game context menu
                        MenuSection = "BackloggdStatus",
                        Description = "Open Backloggd.com",
                        Action = (arg1) =>
                        {
                            backloggdClient.OpenWebView(game.URL);
                            game.GetStatus();
                            SavePluginSettings(settings.Settings);
                        }
                    };
                }
            }
        }


        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            // Add code to be executed when game is finished installing.
            // TODO: If not in Backloggd library, set Backloggd status to Backlog
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // Add code to be executed when Playnite is initialized.
            backloggdClient.CheckLogin();

            // TODO: Change length check to equality check.
            // TODO: Instead of creating a new List, add and remove items from the existing List.
            if (settings.Settings.BackloggdURLs == null || settings.Settings.BackloggdURLs.Count == 0)
            {
                settings.Settings.BackloggdURLs = PlayniteApi.Database.Games.Select(game => new BackloggdURLBinder { Game = game, URL = DefaultURL, StatusList = new List<string> { "Status: Unknown" } }).ToList();
                SavePluginSettings(settings.Settings);
                logger.Info("BackloggdURLs initialized and saved.");
            }

        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // Add code to be executed when library is updated.
            // TODO: Find updated Playnite statuses and update backloggd statuses
        }


        private void SetPlayniteStatus(Game game, string backloggdStatus)
        {
            if (game.CompletionStatus.Name != "Completed")
            {
                var compStat = PlayniteApi.Database.CompletionStatuses.First(status => status.Name.Equals(playniteStatuses[backloggdStatus]));
                game.CompletionStatusId = compStat.Id;
                PlayniteApi.Database.Games.Update(game);
            }

            if (debug)
            {
                logger.Debug($"Game: {game.Name} - Playnite Status Set to: {game.CompletionStatus.Name}");
            }
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
            var view = new BackloggdStatusSettingsView();
            // view.DataContext = settings;
            // settings.Settings.Games = PlayniteApi.Database.Games.ToList();
            // settings.Settings.GameNames = settings.Settings.Games.Select(game => game.Name).ToList();
            // settings.Settings.BackloggdURLs = new List<string>();
            return view;
        }

    }

    public static class PlayniteApiProvider
    {
        public static IPlayniteAPI api { get; set; }
    }
}