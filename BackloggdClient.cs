using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BackloggdStatus
{
    public class BackloggdClient
    {
        private readonly IPlayniteAPI playniteApi = PlayniteApiProvider.Api;
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IWebView webView;
        private const string loginUrl = @"https://www.backloggd.com/users/sign_in";
        private const string logoutUrl = @"https://www.backloggd.com/users/sign_out";


        public bool LoggedIn { get; private set; }
        private const bool verbose = true;

        private const int width = 880;
        private const int height = 530;

        private const string homeUrl = "https://www.backloggd.com";

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
            if (verbose)
            {
                logger.Trace("Login method called");
            }

            webView.LoadingChanged += (s, e) =>
            {
                if (CheckLogin())
                {
                    webView.Close();
                }
            };

            webView.DeleteDomainCookies(".humblebundle.com");
            webView.DeleteDomainCookies("www.humblebundle.com");
            webView.Navigate(loginUrl);
            logger.Info("Navigating to Backloggd Login");
            webView.OpenDialog();

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

            using (var webView = playniteApi.WebViews.CreateOffscreenView())
            {
                logger.Info("Deleting Cookies");
                webView.DeleteDomainCookies(".backloggd.com");
                webView.DeleteDomainCookies("www.backloggd.com");
            }
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

            using (var webView = playniteApi.WebViews.CreateOffscreenView())
            {
                logger.Debug($"Opening WebView to: {gameUrl}");
                statusList = GetGameStatus(webView, gameUrl).GetAwaiter().GetResult();
                playStatus = GetPlayedStatus(webView, gameUrl).GetAwaiter().GetResult();
            }


            statusList = statusList.Select(SetStatusString).ToList();

            // Played Status is always first in the list.
            if (playStatus != null)
            {
                statusList[0] = playStatus;
            }

            return statusList;
        }

        private async Task<List<string>> GetGameStatus(IWebView webView, string url)
        {
            logger.Debug("Private GetGameStatus");

            const string script = @"
                        JSON.stringify(Array.from(document.querySelectorAll('#buttons > .btn-play-fill')).map(el => el.className));
                    ";

            var navigationCompleted = new TaskCompletionSource<bool>();

            List<string> statusList = new List<string>();
            JavaScriptEvaluationResult result = null;

            // This is to prevent the event from being called multiple times.
            bool eventHandled = false;

            webView.Navigate(url);


            webView.LoadingChanged += async (s, e) =>
            {
                if (!e.IsLoading && (!navigationCompleted.Task.IsCompleted && !eventHandled))
                {
                    eventHandled = true;

                    logger.Debug($"Executing Script: {script} at: {webView.GetCurrentAddress()}");

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
                        logger.Error($"Error in GetGameStatus: {exception.Message}");

                        navigationCompleted.SetResult(false);
                    }
                }
            };

            await navigationCompleted.Task.ConfigureAwait(continueOnCapturedContext: false);



            return statusList;
        }

        private async Task<string> GetPlayedStatus(IWebView webView, string url)
        {
            logger.Debug("Private GetPlayedStatus");

            const string script = @"
                        JSON.stringify(document.getElementsByClassName('button-link btn-play mx-auto')[0].getAttribute('play_type'));
                    ";


            var navigationCompleted = new TaskCompletionSource<bool>();

            string playStatus = "";
            JavaScriptEvaluationResult result = null;

            // This is to prevent the event from being called multiple times.
            bool eventHandled = false;

            webView.Navigate(url);


            webView.LoadingChanged += async (s, e) =>
            {
                if (!e.IsLoading && (!navigationCompleted.Task.IsCompleted && !eventHandled))
                {
                    eventHandled = true;

                    logger.Debug($"Executing Script at: {webView.GetCurrentAddress()}");

                    try
                    {
                        result = await webView.EvaluateScriptAsync(script);


                        if (result != null && result.Result != null)
                        {
                            playStatus = Serialization.FromJson<string>(result.Result.ToString());
                        }

                        navigationCompleted.SetResult(true);

                    }
                    catch (Exception exception)
                    {
                        logger.Error($"Error in GetGameStatus: {exception.Message}");

                        navigationCompleted.SetResult(false);
                    }
                }
            };

            await navigationCompleted.Task.ConfigureAwait(continueOnCapturedContext: false);



            return playStatus;
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

            using (var webView = playniteApi.WebViews.CreateOffscreenView())
            {
                logger.Debug($"Opening WebView to: {gameUrl}");
                gameName = GetBackloggdName(webView, gameUrl).GetAwaiter().GetResult();
            }

            logger.Debug($"GameName set to {gameName}");

            return gameName;
        }

        private async Task<string> GetBackloggdName(IWebView webView, string url)
        {
            if (verbose)
            {
                logger.Trace("Private GetBackloggdName method called");
            }

            logger.Debug("Private GetBackloggdName");

            const string script = @"
                        document.querySelector('#title > div.col-12.pr-0 > div > div > h1').textContent;
                    ";

            var navigationCompleted = new TaskCompletionSource<bool>();

            string gameName = "Could Not Find Game Name on Backloggd";

            JavaScriptEvaluationResult result = null;
            bool eventHandled = false;

            webView.Navigate(url);


            webView.LoadingChanged += async (s, e) =>
            {
                if (!e.IsLoading && (!navigationCompleted.Task.IsCompleted && !eventHandled))
                {
                    eventHandled = true;


                    logger.Debug($"Executing Script at: {webView.GetCurrentAddress()}");

                    try
                    {
                        result = await webView.EvaluateScriptAsync(script);


                        if (result != null && result.Result != null)
                        {
                            gameName = result.Result.ToString();
                        }

                        navigationCompleted.SetResult(true);

                    }
                    catch (Exception exception)
                    {
                        logger.Error($"Error in GetBackloggdName: {exception.Message}");

                        navigationCompleted.SetResult(false);
                    }
                }
            };

            await navigationCompleted.Task.ConfigureAwait(continueOnCapturedContext: false);



            return gameName;
        }


        /// <summary>
        /// Opens a WebView to given url.
        /// If no url is given, opens to Backloggd.com
        /// </summary>
        public void OpenWebView(string url = homeUrl)
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

            const string script = @"
                        var acceptButton = document.querySelector('#cookie-banner-accept');
                        acceptButton.click();
                    ";

            ExecuteScript(webView, script);
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
        }

        private async Task CheckLogin(IWebView webView)
        {
            if (verbose)
            {
                logger.Trace("Private CheckLogin method called");
            }

            // This Menu is the sign-out box which only appears when user is logged in.
            const string script = @"
                        document.querySelector('#mobile-user-nav > div:nth-child(3) > a') !== null;
                    ";

            JavaScriptEvaluationResult result = null;

            logger.Debug("Private CheckLogin");
            webView.Navigate("https://www.backloggd.com");


            var navigationCompleted = new TaskCompletionSource<bool>();
            bool eventHandled = false;

            webView.LoadingChanged += async (s, e) =>
            {
                if (!e.IsLoading && (!navigationCompleted.Task.IsCompleted && !eventHandled))
                {
                    eventHandled = true;

                    logger.Debug("In webView.LoadingChanged - CheckLogin method");
                    logger.Debug($"Executing Script at: {webView.GetCurrentAddress()}");

                    try
                    {
                        result = await webView.EvaluateScriptAsync(script);
                        logger.Debug($"Executing Script {script} - Result: {result?.Result}");

                        if (result != null && result.Result != null)
                        {
                            if (bool.Parse(result.Result.ToString()))
                            {
                                logger.Info("Logged In");
                                LoggedIn = true;
                            }
                            else
                            {
                                logger.Debug("Logged Out");
                                LoggedIn = false;
                            }
                        }
                        else
                        {
                            logger.Debug("Result is not set correctly");
                            LoggedIn = false;

                        }

                        if (verbose)
                        {
                            logger.Trace($"LoggedIn set to {LoggedIn}");
                        }

                        navigationCompleted.SetResult(true);
                    }
                    catch (Exception exception)
                    {
                        logger.Error($"Error in GetGameStatus: {exception.Message}");
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


        public void ToggleStatus(string gameURL, string status)
        {
            if (verbose)
            {
                logger.Trace("ToggleStatus method called");
            }

            string script;

            if (buttonMapper[status] != "0")
            {
                script = $"document.getElementsByClassName('button-link btn-play mx-auto')[{buttonMapper[status]}].click();";
            }
            else
            {
                switch (status)
                {
                    case "Played":
                        script = @"
                            document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                            timeoutId = setTimeout(function(){
                                document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                                document.querySelector('#played').click();
                            },5000);
                            clearTimeout(timeoutID);";
                        break;
                    case "Completed":
                        script = @"
                            document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                            timeoutId = setTimeout(function(){
                                document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                                document.querySelector('#completed').click();
                            },3000);
                            clearTimeout(timeoutID);";
                        break;
                    case "Retired":
                        script = @"
                            document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                            timeoutId = setTimeout(function(){
                                document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                                document.querySelector('#retired').click();
                            },3000);
                            clearTimeout(timeoutID);";
                        break;
                    case "Shelved":
                        script = @"
                            document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                            timeoutId = setTimeout(function(){
                                document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                                document.querySelector('#shelved').click();
                            },3000);
                            clearTimeout(timeoutID);";
                        break;
                    case "Abandoned":
                        script = @"
                            document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                            timeoutId = setTimeout(function(){
                                document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                                document.querySelector('#abandoned').click();
                            },3000);
                            clearTimeout(timeoutID);";
                        break;
                    default:
                        // Deselect Played
                        script = @"
                            document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                            document.getElementsByClassName('button-link btn-play mx-auto')[0].click();
                            document.querySelector('#unset-played-btn').click();";
                        break;
                }

            }


            using (var webView = playniteApi.WebViews.CreateOffscreenView())
            {
                webView.Navigate(gameURL);
                ExecuteScript(webView, script);
            }


        }


        private void ExecuteScript(IWebView webView, string script)
        {
            if (verbose)
            {
                logger.Trace("ExecuteScript method called");
            }

            bool eventHandled = false;
            var navigationCompleted = new TaskCompletionSource<bool>();

            webView.LoadingChanged += async (s, e) =>
            {
                if (!e.IsLoading && (!navigationCompleted.Task.IsCompleted && !eventHandled))
                {
                    eventHandled = true;
                    logger.Debug("In ExecuteScript LoadingChanged method");

                    try
                    {
                        await webView.EvaluateScriptAsync(script);

                        logger.Debug($"Executing Script {script} at: {webView.GetCurrentAddress()}");

                        navigationCompleted.SetResult(true);
                    }
                    catch (Exception exception)
                    {
                        logger.Error(exception.Message);

                        navigationCompleted.SetResult(true);
                    }
                }
            };

            navigationCompleted.Task.Wait();
            // await navigationCompleted.Task.ConfigureAwait(continueOnCapturedContext: false);
        }


        public string SetBackloggdUrl(string name = null)
        {
            string searchUrl;
            string url = BackloggdStatus.DefaultURL;

            if (name != null)
            {
                searchUrl = $"https://www.backloggd.com/search/games/{name.Replace(" ", "%20")}";
            }
            else
            {
                searchUrl = homeUrl;
            }

            using (var webView = PlayniteApiProvider.Api.WebViews.CreateView(width, height))
            {
                // TODO: Make this faster if possible.

                webView.LoadingChanged += (s, e) =>
                {
                    var currentAddress = webView.GetCurrentAddress();
                    if (!string.IsNullOrEmpty(currentAddress) && currentAddress.Contains("https://www.backloggd.com/games"))
                    {
                        url = currentAddress;
                        webView.Close();
                    }
                };

                webView.Navigate(searchUrl);

                webView.OpenDialog();

                if (webView.GetCurrentAddress().Contains("https://www.backloggd.com/games"))
                {
                    url = webView.GetCurrentAddress();

                }
            }

            return url;
        }
    }
}
