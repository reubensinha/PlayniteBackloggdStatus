using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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

        private readonly SemaphoreSlim autoStatusSyncLock = new SemaphoreSlim(1, 1);

        // Games whose CompletionStatusId was just written by a pull operation — the resulting
        // ItemUpdated event must be ignored by the automatic push handler, or a pull would
        // immediately trigger a push back to Backloggd (possibly undoing the pull if the push
        // and pull mappings for that status aren't exact inverses).
        private readonly HashSet<Guid> pullSuppressedGameIds = new HashSet<Guid>();
        private readonly object pullSuppressLock = new object();

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

            api.Database.Games.ItemUpdated += Games_ItemUpdated;

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

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            PlayniteApi.Database.Games.ItemUpdated -= Games_ItemUpdated;
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            Settings.Settings.BackloggdGamesList.RemoveAll(x => x.GameId == args.Game.Id);
            SavePluginSettings(Settings.Settings);
        }

        private void Games_ItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            if (!loggedIn || !Settings.Settings.AutoApplyStatusMappings) return;

            var pending = new List<PendingMapping>();
            foreach (var u in e.UpdatedItems)
            {
                if (u.OldData.CompletionStatusId == u.NewData.CompletionStatusId)
                    continue; // ItemUpdateEvent<Game> has no changed-field list — diff Old/New directly

                lock (pullSuppressLock)
                {
                    if (pullSuppressedGameIds.Contains(u.NewData.Id))
                        continue; // this change came from our own pull — don't push it back
                }

                var bg = Settings.Settings.BackloggdGamesList.FirstOrDefault(x => x.GameId == u.NewData.Id);
                if (bg == null || string.IsNullOrEmpty(bg.BackloggdUrl))
                    continue; // only linked games

                var mapping = Settings.Settings.StatusMappings
                    .FirstOrDefault(m => m.CompletionStatusId == u.NewData.CompletionStatusId);
                if (mapping == null || StatusMappingResolver.IsNoOpMapping(mapping))
                    continue; // unmapped — opt-in only

                pending.Add(new PendingMapping { Game = bg, Mapping = mapping });
            }
            if (pending.Count == 0) return;

            Task.Run(async () =>
            {
                await autoStatusSyncLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    foreach (var p in pending)
                        await ApplyStatusMappingAsync(p.Game, p.Mapping).ConfigureAwait(false);
                }
                finally { autoStatusSyncLock.Release(); }
            });
        }

        private class PendingMapping
        {
            public BackloggdGame Game;
            public CompletionStatusMapping Mapping;
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

            Settings.OnSyncAllRequested = () => PlayniteApi.Dialogs.ActivateGlobalProgress(
                args => SyncAll(args),
                new GlobalProgressOptions("Syncing all games…", cancelable: true));

            Settings.OnApplyStatusMappingsRequested = () =>
            {
                var candidates = BuildPushCandidates();

                var confirm = new Views.StatusMappingConfirmDialog(
                    "Apply Status Mappings",
                    "Playnite Game", "Current Backloggd Status", "New Backloggd Status",
                    candidates.Select(c => new Views.StatusMappingPreviewRow
                    {
                        GameName      = c.Game.BackloggdName,
                        CurrentStatus = BackloggdStatusSettingsViewModel.BuildStatusSummary(c.Game),
                        NewStatus     = BackloggdStatusSettingsViewModel.BuildStatusSummary(SimulateApply(c.Game, c.Mapping))
                    }).ToList())
                { Owner = Application.Current.MainWindow };

                if (confirm.ShowDialog() != true) return;

                List<Views.StatusMappingReportRow> report = null;
                PlayniteApi.Dialogs.ActivateGlobalProgress(
                    async args => report = await ApplyPushCandidatesAsync(candidates, args),
                    new GlobalProgressOptions("Applying status mappings…", cancelable: true));

                if (Settings.Settings.IsDebugMode && report != null && report.Count > 0)
                {
                    new Views.StatusMappingReportDialog(
                        "Status Mapping Report — Push",
                        "Backloggd Game", "Old Status", "New Status", report)
                    { Owner = Application.Current.MainWindow }.ShowDialog();
                }
            };

            Settings.OnPullFromBackloggdRequested = () =>
            {
                List<PullCandidate> candidates = null;
                PlayniteApi.Dialogs.ActivateGlobalProgress(
                    args => candidates = BuildPullCandidates(args),
                    new GlobalProgressOptions("Checking Backloggd status…", cancelable: true));

                if (candidates == null || candidates.Count == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage("No status changes to pull.", "BackloggdStatus");
                    return;
                }

                var confirm = new Views.StatusMappingConfirmDialog(
                    "Pull From Backloggd",
                    "Backloggd Game", "Current Playnite Status", "New Playnite Status",
                    candidates.Select(c => new Views.StatusMappingPreviewRow
                    {
                        GameName      = c.RefreshedBg.BackloggdName,
                        CurrentStatus = c.OldCompletionStatusName,
                        NewStatus     = c.NewCompletionStatusName
                    }).ToList())
                { Owner = Application.Current.MainWindow };

                if (confirm.ShowDialog() != true) return;

                var report = ApplyPullCandidates(candidates);

                if (Settings.Settings.IsDebugMode && report.Count > 0)
                {
                    new Views.StatusMappingReportDialog(
                        "Status Mapping Report — Pull",
                        "Backloggd Game", "Old Playnite Status", "New Playnite Status", report)
                    { Owner = Application.Current.MainWindow }.ShowDialog();
                }
            };

            Settings.OnOpenLogRequested = () =>
            {
                try { System.Diagnostics.Process.Start(GetPluginUserDataPath()); }
                catch (Exception ex) { logger.Error($"Could not open log folder: {ex.Message}"); }
            };

#if DEBUG
            string dataPath = GetPluginUserDataPath();
            Settings.OnRunTestsRequested = () =>
            {
                List<TestResult> results = null;
                PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                {
                    progress.IsIndeterminate = true;
                    progress.Text = "Running integration tests…";
                    results = new BackloggdTestRunner(backloggdAPI, dataPath).RunAll();
                }, new GlobalProgressOptions("Integration Tests", cancelable: false));

                if (results != null)
                    new Views.TestResultsDialog(results) { Owner = Application.Current.MainWindow }.ShowDialog();
            };
#endif

            Settings.OnCollectDiagnosticsRequested = () =>
            {
                string report = null;
                PlayniteApi.Dialogs.ActivateGlobalProgress(args =>
                {
                    args.IsIndeterminate = true;
                    var sb = new StringBuilder();
                    sb.AppendLine("BackloggdStatus Diagnostics");
                    sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine();
                    sb.AppendLine("=== Plugin ===");
                    sb.AppendLine($"Version:       {typeof(BackloggdStatus).Assembly.GetName().Version}");
                    sb.AppendLine($"SyncOnStartup: {Settings.Settings.SyncOnStartup}");
                    sb.AppendLine($"IsDebugMode:   {Settings.Settings.IsDebugMode}");
                    sb.AppendLine($"Mapped games:  {Settings.Settings.BackloggdGamesList.Count}");
                    sb.AppendLine();
                    sb.AppendLine("=== Playnite ===");
                    sb.AppendLine($"Version: {PlayniteApi.ApplicationInfo.ApplicationVersion}");
                    sb.AppendLine();
                    sb.AppendLine("=== System ===");
                    sb.AppendLine($"OS:         {Environment.OSVersion}");
                    sb.AppendLine($".NET:       {Environment.Version}");
                    sb.AppendLine($"64-bit OS:  {Environment.Is64BitOperatingSystem}");
                    sb.AppendLine($"Processors: {Environment.ProcessorCount}");
                    sb.AppendLine();
                    sb.AppendLine("=== Log File ===");
                    var logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Playnite", "extensions.log");
                    sb.AppendLine($"Expected path: {logPath}");
                    sb.AppendLine($"Exists:        {File.Exists(logPath)}");
                    sb.AppendLine();
                    sb.AppendLine("=== Backloggd Connectivity ===");
                    sb.AppendLine(backloggdAPI.RunConnectivityDiagnostics());
                    report = sb.ToString();
                }, new GlobalProgressOptions("Collecting diagnostics…", cancelable: false));

                if (report != null)
                {
                    var outPath = Path.Combine(GetPluginUserDataPath(), $"diagnostics_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt");
                    File.WriteAllText(outPath, report);
                    PlayniteApi.Dialogs.ShowMessage($"Diagnostics saved to:\n{outPath}", "BackloggdStatus");
                    try { System.Diagnostics.Process.Start(GetPluginUserDataPath()); } catch { }
                }
            };

            Settings.LogFilePath = GetPluginUserDataPath();
            Settings.RefreshMappedGames(PlayniteApi);
            Settings.RefreshStatusMappings(PlayniteApi);
            Settings.RefreshPullStatusMappings(PlayniteApi);

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
                    Action = _ => PlayniteApi.Dialogs.ActivateGlobalProgress(
                        progress => SyncAll(progress),
                        new GlobalProgressOptions("Syncing all games…", cancelable: true))
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
                Action = _ =>
                {
                    BackloggdGame updated = null;
                    var r = PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                    {
                        progress.IsIndeterminate = true;
                        updated = backloggdAPI.RefreshStatus(bg);
                    }, new GlobalProgressOptions("Refreshing status…", cancelable: true));
                    if (!r.Canceled)
                        ReplaceGame(bg, updated);
                }
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
                        BackloggdGame updated = null;
                        var r = PlayniteApi.Dialogs.ActivateGlobalProgress(async progress =>
                        {
                            progress.IsIndeterminate = true;
                            // Opens the played-type modal, then clicks #unset-played-btn ("Mark as unplayed")
                            await backloggdAPI.ToggleStatusAsync(bg.BackloggdUrl, "unset-played-btn", playedAlreadySet: true);
                            updated = backloggdAPI.RefreshStatus(bg);
                        }, new GlobalProgressOptions("Removing played status…", cancelable: true));
                        if (!r.Canceled)
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
                        BackloggdGame updated = null;
                        var r = PlayniteApi.Dialogs.ActivateGlobalProgress(async progress =>
                        {
                            progress.IsIndeterminate = true;
                            await backloggdAPI.ToggleStatusAsync(bg.BackloggdUrl, psName.ToLower(), bg.Played.HasValue);
                            updated = backloggdAPI.RefreshStatus(bg);
                        }, new GlobalProgressOptions($"Setting {psName}…", cancelable: true));
                        if (!r.Canceled)
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
                    BackloggdGame updated = null;
                    var r = PlayniteApi.Dialogs.ActivateGlobalProgress(async args =>
                    {
                        args.IsIndeterminate = true;
                        await backloggdAPI.ToggleStatusAsync(bg.BackloggdUrl, statusName);
                        updated = backloggdAPI.RefreshStatus(bg);
                    }, new GlobalProgressOptions($"Setting {statusName}…", cancelable: true));
                    if (!r.Canceled)
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

        private void SyncAll(GlobalProgressActionArgs args = null)
        {
            logger.Info("SyncAll started.");
            var games = Settings.Settings.BackloggdGamesList.ToList();
            if (args != null) args.ProgressMaxValue = games.Count;

            for (int i = 0; i < games.Count; i++)
            {
                if (args?.CancelToken.IsCancellationRequested == true) break;
                var bg = games[i];
                if (args != null)
                {
                    args.Text = $"Syncing {bg.BackloggdName} ({i + 1}/{games.Count})";
                    args.CurrentProgressValue = i + 1;
                }
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
            List<BackloggdSearchResult> results = null;
            var searchResult = PlayniteApi.Dialogs.ActivateGlobalProgress(args =>
            {
                args.IsIndeterminate = true;
                results = backloggdAPI.SearchGames(playniteGame.Name);
            }, new GlobalProgressOptions($"Searching for \"{playniteGame.Name}\"…", cancelable: true));

            if (searchResult.Canceled) return;

            if (results == null || results.Count == 0)
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
                BackloggdGame linked = null;
                PlayniteApi.Dialogs.ActivateGlobalProgress(args =>
                {
                    args.IsIndeterminate = true;
                    linked = backloggdAPI.GetGameFromURL(dialog.Result.Url, playniteGame.Id);
                }, new GlobalProgressOptions($"Fetching \"{dialog.Result.Title}\"…"));

                if (linked != null)
                {
                    linked.LastSynced = DateTime.Now;
                    Settings.Settings.BackloggdGamesList.RemoveAll(x => x.GameId == playniteGame.Id);
                    Settings.Settings.BackloggdGamesList.Add(linked);
                    SavePluginSettings(Settings.Settings);
                }
            }
        }

        private async Task ApplyStatusMappingAsync(BackloggdGame bg, CompletionStatusMapping mapping)
        {
            var ops = StatusMappingResolver.ComputeOperations(bg, mapping);
            if (ops.Count == 0)
            {
                logger.Debug($"Skipping status mapping push for '{bg.BackloggdName}' — already matches the mapped state.");
                return;
            }

            try
            {
                foreach (var op in ops)
                    await backloggdAPI.ToggleStatusAsync(bg.BackloggdUrl, op.Status, op.PlayedAlreadySet).ConfigureAwait(false);

                var updated = backloggdAPI.RefreshStatus(bg);
                ReplaceGame(bg, updated);
                logger.Info($"Synced completion status change for '{bg.BackloggdName}' -> {ops.Count} operation(s) applied.");
            }
            catch (Exception ex)
            {
                logger.Error($"Status mapping push failed for GameId={bg.GameId}: {ex.Message}");
            }
        }

        // Returns a copy of bg with Playing/Backlog/Wishlist/Played updated per mapping's
        // non-Unchanged dimensions — used to preview the resulting state without touching the network.
        private static BackloggdGame SimulateApply(BackloggdGame bg, CompletionStatusMapping mapping)
        {
            var simulated = new BackloggdGame
            {
                GameId = bg.GameId,
                BackloggdName = bg.BackloggdName,
                BackloggdUrl = bg.BackloggdUrl,
                Playing = mapping.Playing == TriState.Unchanged ? bg.Playing : mapping.Playing == TriState.On,
                Backlog = mapping.Backlog == TriState.Unchanged ? bg.Backlog : mapping.Backlog == TriState.On,
                Wishlist = mapping.Wishlist == TriState.Unchanged ? bg.Wishlist : mapping.Wishlist == TriState.On,
                Played = bg.Played
            };

            switch (mapping.Played)
            {
                case PlayedTargetState.Unchanged: break;
                case PlayedTargetState.Clear: simulated.Played = null; break;
                default: simulated.Played = (PlayedStatus)Enum.Parse(typeof(PlayedStatus), mapping.Played.ToString()); break;
            }

            return simulated;
        }

        // ── Push: "Apply Status Mappings Now" — build candidates, then apply on confirmation ──

        private List<PendingMapping> BuildPushCandidates()
        {
            var candidates = new List<PendingMapping>();
            foreach (var bg in Settings.Settings.BackloggdGamesList)
            {
                var playniteGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.Id == bg.GameId);
                if (playniteGame == null) continue;

                var mapping = Settings.Settings.StatusMappings
                    .FirstOrDefault(m => m.CompletionStatusId == playniteGame.CompletionStatusId);
                if (mapping == null || StatusMappingResolver.IsNoOpMapping(mapping)) continue;
                if (StatusMappingResolver.ComputeOperations(bg, mapping).Count == 0) continue;

                candidates.Add(new PendingMapping { Game = bg, Mapping = mapping });
            }
            return candidates;
        }

        private async Task<List<Views.StatusMappingReportRow>> ApplyPushCandidatesAsync(List<PendingMapping> candidates, GlobalProgressActionArgs args)
        {
            var report = new List<Views.StatusMappingReportRow>();

            await autoStatusSyncLock.WaitAsync().ConfigureAwait(false);
            try
            {
                args.ProgressMaxValue = candidates.Count;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (args.CancelToken.IsCancellationRequested) break;
                    var bg = candidates[i].Game;
                    var mapping = candidates[i].Mapping;
                    args.CurrentProgressValue = i + 1;
                    args.Text = $"Applying mapping to {bg.BackloggdName} ({i + 1}/{candidates.Count})…";

                    string oldSummary = BackloggdStatusSettingsViewModel.BuildStatusSummary(bg);
                    await ApplyStatusMappingAsync(bg, mapping).ConfigureAwait(false);
                    string newSummary = BackloggdStatusSettingsViewModel.BuildStatusSummary(bg);

                    report.Add(new Views.StatusMappingReportRow
                    {
                        GameName  = bg.BackloggdName,
                        OldStatus = oldSummary,
                        NewStatus = newSummary,
                        Changed   = oldSummary != newSummary
                    });
                }
                logger.Info($"Apply Status Mappings complete — {report.Count} game(s) processed.");
            }
            finally { autoStatusSyncLock.Release(); }

            return report;
        }

        // ── Pull: "Pull from Backloggd Now" — build candidates, then apply on confirmation ──

        private class PullCandidate
        {
            public BackloggdGame OriginalBg;  // the object currently stored in BackloggdGamesList — needed by ReplaceGame to find it
            public BackloggdGame RefreshedBg; // freshly re-scraped state from backloggdAPI.RefreshStatus
            public Game PlayniteGame;
            public Guid TargetCompletionStatusId;
            public string OldCompletionStatusName;
            public string NewCompletionStatusName;
        }

        private List<PullCandidate> BuildPullCandidates(GlobalProgressActionArgs args)
        {
            var candidates = new List<PullCandidate>();
            var games = Settings.Settings.BackloggdGamesList.ToList();
            args.ProgressMaxValue = games.Count;

            for (int i = 0; i < games.Count; i++)
            {
                if (args.CancelToken.IsCancellationRequested) break;
                var bg = games[i];
                args.CurrentProgressValue = i + 1;
                args.Text = $"Checking {bg.BackloggdName} ({i + 1}/{games.Count})…";

                var playniteGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.Id == bg.GameId);
                if (playniteGame == null || string.IsNullOrEmpty(bg.BackloggdUrl)) continue;

                var refreshed = backloggdAPI.RefreshStatus(bg);
                if (refreshed == null) continue;

                var sourceStatus = StatusMappingResolver.ResolvePullSourceStatus(refreshed);
                if (sourceStatus == null) continue;

                var mapping = Settings.Settings.PullStatusMappings
                    .FirstOrDefault(m => m.BackloggdSourceStatus == sourceStatus.Value);
                if (mapping == null || mapping.TargetCompletionStatusId == null) continue;
                if (mapping.TargetCompletionStatusId.Value == playniteGame.CompletionStatusId) continue;

                string oldName = PlayniteApi.Database.CompletionStatuses
                    .FirstOrDefault(s => s.Id == playniteGame.CompletionStatusId)?.Name ?? "(none)";
                string newName = PlayniteApi.Database.CompletionStatuses
                    .FirstOrDefault(s => s.Id == mapping.TargetCompletionStatusId.Value)?.Name ?? "(unknown)";

                candidates.Add(new PullCandidate
                {
                    OriginalBg = bg,
                    RefreshedBg = refreshed,
                    PlayniteGame = playniteGame,
                    TargetCompletionStatusId = mapping.TargetCompletionStatusId.Value,
                    OldCompletionStatusName = oldName,
                    NewCompletionStatusName = newName
                });
            }
            return candidates;
        }

        private List<Views.StatusMappingReportRow> ApplyPullCandidates(List<PullCandidate> candidates)
        {
            var report = new List<Views.StatusMappingReportRow>();

            foreach (var candidate in candidates)
            {
                lock (pullSuppressLock) { pullSuppressedGameIds.Add(candidate.PlayniteGame.Id); }
                try
                {
                    candidate.PlayniteGame.CompletionStatusId = candidate.TargetCompletionStatusId;
                    PlayniteApi.Database.Games.Update(candidate.PlayniteGame);
                    ReplaceGame(candidate.OriginalBg, candidate.RefreshedBg);
                }
                finally
                {
                    lock (pullSuppressLock) { pullSuppressedGameIds.Remove(candidate.PlayniteGame.Id); }
                }

                report.Add(new Views.StatusMappingReportRow
                {
                    GameName  = candidate.RefreshedBg.BackloggdName,
                    OldStatus = candidate.OldCompletionStatusName,
                    NewStatus = candidate.NewCompletionStatusName,
                    Changed   = true
                });
            }

            logger.Info($"Pull From Backloggd complete — {report.Count} game(s) updated.");
            return report;
        }

        private static string Check(bool active) => active ? "✓ " : "  ";
    }

    public static class PlayniteApiProvider
    {
        public static IPlayniteAPI Api { get; set; }
    }
}
