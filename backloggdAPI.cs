using AngleSharp;
using AngleSharp.Extensions;
using AngleSharp.Parser.Html;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Navigation;
using static BackloggdStatus.BackloggdGame;

namespace BackloggdStatus
{
    public class BackloggdAPI
    {
        private readonly IPlayniteAPI PlayniteApi = PlayniteApiProvider.Api;
        private static readonly ILogger logger = LogManager.GetLogger();
        private IWebView webView;

        public const string baseUrl = @"https://backloggd.com";
        private const bool verbose = true;

        private readonly Dictionary<string, string> statusMapper = new Dictionary<string, string>
        {
            { "wishlist-btn-container", "wishlist" },
            { "backlog-btn-container", "backlog" },
            { "playing-btn-container", "playing" },
            { "play-btn-container", "played" }
        };

        private readonly Dictionary<string, string> buttonMapper = new Dictionary<string, string>
        {
            { "Wishlist", "3" },
            { "Backlog", "2" },
            { "Playing", "1" },
            { "Played", "0" },
            { "Completed", "0" },
            { "Retired", "0" },
            { "Shelved", "0" },
            { "Abandoned", "0" },
            { "Unplayed", "0" }
        };

        public BackloggdAPI()
        {
            webView = PlayniteApi.WebViews.CreateOffscreenView();
        }

        /// <summary>
        /// Opens the Backloggd.com login page and stores login cookies.
        /// </summary>
        public void Login()
        {
            logger.Trace("Login method called");

            string loginUrl = $@"{baseUrl}/users/sign_in";

            Logout();
            webView.Navigate(loginUrl);
            logger.Info("Navigating to Backloggd Login");
            webView.OpenDialog();


            webView.LoadingChanged += async (s, e) =>
            {
                var url = webView.GetCurrentAddress();
                if (!url.EndsWith("sign_in"))
                {
                    var loggedIn = await Task.Run(() => IsUserLoggedIn());
                    if (loggedIn)
                    {
                        webView.Close();
                    }
                }
            };

            Logout();
            webView.Navigate(loginUrl);
            webView.OpenDialog();
        }

        /// <summary>
        /// Checks if user is logged in to Backloggd.com
        /// </summary>
        public bool IsUserLoggedIn(bool navigate = true)
        {
            if (verbose)
            {
                logger.Trace("Public IsUserLoggedIn method called");
            }

            logger.Debug("Public IsUserLoggedIn");

            if (navigate)
            {
                webView.NavigateAndWait(baseUrl);
            }

            // TODO: test this method
            // Check if #mobile-user-nav') exists, return false if it does.
            var parser = new HtmlParser();
            var document = parser.Parse(webView.GetPageSource());
            var userNav = document.QuerySelector("#mobile-user-nav");

            if (userNav == null)
            {
                BackloggdStatus.loggedIn = false;
                return false;
            }
            else
            {
                BackloggdStatus.loggedIn = true;
                return true;
            }

        }

        /// <summary>
        /// Deletes all cookies from Backloggd.com
        /// Logs out of Backloggd.com
        /// </summary>
        private void DeleteCookies()
        {
            if (verbose)
            {
                logger.Trace("DeleteCookies method called");
            }

            logger.Info("Deleting Cookies");
            webView.DeleteDomainCookies(".backloggd.com");
            webView.DeleteDomainCookies("www.backloggd.com");
        }

        /// <summary>
        /// Logs out of Backloggd.com by deleting cookies.
        /// </summary>
        public void Logout()
        {
            DeleteCookies();
            //IsUserLoggedIn();
        }


        public BackloggdGame RefreshStatus(BackloggdGame game)
        {
            if (verbose)
            {
                logger.Trace("RefreshStatus method called");
            }

            if (game == null)
            {
                logger.Error("Game is null in RefreshStatus method.");
                return null;
            }

            string backloggdURL = game.BackloggdUrl;
            Guid gameID = game.GameId;

            return GetGameFromURL(backloggdURL, gameID);
        }

        public BackloggdGame GetGameFromURL(string backloggdURL, Guid gameID)
        {
            if (verbose)
            {
                logger.Trace("GetGameFromURL method called");
            }

            if (string.IsNullOrEmpty(backloggdURL))
            {
                logger.Error("Game URL is null or empty in GetGameFromURL method.");
                return null;
            }

            logger.Debug($"Opening WebView to: {backloggdURL}");
            webView.NavigateAndWait(backloggdURL);

            string pagesource = webView.GetPageSource();
            var parser = new HtmlParser();
            var document = parser.Parse(pagesource);


            var gameNameElement = document.QuerySelector("#title > div.col-12.px-1 > div > div > h1");
            if (gameNameElement == null)
            {
                logger.Error("Game name not found");
                return null;
            }

            var playingBool = false;
            var backlogBool = false;
            var wishlistBool = false;
            BackloggdGame.PlayedStatus? playedStatus = null;


            var statusElements = document.QuerySelectorAll("#buttons > .btn-play-fill");
            var statusList = statusElements.Select(el => el.ClassName).ToList();
            //statusList = statusList.Select(SetStatusString).ToList();

            var playedStatusElement = document.GetElementsByClassName("button-link btn-play mx-auto")[0];
            var playStatus = playedStatusElement.GetAttribute("play_type");

            // Played Status is always first in the list.
            if (playStatus != null)
            {
                statusList[0] = playStatus;
            }

            foreach (var status in statusList)
            {
                switch(status)
                {
                    case "wishlist-btn-container":
                        wishlistBool = true;
                        break;
                    case "backlog-btn-container":
                        backlogBool = true;
                        break;
                    case "playing-btn-container":
                        playingBool = true;
                        break;
                    case "played":
                        playedStatus = BackloggdGame.PlayedStatus.Played;
                        break;
                    case "completed":
                        playedStatus = BackloggdGame.PlayedStatus.Completed;
                        break;
                    case "retired":
                        playedStatus = BackloggdGame.PlayedStatus.Retired;
                        break;
                    case "shelved":
                        playedStatus = BackloggdGame.PlayedStatus.Shelved;
                        break;
                    case "abandoned":
                        playedStatus = BackloggdGame.PlayedStatus.Abandoned;
                        break;
                }
            }


            

            BackloggdGame game = new BackloggdGame
            {
                GameId = gameID,
                BackloggdName = gameNameElement.TextContent.Trim(),
                BackloggdUrl = backloggdURL,
                Playing = playingBool,
                Backlog = backlogBool,
                Wishlist = wishlistBool,
                Played = playedStatus
            };

            return game;
        }


        /// <summary>
        /// Opens a WebView to given url.
        /// If no url is given, opens to Backloggd.com
        /// </summary>
        public void OpenWebView(string url = baseUrl)
        {
            // TODO: Check if necessary
            if (verbose)
            {
                logger.Trace($"OpenWebView to {url} method called");
            }

            webView.Navigate(url);
            webView.OpenDialog();

            logger.Info("Opening webView");
            IsUserLoggedIn();

        }

        

        /// <summary>
        /// Generates the JavaScript needed to toggle the game's status based on the status parameter.
        /// </summary>
        private string GenerateStatusToggleScript(string status)
        {
            if (buttonMapper.TryGetValue(status, out string index) && index != "0")
            {
                return $"document.getElementsByClassName('button-link btn-play mx-auto')[{index}].click();";
            }

            return $@"
                // First click on the play button
                document.getElementsByClassName('button-link btn-play mx-auto')[0].click();

                // Wait for the page to update, then proceed with the next actions
                const waitForElement = (selector, callback) => {{
                    const interval = setInterval(() => {{
                        if (document.querySelector(selector)) {{
                            clearInterval(interval);
                            callback();
                        }}
                    }}, 500);
                }};

                waitForElement('#{status}', () => {{
                    document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                    document.querySelector('#{status}').click();
                }});
            ";
        }


        public async Task ToggleStatusAsync(string gameURL, string status)
        {
            if (verbose)
            {
                logger.Trace("ToggleStatusAsync method called");
            }

            // Generate the script based on the status
            string script = GenerateStatusToggleScript(status);

            try
            {
                webView.NavigateAndWait(gameURL);
                await ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to toggle status {status} for URL {gameURL}: {ex.Message}");
            }
        }

        private async Task ExecuteScriptAsync(string script)
        {
            if (verbose)
            {
                logger.Trace("ExecuteScriptAsync method called");
            }

            bool eventHandled = false;
            var navigationCompleted = new TaskCompletionSource<bool>();

            webView.LoadingChanged += async (s, e) =>
            {
                if (!e.IsLoading && (!navigationCompleted.Task.IsCompleted && !eventHandled))
                {
                    eventHandled = true;
                    logger.Debug("In ExecuteScriptAsync LoadingChanged event handler");

                    try
                    {
                        var result = await webView.EvaluateScriptAsync(script);
                        logger.Debug($"Executed script: {script} at: {webView.GetCurrentAddress()} with result: {result}");
                        navigationCompleted.SetResult(true);
                    }
                    catch (Exception exception)
                    {
                        logger.Error($"Error in ExecuteScriptAsync: {exception.Message}");
                        navigationCompleted.SetResult(false);
                    }
                }
            };

            // Wait for the script execution to complete
            await navigationCompleted.Task.ConfigureAwait(false);
        }



        public string SetBackloggdUrl(string name = null)
        {
            logger.Trace("In SetBackloggdUrlAsync");
            string searchUrl = name != null
                ? $"{baseUrl}/search/games/{name.Replace(" ", "%20")}"
                : baseUrl;

            //string url = BackloggdStatus.DefaultURL;
            //var navigationCompleted = new TaskCompletionSource<string>();

            //webView.LoadingChanged += (s, e) =>
            //{
            //    if (!e.IsLoading)
            //    {
            //        var currentAddress = webView.GetCurrentAddress();
            //        if (!string.IsNullOrEmpty(currentAddress) && currentAddress.Contains("backloggd.com/games"))
            //        {
            //            navigationCompleted.SetResult(currentAddress);
            //            webView.Close();
            //        }
            //    }
            //};

            webView.Navigate(searchUrl);
            webView.OpenDialog();

            var currentAddress = webView.GetCurrentAddress();
            logger.Trace($"currentAddress is: {currentAddress}");

            if (string.IsNullOrEmpty(currentAddress) || !currentAddress.Contains($"{baseUrl}/games"))
            {
                //navigationCompleted.SetResult(currentAddress);
                //webView.Close();
                currentAddress = BackloggdStatus.DefaultURL;
            }
            logger.Trace($"SetBackloggdUrlAsync returning {currentAddress}");
            return currentAddress;
        }

    }
}
