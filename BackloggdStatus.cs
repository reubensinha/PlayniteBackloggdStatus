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

        public const string DefaultURL = "URL not Set";

        private readonly BackloggdClient backloggdClient;

        public BackloggdStatus(IPlayniteAPI api) : base(api)
        {
            if (verbose)
            {
                logger.Trace("BackloggdStatus Constructor Called");
            }

            PlayniteApiProvider.Api = api;

            settings = new BackloggdStatusSettingsViewModel(this, api);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            logger.Info("BackloggdStatus Initialized");


            backloggdClient = new BackloggdClient();

            // Keeps BackloggdURLs updated with Library
            api.Database.Games.ItemCollectionChanged += (_, args) =>
            {
                PlayniteApi.Dialogs.ShowMessage(args.AddedItems.Count + " items added into the library.");
                args.AddedItems.ForEach(game =>
                {
                    if (settings.Settings.BackloggdURLs.FirstOrDefault(x => x.GameId == game.Id) == null)
                    {
                        settings.Settings.BackloggdURLs.Add(new BackloggdURLBinder()
                        {
                            GameId = game.Id
                        });
                        SavePluginSettings(settings.Settings);
                        logger.Info("Game not found in BackloggdURLs. Added to BackloggdURLs.");
                    }
                });

                args.RemovedItems.ForEach(game =>
                {
                    settings.Settings.BackloggdURLs.RemoveAll(x => x.GameId == game.Id);
                    SavePluginSettings(settings.Settings);
                    logger.Info("Game not found in library. Removed from BackloggdURLs.");
                });

            };

        }


        // To add new main menu items override GetMainMenuItems
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            if (verbose)
            {
                logger.Trace("GetMainMenuItems Called");
            }

            if (!backloggdClient.LoggedIn)
            {
                yield return new MainMenuItem
                {
                    // Added into "Extensions -> BackloggdStatus" menu
                    MenuSection = "@BackloggdStatus",
                    Description = "Sign In",
                    Action = (arg1) => backloggdClient.Login()
                };
            } 
            else
            {
                yield return new MainMenuItem
                {
                    // Added into "Extensions -> BackloggdStatus" menu
                    MenuSection = "@BackloggdStatus",
                    Description = "Sign Out",
                    Action = (arg1) => backloggdClient.Logout()
                };
            }

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
                Action = (arg1) => backloggdClient.Login()
            };

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

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (verbose)
            {
                logger.Trace("GetGameMenuItems Called");
            }
            var metadataLink = args.Games[0].Links.Select(link => link).FirstOrDefault(link => link.Name == "Backloggd");

            BackloggdURLBinder game = settings.Settings.BackloggdURLs.First(x => x.GameId == args.Games[0].Id);

            if (!backloggdClient.LoggedIn)
            {
                yield return new GameMenuItem
                {
                    // Added into game context menu
                    MenuSection = "BackloggdStatus",
                    Description = "Sign In",
                    Action = (arg1) => backloggdClient.Login()
                };

                yield break;
            }

            if (metadataLink == null)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = DefaultURL,
                    Action = (arg1) =>
                    {
                        string url = backloggdClient.SetBackloggdUrl(args.Games[0].Name);
                        if (url.Contains("https://www.backloggd.com/games"))
                        {
                            args.Games[0].Links.Add(new Link("Backloggd", url));
                        }
                        game.RefreshStatus();
                        SavePluginSettings(settings.Settings);
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
                    game.RefreshStatus();
                    SavePluginSettings(settings.Settings);
                }
            };


            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = game.BackloggdName
            };

            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = "-"
            };

            // TODO: Played status has special rules. Implement them.
            if (game.Played != null)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = game.Played.ToString(),
                    Action = (arg1) =>
                    {
                        backloggdClient.ToggleStatus(metadataLink.Url, "Unplayed");
                        game.RefreshStatus();
                        SavePluginSettings(settings.Settings);
                    }
                };
            }

            if (game.Playing)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = "Playing",
                    Action = (arg1) =>
                    {
                        backloggdClient.ToggleStatus(metadataLink.Url, "Playing");
                        game.RefreshStatus();
                        SavePluginSettings(settings.Settings);
                    }
                };
            }

            if (game.Backlog)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = "Backlog",
                    Action = (arg1) =>
                    {
                        backloggdClient.ToggleStatus(metadataLink.Url, "Backlog");
                        game.RefreshStatus();
                        SavePluginSettings(settings.Settings);
                    }
                };
            }

            if (game.Wishlist)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = "Wishlist",
                    Action = (arg1) =>
                    {
                        backloggdClient.ToggleStatus(metadataLink.Url, "Wishlist");
                        game.RefreshStatus();
                        SavePluginSettings(settings.Settings);
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
                    backloggdClient.ToggleStatus(metadataLink.Url, "Played");
                    game.RefreshStatus();
                    SavePluginSettings(settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status | Played",
                Description = "Completed",
                Action = (arg1) =>
                {
                    backloggdClient.ToggleStatus(metadataLink.Url, "Completed");
                    game.RefreshStatus();
                    SavePluginSettings(settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status | Played",
                Description = "Retired",
                Action = (arg1) =>
                {
                    backloggdClient.ToggleStatus(metadataLink.Url, "Retired");
                    game.RefreshStatus();
                    SavePluginSettings(settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status | Played",
                Description = "Shelved",
                Action = (arg1) =>
                {
                    backloggdClient.ToggleStatus(metadataLink.Url, "Shelved");
                    game.RefreshStatus();
                    SavePluginSettings(settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status | Played",
                Description = "Abandoned",
                Action = (arg1) =>
                {
                    backloggdClient.ToggleStatus(metadataLink.Url, "Abandoned");
                    game.RefreshStatus();
                    SavePluginSettings(settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status",
                Description = "Playing",
                Action = (arg1) => 
                {
                    backloggdClient.ToggleStatus(metadataLink.Url, "Playing");
                    game.RefreshStatus();
                    SavePluginSettings(settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status",
                Description = "Backlog",
                Action = (arg1) =>
                {
                    backloggdClient.ToggleStatus(metadataLink.Url, "Backlog");
                    game.RefreshStatus();
                    SavePluginSettings(settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                // Added into game context menu
                MenuSection = "BackloggdStatus | Toggle Status",
                Description = "Wishlist",
                Action = (arg1) =>
                {
                    backloggdClient.ToggleStatus(metadataLink.Url, "Wishlist"); 
                    game.RefreshStatus(); 
                    SavePluginSettings(settings.Settings);
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
                    backloggdClient.OpenWebView(metadataLink.Url);
                    game.RefreshStatus();
                    SavePluginSettings(settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = "Change Backloggd Game",
                Action = (arg1) =>
                {
                    string newUrl = backloggdClient.SetBackloggdUrl(args.Games[0].Name);
                    if (newUrl.Contains("https://www.backloggd.com/games"))
                    {
                        metadataLink.Url = newUrl;
                    }
                    else
                    {
                        args.Games[0].Links.Remove(metadataLink);
                    }
                    game.RefreshStatus();
                    SavePluginSettings(settings.Settings);
                }
            };
            
        }


        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            // Add code to be executed when game is finished installing.
            // TODO: If not in Backloggd library, set Backloggd status to Backlog
            // TODO: Add to BackloggdURLs
            
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // Add code to be executed when Playnite is initialized.
            backloggdClient.CheckLogin();

            // TODO: There has to be a better way to do this.
            if (settings.Settings.BackloggdURLs == null || settings.Settings.BackloggdURLs.Count == 0)
            {
                settings.Settings.BackloggdURLs = PlayniteApi.Database.Games.Select(game => new BackloggdURLBinder 
                    { 
                        GameId = game.Id
                    }).ToList();

                SavePluginSettings(settings.Settings);
                logger.Info("BackloggdURLs initialized and saved.");

                return;
            }

            foreach (BackloggdURLBinder backloggdURL in settings.Settings.BackloggdURLs.ToList())
            {
                Game game = PlayniteApi.Database.Games.FirstOrDefault(x => x.Id == backloggdURL.GameId);
                if (game == null)
                {
                    settings.Settings.BackloggdURLs.Remove(backloggdURL);
                    logger.Info("Game not found in library. Removed from BackloggdURLs.");
                }
            }

            foreach (Game databaseGame in PlayniteApi.Database.Games)
            {
                BackloggdURLBinder game = settings.Settings.BackloggdURLs.FirstOrDefault(x => x.GameId == databaseGame.Id);
                if (game == null)
                {
                    settings.Settings.BackloggdURLs.Add(new BackloggdURLBinder
                    {
                        GameId = databaseGame.Id
                    });
                    logger.Info("Game not found in BackloggdURLs. Added to BackloggdURLs.");

                    game = settings.Settings.BackloggdURLs.First(x => x.GameId == databaseGame.Id);
                }

                // TODO: Idk if this is working.
                game.RefreshStatus();

            }

            SavePluginSettings(settings.Settings);
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // Add code to be executed when library is updated.
            // TODO: Add new games to BackloggdURLs and Remove games no longer in library.
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
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
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