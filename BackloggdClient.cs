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

namespace BackloggdStatus
{
    public class BackloggdClient
    {
        private readonly IPlayniteAPI playniteApi = PlayniteApiProvider.api;
        private readonly ILogger logger = LogManager.GetLogger();


        private bool loggedIn { get; set; }
        private const bool verbose = true;

        private const int width = 880;
        private const int height = 530;

        private const string HomeUrl = "https://www.backloggd.com";


        /// <summary>
        /// Deletes all cookies from Backloggd.com
        /// </summary>
        public void DeleteCookies()
        {
            if (verbose)
            {
                logger.Trace("DeleteCookies method called");
            }

            using (var webView = playniteApi.WebViews.CreateOffscreenView())
            {
                logger.Info("Deleting Cookies");
                webView.DeleteDomainCookies(".backloggd.com");
                webView.DeleteDomainCookies("www.backloggd.com");
            }
        }


        public List<string> GetGameStatus(string gameUrl)
        {
            //TODO: Find current status
            List<string> statusList;

            if (verbose)
            {
                logger.Trace("Public GetGameStatus method called");
            }

            logger.Debug("Public GetGameStatus");

            using (var webView = playniteApi.WebViews.CreateOffscreenView())
            {
                logger.Debug($"Opening WebView to: {gameUrl}");
                statusList = GetGameStatus(webView, gameUrl).GetAwaiter().GetResult();
            }


            statusList = statusList.Select(SetStatusString).ToList();


            logger.Debug($"StatusList has count: {statusList.Count} set to: {statusList} : {statusList.GetType()}");

            return statusList;
        }

        private async Task<List<string>> GetGameStatus(IWebView webView, string url)
        {
            if (verbose)
            {
                logger.Trace("Private GetGameStatus method called");
            }

            logger.Debug("Private GetGameStatus");

            bool eventHandled = false;
            var navigationCompleted = new TaskCompletionSource<bool>();

            List<string> statusList = new List<string>();
            JavaScriptEvaluationResult result = null;

            webView.Navigate(url);


            webView.LoadingChanged += async (s, e) =>
            {
                if (!e.IsLoading && !navigationCompleted.Task.IsCompleted)
                {
                    eventHandled = true;

                    string script = @"
                        JSON.stringify(Array.from(document.querySelectorAll('#buttons > .btn-play-fill')).map(el => el.className));
                    ";

                    logger.Debug($"Executing Script at: {webView.GetCurrentAddress()}");

                    try
                    {
                        result = await webView.EvaluateScriptAsync(script);


                        if (result != null && result.Result != null)
                        {
                            statusList = Serialization.FromJson<List<string>>(result.Result.ToString());
                        }

                        navigationCompleted.SetResult(true);

                    }
                    catch (Exception exception)
                    {
                        logger.Error($"Error in GetGameStatus: { exception.Message}");

                        navigationCompleted.SetResult(false);
                    }
                }
            };

            await navigationCompleted.Task.ConfigureAwait(continueOnCapturedContext: false);



            if (verbose)
            {
                logger.Trace("GetGameStatus method finished");
            }


            return statusList;
        }

        private string SetStatusString(string status)
        {
            if (verbose)
            {
                logger.Trace($"SetStatusString method called with {status}");
            }

            logger.Debug("SetStatusString");

            Dictionary<string, string> statusMapper = new Dictionary<string, string>
            {
                { "wishlist-btn-container", "Status: Wishlist" },
                { "backlog-btn-container", "Status: Backlog" },
                { "playing-btn-container", "Status: Playing" },
                { "play-btn-container", "Status: Played" } // TODO: Find What type of 'played' status this is.
            };

            foreach (var key in statusMapper.Keys)
            {
                if (status.Contains(key))
                {
                    return statusMapper[key];
                }
            }

            return "Status: Unknown";
        }


        /// <summary>
        /// Opens a WebView to given url.
        /// </summary>
        public void OpenWebView(string url = HomeUrl)
        {
            if (verbose)
            {
                logger.Trace($"OpenWebView to {url} method called");
            }

            using (var webView = playniteApi.WebViews.CreateView(width, height))
            {
                webView.Navigate(url);
                webView.OpenDialog();
            }

            logger.Info("Opening webView");
            CheckLogin();

        }

        /// <summary>
        /// Opens the Backloggd.com login page and stores login cookies.
        /// </summary>
        public void Login()
        {
            if (verbose)
            {
                logger.Trace("Login method called");
            }

            using (var webView = playniteApi.WebViews.CreateView(width, height))
            {
                webView.Navigate("https://www.backloggd.com/users/sign_in");
                AcceptCookies(webView);
                webView.OpenDialog();

                logger.Info("Navigating to Backloggd Login");
            }

            CheckLogin();

        }

        /// <summary>
        /// Logs out of Backloggd.com by deleting cookies.
        /// </summary>
        public void Logout()
        {
            DeleteCookies();
            CheckLogin();
        }

        /// <summary>
        /// Clicks Accept Cookies on Backloggd.com
        /// </summary>
        /// <param name="webView">webView open to Backloggd.com</param>
        private void AcceptCookies(IWebView webView)
        {
            if (verbose)
            {
                logger.Trace("AcceptCookies method called");
            }

            bool eventHandled = false;

            webView.LoadingChanged += async (s, e) =>
            {
                if (!e.IsLoading && !eventHandled)
                {
                    eventHandled = true;

                    string script = @"
                        var acceptButton = document.querySelector('#cookie-banner-accept');
                        acceptButton.click();
                    ";

                    try
                    {
                        await webView.EvaluateScriptAsync(script);
                        logger.Debug("Cookies Accepted");
                    }
                    catch (Exception exception)
                    {
                        logger.Error(exception.Message);
                    }
                }
            };
        }

        /// <summary>
        /// Checks if user is logged in to Backloggd.com
        /// </summary>
        public void CheckLogin()
        {
            if (verbose)
            {
                logger.Trace("Public CheckLogin method called");
            }

            logger.Debug("Public CheckLogin");

            using (var webView = playniteApi.WebViews.CreateOffscreenView())
            {
                CheckLogin(webView).GetAwaiter().GetResult();
            }

            // Refresh Main Menu
            // playniteApi.Database.BeginBufferUpdate();
            // playniteApi.Database.EndBufferUpdate();

        }

        private async Task CheckLogin(IWebView webView)
        {
            // TODO: This is broken. Fix it.
            if (verbose)
            {
                logger.Trace("Private CheckLogin method called");
            }
        
            logger.Debug("Private CheckLogin");
            webView.Navigate("https://www.backloggd.com");
        
        
            var navigationCompleted = new TaskCompletionSource<bool>();
            bool eventHandled = false;
        
            webView.LoadingChanged += async (s, e) =>
            {
                if (!e.IsLoading && !eventHandled)
                {
                    eventHandled = true;
                    // This Menu is the sign-out box which only appears when user is logged in.
                    string script = "document.querySelector('#mobile-user-nav > div:nth-child(3) > a');";
                    logger.Debug("In webView.LoadingChanged - CheckLogin method");

                    try
                    {
                        var result = await webView.EvaluateScriptAsync(script);
                        logger.Debug("Script Run");

                        if (result.Result != null)
                        {
                            logger.Info("Logged In");
                            loggedIn = true;
                        }
                        else
                        {
                            logger.Info("Logged Out");
                            loggedIn = false;
                            
                        }
                        navigationCompleted.SetResult(true);

                        if (verbose)
                        {
                            logger.Trace($"loggedIn set to {loggedIn}");
                        }
                    }
                    catch (Exception exception)
                    {
                        logger.Error(exception.Message);
                        navigationCompleted.SetResult(false);
                    }
                }
            };


        
            await navigationCompleted.Task.ConfigureAwait(continueOnCapturedContext: false);
        
            if (verbose)
            {
                logger.Trace("CheckLogin method finished");
            }
        }

        public string SetBackloggdUrl()
        {
            string URL = BackloggdStatus.DefaultURL;

            using (var webView = PlayniteApiProvider.api.WebViews.CreateView(width, height))
            {

                // TODO: Make this faster.
                // TODO: Open directly to search result using game name.
                webView.LoadingChanged += (s, e) =>
                {
                    var currentAddress = webView.GetCurrentAddress();
                    if (!string.IsNullOrEmpty(currentAddress) && currentAddress.Contains("https://www.backloggd.com/games"))
                    {
                        URL = currentAddress;
                        webView.Close();
                    }
                };

                webView.Navigate(HomeUrl);

                webView.OpenDialog();

                if (webView.GetCurrentAddress().Contains("https://www.backloggd.com/games"))
                {
                    URL = webView.GetCurrentAddress();

                    // TODO: I don't think this is necessary? But I had it in before. Plz Verify
                    // webView.Close();
                }
            }

            return URL;
        }

    }
}
