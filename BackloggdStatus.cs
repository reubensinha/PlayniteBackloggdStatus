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


namespace BackloggdStatus
{
    public class BackloggdStatus : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private BackloggdStatusSettingsViewModel settings { get; set; }
        public override Guid Id { get; } = Guid.Parse("228e1135-a326-4a8d-8ee9-edc1c61c0982");

        private const bool debug = false;
        private const bool verbose = false;

        private const int width = 880;
        private const int height = 530;

        public const string DefaultURL = "URL not Set";

        internal static bool loggedIn;

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

            using (var view = PlayniteApi.WebViews.CreateOffscreenView())
            {
                BackloggdClient backloggdClient = new BackloggdClient(view);
                backloggdClient.IsUserLoggedIn();
            }

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
                        if (settings.Settings.BackloggdURLs.FirstOrDefault(x => x.GameId == game.Id) == null)
                        {
                            settings.Settings.BackloggdURLs.Add(new BackloggdURLBinder
                            {
                                GameId = game.Id
                            });
                            logger.Info($"Added new game {game.Name} to BackloggdURLs.");
                        }
                    }
                }

                if (args.RemovedItems.Any())
                {
                    foreach (var game in args.RemovedItems)
                    {
                        settings.Settings.BackloggdURLs.RemoveAll(x => x.GameId == game.Id);
                        logger.Info($"Removed game {game.Name} from BackloggdURLs.");
                    }
                }

                SavePluginSettings(settings.Settings);
            };
        }

        private void SynchronizeSettingsWithLibrary()
        {
            if (verbose)
            {
                logger.Trace("Synchronizing settings with library.");
            }

            var currentGameIds = settings.Settings.BackloggdURLs.Select(binder => binder.GameId).ToHashSet();
            var libraryGames = PlayniteApi.Database.Games;

            // Add missing games to the settings
            foreach (var game in libraryGames)
            {
                if (!currentGameIds.Contains(game.Id))
                {
                    settings.Settings.BackloggdURLs.Add(new BackloggdURLBinder
                    {
                        GameId = game.Id
                    });
                    logger.Info($"Synchronized game {game.Name} into BackloggdURLs.");
                }
            }

            // Remove games from the settings that are no longer in the library
            settings.Settings.BackloggdURLs.RemoveAll(binder => !libraryGames.Any(game => game.Id == binder.GameId));

            SavePluginSettings(settings.Settings);
        }


        // To add new main menu items override GetMainMenuItems
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            if (verbose)
            {
                logger.Trace("GetMainMenuItems Called");
            }

            if (!loggedIn)
            {
                yield return new MainMenuItem
                {
                    // Added into "Extensions -> BackloggdStatus" menu
                    MenuSection = "@BackloggdStatus",
                    Description = "Sign In",
                    Action = (arg1) => 
                    {
                        using (var view = PlayniteApi.WebViews.CreateView(width, height))
                        {
                            BackloggdClient backloggdClient = new BackloggdClient(view);
                            backloggdClient.Login();
                        }
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
                        using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                        {
                            BackloggdClient backloggdClient = new BackloggdClient(view);
                            backloggdClient.Logout();
                        }
                    }
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
                Action = (arg1) => 
                {
                    using (var view = PlayniteApi.WebViews.CreateView(width, height))
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        backloggdClient.Login();
                    }
                }
            };

            yield return new MainMenuItem
            {
                // Added into "Extensions -> BackloggdStatus" menu
                MenuSection = "@BackloggdStatus",
                Description = "Open WebView",
                Action = (args1) => 
                { 
                    using (var view = PlayniteApi.WebViews.CreateView(width, height)) 
                    { 
                        BackloggdClient backloggdClient = new BackloggdClient(view); 
                        backloggdClient.OpenWebView(); 
                    } 
                }
            };

            yield return new MainMenuItem
            {
                // Added into "Extensions -> BackloggdStatus" menu
                MenuSection = "@BackloggdStatus",
                Description = "Sign Out",
                Action = (arg1) =>
                {
                    using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        backloggdClient.Logout();
                    }
                }
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

            if (!loggedIn)
            {
                yield return new GameMenuItem
                {
                    // Added into game context menu
                    MenuSection = "BackloggdStatus",
                    Description = "Sign In",
                    Action = (arg1) =>
                    {
                        using (var view = PlayniteApi.WebViews.CreateView(width, height))
                        {
                            BackloggdClient backloggdClient = new BackloggdClient(view);
                            backloggdClient.Login();
                        }
                    }
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
                        string url;
                        using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                        {
                            BackloggdClient backloggdClient = new BackloggdClient(view);
                            url = backloggdClient.SetBackloggdUrlAsync(args.Games[0].Name).GetAwaiter().GetResult();

                        }
                        if (url.Contains("https://www.backloggd.com/games"))
                        {
                            args.Games[0].Links.Add(new Link("Backloggd", url));
                            PlayniteApi.Database.Games.Update(args.Games[0]);
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

            if (game.Played != null)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = game.Played.ToString(),
                    Action = (arg1) =>
                    {
                        using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                        {
                            BackloggdClient backloggdClient = new BackloggdClient(view);
                            backloggdClient.ToggleStatusAsync(metadataLink.Url, "unset-played-btn").GetAwaiter().GetResult();
                            game.RefreshStatus(view);
                        }
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
                        using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                        {
                            BackloggdClient backloggdClient = new BackloggdClient(view);
                            backloggdClient.ToggleStatusAsync(metadataLink.Url, "Playing").GetAwaiter().GetResult();
                            game.RefreshStatus(view);
                        }
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
                        using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                        {
                            BackloggdClient backloggdClient = new BackloggdClient(view);
                            backloggdClient.ToggleStatusAsync(metadataLink.Url, "Backlog").GetAwaiter().GetResult();
                            game.RefreshStatus(view);
                        }
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
                        using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                        {
                            BackloggdClient backloggdClient = new BackloggdClient(view);
                            backloggdClient.ToggleStatusAsync(metadataLink.Url, "Wishlist").GetAwaiter().GetResult();
                            game.RefreshStatus(view);
                        }
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
                    using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        backloggdClient.ToggleStatusAsync(metadataLink.Url, "played").GetAwaiter().GetResult();
                        game.RefreshStatus(view);
                    }
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
                    using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        backloggdClient.ToggleStatusAsync(metadataLink.Url, "completed").GetAwaiter().GetResult();
                        game.RefreshStatus(view);
                    }
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
                    using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        backloggdClient.ToggleStatusAsync(metadataLink.Url, "retired").GetAwaiter().GetResult();
                        game.RefreshStatus(view);
                    }
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
                    using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        backloggdClient.ToggleStatusAsync(metadataLink.Url, "shelved").GetAwaiter().GetResult();
                        game.RefreshStatus(view);
                    }
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
                    using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        backloggdClient.ToggleStatusAsync(metadataLink.Url, "abandoned").GetAwaiter().GetResult();
                        game.RefreshStatus(view);
                    }
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
                    using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        backloggdClient.ToggleStatusAsync(metadataLink.Url, "Playing").GetAwaiter().GetResult();
                        game.RefreshStatus(view);
                    }
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
                    using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        backloggdClient.ToggleStatusAsync(metadataLink.Url, "Backlog").GetAwaiter().GetResult();
                        game.RefreshStatus(view);
                    }
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
                    using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        backloggdClient.ToggleStatusAsync(metadataLink.Url, "Wishlist").GetAwaiter().GetResult();
                        game.RefreshStatus(view);
                    }
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
                    using (var view = PlayniteApi.WebViews.CreateView(width, height))
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        backloggdClient.OpenWebView(metadataLink.Url);
                        game.RefreshStatus(view);
                    }
                    SavePluginSettings(settings.Settings);
                }
            };

            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = "Change Backloggd Game",
                Action = (arg1) =>
                {
                    string newUrl;
                    using (var view  = PlayniteApi.WebViews.CreateView(width, height))
                    {
                        BackloggdClient backloggdClient = new BackloggdClient(view);
                        newUrl = backloggdClient.SetBackloggdUrlAsync(args.Games[0].Name).GetAwaiter().GetResult();

                    }
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

        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // Add code to be executed when Playnite is initialized.
            logger.Info("Revalidating statuses for all games on application startup.");
            foreach (var binder in settings.Settings.BackloggdURLs.ToList())
            {
                var game = PlayniteApi.Database.Games.FirstOrDefault(g => g.Id == binder.GameId);
                if (game == null)
                {
                    settings.Settings.BackloggdURLs.Remove(binder);
                    logger.Info($"Removed missing game with ID {binder.GameId} from BackloggdURLs.");
                }
                else
                {
                    binder.RefreshStatus();
                }
            }

            SavePluginSettings(settings.Settings);
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
            logger.Info($"Game uninstalled: {args.Game.Name}. Removing from BackloggdURLs.");
            settings.Settings.BackloggdURLs.RemoveAll(x => x.GameId == args.Game.Id);
            SavePluginSettings(settings.Settings);
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