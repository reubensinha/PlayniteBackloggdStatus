using AngleSharp.Parser.Html;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BackloggdStatus
{
    public class BackloggdAPI
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IWebView webView;

        public const string BaseUrl = "https://backloggd.com";

        // ── CSS selector constants — update here if Backloggd changes HTML ──
        //
        // ── Page: https://backloggd.com  (used by IsUserLoggedIn) ───────────
        // <nav id="mobile-user-nav"> — present only when logged in; used as the login check
        private const string SelUserNav     = "#mobile-user-nav";

        // The nav-link anchor itself — id="navbarDropdown" IS the <a>, not a parent
        // private const string SelUserNavLink = "#mobile-user-nav a";
        private const string SelUserNavLink = "#navbarDropdown";


        // ── Page: https://backloggd.com/games/<slug>  (used by GetGameFromURL / ToggleStatusAsync) ──
        // <h1> inside the game title section (both mobile and desktop share .game-title-section)
        private const string SelGameTitle = ".game-title-section h1";

        // WaitForElement sentinel in GetGameFromURL — signals that the game page has loaded.
        // Must be absent from search results pages: if it is present there, WaitForElement will
        // return true while the webView DOM still contains search HTML, causing GetPageSource to
        // read the wrong page (issue #7 root cause). Kept separate from SelGameTitle so the
        // sentinel can be changed independently and so BackloggdTestRunner can read and validate it.
        internal const string SelGamePageAwaiter = SelGameTitle;
        
        // Elements with class "btn-play-fill" — status buttons that are visually active.
        // Each element's className (or play_type attribute on the first one) identifies the
        // active status: playing-btn-container, backlog-btn-container, wishlist-btn-container,
        // played, completed, retired, shelved, abandoned.
        private const string SelStatusFill  = ".btn-play-fill";
        
        // CSS selector for querySelectorAll() — all four clickable status buttons in #buttons.
        // Index 0 = Play/Played, 1 = Playing, 2 = Backlog, 3 = Wishlist.
        // The first button carries a play_type attribute ("played", "completed", etc.)
        // when a finished status is active. Uses stable layout classes only (no active-state class).
        private const string SelPlayButton  = "#buttons > div.col.px-0.mt-auto > button";


        // ── Page: https://backloggd.com/search/games/<query>  (used by SearchGames) ──
        // Wrapper element for each search result card — selects ALL results.
        private const string SelSearchCard  = "#search-results > div > div";
        
        // Relative selectors — queried against each individual card element in the SearchGames loop.
        // <a> whose href is the game's slug path (e.g. /games/half-life)
        private const string SelSearchLink  = "div.col.my-auto > div > div.col.my-auto > div:nth-child(1) > div > a";
        
        // <img> — the cover art thumbnail; src is the image URL
        private const string SelSearchImage = "div.col-2.col-lg-1.my-auto.pr-0 > a > div > div > img";
        
        // <h3> containing the game title text
        private const string SelSearchTitle = "div.col.my-auto > div > div.col.my-auto > div:nth-child(1) > div > a > h3";
        
        // <span> inside the title <h3> containing the release year
        private const string SelSearchYear  = "div.col.my-auto > div > div.col.my-auto > div:nth-child(1) > div > a > h3 > span";

        // ── Button index map for quick-toggle statuses ───────────────────────
        private readonly Dictionary<string, string> buttonIndexMap = new Dictionary<string, string>
        {
            { "Wishlist", "3" },
            { "Backlog",  "2" },
            { "Playing",  "1" }
        };

        // ── Callback wired by the plugin to display username in settings ─────
        public Action<string> OnUsernameResolved { get; set; }

        public BackloggdAPI(IWebView webView)
        {
            this.webView = webView;
        }

        // ────────────────────────────────────────────────────────────────────
        // Auth
        // ────────────────────────────────────────────────────────────────────

        public void Login(IWebView view)
        {
            string loginUrl = $"{BaseUrl}/users/sign_in";

            EventHandler<WebViewLoadingChangedEventArgs> handler = null;
            handler = async (s, e) =>
            {
                var url = view.GetCurrentAddress();
                if (!string.IsNullOrEmpty(url) && !url.EndsWith("sign_in"))
                {
                    bool ok = await Task.Run(() => IsUserLoggedIn()).ConfigureAwait(false);
                    if (ok)
                    {
                        view.LoadingChanged -= handler;
                        view.Close();
                    }
                }
            };

            view.LoadingChanged += handler;
            DeleteCookies(view);
            view.Navigate(loginUrl);
            view.OpenDialog();
            view.LoadingChanged -= handler; // safety unsubscribe if closed manually
        }

        public bool IsUserLoggedIn(bool navigate = true)
        {
            if (navigate)
                webView.NavigateAndWait(BaseUrl);

            var parser   = new HtmlParser();
            var document = parser.Parse(webView.GetPageSource());
            var userNav  = document.QuerySelector(SelUserNav);

            if (userNav == null)
            {
                BackloggdStatus.loggedIn = false;
                return false;
            }

            BackloggdStatus.loggedIn = true;

            var usernameEl = document.QuerySelector(SelUserNavLink);
            string username = usernameEl?.TextContent.Trim();
            if (!string.IsNullOrEmpty(username))
                OnUsernameResolved?.Invoke(username);

            return true;
        }

        public void Logout()
        {
            DeleteCookies(webView);
            BackloggdStatus.loggedIn = false;
        }

        private void DeleteCookies(IWebView view)
        {
            view.DeleteDomainCookies(".backloggd.com");
            view.DeleteDomainCookies("www.backloggd.com");
        }

        // ────────────────────────────────────────────────────────────────────
        // Game status read
        // ────────────────────────────────────────────────────────────────────

        public BackloggdGame RefreshStatus(BackloggdGame game)
        {
            if (game == null)
            {
                logger.Error("RefreshStatus called with null game.");
                return null;
            }
            return GetGameFromURL(game.BackloggdUrl, game.GameId);
        }

        public BackloggdGame GetGameFromURL(string backloggdURL, Guid gameId)
        {
            if (string.IsNullOrEmpty(backloggdURL))
            {
                logger.Error("GetGameFromURL: URL is null or empty.");
                return null;
            }

            logger.Debug($"GetGameFromURL: navigating to {backloggdURL}");
            webView.NavigateAndWait(backloggdURL);

            // Wait for the game page to load. SelGamePageAwaiter must be absent from search
            // results pages — see its declaration for the full constraint (issue #7).
            bool elementReady = WaitForElement(SelGamePageAwaiter);
            logger.Debug($"GetGameFromURL: WaitForElement('{SelGamePageAwaiter}') = {elementReady}");
            if (!elementReady)
                logger.Warn("GetGameFromURL: game page did not load within WaitForElement timeout — page may not have loaded.");

            var parser     = new HtmlParser();
            var pageSource = webView.GetPageSource();
            logger.Debug($"GetGameFromURL: parsing page — actual URL: {webView.GetCurrentAddress()}, source length: {pageSource?.Length ?? 0}");
            var document = parser.Parse(pageSource);

            // Resilient title selector — falls back through Google-Translate wrapper elements
            var titleEl = document.QuerySelector(SelGameTitle)
                       ?? document.QuerySelector(SelGameTitle + " font")
                       ?? document.QuerySelector(SelGameTitle + " font font");
            if (titleEl == null)
            {
                logger.Error($"GetGameFromURL: title not found. Expected='{backloggdURL}', actual='{webView.GetCurrentAddress()}', source={pageSource?.Length ?? 0} bytes");
                logger.Error($"GetGameFromURL: page snippet: {pageSource?.Substring(0, Math.Min(400, pageSource?.Length ?? 0))}");
                return null;
            }
            string gameName = titleEl.TextContent.Trim();

            // ── Parse status button states ────────────────────────────────
            // SelStatusFill selects the container divs that have btn-play-fill (active state).
            // Each container div has multiple classes; ClassList.Contains() checks individual tokens.
            bool playingBool  = false;
            bool backlogBool  = false;
            bool wishlistBool = false;
            PlayedStatus? playedStatus = null;

            foreach (var el in document.QuerySelectorAll(SelStatusFill))
            {
                var cls = el.ClassList;
                if (cls.Contains("playing-btn-container"))
                    playingBool = true;
                else if (cls.Contains("backlog-btn-container"))
                    backlogBool = true;
                else if (cls.Contains("wishlist-btn-container"))
                    wishlistBool = true;
                else if (cls.Contains("played-btn-container"))
                {
                    // The play_type attribute on the child button identifies the finished sub-status
                    var playType = el.QuerySelector("button")?.GetAttribute("play_type");
                    switch (playType)
                    {
                        case "played":     playedStatus = PlayedStatus.Played;    break;
                        case "completed":  playedStatus = PlayedStatus.Completed; break;
                        case "retired":    playedStatus = PlayedStatus.Retired;   break;
                        case "shelved":    playedStatus = PlayedStatus.Shelved;   break;
                        case "abandoned":  playedStatus = PlayedStatus.Abandoned; break;
                    }
                }
            }

            return new BackloggdGame
            {
                GameId        = gameId,
                BackloggdName = gameName,
                BackloggdUrl  = backloggdURL,
                Playing       = playingBool,
                Backlog       = backlogBool,
                Wishlist      = wishlistBool,
                Played        = playedStatus
            };
        }

        // ────────────────────────────────────────────────────────────────────
        // Game status write
        // ────────────────────────────────────────────────────────────────────

        public async Task ToggleStatusAsync(string gameURL, string status, bool playedAlreadySet = false)
        {
            // Modal operations (played sub-types, unset) require a fresh page load — Backloggd's
            // modal JS doesn't reinitialize correctly after repeated Turbo Stream updates on the
            // same page. Quick toggles (Playing/Backlog/Wishlist) are stateless and safe to skip.
            bool isModalOp = !buttonIndexMap.ContainsKey(status);
            if (isModalOp)
                webView.NavigateAndWait(gameURL);
            else
                NavigateIfNeeded(gameURL);
            WaitForElement(".logging-btns");

            bool isPlayedSubType = !buttonIndexMap.ContainsKey(status) && status != "unset-played-btn";

            if (isPlayedSubType && !playedAlreadySet)
            {
                // From unset: button[0] sets Completed immediately with no modal.
                await ExecuteScriptAsync($"document.querySelectorAll('{SelPlayButton}')[0].click();")
                    .ConfigureAwait(false);

                if (status == "completed")
                {
                    Thread.Sleep(1000);
                    return;
                }

                // For any other sub-type: poll until the played-btn-container shows the
                // Completed state — only then has button[0]'s behaviour changed to "open modal".
                WaitForElement(".played-btn-container.btn-play-fill", maxAttempts: 10, delayMs: 200);
            }

            string script = GenerateStatusToggleScript(status);
            await ExecuteScriptAsync(script).ConfigureAwait(false);

            // Quick toggles need one Turbo Stream round-trip (~1s); modal ops need two (~1.5s).
            Thread.Sleep(isModalOp ? 1500 : 1000);
        }

        private string GenerateStatusToggleScript(string status)
        {
            // Quick toggles — Backlog, Playing, Wishlist — click by button index
            if (buttonIndexMap.TryGetValue(status, out string index))
            {
                return $"document.querySelectorAll('{SelPlayButton}')[{index}].click();";
            }

            // Played-type statuses (played/completed/retired/shelved/abandoned) and the special
            // "unset-played-btn" value all follow the same pattern: click button[0] to open the
            // "Set your played status" modal, then wait for and click the target element by id.
            // For sub-types the id is the status name (e.g. #retired).
            // For unset, the modal contains a dedicated #unset-played-btn ("Mark as unplayed").
            return $@"
                document.querySelectorAll('{SelPlayButton}')[0].click();
                const waitForEl = (sel, cb) => {{
                    const iv = setInterval(() => {{
                        if (document.querySelector(sel)) {{
                            clearInterval(iv);
                            cb();
                        }}
                    }}, 200);
                    setTimeout(() => clearInterval(iv), 8000);
                }};
                waitForEl('#{status}', () => {{
                    document.querySelector('#{status}').click();
                }});
            ";
        }

        private async Task ExecuteScriptAsync(string script)
        {
            try
            {
                var result = await webView.EvaluateScriptAsync(script).ConfigureAwait(false);
                if (result?.Success == false)
                    logger.Error($"Script JS error at {webView.GetCurrentAddress()}: {result.Message}");
                else
                    logger.Debug($"Script executed at {webView.GetCurrentAddress()}");
            }
            catch (Exception ex)
            {
                logger.Error($"ExecuteScriptAsync failed: {ex.Message}");
                throw;
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Game search
        // ────────────────────────────────────────────────────────────────────

        public List<BackloggdSearchResult> SearchGames(string query)
        {
            string searchUrl = $"{BaseUrl}/search/games/{Uri.EscapeDataString(query)}";
            logger.Debug($"SearchGames: navigating to {searchUrl}");
            webView.NavigateAndWait(searchUrl);
            logger.Debug($"SearchGames: landed on '{webView.GetCurrentAddress()}'");

            // #search-results is empty in the initial HTML — results are injected by a Turbo
            // Frame with loading="lazy", which may not fire in an offscreen view (no viewport).
            // Force it to load immediately.
            bool framePresent = false;
            try
            {
                var frameCheck = webView.EvaluateScriptAsync(
                    "!!document.querySelector('turbo-frame#pagination')"
                ).GetAwaiter().GetResult();
                framePresent = frameCheck?.Result is true;
            }
            catch (Exception ex) { logger.Warn($"SearchGames: turbo-frame check failed: {ex.Message}"); }
            logger.Debug($"SearchGames: turbo-frame#pagination present={framePresent}");

            webView.EvaluateScriptAsync(
                "var f = document.querySelector('turbo-frame#pagination');" +
                "if (f) {" +
                "  f.removeAttribute('loading');" +
                "  if (typeof f.reload === 'function') { f.reload(); }" +
                "  else { var s = f.getAttribute('src'); f.setAttribute('src',''); f.setAttribute('src',s); }" +
                "}"
            ).GetAwaiter().GetResult();

            bool resultsReady = WaitForElement("#search-results > *");
            logger.Debug($"SearchGames: WaitForElement('#search-results > *') = {resultsReady}, URL now='{webView.GetCurrentAddress()}'");
            if (!resultsReady)
                logger.Warn("SearchGames: #search-results still empty after wait — Turbo Frame may not have loaded.");

            var parser     = new HtmlParser();
            var pageSource = webView.GetPageSource();
            var document   = parser.Parse(pageSource);
            var results    = new List<BackloggdSearchResult>();

            foreach (var card in document.QuerySelectorAll(SelSearchCard).Take(10))
            {
                var link  = card.QuerySelector(SelSearchLink);
                if (link == null) continue;

                var img   = card.QuerySelector(SelSearchImage);
                var title = card.QuerySelector(SelSearchTitle);
                var year  = card.QuerySelector(SelSearchYear);

                results.Add(new BackloggdSearchResult
                {
                    Title        = title?.TextContent.Trim()
                                ?? link.GetAttribute("title")
                                ?? "Unknown",
                    Url          = BaseUrl + link.GetAttribute("href"),
                    ThumbnailUrl = img?.GetAttribute("src"),
                    Year         = year?.TextContent.Trim()
                });
            }

            logger.Debug($"SearchGames: {results.Count} results for \"{query}\".");
            if (results.Count == 0)
                logger.Warn($"SearchGames: zero results — page snippet: {pageSource?.Substring(0, Math.Min(300, pageSource?.Length ?? 0))}");
            return results;
        }

        // ────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────

        private void NavigateIfNeeded(string url)
        {
            var current = webView.GetCurrentAddress() ?? "";
            bool skip = current.TrimEnd('/').Equals(url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
            logger.Debug($"NavigateIfNeeded: current='{current}' → {(skip ? "skipping (already on page)" : $"navigating to '{url}'")}");
            if (!skip)
                webView.NavigateAndWait(url);
        }

        /// <summary>
        /// Polls the page until <paramref name="cssSelector"/> is present, or
        /// gives up after <paramref name="maxAttempts"/> * <paramref name="delayMs"/> ms.
        /// Returns true if the element was found.
        /// </summary>
        private bool WaitForElement(string cssSelector, int maxAttempts = 30, int delayMs = 500)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    var r = webView.EvaluateScriptAsync(
                        $"document.querySelector('{cssSelector}') !== null"
                    ).GetAwaiter().GetResult();

                    if (r?.Result is true)
                    {
                        logger.Debug($"WaitForElement('{cssSelector}'): found after {i + 1} poll(s) ({(i + 1) * delayMs}ms)");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn($"WaitForElement poll failed: {ex.Message}");
                }
                Thread.Sleep(delayMs);
            }
            logger.Warn($"WaitForElement('{cssSelector}'): timed out after {maxAttempts * delayMs}ms");
            return false;
        }

        public string RunConnectivityDiagnostics()
        {
            var sb = new System.Text.StringBuilder();

            // ── Homepage load ────────────────────────────────────────────────
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                webView.NavigateAndWait(BaseUrl);
                sw.Stop();
                var homeSource = webView.GetPageSource() ?? "";
                var homeDoc    = new AngleSharp.Parser.Html.HtmlParser().Parse(homeSource);
                var homeTitle  = homeDoc.QuerySelector("title")?.TextContent?.Trim() ?? "(no title)";
                sb.AppendLine($"Homepage load: {sw.ElapsedMilliseconds}ms");
                sb.AppendLine($"  Landed on:   {webView.GetCurrentAddress()}");
                sb.AppendLine($"  Source size: {homeSource.Length} bytes");
                sb.AppendLine($"  Page title:  {homeTitle}");

                bool loggedIn = IsUserLoggedIn(navigate: false);
                sb.AppendLine($"  Logged in:   {loggedIn}");
            }
            catch (Exception ex)
            {
                sw.Stop();
                sb.AppendLine($"Homepage load: FAILED after {sw.ElapsedMilliseconds}ms — {ex.Message}");
            }

            sb.AppendLine();

            // ── Game page load ───────────────────────────────────────────────
            const string testUrl = "https://backloggd.com/games/left-4-dead-2/";
            sw.Restart();
            try
            {
                webView.NavigateAndWait(testUrl);
                sw.Stop();
                var gameSource  = webView.GetPageSource() ?? "";
                var gameDoc     = new AngleSharp.Parser.Html.HtmlParser().Parse(gameSource);
                bool hasTitle   = gameDoc.QuerySelector(".game-title-section h1") != null;
                bool hasButtons = gameDoc.QuerySelector(".logging-btns") != null;
                var  pageTitle  = gameDoc.QuerySelector("title")?.TextContent?.Trim() ?? "(no title)";
                sb.AppendLine($"Game page load ({testUrl}):");
                sb.AppendLine($"  Load time:       {sw.ElapsedMilliseconds}ms");
                sb.AppendLine($"  Landed on:       {webView.GetCurrentAddress()}");
                sb.AppendLine($"  Source size:     {gameSource.Length} bytes");
                sb.AppendLine($"  Page title:      {pageTitle}");
                sb.AppendLine($"  Title selector:  {(hasTitle ? "FOUND" : "NOT FOUND")}");
                sb.AppendLine($"  Button selector: {(hasButtons ? "FOUND" : "NOT FOUND")}");
                if (!hasTitle || !hasButtons)
                    sb.AppendLine($"  Page snippet:    {gameSource.Substring(0, Math.Min(400, gameSource.Length))}");
            }
            catch (Exception ex)
            {
                sw.Stop();
                sb.AppendLine($"Game page load: FAILED after {sw.ElapsedMilliseconds}ms — {ex.Message}");
            }

            return sb.ToString();
        }
    }
}
