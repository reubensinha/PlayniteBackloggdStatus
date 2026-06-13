using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BackloggdStatus
{
    public enum PlayedStatus
    {
        Played,
        Completed,
        Retired,
        Shelved,
        Abandoned
    }

    public class BackloggdStatusSettings : ObservableObject
    {
        public List<BackloggdGame> BackloggdGamesList { get; set; } = new List<BackloggdGame>();
        public bool SyncOnStartup { get; set; } = false;
        public bool IsDebugMode   { get; set; } = false;
    }

    public class BackloggdStatusSettingsViewModel : ObservableObject, ISettings
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly BackloggdStatus plugin;
        private readonly IPlayniteAPI api;

        private BackloggdStatusSettings editingClone;

        private BackloggdStatusSettings settings;
        public BackloggdStatusSettings Settings
        {
            get => settings;
            set => SetValue(ref settings, value);
        }

        // ── Runtime-only display state ──────────────────────────────────────

        [DontSerialize]
        private string _username = "Not signed in";
        public string Username
        {
            get => _username;
            private set => SetValue(ref _username, value);
        }

        public void UpdateUsername(string name) => Username = string.IsNullOrEmpty(name) ? "Not signed in" : name;

        // ── Action delegates wired by GetSettingsView() ─────────────────────

        [DontSerialize] public Action         OnSignInRequested  { get; set; }
        [DontSerialize] public Action         OnSignOutRequested { get; set; }
        [DontSerialize] public Action<Guid>   OnUnlinkRequested  { get; set; }
        [DontSerialize] public Action         OnSyncAllRequested { get; set; }
        [DontSerialize] public Action         OnOpenLogRequested { get; set; }
        [DontSerialize] public string         LogFilePath        { get; set; }

        // ── Mapped games display list (bound by settings DataGrid) ──────────

        [DontSerialize]
        public ObservableCollection<MappedGameRow> MappedGames { get; }
            = new ObservableCollection<MappedGameRow>();

        public void RefreshMappedGames(IPlayniteAPI playniteApi)
        {
            MappedGames.Clear();
            foreach (var bg in Settings.BackloggdGamesList)
            {
                var game = playniteApi.Database.Games.FirstOrDefault(g => g.Id == bg.GameId);
                MappedGames.Add(new MappedGameRow
                {
                    GameId            = bg.GameId,
                    PlayniteName      = game?.Name ?? "(Unknown)",
                    BackloggdName     = bg.BackloggdName,
                    StatusSummary     = BuildStatusSummary(bg),
                    LastSyncedDisplay = bg.LastSynced.HasValue
                        ? bg.LastSynced.Value.ToString("yyyy-MM-dd HH:mm")
                        : "Never"
                });
            }
        }

        private static string BuildStatusSummary(BackloggdGame bg)
        {
            var parts = new List<string>();
            if (bg.Playing) parts.Add("Playing");
            if (bg.Backlog)  parts.Add("Backlog");
            if (bg.Wishlist) parts.Add("Wishlist");
            if (bg.Played.HasValue) parts.Add(bg.Played.Value.ToString());
            return parts.Count > 0 ? string.Join(", ", parts) : "None";
        }

        // ── Constructor ─────────────────────────────────────────────────────

        public BackloggdStatusSettingsViewModel(BackloggdStatus plugin, IPlayniteAPI api)
        {
            this.plugin = plugin;
            this.api    = api;

            var savedSettings = plugin.LoadPluginSettings<BackloggdStatusSettings>();
            if (savedSettings != null)
            {
                Settings = savedSettings;
                logger.Debug("Settings loaded from disk.");
            }
            else
            {
                Settings = new BackloggdStatusSettings();
                logger.Debug("No saved settings found — using defaults.");
            }

            if (Settings.BackloggdGamesList == null)
                Settings.BackloggdGamesList = new List<BackloggdGame>();
        }

        // ── ISettings ───────────────────────────────────────────────────────

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
            plugin.Settings = this;
            OnPropertyChanged();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }

    // ── BackloggdGame ────────────────────────────────────────────────────────

    public class BackloggdGame : ObservableObject
    {
        [DontSerialize]
        private static readonly ILogger logger = LogManager.GetLogger();

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
            set => SetValue(ref backloggdName, value);
        }

        private string backloggdUrl;
        public string BackloggdUrl
        {
            get => backloggdUrl;
            set => SetValue(ref backloggdUrl, value);
        }

        private bool playing;
        public bool Playing
        {
            get => playing;
            set => SetValue(ref playing, value);
        }

        private bool backlog;
        public bool Backlog
        {
            get => backlog;
            set => SetValue(ref backlog, value);
        }

        private bool wishlist;
        public bool Wishlist
        {
            get => wishlist;
            set => SetValue(ref wishlist, value);
        }

        private PlayedStatus? played;
        public PlayedStatus? Played
        {
            get => played;
            set => SetValue(ref played, value);
        }

        private DateTime? lastSynced;
        public DateTime? LastSynced
        {
            get => lastSynced;
            set => SetValue(ref lastSynced, value);
        }
    }

    // ── MappedGameRow — display-only wrapper for the settings DataGrid ───────

    public class MappedGameRow
    {
        public Guid   GameId            { get; set; }
        public string PlayniteName      { get; set; }
        public string BackloggdName     { get; set; }
        public string StatusSummary     { get; set; }
        public string LastSyncedDisplay { get; set; }
    }

    // ── BackloggdSearchResult — short-lived search result model ─────────────

    public class BackloggdSearchResult
    {
        public string Title        { get; set; }
        public string Url          { get; set; }
        public string ThumbnailUrl { get; set; }
        public string Year         { get; set; }
    }
}
