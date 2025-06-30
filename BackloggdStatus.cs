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
using System.Collections.Specialized;
using System.Collections.ObjectModel;


namespace BackloggdStatus
{
    public class BackloggdStatus : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public BackloggdStatusSettingsViewModel Settings { get; set; }
        public override Guid Id { get; } = Guid.Parse("228e1135-a326-4a8d-8ee9-edc1c61c0982");
        public BackloggdAPI backloggdAPI = new BackloggdAPI();

        private bool debug = false;

        private const int width = 880;
        private const int height = 530;

        public const string DefaultURL = "URL not Set";

        internal static bool loggedIn;

        public BackloggdStatus(IPlayniteAPI api) : base(api)
        {
           logger.Debug("BackloggdStatus Constructor Called");

            PlayniteApiProvider.Api = api;

            Settings = new BackloggdStatusSettingsViewModel(this, api);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            backloggdAPI.IsUserLoggedIn();


            InitializeLibrarySync();

            logger.Info("BackloggdStatus Initialized");

        }

        private void InitializeLibrarySync()
        {
            SynchronizeSettingsWithLibrary();

            // Update settings when the library changes
            PlayniteApi.Database.Games.ItemCollectionChanged += (_, args) =>
            {
                if (args.AddedItems.Any())
                {
                    foreach (var game in args.AddedItems)
                    {
                        if (Settings.Settings.BackloggdGamesList.FirstOrDefault(x => x.GameId == game.Id) == null)
                        {
                            Settings.Settings.BackloggdGamesList.Add(new BackloggdGame
                            {
                                GameId = game.Id
                            });
                            logger.Info($"Added new game {game.Name} to BackloggdGamesList.");
                        }
                    }
                }

                if (args.RemovedItems.Any())
                {
                    foreach (var game in args.RemovedItems)
                    {
                        Settings.Settings.BackloggdGamesList.RemoveAll(x => x.GameId == game.Id);
                        logger.Info($"Removed game {game.Name} from BackloggdGamesList.");
                    }
                }

                SavePluginSettings(Settings.Settings);
            };
        }

        private void SynchronizeSettingsWithLibrary()
        {
            logger.Trace("Backloggd: Synchronizing settings with library.");

            var currentGameIds = Settings.Settings.BackloggdGamesList.Select(binder => binder.GameId).ToHashSet();
            var libraryGames = PlayniteApi.Database.Games;

            // Add missing games to the settings
            foreach (var game in libraryGames)
            {
                if (!currentGameIds.Contains(game.Id))
                {
                    Settings.Settings.BackloggdGamesList.Add(new BackloggdGame
                    {
                        GameId = game.Id
                    });
                    logger.Info($"Synchronized game {game.Name} into BackloggdGamesList.");
                }
            }

            // Remove games from the settings that are no longer in the library
            Settings.Settings.BackloggdGamesList.RemoveAll(binder => !libraryGames.Any(game => game.Id == binder.GameId));

            SavePluginSettings(Settings.Settings);
        }


        // To add new main menu items override GetMainMenuItems
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
           logger.Debug("BakloggdStatus: GetMainMenuItems Called");

            yield return new MainMenuItem
            {
                // Added into "Extensions" menu
                MenuSection = "@BackloggdStatus",
                Description = "Clear Extension Data",
                Action = (arg1) =>
                {
                    logger.Info("Clearing BackloggdStatus settings data.");
                    Settings.Settings.BackloggdGamesList.Clear();
                    SavePluginSettings(Settings.Settings);
                    SynchronizeSettingsWithLibrary();
                }
            };

            if (!loggedIn)
            {
                yield return new MainMenuItem
                {
                    // Added into "Extensions -> BackloggdStatus" menu
                    MenuSection = "@BackloggdStatus",
                    Description = "Sign In",
                    Action = (arg1) => 
                    {
                        backloggdAPI.Login();
                        backloggdAPI.IsUserLoggedIn();
                    }
                };
            }
            else
            {
                yield return new MainMenuItem
                {
                    // Added into "Extensions -> BackloggdStatus" menu
                    MenuSection = "@BackloggdStatus",
                    Description = "Sign Out",
                    Action = (arg1) => 
                    {
                        backloggdAPI.Logout();
                        backloggdAPI.IsUserLoggedIn();
                    }
                };
            }

            yield return new MainMenuItem
            {
                // Added into "Extensions" menu
                MenuSection = "@BackloggdStatus",
                Description = "Toggle Debug Mode",
                Action = (arg1) =>
                {
                    debug = !debug;
                    logger.Info($"Set BackloggdStatus.debug to: {debug}");
                }
            };

            if (!debug)
            {
                yield break;
            }

            yield return new MainMenuItem
            {
                MenuSection = "@BackloggdStatus",
                Description = "-"
            };

            yield return new MainMenuItem
            {
                MenuSection = "@BackloggdStatus",
                Description = "DEBUG OPTIONS"
            };

            yield return new MainMenuItem
            {
                // Added into "Extensions -> BackloggdStatus" menu
                MenuSection = "@BackloggdStatus",
                Description = "Sign In",
                Action = (arg1) => 
                {
                    backloggdAPI.Login();
                    backloggdAPI.IsUserLoggedIn();
                }
            };

            yield return new MainMenuItem
            {
                // Added into "Extensions -> BackloggdStatus" menu
                MenuSection = "@BackloggdStatus",
                Description = "Open WebView",
                Action = (args1) => 
                {
                    backloggdAPI.OpenWebView();
                }
            };

            yield return new MainMenuItem
            {
                // Added into "Extensions -> BackloggdStatus" menu
                MenuSection = "@BackloggdStatus",
                Description = "Sign Out",
                Action = (arg1) =>
                {
                    backloggdAPI.Logout();
                    backloggdAPI.IsUserLoggedIn();
                }
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            logger.Debug("GetGameMenuItems Called");

            //Link metadataLink;

            //if (args.Games[0].Links == null)
            //{
            //    metadataLink = null;
            //}
            //else
            //{
            //    metadataLink = args.Games[0].Links.Select(link => link).FirstOrDefault(link => link.Name == "Backloggd");
            //}

            if (!loggedIn)
            {
                yield return new GameMenuItem
                {
                    // Added into game context menu
                    MenuSection = "BackloggdStatus",
                    Description = "Sign In",
                    Action = (arg1) =>
                    {
                        backloggdAPI.Login();
                        backloggdAPI.IsUserLoggedIn();
                    }
                };

                yield break;
            }

            Game playniteGame = args.Games[0];
            BackloggdGame backloggdGame = Settings.Settings.BackloggdGamesList.FirstOrDefault(x => x.GameId == playniteGame.Id);
            if (backloggdGame == null)
            {
                // Handle the case where no matching element is found
                logger.Error("No matching BackloggdGame found for the game.");
                Settings.Settings.BackloggdGamesList.RemoveAll(x => x.GameId == playniteGame.Id);
                SavePluginSettings(Settings.Settings);

                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = DefaultURL,
                    Action = (arg1) =>
                    {
                        // TODO: Rework SetBackloggdUrl method.
                        string url = backloggdAPI.SetBackloggdUrl(playniteGame.Name);
                        if (url == null)
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage("Failed to get URL. Please try again.", "Backloggd Error");
                            logger.Error("Failed to get URL from webView");
                        } else if (url.Contains($"{BackloggdAPI.baseUrl}/games"))
                        {
                            //if (args.Games[0].Links == null)
                            //{
                            //    args.Games[0].Links = new ObservableCollection<Link>();
                            //}
                            //args.Games[0].Links.Add(new Link("Backloggd", url));

                            backloggdGame = backloggdAPI.GetGameFromURL(url, playniteGame.Id);

                            Settings.Settings.BackloggdGamesList.Add(backloggdGame);
                            SavePluginSettings(Settings.Settings);
                        } 
                        else
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage("Not valid Backloggd Url", "Backloggd Error");
                            logger.Debug($"Not valid Backloggd Url: {url}");
                        }
                    }
                };

                yield break;
            }


            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = "Refresh Status",
                Action = (arg1) =>
                {
                    backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                    SavePluginSettings(Settings.Settings);
                }
            };


            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = backloggdGame.BackloggdName
            };

            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = "-"
            };

            if (backloggdGame.Played != null)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = backloggdGame.Played.ToString(),
                    Action = (arg1) =>
                    {
                        backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "unset-played-btn").GetAwaiter().GetResult();
                        backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                        SavePluginSettings(Settings.Settings);
                    }
                };
            }

            if (backloggdGame.Playing)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = "Playing",
                    Action = (arg1) =>
                    {
                        backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "Playing").GetAwaiter().GetResult();
                        backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                        SavePluginSettings(Settings.Settings);
                    }
                };
            }

            if (backloggdGame.Backlog)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = "Backlog",
                    Action = (arg1) =>
                    {
                        backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "Backlog").GetAwaiter().GetResult();
                        backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                        SavePluginSettings(Settings.Settings);
                    }
                };
            }

            if (backloggdGame.Wishlist)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = "Wishlist",
                    Action = (arg1) =>
                    {
                        backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "Wishlist").GetAwaiter().GetResult();
                        backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                        SavePluginSettings(Settings.Settings);
                    }
                };
            }

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus",
                Description = "-"
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status | Played",
                Description = "Played",
                Action = (arg1) =>
                {
                    backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "played").GetAwaiter().GetResult();
                    backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                    SavePluginSettings(Settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status | Played",
                Description = "Completed",
                Action = (arg1) =>
                {
                    backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "completed").GetAwaiter().GetResult();
                    backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                    SavePluginSettings(Settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status | Played",
                Description = "Retired",
                Action = (arg1) =>
                {
                    backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "retired").GetAwaiter().GetResult();
                    backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                    SavePluginSettings(Settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status | Played",
                Description = "Shelved",
                Action = (arg1) =>
                {
                    backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "shelved").GetAwaiter().GetResult();
                    backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                    SavePluginSettings(Settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status | Played",
                Description = "Abandoned",
                Action = (arg1) =>
                {
                    backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "abandoned").GetAwaiter().GetResult();
                    backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                    SavePluginSettings(Settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status",
                Description = "Playing",
                Action = (arg1) =>
                {
                    backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "Playing").GetAwaiter().GetResult();
                    backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                    SavePluginSettings(Settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status",
                Description = "Backlog",
                Action = (arg1) =>
                {
                    backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "Backlog").GetAwaiter().GetResult();
                    backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                    SavePluginSettings(Settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status",
                Description = "Wishlist",
                Action = (arg1) =>
                {
                    backloggdAPI.ToggleStatusAsync(backloggdGame.BackloggdUrl, "Wishlist").GetAwaiter().GetResult();
                    backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                    SavePluginSettings(Settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus",
                Description = "-"
            };

            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = "Open Backloggd Page",
                Action = (arg1) =>
                {
                    backloggdAPI.OpenWebView(backloggdGame.BackloggdUrl);
                    backloggdGame = backloggdAPI.RefreshStatus(backloggdGame);
                    SavePluginSettings(Settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = "Change Backloggd Game",
                Action = (arg1) =>
                {
                    Settings.Settings.BackloggdGamesList.RemoveAll(x => x.GameId == playniteGame.Id);
                    string url = backloggdAPI.SetBackloggdUrl(playniteGame.Name);
                    if (url == null)
                    {
                        PlayniteApi.Dialogs.ShowErrorMessage("Failed to set Backloggd URL. Please try again.", "Backloggd Error");
                    }
                    else if (url.Contains($"{BackloggdAPI.baseUrl}/games"))
                    {
                        //if (args.Games[0].Links == null)
                        //{
                        //    args.Games[0].Links = new ObservableCollection<Link>();
                        //}
                        //args.Games[0].Links.Add(new Link("Backloggd", url));

                        backloggdGame = backloggdAPI.GetGameFromURL(url, playniteGame.Id);

                        Settings.Settings.BackloggdGamesList.Add(backloggdGame);
                        SavePluginSettings(Settings.Settings);

                    }
                    else
                    {
                        PlayniteApi.Dialogs.ShowErrorMessage("Not valid Backloggd Url", "Backloggd Error");
                        logger.Debug($"Not valid Backloggd Url: {url}");
                    }
                }
            };

        }


        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            // Add code to be executed when game is finished installing.

        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // Add code to be executed when Playnite is initialized.
            logger.Info("Revalidating statuses for all games on application startup.");
            foreach (var backloggdGame in Settings.Settings.BackloggdGamesList.ToList())
            {
                var game = PlayniteApi.Database.Games.FirstOrDefault(g => g.Id == backloggdGame.GameId);
                if (game == null)
                {
                    Settings.Settings.BackloggdGamesList.Remove(backloggdGame);
                    logger.Info($"Removed missing game with ID {backloggdGame.GameId} from BackloggdGamesList.");
                }
                else
                {
                    backloggdAPI.RefreshStatus(backloggdGame);
                }
            }

            SavePluginSettings(Settings.Settings);
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // Add code to be executed when library is updated.
            SynchronizeSettingsWithLibrary();
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

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            // Add code to be executed when game is uninstalled.
            logger.Info($"Game uninstalled: {args.Game.Name}. Removing from BackloggdGamesList.");
            Settings.Settings.BackloggdGamesList.RemoveAll(x => x.GameId == args.Game.Id);
            SavePluginSettings(Settings.Settings);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            var view = new BackloggdStatusSettingsView();
            return view;
        }

    }

    public static class PlayniteApiProvider
    {
        public static IPlayniteAPI Api { get; set; }
    }
}