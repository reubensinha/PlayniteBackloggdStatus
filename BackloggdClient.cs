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
        private IPlayniteAPI playniteApi;
        private ILogger logger;

        // private bool debug = false;

        internal bool loggedIn { get; private set; } = false;

        public BackloggdClient(IPlayniteAPI api, ILogger logger)
        {
            this.playniteApi = api;
            this.logger = logger;
        }


        public void DeleteCookies()
        {
            using (var webView = playniteApi.WebViews.CreateOffscreenView())
            {
                webView.DeleteDomainCookies(".backloggd.com");
                webView.DeleteDomainCookies("www.backloggd.com");
            }
        }

        public void Login()
        {
            var webView = playniteApi.WebViews.CreateView(880, 530);
            webView.Navigate("https://www.backloggd.com/users/sign_in");
            AcceptCookies(webView);
            webView.OpenDialog();

            logger.Info("Navigating to Backloggd Login");
            CheckLogin(webView);

            webView.Close();
            webView.Dispose();
        }

        private void AcceptCookies(IWebView webView)
        {
            webView.LoadingChanged += async (s, e) =>
            {
                if (!e.IsLoading)
                {
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

        private void CheckLogin(IWebView webView)
        {
            // TODO Check if user is logged in to Backloggd.
            webView.Navigate("https://www.backloggd.com");
            logger.Debug("In Check Log in method");
            webView.LoadingChanged += async (s, e) =>
            {
                if (!e.IsLoading)
                {
                    // This HTML Menu is the sign-out box which only appears when user is logged in.
                    string script = "document.querySelector('#mobile-user-nav > div:nth-child(3) > a');";

                    try
                    {
                        var result = await webView.EvaluateScriptAsync(script);
                        if (result != null)
                        {
                            logger.Info("Logged In");
                            loggedIn = true;
                        }
                        else
                        {
                            logger.Info("Logged Out");
                            loggedIn = false;
                        }
                    }
                    catch (Exception exception)
                    {
                        logger.Error(exception.Message);
                    }
                }
            };
        }
    }
}
