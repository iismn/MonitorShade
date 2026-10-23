using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MonitorShade
{
    internal sealed class WebUi : Grid, IDisposable
    {
        internal const string Origin = "https://monitorshade.internal";
        private readonly WebView2 browser = new WebView2();
        private readonly SettingsForm owner;
        private readonly Func<object> snapshot;
        private readonly Action<Dictionary<string, object>> command;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private bool started, disposed, ready;
        private string lastState;
        internal string Error { get; private set; }
        internal bool Ready { get { return ready; } }
        internal CoreWebView2 Core { get { return browser.CoreWebView2; } }
        internal WebUi(SettingsForm window, Func<object> read, Action<Dictionary<string, object>> execute)
        {
            owner = window; snapshot = read; command = execute;
            Children.Add(browser);
            browser.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(browser, true);
            Loaded += async delegate { await Start(); };
            owner.IsVisibleChanged += VisibleChanged;
            timer.Tick += delegate { Publish(); };
        }
        private async void VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (disposed) return;
            if (owner.IsVisible) { if (Core != null) Core.Resume(); await Start(); if (ready) { timer.Start(); Publish(); } }
            else { timer.Stop(); if (Core != null) try { await Core.TrySuspendAsync(); } catch (InvalidOperationException) { } }
        }
        private async Task Start()
        {
            if (started || disposed || !owner.IsVisible) return;
            started = true;
            try
            {
                WebRuntime.Extract("WebView2Loader.dll");
                CoreWebView2Environment.SetLoaderDllFolderPath(WebRuntime.DirectoryPath);
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorShade", "WebView2"));
                if (disposed) return;
                await browser.EnsureCoreWebView2Async(environment);
                if (disposed) return;
                var core = Core;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.IsPinchZoomEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsNonClientRegionSupportEnabled = true;
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += ResourceRequested;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) { e.Cancel = e.Uri != Origin + "/index.html"; };
                core.FrameNavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) { e.Cancel = true; };
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                core.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
                core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                core.WebMessageReceived += delegate(object sender, CoreWebView2WebMessageReceivedEventArgs e) {
                    if (disposed || e.Source != Origin + "/index.html") return;
                    try {
                        if (e.WebMessageAsJson.Length > 8192) throw new ArgumentException("요청이 너무 큽니다.");
                        var message = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
                        if (message == null) return;
                        if (message.ContainsKey("action") && Convert.ToString(message["action"]) == "ready") { ready = true; if (owner.IsVisible) timer.Start(); Publish(); }
                        else { command(message); if (!disposed) Publish(); }
                    } catch (Exception error) { if (!disposed) core.PostWebMessageAsJson(json.Serialize(new { error = error.Message })); }
                };
                core.ProcessFailed += delegate { ready = false; ShowError("화면 렌더러가 종료되었습니다. 설정 창을 다시 시작해 주세요."); };
                core.Navigate(Origin + "/index.html");
            }
            catch (Exception error) { ShowError("설정 화면을 시작하지 못했습니다. Microsoft Edge WebView2 Runtime 설치 상태를 확인해 주세요.\n" + error.Message); }
        }
        private void ShowError(string message)
        {
            if (disposed) return;
            Error = message; timer.Stop(); Children.Clear();
            var panel = new StackPanel { Margin = new Thickness(40), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 16 });
            var close = new Button { Content = "닫기", Margin = new Thickness(0, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            close.Click += delegate { owner.Close(); }; panel.Children.Add(close);
            Background = System.Windows.Media.Brushes.White; Children.Add(panel);
        }
        private void ResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            Stream stream = null; string mime = "text/plain";
            if (e.Request.Method == "GET" && e.Request.Uri.StartsWith(Origin + "/", StringComparison.Ordinal))
            {
                string name = e.Request.Uri.Substring(Origin.Length + 1);
                if (name == "index.html" || name == "app.js" || name == "app.css")
                { stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorShade.Web." + name); mime = name.EndsWith(".html") ? "text/html; charset=utf-8" : name.EndsWith(".js") ? "text/javascript; charset=utf-8" : "text/css; charset=utf-8"; }
                else if (new[] { "Pretendard-Regular.ttf", "Pretendard-SemiBold.ttf", "Manrope-Light.ttf", "Manrope-Regular.ttf" }.Contains(name))
                { var resource = Application.GetResourceStream(new Uri("pack://application:,,,/MonitorShade;component/fonts/" + name.ToLowerInvariant())); stream = resource == null ? null : resource.Stream; mime = "font/ttf"; }
            }
            string headers = "Content-Type: " + mime + "\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data:; connect-src 'none'; frame-src 'none'; base-uri 'none'; form-action 'none'\r\n";
            e.Response = Core.Environment.CreateWebResourceResponse(stream ?? new MemoryStream(), stream == null ? 404 : 200, stream == null ? "Not Found" : "OK", headers);
        }
        internal void Publish()
        {
            if (!ready || disposed || !owner.IsVisible) return;
            string state = json.Serialize(new { state = snapshot() });
            if (state == lastState) return;
            lastState = state; Core.PostWebMessageAsJson(state);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; ready = false; timer.Stop(); owner.IsVisibleChanged -= VisibleChanged; browser.Dispose();
        }
    }
}
