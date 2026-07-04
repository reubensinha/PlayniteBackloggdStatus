#if DEBUG
using AngleSharp.Parser.Html;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace BackloggdStatus
{
    public class TestResult
    {
        public string       Name       { get; set; }
        public bool         Passed     { get; set; }
        public string       Message    { get; set; }
        public string       StackTrace { get; set; }
        public long         ElapsedMs  { get; set; }
        public List<string> Details    { get; set; } = new List<string>();

        public string Display =>
            $"{(Passed ? "✓" : "✗")} {Name}  ({ElapsedMs}ms)" +
            (Passed ? "" : $"\n    {Message}");
    }

    public class BackloggdTestRunner
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly BackloggdAPI api;
        private readonly string dataPath;

        private const string TestGameUrl     = "https://backloggd.com/games/left-4-dead-2/";
        private const string TestSearchQuery = "left 4 dead 2";

        // Match backloggdAPI.cs WaitForElement defaults: 30 attempts × 500ms = 15000ms
        private const int WaitForElementMs     = 15_000;
        private const int WaitForElementWarnMs = 12_000;  // flag if within 3s of limit

        // Per-test diagnostic accumulator — reset by Run() before each test
        private List<string> _currentDetails = new List<string>();
        private void Log(string s) => _currentDetails.Add(s);

        public BackloggdTestRunner(BackloggdAPI api, string dataPath)
        {
            this.api      = api;
            this.dataPath = dataPath;
        }

        public List<TestResult> RunAll()
        {
            var results = new List<TestResult>();

            // ── Selector tests (read-only) ──────────────────────────────────
            results.Add(Run("Login: #mobile-user-nav selector finds user nav", () =>
            {
                string username = null;
                var origCallback = api.OnUsernameResolved;
                api.OnUsernameResolved = name => { username = name; origCallback?.Invoke(name); };
                bool ok = api.IsUserLoggedIn();
                api.OnUsernameResolved = origCallback;

                if (!ok) throw new Exception("Not logged in — #mobile-user-nav selector returned null or user is not signed in");
                Log($"Username resolved: '{username ?? "(none)"}'");
            }));

            results.Add(Run("Game page: title selector (.game-title-section h1)", () =>
            {
                var game = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                if (game == null)
                    throw new Exception("GetGameFromURL returned null — title selector likely broken");
                if (string.IsNullOrWhiteSpace(game.BackloggdName))
                    throw new Exception("Title empty — selector found element but TextContent is blank");
                Log($"Title found: '{game.BackloggdName}'");
                Log($"Current status — Playing:{game.Playing}, Backlog:{game.Backlog}, Wishlist:{game.Wishlist}, Played:{game.Played?.ToString() ?? "null"}");
            }));

            results.Add(Run("Game page: 4 desktop buttons found (#buttons > div.col.px-0.mt-auto > button)", () =>
            {
                api.GetGameFromURL(TestGameUrl, Guid.Empty);
                var doc  = new HtmlParser().Parse(GetCurrentPageSource());
                var btns = doc.QuerySelectorAll("#buttons > div.col.px-0.mt-auto > button");
                Log($"Button count: {btns.Length}");
                if (btns.Length != 4)
                    throw new Exception($"Expected 4 desktop buttons, found {btns.Length} — SelPlayButton may need updating");
            }));

            results.Add(Run("Game page: played button has play_type attribute", () =>
            {
                api.GetGameFromURL(TestGameUrl, Guid.Empty);
                var doc = new HtmlParser().Parse(GetCurrentPageSource());
                var btn = doc.QuerySelector("#buttons > div.col.px-0.mt-auto > button");
                if (btn == null)
                    throw new Exception("First desktop button not found");
                string playType = btn.GetAttribute("play_type");
                Log($"play_type value: '{playType ?? "(missing)"}'");
                if (!btn.HasAttribute("play_type"))
                    throw new Exception("play_type attribute missing — played sub-status reading will be broken");
            }));

            results.Add(Run("Search: Turbo Frame workaround loads results", () =>
            {
                var found = api.SearchGames(TestSearchQuery);
                Log($"Result count: {found.Count}");
                if (found.Count == 0)
                    throw new Exception("No results — Turbo Frame lazy-load workaround may be broken or #search-results selector changed");
                Log($"First result: '{found[0].Title}' ({found[0].Url})");
                if (found.Count > 1) Log($"Second result: '{found[1].Title}'");
            }));

            results.Add(Run("Link flow: GetGameFromURL succeeds immediately after SearchGames", () =>
            {
                // Regression test for issue #7: NavigateIfNeeded in GetGameFromURL could skip
                // navigation after SearchGames left the webView in an unexpected Turbo Drive URL
                // state, causing GetGameFromURL to parse stale DOM and return null.
                var found = api.SearchGames(TestSearchQuery);
                if (found.Count == 0)
                    throw new Exception("SearchGames returned no results — cannot exercise link flow");
                Log($"SearchGames returned {found.Count} results; first: '{found[0].Title}' ({found[0].Url})");
                var game = api.GetGameFromURL(found[0].Url, Guid.Empty);
                Log($"GetGameFromURL result: {(game == null ? "null" : $"'{game.BackloggdName}'")}");
                if (game == null)
                    throw new Exception("GetGameFromURL returned null immediately after SearchGames — navigation was likely skipped");
            }));

            // ── Timing tests ────────────────────────────────────────────────
            results.Add(Run("Timing: game page loads within WaitForElement window", () =>
            {
                var sw   = Stopwatch.StartNew();
                var game = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                sw.Stop();
                Log($"Elapsed: {sw.ElapsedMilliseconds}ms  (warn at {WaitForElementWarnMs}ms, limit {WaitForElementMs}ms)");
                if (game == null)
                    throw new Exception("GetGameFromURL returned null");
                if (sw.ElapsedMilliseconds > WaitForElementWarnMs)
                    throw new Exception($"Page load took {sw.ElapsedMilliseconds}ms — within 3s of the {WaitForElementMs}ms WaitForElement timeout");
            }));

            results.Add(Run("Timing: search results load within WaitForElement window", () =>
            {
                var sw    = Stopwatch.StartNew();
                var found = api.SearchGames(TestSearchQuery);
                sw.Stop();
                Log($"Elapsed: {sw.ElapsedMilliseconds}ms  (warn at {WaitForElementWarnMs}ms, limit {WaitForElementMs}ms)");
                Log($"Result count: {found.Count}");
                if (found.Count == 0)
                    throw new Exception("No results returned");
                if (sw.ElapsedMilliseconds > WaitForElementWarnMs)
                    throw new Exception($"Search took {sw.ElapsedMilliseconds}ms — within 3s of the {WaitForElementMs}ms WaitForElement timeout");
            }));

            // ── Operation tests (state-modifying; leave account clean) ──────
            results.Add(Run("Operation: Toggle Playing — set then unset", () =>
            {
                var before = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                if (before == null) throw new Exception("Initial GetGameFromURL failed");
                Log($"Initial state — Playing:{before.Playing}, Played:{before.Played?.ToString() ?? "null"}");
                before = EnsureCleanState(before);

                api.ToggleStatusAsync(TestGameUrl, "Playing").GetAwaiter().GetResult();
                LogDomState("after-set-playing");
                var afterSet = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                Log($"After set — Playing:{afterSet?.Playing}");
                if (afterSet?.Playing != true)
                    throw new Exception("Playing not active after toggle — button[1] click or selector broken");

                api.ToggleStatusAsync(TestGameUrl, "Playing").GetAwaiter().GetResult();
                LogDomState("after-unset-playing");
                var afterUnset = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                Log($"After unset — Playing:{afterUnset?.Playing}");
                if (afterUnset?.Playing == true)
                    throw new Exception("Playing still active after second toggle");
            }));

            results.Add(Run("Operation: Set Played (Completed) from unset — direct click, no modal", () =>
            {
                var before = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                if (before == null) throw new Exception("Initial GetGameFromURL failed");
                Log($"Initial state — Played:{before.Played?.ToString() ?? "null"}");
                before = EnsureCleanState(before);
                Log($"State after precondition cleanup — Played:{before?.Played?.ToString() ?? "null"}");

                api.ToggleStatusAsync(TestGameUrl, "completed", playedAlreadySet: false).GetAwaiter().GetResult();
                LogDomState("after-set-completed");
                var afterSet = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                Log($"After set — Played:{afterSet?.Played?.ToString() ?? "null"}");
                if (afterSet?.Played != PlayedStatus.Completed)
                    throw new Exception($"Expected Completed, got {afterSet?.Played?.ToString() ?? "null"} — direct-set path broken");

                api.ToggleStatusAsync(TestGameUrl, "unset-played-btn", playedAlreadySet: true).GetAwaiter().GetResult();
                LogDomState("after-unset-completed");
                var afterUnset = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                Log($"After unset — Played:{afterUnset?.Played?.ToString() ?? "null"}");
                if (afterUnset?.Played.HasValue == true)
                    throw new Exception("Played not cleared after unset — #unset-played-btn modal may be broken");
            }));

            results.Add(Run("Operation: Set Played sub-type (Retired) from unset — requires double-click workaround", () =>
            {
                var before = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                if (before == null) throw new Exception("Initial GetGameFromURL failed");
                Log($"Initial state — Played:{before.Played?.ToString() ?? "null"}");
                before = EnsureCleanState(before);
                Log($"State after precondition cleanup — Played:{before?.Played?.ToString() ?? "null"}");

                api.ToggleStatusAsync(TestGameUrl, "retired", playedAlreadySet: false).GetAwaiter().GetResult();
                LogDomState("after-set-retired");
                var afterSet = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                Log($"After set — Played:{afterSet?.Played?.ToString() ?? "null"}");
                if (afterSet?.Played != PlayedStatus.Retired)
                    throw new Exception($"Expected Retired, got {afterSet?.Played?.ToString() ?? "null"} — double-click workaround or modal JS broken");

                api.ToggleStatusAsync(TestGameUrl, "unset-played-btn", playedAlreadySet: true).GetAwaiter().GetResult();
                LogDomState("after-cleanup-retired");
                var afterUnset = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                Log($"After cleanup — Played:{afterUnset?.Played?.ToString() ?? "null"}");
                if (afterUnset?.Played.HasValue == true)
                    throw new Exception("Cleanup failed: Played status not cleared");
            }));

            results.Add(Run("Timing: played status modal appears within 8s JS timeout", () =>
            {
                // Set Completed first so button[0] opens the modal on next click (not sets directly from unset)
                var before = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                before = EnsureCleanState(before);
                api.ToggleStatusAsync(TestGameUrl, "completed", playedAlreadySet: false).GetAwaiter().GetResult();
                Log("Completed status set — button[0] will now open modal");

                var sw = Stopwatch.StartNew();
                api.ToggleStatusAsync(TestGameUrl, "unset-played-btn", playedAlreadySet: true).GetAwaiter().GetResult();
                sw.Stop();
                Log($"Modal interaction elapsed: {sw.ElapsedMilliseconds}ms  (limit: 6500ms before 8s JS timeout)");
                LogDomState("after-modal-unset");

                var final = api.GetGameFromURL(TestGameUrl, Guid.Empty);
                Log($"Final state — Played:{final?.Played?.ToString() ?? "null"}");
                if (final?.Played.HasValue == true)
                    throw new Exception($"Played not cleared after {sw.ElapsedMilliseconds}ms — modal JS likely timed out (8s limit)");
                if (sw.ElapsedMilliseconds > 6_500)
                    throw new Exception($"Modal interaction took {sw.ElapsedMilliseconds}ms — within 1.5s of the 8s JS timeout limit");
            }));

            WriteResultsLog(results);
            return results;
        }

        // If the test game has pre-existing state (real account data), clean it up
        // and log what was cleared so the test report explains the auto-correction.
        private BackloggdGame EnsureCleanState(BackloggdGame state)
        {
            bool dirty = false;
            if (state.Playing)
            {
                Log("[Precondition] Playing was already set — clearing before test");
                api.ToggleStatusAsync(TestGameUrl, "Playing").GetAwaiter().GetResult();
                LogDomState("precondition-after-unset-playing");
                dirty = true;
            }
            if (state.Played.HasValue)
            {
                Log($"[Precondition] Played was {state.Played} — clearing before test");
                api.ToggleStatusAsync(TestGameUrl, "unset-played-btn", playedAlreadySet: true).GetAwaiter().GetResult();
                LogDomState("precondition-after-unset-played");
                dirty = true;
            }
            if (!dirty) return state;
            var refetched = api.GetGameFromURL(TestGameUrl, Guid.Empty);
            LogDomState("precondition-after-refetch");
            return refetched;
        }

        // Reaches into BackloggdAPI's private webView field to get page source for button-count assertions.
        private string GetCurrentPageSource()
        {
            var field   = typeof(BackloggdAPI).GetField("webView", BindingFlags.NonPublic | BindingFlags.Instance);
            var webView = field?.GetValue(api) as Playnite.SDK.IWebView;
            return webView?.GetPageSource() ?? throw new Exception("Could not access BackloggdAPI.webView via reflection");
        }

        private string RunJs(string script)
        {
            var field = typeof(BackloggdAPI).GetField("webView", BindingFlags.NonPublic | BindingFlags.Instance);
            var wv    = field?.GetValue(api) as Playnite.SDK.IWebView;
            if (wv == null) return "(webView unavailable)";
            try
            {
                var r = wv.EvaluateScriptAsync(script).GetAwaiter().GetResult();
                return r?.Result?.ToString() ?? "(null)";
            }
            catch (Exception ex) { return $"(JS error: {ex.Message})"; }
        }

        private void LogDomState(string label)
        {
            const string btnSel = "#buttons > div.col.px-0.mt-auto > button";
            Log($"[DOM:{label}]");
            Log($"  url:           {RunJs("window.location.href")}");
            Log($"  play_type:     {RunJs($"document.querySelector('{btnSel}')?.getAttribute('play_type') ?? '(no button)'")}");
            Log($"  played-fill:   {RunJs("!!document.querySelector('.played-btn-container.btn-play-fill')")}");
            Log($"  playing-fill:  {RunJs("!!document.querySelector('.playing-btn-container.btn-play-fill')")}");
            Log($"  #unset-played: {RunJs("!!document.querySelector('#unset-played-btn')")}");
            Log($"  modal-any:     {RunJs("!!document.querySelector('#unset-played-btn,#retired,#completed,#shelved,#abandoned,#played')")}");
        }

        private TestResult Run(string name, Action test)
        {
            _currentDetails = new List<string>();
            logger.Info($"[Test] {name}");
            var sw = Stopwatch.StartNew();
            try
            {
                test();
                sw.Stop();
                logger.Info($"[Test] PASS ({sw.ElapsedMilliseconds}ms): {name}");
                return new TestResult { Name = name, Passed = true, ElapsedMs = sw.ElapsedMilliseconds, Details = _currentDetails };
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.Error($"[Test] FAIL ({sw.ElapsedMilliseconds}ms): {name} — {ex.Message}");
                try { LogDomState("failure-dump"); } catch { }
                return new TestResult { Name = name, Passed = false, Message = ex.Message, StackTrace = ex.ToString(), ElapsedMs = sw.ElapsedMilliseconds, Details = _currentDetails };
            }
        }

        private void WriteResultsLog(List<TestResult> results)
        {
            try
            {
                int passed = 0, failed = 0;
                var lines = new List<string>();

                lines.Add("══════════════════════════════════════════════════════");
                lines.Add("  BackloggdStatus Integration Test Run");
                lines.Add($"  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                lines.Add($"  Test game:    {TestGameUrl}");
                lines.Add($"  Search query: \"{TestSearchQuery}\"");
                lines.Add("══════════════════════════════════════════════════════");
                lines.Add("");

                foreach (var r in results)
                {
                    lines.Add($"{(r.Passed ? "✓ PASS" : "✗ FAIL")}  [{r.ElapsedMs}ms]  {r.Name}");
                    foreach (var d in r.Details)
                        lines.Add($"    {d}");
                    if (!r.Passed)
                    {
                        lines.Add($"  ERROR: {r.Message}");
                        if (!string.IsNullOrEmpty(r.StackTrace))
                        {
                            lines.Add("  Stack trace:");
                            foreach (var line in r.StackTrace.Split('\n'))
                                lines.Add($"    {line.TrimEnd()}");
                        }
                    }
                    lines.Add("");
                    if (r.Passed) passed++; else failed++;
                }

                lines.Add("──────────────────────────────────────────────────────");
                lines.Add($"  Total: {passed} passed, {failed} failed  (of {results.Count})");
                lines.Add("──────────────────────────────────────────────────────");

                File.WriteAllLines(Path.Combine(dataPath, "test_results.txt"), lines);
            }
            catch (Exception ex)
            {
                logger.Warn($"Could not write test results log: {ex.Message}");
            }
        }
    }
}
#endif
