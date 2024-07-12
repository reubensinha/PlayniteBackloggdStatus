using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace BackloggdStatus
{
    public class BackloggdClient
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly ILogger logger;


        private bool loggedIn { get; set; }
        private const bool verbose = true;

        private const int width = 880;
        private const int height = 530;

        private const string HomeUrl = "https://www.backloggd.com";

        /// <summary>
        /// Client for interacting with Backloggd.com webpage.
        /// </summary>
        /// <param name="api">Playnite API object</param>
        /// <param name="logger">Playnite logger object</param>
        public BackloggdClient(IPlayniteAPI api, ILogger logger)
        {
            playniteApi = api;
            this.logger = logger;
        }

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


        public string GetGameStatus(string game)
        {
            //TODO: Find current status
            return "Current Status";
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

    }
}
