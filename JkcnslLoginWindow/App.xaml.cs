using System;
using System.Windows;

namespace JkcnslLoginWindow
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // 終了時にブラウザデータ全てを削除するかどうか
        public static bool DeleteBrowserDataOnExit = false;
        // パスワードの自動保存(ブラウザのパスワードマネージャー)を無効にするかどうか
        public static bool DisablePasswordAutosave = false;
        // ブラウザのCookie情報をリセットするかどうか(次回以降も2段階認証がかかる)
        public static bool ResetBrowserSiteData = false;
        // 起動後最初に開く場所
        public static string HomeUri = "https://live.nicovideo.jp/";

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            bool setHomeUri = false;
            foreach (string arg in e.Args)
            {
                if (setHomeUri)
                {
                    setHomeUri = false;
                    HomeUri = arg;
                }
                if (arg.Equals("--DeleteBrowserDataOnExit", StringComparison.OrdinalIgnoreCase))
                {
                    DeleteBrowserDataOnExit = true;
                }
                else if (arg.Equals("--DisablePasswordAutosave", StringComparison.OrdinalIgnoreCase))
                {
                    DisablePasswordAutosave = true;
                }
                else if (arg.Equals("--ResetBrowserSiteData", StringComparison.OrdinalIgnoreCase))
                {
                    ResetBrowserSiteData = true;
                }
                else if (arg.Equals("--HomeUri", StringComparison.OrdinalIgnoreCase))
                {
                    setHomeUri = true;
                }
            }
        }
    }
}
