using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace JkcnslLoginWindow
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const string GetCookiesUri = "https://live.nicovideo.jp/";
        const string NicovideoDomain = ".nicovideo.jp";
        readonly Regex SaveCookieNamePattern = new Regex(@"^(?:nicosid|user_session|user_session_secure)$");
        readonly string JkcnslPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "jkcnsl.exe");
        readonly string WebView2DataPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "JkcnslLoginWindowData");
        string settingUserAgent = null;
        string settingNicovideoCookie = null;
        bool canClose = false;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                using (Process process = Process.Start(new ProcessStartInfo
                {
                    FileName = JkcnslPath,
                    Arguments = "-c S",
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                }))
                {
                    if (process != null)
                    {
                        string result = process.StandardOutput.ReadToEnd();
                        Match m = Regex.Match(result, "^useragent ([^\n\r]*)", RegexOptions.Multiline);
                        if (m.Success)
                        {
                            settingUserAgent = m.Groups[1].Value;
                        }
                        m = Regex.Match(result, "^nicovideo_cookie ([^\n\r]*)", RegexOptions.Multiline);
                        if (m.Success)
                        {
                            settingNicovideoCookie = m.Groups[1].Value;
                        }
                    }
                }
            }
            catch
            {
                // jkcnslの設定を取得できなかったので保存機能は無効にする
                button_save.IsEnabled = false;
            }

            webView2.NavigationCompleted += (sender, e) =>
            {
                textBox_uri.Text = webView2.Source.ToString();
            };
            textBox_uri.Text = App.HomeUri;
        }

        async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await webView2.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, WebView2DataPath));
            textBox_userAgent.Text = settingUserAgent ?? webView2.CoreWebView2.Settings.UserAgent;

            // パスワード情報以外クリア
            await webView2.CoreWebView2.Profile.ClearBrowsingDataAsync(
                (!App.DeleteBrowserDataOnExit && !App.ResetBrowserSiteData ? CoreWebView2BrowsingDataKinds.AllDomStorage : CoreWebView2BrowsingDataKinds.AllSite) |
                CoreWebView2BrowsingDataKinds.DiskCache |
                CoreWebView2BrowsingDataKinds.DownloadHistory |
                CoreWebView2BrowsingDataKinds.GeneralAutofill |
                CoreWebView2BrowsingDataKinds.BrowsingHistory |
                CoreWebView2BrowsingDataKinds.Settings);
            webView2.CoreWebView2.Profile.IsPasswordAutosaveEnabled = !App.DeleteBrowserDataOnExit && !App.DisablePasswordAutosave;

            // 保存対象のCookieのみクリア
            foreach (CoreWebView2Cookie item in await webView2.CoreWebView2.CookieManager.GetCookiesAsync(GetCookiesUri))
            {
                if (SaveCookieNamePattern.IsMatch(item.Name))
                {
                    webView2.CoreWebView2.CookieManager.DeleteCookie(item);
                }
            }
            if (settingNicovideoCookie != null)
            {
                // jkcnslのCookie情報をリストア
                foreach (string keyValue in settingNicovideoCookie.Split(';'))
                {
                    int index = keyValue.IndexOf('=');
                    if (index >= 0)
                    {
                        string key = keyValue.Substring(0, index).Trim();
                        string value = keyValue.Substring(index + 1).Trim();
                        if (key.Length > 0)
                        {
                            CoreWebView2Cookie cookie = webView2.CoreWebView2.CookieManager.CreateCookie(key, value, NicovideoDomain, "/");
                            cookie.IsSecure = true;
                            webView2.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
                        }
                    }
                }
            }
            Button_navigate_Click(sender, e);
        }

        async void Button_navigate_Click(object sender, RoutedEventArgs e)
        {
            webView2.CoreWebView2.Settings.UserAgent = textBox_userAgent.Text;
            webView2.CoreWebView2.Navigate(textBox_uri.Text);
        }

        async Task<string> GetSaveCookie()
        {
            List<CoreWebView2Cookie> cookies = await webView2.CoreWebView2.CookieManager.GetCookiesAsync(GetCookiesUri);
            cookies.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            string cookie = "";
            foreach (CoreWebView2Cookie item in cookies)
            {
                if (SaveCookieNamePattern.IsMatch(item.Name) && Regex.IsMatch(item.Value, @"^[^\0-\x1f\x7f "",;\\]*$"))
                {
                    cookie += (cookie.Length > 0 ? "; " : "") + item.Name + "=" + item.Value;
                }
            }
            return cookie;
        }

        async Task<bool> SaveCookie()
        {
            string cookie = await GetSaveCookie();
            for (int i = settingUserAgent == textBox_userAgent.Text ? 1 : 0; i < (settingNicovideoCookie == cookie ? 1 : 2); i++)
            {
                bool failed = true;
                try
                {
                    using (Process process = Process.Start(new ProcessStartInfo
                    {
                        FileName = JkcnslPath,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                    }))
                    {
                        if (process != null)
                        {
                            process.StandardInput.AutoFlush = true;
                            process.StandardInput.WriteLine(i == 0 ? "Suseragent " + textBox_userAgent.Text : "Snicovideo_cookie " + cookie);
                            failed = !(process.StandardOutput.ReadLine() ?? "").StartsWith('.');
                            process.StandardInput.WriteLine("q");
                            process.StandardOutput.ReadToEnd();
                            process.WaitForExit();
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    if (failed)
                    {
                        return false;
                    }
                }
                if (failed)
                {
                    MessageBox.Show((i == 0 ? "\"useragent\"" : "\"nicovideo_cookie\"") + "をjkcnslに保存できませんでした", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                if (i == 0)
                {
                    settingUserAgent = textBox_userAgent.Text;
                }
                else
                {
                    settingNicovideoCookie = cookie;
                }
            }
            return true;
        }

        async void Button_copy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(await GetSaveCookie());
        }

        async void button_save_Click(object sender, RoutedEventArgs e)
        {
            await SaveCookie();
        }

        async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (canClose)
            {
#pragma warning disable CS0162
                if (App.DeleteBrowserDataOnExit)
                {
                    // 掃除
                    webView2.Dispose();
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            Directory.Delete(WebView2DataPath, true);
                        }
                        catch (DirectoryNotFoundException)
                        {
                            break;
                        }
                        catch
                        {
                            Thread.Sleep(1000);
                            continue;
                        }
                        break;
                    }
                }
#pragma warning restore CS0162
                return;
            }

            e.Cancel = true;
            if (!button_save.IsEnabled || (settingUserAgent == textBox_userAgent.Text && settingNicovideoCookie == await GetSaveCookie()))
            {
                canClose = true;
                Close();
                return;
            }
            MessageBoxResult result = MessageBox.Show("現在の情報をjkcnslに保存しますか?", "確認", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.No || (result == MessageBoxResult.Yes && await SaveCookie()))
            {
                canClose = true;
                Close();
            }
        }
    }
}
