using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Security.Policy;
using AngleSharp.Parser.Html;
using AngleSharp;
using AngleSharp.Extensions;
using System.Windows.Navigation;

namespace BackloggdStatus
{
    public class BackloggdClient
    {
        private readonly IPlayniteAPI PlayniteApi = PlayniteApiProvider.Api;
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IWebView webView;

        public const string baseUrl = @"https://backloggd.com";

        private string loginUrl = $@"{baseUrl}/users/sign_in";


        
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

        public BackloggdClient(IWebView webView)
        {
            this.webView = webView;
        }

        /// <summary>
        /// Opens the Backloggd.com login page and stores login cookies.
        /// </summary>
        public void Login()
        {
            logger.Trace("Login method called");

            Logout();
            webView.Navigate(loginUrl);
            logger.Info("Navigating to Backloggd Login");
            webView.OpenDialog();

            //webView.LoadingChanged += (s, e) =>
            //{
            //    if (webView.GetCurrentAddress() == baseUrl)
            //    {
            //        webView.Close();
            //    }
            //};
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
        /// Takes a url string of a game on Backloggd.com
        /// returns a list of all the statuses the user has set for that game.
        /// </summary>
        public List<string> GetGameStatus(string gameUrl)
        {
            List<string> statusList;
            string playStatus;

            logger.Debug("Public GetGameStatus");

            logger.Debug($"Opening WebView to: {gameUrl}");

            webView.NavigateAndWait(gameUrl);
            string pagesource = webView.GetPageSource();

            var parser = new HtmlParser();
            var document = parser.Parse(pagesource);

            var statusElements = document.QuerySelectorAll("#buttons > .btn-play-fill");
            statusList = statusElements.Select(el => el.ClassName).ToList();


            var playedStatusElement = document.GetElementsByClassName("button-link btn-play mx-auto")[0];
            playStatus = playedStatusElement.GetAttribute("play_type");

            statusList = statusList.Select(SetStatusString).ToList();

            // Played Status is always first in the list.
            if (playStatus != null)
            {
                statusList[0] = playStatus;
            }

            return statusList;
        }

        private string SetStatusString(string status)
        {
            logger.Debug("SetStatusString");

            foreach (var key in statusMapper.Keys)
            {
                if (status.Contains(key))
                {
                    return statusMapper[key];
                }
            }

            return "Unknown";
        }


        public string GetBackloggdName(string gameUrl)
        {
            string gameName;

            logger.Debug("Public GetBackloggdName");

            webView.NavigateAndWait(gameUrl);
            string pagesource = webView.GetPageSource();

            var parser = new HtmlParser();
            var document = parser.Parse(pagesource);
            
            var gameElement = document.QuerySelector("#title > div.col-12.pr-0 > div > div > h1");
            if (gameElement == null)
            {
                throw new Exception("GameName not found");
            }

            gameName = gameElement.TextContent;

            logger.Debug($"GameName set to {gameName}");

            return gameName;
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
        /// Logs out of Backloggd.com by deleting cookies.
        /// </summary>
        public void Logout()
        {
            DeleteCookies();
            //IsUserLoggedIn();
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
