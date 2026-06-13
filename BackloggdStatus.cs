using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BackloggdStatus
{
    public class BackloggdStatus : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public BackloggdStatusSettingsViewModel Settings { get; set; }
        public override Guid Id { get; } = Guid.Parse("228e1135-a326-4a8d-8ee9-edc1c61c0982");
        public BackloggdAPI backloggdAPI;

        internal static bool loggedIn;

        public BackloggdStatus(IPlayniteAPI api) : base(api)
        {
            logger.Debug("BackloggdStatus constructor called.");

            PlayniteApiProvider.Api = api;

            Settings = new BackloggdStatusSettingsViewModel(this, api);
            Properties = new GenericPluginProperties { HasSettings = true };

            var backgroundView = api.WebViews.CreateOffscreenView();
            backloggdAPI = new BackloggdAPI(backgroundView);
            backloggdAPI.OnUsernameResolved = name => Settings.UpdateUsername(name);

            backloggdAPI.IsUserLoggedIn();

            logger.Info("BackloggdStatus initialized.");
        }

        // ────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ────────────────────────────────────────────────────────────────────

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (Settings.Settings.SyncOnStartup)
                Task.Run(() => SyncAll());
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            Settings.Settings.BackloggdGamesList.RemoveAll(x => x.GameId == args.Game.Id);
            SavePluginSettings(Settings.Settings);
        }

        // ────────────────────────────────────────────────────────────────────
        // Settings
        // ────────────────────────────────────────────────────────────────────

        public override ISettings GetSettings(bool firstRunSettings) => Settings;

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            Settings.OnSignInRequested = () =>
            {
                using (var v = PlayniteApi.WebViews.CreateView(500, 500))
                    backloggdAPI.Login(v);
                backloggdAPI.IsUserLoggedIn();
            };

            Settings.OnSignOutRequested = () =>
            {
                backloggdAPI.Logout();
                Settings.UpdateUsername("Not signed in");
            };

            Settings.OnUnlinkRequested = id =>
            {
                Settings.Settings.BackloggdGamesList.RemoveAll(x => x.GameId == id);
                SavePluginSettings(Settings.Settings);
                Settings.RefreshMappedGames(PlayniteApi);
            };

            Settings.OnSyncAllRequested = () => SyncAll();

            Settings.OnOpenLogRequested = () =>
            {
                try { System.Diagnostics.Process.Start(GetPluginUserDataPath()); }
                catch (Exception ex) { logger.Error($"Could not open log folder: {ex.Message}"); }
            };

            Settings.LogFilePath = GetPluginUserDataPath();
            Settings.RefreshMappedGames(PlayniteApi);

            var view = new BackloggdStatusSettingsView();
            view.DataContext = Settings;
            return view;
        }

        // ────────────────────────────────────────────────────────────────────
        // Main menu
        // ────────────────────────────────────────────────────────────────────

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            if (!loggedIn)
            {
                yield return new MainMenuItem
                {
                    MenuSection = "@BackloggdStatus",
                    Description = "Sign In",
                    Action = _ =>
                    {
                        using (var v = PlayniteApi.WebViews.CreateView(500, 500))
                            backloggdAPI.Login(v);
                        backloggdAPI.IsUserLoggedIn();
                    }
                };
            }
            else
            {
                yield return new MainMenuItem
                {
                    MenuSection = "@BackloggdStatus",
                    Description = "Sign Out",
                    Action = _ =>
                    {
                        backloggdAPI.Logout();
                        Settings.UpdateUsername("Not signed in");
                    }
                };

                yield return new MainMenuItem
                {
                    MenuSection = "@BackloggdStatus",
                    Description = "Sync All",
                    Action = _ => Task.Run(() => SyncAll()).GetAwaiter().GetResult()
                };
            }

            yield return new MainMenuItem
            {
                MenuSection = "@BackloggdStatus",
                Description = "Clear Extension Data",
                Action = _ =>
                {
                    Settings.Settings.BackloggdGamesList.Clear();
                    SavePluginSettings(Settings.Settings);
                }
            };
        }

        // ────────────────────────────────────────────────────────────────────
        // Game context menu
        // ────────────────────────────────────────────────────────────────────

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var playniteGame = args.Games.First();
            var bg = Settings.Settings.BackloggdGamesList
                .FirstOrDefault(x => x.GameId == playniteGame.Id);

            if (!loggedIn)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = "Sign In",
                    Action = _ =>
                    {
                        using (var v = PlayniteApi.WebViews.CreateView(500, 500))
                            backloggdAPI.Login(v);
                        backloggdAPI.IsUserLoggedIn();
                    }
                };
                yield break;
            }

            if (bg == null || string.IsNullOrEmpty(bg.BackloggdUrl))
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus",
                    Description = "Link to Backloggd…",
                    Action = _ => LinkGame(playniteGame)
                };
                yield break;
            }

            // ── Linked game menu ─────────────────────────────────────────
            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = "Refresh Status",
                Action = _ => ReplaceGame(bg, Task.Run(() => backloggdAPI.RefreshStatus(bg)).GetAwaiter().GetResult())
            };

            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = bg.BackloggdName   // label — no action
            };

            yield return new GameMenuItem { MenuSection = "BackloggdStatus", Description = "-" };

            yield return StatusToggle(bg, "Playing", bg.Playing);
            yield return StatusToggle(bg, "Backlog",  bg.Backlog);
            yield return StatusToggle(bg, "Wishlist", bg.Wishlist);

            yield return new GameMenuItem { MenuSection = "BackloggdStatus", Description = "-" };

            // ── Played-status submenu ────────────────────────────────────
            if (bg.Played.HasValue)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus | Played Status",
                    Description = "Remove Played Status",
                    Action = _ =>
                    {
                        var updated = Task.Run(async () =>
                        {
                            // Opens the played-type modal, then clicks #unset-played-btn ("Mark as unplayed")
                            await backloggdAPI.ToggleStatusAsync(bg.BackloggdUrl, "unset-played-btn");
                            return backloggdAPI.RefreshStatus(bg);
                        }).GetAwaiter().GetResult();
                        ReplaceGame(bg, updated);
                    }
                };
            }

            foreach (PlayedStatus ps in Enum.GetValues(typeof(PlayedStatus)))
            {
                bool isActive = bg.Played == ps;
                string psName = ps.ToString();
                yield return new GameMenuItem
                {
                    MenuSection = "BackloggdStatus | Played Status",
                    Description = Check(isActive) + psName,
                    Action = _ =>
                    {
                        var updated = Task.Run(async () =>
                        {
                            await backloggdAPI.ToggleStatusAsync(bg.BackloggdUrl, psName.ToLower());
                            return backloggdAPI.RefreshStatus(bg);
                        }).GetAwaiter().GetResult();
                        ReplaceGame(bg, updated);
                    }
                };
            }

            yield return new GameMenuItem { MenuSection = "BackloggdStatus", Description = "-" };

            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = "Open on Backloggd",
                Action = _ =>
                {
                    try { System.Diagnostics.Process.Start(bg.BackloggdUrl); }
                    catch (Exception ex) { logger.Error($"Could not open URL: {ex.Message}"); }
                }
            };

            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = "Link to Backloggd…",
                Action = _ => LinkGame(playniteGame)
            };

            yield return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = "Unlink from Backloggd",
                Action = _ =>
                {
                    Settings.Settings.BackloggdGamesList.RemoveAll(x => x.GameId == playniteGame.Id);
                    SavePluginSettings(Settings.Settings);
                }
            };
        }

        // ────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────

        private GameMenuItem StatusToggle(BackloggdGame bg, string statusName, bool isActive)
        {
            return new GameMenuItem
            {
                MenuSection = "BackloggdStatus",
                Description = Check(isActive) + statusName,
                Action = _ =>
                {
                    var updated = Task.Run(async () =>
                    {
                        await backloggdAPI.ToggleStatusAsync(bg.BackloggdUrl, statusName);
                        return backloggdAPI.RefreshStatus(bg);
                    }).GetAwaiter().GetResult();
                    ReplaceGame(bg, updated);
                }
            };
        }

        private void ReplaceGame(BackloggdGame old, BackloggdGame updated)
        {
            if (updated == null) return;
            updated.LastSynced = DateTime.Now;

            int idx = Settings.Settings.BackloggdGamesList.IndexOf(old);
            if (idx >= 0)
                Settings.Settings.BackloggdGamesList[idx] = updated;
            else
                Settings.Settings.BackloggdGamesList.Add(updated);

            SavePluginSettings(Settings.Settings);
        }

        private void SyncAll()
        {
            logger.Info("SyncAll started.");
            foreach (var bg in Settings.Settings.BackloggdGamesList.ToList())
            {
                if (PlayniteApi.Database.Games.FirstOrDefault(g => g.Id == bg.GameId) == null)
                {
                    Settings.Settings.BackloggdGamesList.Remove(bg);
                    logger.Info($"Removed stale entry for game ID {bg.GameId}.");
                    continue;
                }
                ReplaceGame(bg, backloggdAPI.RefreshStatus(bg));
            }
            SavePluginSettings(Settings.Settings);
            logger.Info("SyncAll complete.");
        }

        private void LinkGame(Game playniteGame)
        {
            var results = Task.Run(() => backloggdAPI.SearchGames(playniteGame.Name))
                             .GetAwaiter().GetResult();

            if (results.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    $"No Backloggd results found for \"{playniteGame.Name}\".",
                    "BackloggdStatus");
                return;
            }

            var dialog = new Views.GameSearchDialog(playniteGame.Name, results)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                var linked = Task.Run(() =>
                    backloggdAPI.GetGameFromURL(dialog.Result.Url, playniteGame.Id))
                    .GetAwaiter().GetResult();

                if (linked != null)
                {
                    linked.LastSynced = DateTime.Now;
                    Settings.Settings.BackloggdGamesList.RemoveAll(x => x.GameId == playniteGame.Id);
                    Settings.Settings.BackloggdGamesList.Add(linked);
                    SavePluginSettings(Settings.Settings);
                }
            }
        }

        private static string Check(bool active) => active ? "✓ " : "  ";
    }

    public static class PlayniteApiProvider
    {
        public static IPlayniteAPI Api { get; set; }
    }
}
