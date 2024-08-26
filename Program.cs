using dwango.nicolive.chat.service.edge;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace jkcnsl
{
    class Program
    {
        const string UserAgent = "Mozilla/5.0";

        const int MaxAcceptableWebSocketPayloadSize = 32768;
        const int MaxAcceptableProtoBufChunkSize = 1048576;
        const int HttpGetTimeoutSec = 8;
        const int WebSocketTimeoutSec = 15;
        const int MixedStreamReconnectionSec = 20;

        // HttpClientは使いまわす。クッキーは共有しない
        static HttpClient _httpClientInstance;
        static HttpClient HttpClientInstance
        {
            get
            {
                if (_httpClientInstance == null)
                {
                    _httpClientInstance = new HttpClient(new HttpClientHandler
                    {
                        AutomaticDecompression = DecompressionMethods.All,
                        UseCookies = false
                    }) { Timeout = TimeSpan.FromSeconds(HttpGetTimeoutSec) };
                }
                return _httpClientInstance;
            }
        }

        // .nicovideo.jpのメッセージサーバ専用
        static HttpClient _nicovideoClientInstance;
        static HttpClient NicovideoClientInstance
        {
            get
            {
                if (_nicovideoClientInstance == null)
                {
                    _nicovideoClientInstance = new HttpClient(new HttpClientHandler
                    {
                        AutomaticDecompression = DecompressionMethods.All,
                        UseCookies = false
                    });
                }
                return _nicovideoClientInstance;
            }
        }

        static readonly BlockingCollection<string> ResponseLines = new BlockingCollection<string>();

        static bool _nicovideoLoginChecked = false;

        class StreamMixingInfo
        {
            public bool dropForwardedChat;
            public bool ignoreUnspecifiedDestinationPost;
            public TimeSpan nicovideoServerUnixTime = TimeSpan.Zero;
            public int nicovideoServerUnixTimeTick;
            public TimeSpan refugeServerUnixTime = TimeSpan.Zero;
            public int refugeServerUnixTimeTick;
        }

        static void Main(string[] args)
        {
            Process parentProcess;
            if (args.Length == 2 && args[0] == "-p")
            {
                // 親プロセスの不正終了を監視する
                try
                {
                    parentProcess = Process.GetProcessById(int.Parse(args[1]));
                    parentProcess.EnableRaisingEvents = true;
                    parentProcess.Exited += (sender, e) =>
                    {
                        // 非常時なので雑に落とす
                        Trace.WriteLine("Parent process exited!");
                        Environment.Exit(1);
                    };
                }
                catch (Exception e)
                {
                    Trace.WriteLine(e.ToString());
                }
            }
            else if (args.Length != 0)
            {
                Trace.WriteLine("Invalid argument!");
                return;
            }

            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
            var commands = new BlockingCollection<string>();
            var quitCts = new CancellationTokenSource();

            Task processTask = Task.Run(async () =>
            {
                try
                {
                    // 標準入力の各行の先頭文字を命令、それ以外を引数とするコマンドを処理し、結果を標準出力する
                    // 汎用のコマンド:
                    //  '+' 処理中のコマンドに入力を与える。処理中でなければ何もしない
                    //  'c' 処理中のコマンドを終了させる。処理中でなければ何もしない
                    // 結果:
                    //  '-' 出力
                    //  '.' 処理が終了した
                    //  '!' 処理が異常終了した
                    //  '?' 不明なコマンドだった
                    foreach (string comm in commands.GetConsumingEnumerable(quitCts.Token))
                    {
                        quitCts.Token.ThrowIfCancellationRequested();
                        switch (comm.FirstOrDefault())
                        {
                            case 'A':
                                if (comm == "Ai")
                                {
                                    await NicovideoLoginAsync(quitCts.Token);
                                }
                                else if (comm == "Ao")
                                {
                                    await NicovideoLogoutAsync(quitCts.Token);
                                }
                                else
                                {
                                    ResponseLines.Add("!");
                                }
                                break;
                            case 'G':
                                {
                                    string[] arg = comm.Substring(1).Split(new char[] { ' ' }, 2);
                                    await GetHttpGetStringAsync(arg[0], arg.Length >= 2 ? arg[1] : "", quitCts.Token);
                                }
                                break;
                            case 'L':
                                {
                                    string[] arg = comm.Substring(1).Split(new char[] { ' ' }, 2);
                                    ResponseLines.Add(await GetNicovideoStreamAsync(arg[0], arg.Length >= 2 ? arg[1] : "", commands, new StreamMixingInfo(), quitCts.Token));
                                }
                                break;
                            case 'R':
                                {
                                    string[] arg = comm.Substring(1).Split(new char[] { ' ' }, 4);
                                    // 避難所の種類によって解釈を変える。現在は"R1""R2"のみ
                                    if (arg.Length < 2 || (arg[0] != "1" && arg[0] != "2"))
                                    {
                                        ResponseLines.Add("!");
                                        break;
                                    }
                                    if (arg[0] == "2" && arg.Length >= 3)
                                    {
                                        // 混合
                                        await GetNicovideoRefugeMixedStreamAsync(arg[1], arg[2], arg.Length >= 4 ? arg[3] : "", commands, quitCts.Token);
                                        break;
                                    }
                                    // 避難所のみ。"R2"のときは混合時と同様に転送されたコメントを捨てる
                                    ResponseLines.Add(await GetRefugeStreamAsync(arg[1], commands, new StreamMixingInfo { dropForwardedChat = arg[0] == "2" }, quitCts.Token));
                                }
                                break;
                            case 'S':
                                {
                                    string[] arg = comm.Substring(1).Split(new char[] { ' ' }, 2);
                                    if (arg.Length < 2)
                                    {
                                        if (arg[0] == "nicovideo_cookie")
                                        {
                                            // クッキーを削除
                                            _nicovideoLoginChecked = false;
                                            Settings.Instance.nicovideo_cookie = null;
                                        }
                                        else if (arg[0] == "mail")
                                        {
                                            // 設定を削除
                                            Settings.Instance.nicovideo_cookie = null;
                                            Settings.Instance.mail = null;
                                        }
                                        else if (arg[0] == "password")
                                        {
                                            Settings.Instance.nicovideo_cookie = null;
                                            Settings.Instance.password = null;
                                        }
                                        else if (arg[0] == "useragent")
                                        {
                                            Settings.Instance.useragent = null;
                                        }
                                        else if (arg[0].Length == 0)
                                        {
                                            // すべての設定を出力
                                            if (Settings.Instance.nicovideo_cookie != null)
                                            {
                                                ResponseLines.Add("-nicovideo_cookie " + Settings.Instance.nicovideo_cookie);
                                            }
                                            if (Settings.Instance.mail != null)
                                            {
                                                ResponseLines.Add("-mail " + Settings.Instance.mail);
                                            }
                                            if (Settings.Instance.password != null)
                                            {
                                                // 3文字だけ表示
                                                string maskedPassword = Settings.Instance.password;
                                                if (maskedPassword.Length > 3)
                                                {
                                                    maskedPassword = maskedPassword.Substring(0, 3) + new string('*', maskedPassword.Length - 3);
                                                }
                                                ResponseLines.Add("-password " + maskedPassword);
                                            }
                                            ResponseLines.Add("-useragent " + (Settings.Instance.useragent ?? UserAgent));
                                            ResponseLines.Add(".");
                                            break;
                                        }
                                        else
                                        {
                                            ResponseLines.Add("!");
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        // 設定を変更
                                        if (arg[0] == "mail")
                                        {
                                            _nicovideoLoginChecked = false;
                                            Settings.Instance.nicovideo_cookie = null;
                                            Settings.Instance.mail = arg[1];
                                        }
                                        else if (arg[0] == "password")
                                        {
                                            _nicovideoLoginChecked = false;
                                            Settings.Instance.nicovideo_cookie = null;
                                            Settings.Instance.password = arg[1];
                                        }
                                        else if (arg[0] == "useragent")
                                        {
                                            Settings.Instance.useragent = arg[1];
                                        }
                                        else
                                        {
                                            ResponseLines.Add("!");
                                            break;
                                        }
                                    }
                                    Settings.Instance.Save();
                                    ResponseLines.Add(".");
                                }
                                break;
                            case '+':
                            case 'c':
                                break;
                            default:
                                ResponseLines.Add("?");
                                break;
                        }
                    }
                }
                catch { }
            });

            // 確実に終了するため標準出力は別スレッド
            Task writeTask = Task.Run(() =>
            {
                try
                {
                    foreach (string response in ResponseLines.GetConsumingEnumerable(quitCts.Token))
                    {
                        quitCts.Token.ThrowIfCancellationRequested();
                        Console.WriteLine(response);
                    }
                }
                catch { }
            });

            // 標準入力はブロックさせずに読み続ける
            for (; ; )
            {
                string comm = Console.ReadLine();
                if (comm == null || comm.FirstOrDefault() == 'q')
                {
                    Trace.WriteLine("Quit");
                    // 終了
                    break;
                }
                commands.Add(comm);
            }
            quitCts.Cancel();

            // すこし待つ
            Task.WaitAll(new Task[] { processTask, writeTask }, TimeSpan.FromSeconds(8));
        }

        /// <summary>汎用のHTTP-GET</summary>
        static async Task GetHttpGetStringAsync(string uri, string cookie, CancellationToken ct)
        {
            string ret;
            try
            {
                ret = await HttpClientGetStringAsync(uri, cookie, ct);
            }
            catch
            {
                ct.ThrowIfCancellationRequested();
                ResponseLines.Add("!");
                return;
            }
            foreach (string r in ret.Replace("\r", "").Split('\n'))
            {
                ResponseLines.Add("-" + r);
            }
            ResponseLines.Add(".");
        }

        /// <summary>実況ストリーム(混合)</summary>
        static async Task GetNicovideoRefugeMixedStreamAsync(string webSocketUrl, string lvId, string nicovideoCookie, BlockingCollection<string> commands, CancellationToken ct)
        {
            bool closing = false;
            var nicovideoCommands = new BlockingCollection<string>();
            var refugeCommands = new BlockingCollection<string>();
            var pollingAsync = async () =>
            {
                while (!closing)
                {
                    // 入力を複写して転送
                    string comm;
                    while (commands.TryTake(out comm))
                    {
                        closing = closing || comm.FirstOrDefault() == 'c';
                        nicovideoCommands.Add(comm);
                        refugeCommands.Add(comm);
                    }
                    await Task.Delay(100, ct);
                }
            };

            var mixingInfo = new StreamMixingInfo
            {
                dropForwardedChat = true,
                ignoreUnspecifiedDestinationPost = true
            };
            bool nicovideoConnected = false;
            bool nicovideoInitialized = false;
            bool refugeConnected = false;
            bool refugeInitialized = false;
            var nicovideoAsync = async () =>
            {
                // 入力の指示により閉じられるか両方未接続になるまで
                do
                {
                    nicovideoConnected = true;
                    nicovideoInitialized = true;
                    bool failed = await GetNicovideoStreamAsync(lvId, nicovideoCookie, nicovideoCommands, mixingInfo, ct) != ".";
                    nicovideoConnected = false;
                    // 適当なタグをでっちあげて切断を通知
                    ResponseLines.Add("-<x_disconnect status=\"" + (failed ? 1 : 0) + "\" />");
                    for (int wait = MixedStreamReconnectionSec * 5; !closing && (!refugeInitialized || refugeConnected) && wait > 0; wait--)
                    {
                        await Task.Delay(200, ct);
                        // 入力を捨てる
                        string comm;
                        while (nicovideoCommands.TryTake(out comm)) { }
                    }
                }
                while (!closing && (!refugeInitialized || refugeConnected));
                closing = true;
            };
            var refugeAsync = async () =>
            {
                // 入力の指示により閉じられるか両方未接続になるまで
                do
                {
                    refugeConnected = true;
                    refugeInitialized = true;
                    bool failed = await GetRefugeStreamAsync(webSocketUrl, refugeCommands, mixingInfo, ct) != ".";
                    refugeConnected = false;
                    // 適当なタグをでっちあげて切断を通知
                    ResponseLines.Add("-<x_disconnect status=\"" + (failed ? 1 : 0) + "\" refuge=\"1\" />");
                    for (int wait = MixedStreamReconnectionSec * 5; !closing && (!nicovideoInitialized || nicovideoConnected) && wait > 0; wait--)
                    {
                        await Task.Delay(200, ct);
                        // 入力を捨てる
                        string comm;
                        while (refugeCommands.TryTake(out comm)) { }
                    }
                }
                while (!closing && (!nicovideoInitialized || nicovideoConnected));
                closing = true;
            };

            await Task.WhenAll(new Task[] { pollingAsync(), nicovideoAsync(), refugeAsync() });
            ResponseLines.Add(".");
        }

        /// <summary>実況ストリーム(.nicovideo.jp)</summary>
        static async Task<string> GetNicovideoStreamAsync(string lvId, string cookie, BlockingCollection<string> commands, StreamMixingInfo mixingInfo, CancellationToken ct)
        {
            string webSocketUrl = null;
            WatchEmbeddedUser embeddedUser = null;
            if (Regex.IsMatch(lvId, "^(?:ch|co|lv)[0-9]+$"))
            {
                // 視聴セッション情報を取得
                try
                {
                    // ログイン情報が設定されているときcookie引数は使わない
                    cookie = await GetNicovideoLoginCookieAsync(ct) ?? cookie;

                    string ret = await HttpClientGetStringAsync("https://live.nicovideo.jp/watch/" + lvId, cookie, ct);
                    Match match = Regex.Match(ret, "<script(?= )([^>]*? id=\"embedded-data\"[^>]*)>");
                    if (match.Success)
                    {
                        match = Regex.Match(match.Groups[1].Value, " data-props=\"([^\"]*)\"");
                        if (match.Success)
                        {
                            var js = new DataContractJsonSerializer(typeof(WatchEmbedded));
                            var embedded = (WatchEmbedded)js.ReadObject(new MemoryStream(Encoding.UTF8.GetBytes(HttpUtility.HtmlDecode(match.Groups[1].Value))));
                            // 一応ドメインを検査しておく(スクレイピングなので。また、cookieを送信するため)
                            if (embedded.site != null && embedded.site.relive != null &&
                                Regex.IsMatch(embedded.site.relive.webSocketUrl ?? "", @"^wss://[0-9A-Za-z.-]+\.nicovideo\.jp/"))
                            {
                                webSocketUrl = embedded.site.relive.webSocketUrl;
                                embeddedUser = embedded.user;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    ct.ThrowIfCancellationRequested();
                    Trace.WriteLine(e.ToString());
                }
            }
            if (webSocketUrl == null)
            {
                return "!";
            }

            using (var watchSession = new ClientWebSocket())
            using (var msEntry = new MemoryStream())
            using (var msSegment = new MemoryStream())
            using (var msPrefetch = new MemoryStream())
            using (var closeCts = new CancellationTokenSource())
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, closeCts.Token))
            {
                Task pollingTask = null;
                Task<WebSocketReceiveResult> watchRecvTask = null;
                Task entryTask = null;
                Task segmentTask = null;
                Task prefetchTask = null;
                Stream entryStream = null;
                Stream segmentStream = null;
                Stream prefetchStream = null;
                HttpClient client = null;
                bool clientIsSameSite = false;

                try
                {
                    // Frameworkはここで例外になる("error"が返るのでUAは必須)
                    watchSession.Options.SetRequestHeader("User-Agent", Settings.Instance.useragent ?? UserAgent);
                    watchSession.Options.SetRequestHeader("Accept", "*/*");
                    // 視聴ページから接続するようなコンテキスト
                    watchSession.Options.SetRequestHeader("Cache-Control", "no-cache");
                    watchSession.Options.SetRequestHeader("Origin", "https://live.nicovideo.jp");
                    watchSession.Options.SetRequestHeader("Sec-Fetch-Dest", "empty");
                    watchSession.Options.SetRequestHeader("Sec-Fetch-Mode", "websocket");
                    watchSession.Options.SetRequestHeader("Sec-Fetch-Site", "same-site");
                    if (cookie.Length > 0)
                    {
                        watchSession.Options.SetRequestHeader("Cookie", cookie);
                    }
                    // 視聴セッションに接続
                    await DoWebSocketAction(async ct => await watchSession.ConnectAsync(new Uri(webSocketUrl), ct), ct);
                    await DoWebSocketAction(async ct => await watchSession.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
                                            "{\"type\":\"startWatching\",\"data\":{\"reconnect\":false}}")),
                                            WebSocketMessageType.Text, true, ct), ct);

                    string viewUri = null;
                    var vposBaseUnixTime = TimeSpan.Zero;
                    string hashedUserId = null;
                    int keepSeatIntervalSec = 0;
                    int keepSeatTick = 0;
                    var readEntryBuf = new byte[512];
                    var readSegmentBuf = new byte[512];
                    var readPrefetchBuf = new byte[512];
                    var watchBuf = new byte[MaxAcceptableWebSocketPayloadSize];
                    int watchCount = 0;
                    var serverUnixTime = TimeSpan.Zero;
                    int serverUnixTimeTick = 0;
                    bool wroteFirstChat = false;
                    bool wroteLiveChat = false;

                    var jsWatchSessionPost = new DataContractJsonSerializer(typeof(WatchSessionPost));
                    var jsWatchSessionResult = new DataContractJsonSerializer(typeof(WatchSessionResult));
                    var jsWatchSessionResultForError = new DataContractJsonSerializer(typeof(WatchSessionResultForError));
                    var jsWatchSessionResultForMessageServer = new DataContractJsonSerializer(typeof(WatchSessionResultForMessageServer));
                    var jsWatchSessionResultForSeat = new DataContractJsonSerializer(typeof(WatchSessionResultForSeat));
                    var jsWatchSessionResultForServerTime = new DataContractJsonSerializer(typeof(WatchSessionResultForServerTime));

                    string nextAt = "now";
                    bool closed = false;
                    while (!closed && (viewUri == null || entryTask != null || nextAt != null))
                    {
                        var gracefulClose = async () =>
                        {
                            if (!closed)
                            {
                                await DoWebSocketAction(async ct => await watchSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct), ct);
                                closed = true;
                            }
                        };
                        bool watchReceived = false;
                        ChunkedMessage chunkedMessage = null;
                        {
                            ct.ThrowIfCancellationRequested();
                            int keepSeatElapsed = ((Environment.TickCount & int.MaxValue) - keepSeatTick) & int.MaxValue;
                            if (keepSeatIntervalSec > 0 && keepSeatElapsed > keepSeatIntervalSec * 1000)
                            {
                                // 座席を維持
                                Trace.WriteLine("keepSeat");
                                await DoWebSocketAction(async ct => await watchSession.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
                                                        "{\"type\":\"keepSeat\"}")), WebSocketMessageType.Text, true, ct), ct);
                                keepSeatTick = Environment.TickCount & int.MaxValue;
                            }

                            pollingTask = pollingTask ?? Task.Delay(200, linkedCts.Token);
                            watchRecvTask = watchRecvTask ?? watchSession.ReceiveAsync(new ArraySegment<byte>(watchBuf, watchCount, watchBuf.Length - watchCount), linkedCts.Token);
                            if (entryTask == null && viewUri != null)
                            {
                                if (client == null)
                                {
                                    client = NicovideoClientInstance;
                                    client.DefaultRequestHeaders.Clear();
                                    client.DefaultRequestHeaders.Add("User-Agent", Settings.Instance.useragent ?? UserAgent);
                                    client.DefaultRequestHeaders.Add("Accept", "*/*");
                                    // 視聴ページからフェッチするようなコンテキスト。おそらくCDN送りになるのでプライベートな情報はつけない
                                    client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
                                    client.DefaultRequestHeaders.Add("Origin", "https://live.nicovideo.jp");
                                    client.DefaultRequestHeaders.Add("Referer", "https://live.nicovideo.jp/");
                                    client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
                                    client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
                                    clientIsSameSite = Regex.IsMatch(viewUri, @"^https://[0-9A-Za-z.-]+\.nicovideo\.jp/");
                                    client.DefaultRequestHeaders.Add("Sec-Fetch-Site", clientIsSameSite ? "same-site" : "cross-site");
                                }
                                // プレイリスト接続開始
                                entryTask = client.GetStreamAsync(viewUri + "?at=" + nextAt, linkedCts.Token);
                                nextAt = null;
                            }
                            Task completedTask = await Task.WhenAny((new Task[] { pollingTask, watchRecvTask, entryTask, segmentTask, prefetchTask }).Where(a => a != null));
                            if (completedTask == pollingTask)
                            {
                                // 定期的に入力をチェック
                                await pollingTask;
                                pollingTask = null;
                                string comm;
                                while (commands.TryTake(out comm))
                                {
                                    if (comm.FirstOrDefault() == 'c')
                                    {
                                        // 閉じる
                                        await gracefulClose();
                                        break;
                                    }
                                    string dest_, color_, font_, position_, size_, text_;
                                    bool isAnonymous_;
                                    if (comm.FirstOrDefault() == '+' &&
                                        ParsePostComment(comm.Substring(1), out dest_, out color_, out font_, out isAnonymous_, out position_, out size_, out text_) &&
                                        (dest_ == "nico" || !mixingInfo.ignoreUnspecifiedDestinationPost))
                                    {
                                        // コメント投稿
                                        if ((dest_ == "nico" || dest_ == null) && vposBaseUnixTime > TimeSpan.Zero && serverUnixTime >= vposBaseUnixTime)
                                        {
                                            // vposは10msec単位。内部時計のずれに影響されないようにサーバ時刻を基準に補正
                                            int vpos = (int)(serverUnixTime - vposBaseUnixTime).TotalSeconds * 100 + (((Environment.TickCount & int.MaxValue) - serverUnixTimeTick) & int.MaxValue) / 10;
                                            var ms = new MemoryStream();
                                            jsWatchSessionPost.WriteObject(ms, new WatchSessionPost()
                                            {
                                                data = new WatchSessionPostData()
                                                {
                                                    color = color_,
                                                    font = font_,
                                                    isAnonymous = isAnonymous_,
                                                    position = position_,
                                                    size = size_,
                                                    text = text_,
                                                    vpos = vpos
                                                },
                                                type = "postComment"
                                            });
                                            byte[] post = ms.ToArray();
                                            Trace.WriteLine(Encoding.UTF8.GetString(post));
                                            await DoWebSocketAction(async ct => await watchSession.SendAsync(new ArraySegment<byte>(post), WebSocketMessageType.Text, true, ct), ct);
                                        }
                                        else
                                        {
                                            // 投稿拒否
                                            ResponseLines.Add("-<chat_result status=\"1\" />");
                                        }
                                    }
                                }
                            }
                            else if (completedTask == watchRecvTask)
                            {
                                WebSocketReceiveResult ret = await watchRecvTask;
                                watchRecvTask = null;
                                if (ret.MessageType != WebSocketMessageType.Text || watchCount + ret.Count >= watchBuf.Length)
                                {
                                    // 終了または処理できないフレーム。閉じる
                                    await gracefulClose();
                                }
                                else
                                {
                                    watchCount += ret.Count;
                                    watchReceived = ret.EndOfMessage;
                                }
                            }
                            else if (completedTask == entryTask)
                            {
                                if (entryStream == null)
                                {
                                    // プレイリスト接続完了
                                    entryStream = await (Task<Stream>)entryTask;
                                    entryTask = ReadProtoBufChunkAsync(entryStream, msEntry, readEntryBuf, linkedCts.Token);
                                }
                                else
                                {
                                    MemoryStream ms = await (Task<MemoryStream>)entryTask;
                                    entryTask = null;
                                    if (ms == null)
                                    {
                                        // プレイリスト切断
                                        entryStream.Close();
                                        entryStream = null;
                                        Trace.WriteLine("Playlist stream closed");
                                    }
                                    else
                                    {
                                        // チャンク取得完了
                                        var chunkedEntry = ProtoBuf.Serializer.Deserialize<ChunkedEntry>(ms);
                                        if (chunkedEntry.next != null)
                                        {
                                            nextAt = chunkedEntry.next.at.ToString();
                                            Trace.WriteLine("Playlist next.at = " + nextAt);
                                        }
                                        if (chunkedEntry.segment != null)
                                        {
                                            string segmentUri = chunkedEntry.segment.uri;
                                            Trace.WriteLine("segment.uri = " + segmentUri);
                                            if (Regex.IsMatch(segmentUri, @"^https://[0-9A-Za-z.-]+\.nicovideo\.jp/") != clientIsSameSite)
                                            {
                                                // リクエストヘッダの内容と矛盾してしまうため
                                                throw new NotImplementedException("The domain category of segment.uri is inconsistent with the request header.");
                                            }
                                            // ライブ用途なので速やかに接続開始する
                                            if (segmentTask == null)
                                            {
                                                segmentTask = client.GetStreamAsync(segmentUri, linkedCts.Token);
                                            }
                                            else if (prefetchTask == null)
                                            {
                                                if (prefetchStream != null)
                                                {
                                                    prefetchStream.Close();
                                                    prefetchStream = null;
                                                    Trace.WriteLine("Prefetch skipped");
                                                }
                                                prefetchTask = client.GetStreamAsync(segmentUri, linkedCts.Token);
                                                Trace.WriteLine("Prefetch started");
                                            }
                                        }
                                        entryTask = ReadProtoBufChunkAsync(entryStream, msEntry, readEntryBuf, linkedCts.Token);
                                    }
                                }
                            }
                            else if (completedTask == segmentTask)
                            {
                                if (segmentStream == null)
                                {
                                    // セグメント接続完了
                                    segmentStream = await (Task<Stream>)segmentTask;
                                    segmentTask = ReadProtoBufChunkAsync(segmentStream, msSegment, readSegmentBuf, linkedCts.Token);
                                }
                                else
                                {
                                    MemoryStream ms = await (Task<MemoryStream>)segmentTask;
                                    segmentTask = null;
                                    if (ms == null)
                                    {
                                        // セグメント切断
                                        segmentStream.Close();
                                        Trace.WriteLine("Segment stream closed");
                                        // プリフェッチタスクを引き継ぐ
                                        segmentTask = prefetchTask;
                                        segmentStream = prefetchStream;
                                        prefetchTask = null;
                                        prefetchStream = null;
                                        if (segmentTask == null && segmentStream != null)
                                        {
                                            // プリフェッチ済み
                                            chunkedMessage = ProtoBuf.Serializer.Deserialize<ChunkedMessage>(msPrefetch);
                                            segmentTask = ReadProtoBufChunkAsync(segmentStream, msSegment, readSegmentBuf, linkedCts.Token);
                                        }
                                    }
                                    else
                                    {
                                        chunkedMessage = ProtoBuf.Serializer.Deserialize<ChunkedMessage>(ms);
                                        segmentTask = ReadProtoBufChunkAsync(segmentStream, msSegment, readSegmentBuf, linkedCts.Token);
                                    }
                                }
                            }
                            else
                            {
                                if (prefetchStream == null)
                                {
                                    // プリフェッチセグメント接続完了
                                    prefetchStream = await (Task<Stream>)prefetchTask;
                                    prefetchTask = ReadProtoBufChunkAsync(prefetchStream, msPrefetch, readPrefetchBuf, linkedCts.Token);
                                }
                                else if (await (Task<MemoryStream>)prefetchTask == null)
                                {
                                    // プリフェッチセグメント切断
                                    prefetchTask = null;
                                    prefetchStream.Close();
                                    prefetchStream = null;
                                    Trace.WriteLine("Prefetch stream closed");
                                }
                                else
                                {
                                    // プリフェッチ完了
                                    prefetchTask = null;
                                    Trace.WriteLine("Prefetch done");
                                }
                            }
                        }

                        if (watchReceived)
                        {
                            var message = (WatchSessionResult)jsWatchSessionResult.ReadObject(new MemoryStream(watchBuf, 0, watchCount));
                            switch (message.type)
                            {
                                case "disconnect":
                                case "reconnect":
                                    Trace.WriteLine(message.type);
                                    // とりあえず再接続要求も切断扱い
                                    await gracefulClose();
                                    break;
                                case "error":
                                    Trace.WriteLine("error");
                                    {
                                        var error = ((WatchSessionResultForError)jsWatchSessionResultForError.ReadObject(new MemoryStream(watchBuf, 0, watchCount))).data;
                                        if (error != null)
                                        {
                                            Trace.WriteLine(Encoding.UTF8.GetString(watchBuf, 0, watchCount));
                                            if (error.code == "INVALID_MESSAGE")
                                            {
                                                ResponseLines.Add("-<chat_result status=\"1\" />");
                                            }
                                            else if (error.code == "COMMENT_POST_NOT_ALLOWED")
                                            {
                                                ResponseLines.Add("-<chat_result status=\"4\" />");
                                            }
                                        }
                                    }
                                    break;
                                case "messageServer":
                                    Trace.WriteLine("messageServer");
                                    // メッセージサーバの接続先情報
                                    {
                                        var messageServer = ((WatchSessionResultForMessageServer)jsWatchSessionResultForMessageServer.ReadObject(new MemoryStream(watchBuf, 0, watchCount))).data;
                                        if (messageServer != null)
                                        {
                                            if (messageServer.vposBaseTime != null && vposBaseUnixTime <= TimeSpan.Zero)
                                            {
                                                DateTime d;
                                                if (DateTime.TryParse(messageServer.vposBaseTime, CultureInfo.InvariantCulture, out d))
                                                {
                                                    vposBaseUnixTime = d.ToUniversalTime() - new DateTime(1970, 1, 1);
                                                }
                                            }
                                            viewUri = viewUri ?? messageServer.viewUri;
                                            hashedUserId = hashedUserId ?? messageServer.hashedUserId;

                                            // 適当なタグをでっちあげてxmlに変換。本来の"room"メッセージは廃止された
                                            ResponseLines.Add(("-<x_room" +
                                                " thread_id=\"" + lvId + "_" + (long)vposBaseUnixTime.TotalSeconds + "\"" +
                                                (hashedUserId != null ? " hashed_user_id=\"" + HtmlEncodeAmpLtGt(hashedUserId, true) + "\"" : "") +
                                                (embeddedUser != null && embeddedUser.id != null ? " user_id=\"" + HtmlEncodeAmpLtGt(embeddedUser.id, true) + "\"" : "") +
                                                (embeddedUser != null && embeddedUser.nickname != null ? " nickname=\"" + HtmlEncodeAmpLtGt(embeddedUser.nickname, true) + "\"" : "") +
                                                (embeddedUser != null && embeddedUser.isLoggedIn ? " is_logged_in=\"1\"" : "") +
                                                " />").Replace("\n", "&#10;").Replace("\r", "&#13;"));
                                        }
                                    }
                                    break;
                                case "ping":
                                    Trace.WriteLine("ping-pong");
                                    await DoWebSocketAction(async ct => await watchSession.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
                                                            "{\"type\":\"pong\"}")), WebSocketMessageType.Text, true, ct), ct);
                                    break;
                                case "postCommentResult":
                                    Trace.WriteLine("postCommentResult");
                                    // コメント投稿に成功
                                    ResponseLines.Add("-<chat_result status=\"0\" />");
                                    break;
                                case "seat":
                                    Trace.WriteLine("seat");
                                    {
                                        var seat = ((WatchSessionResultForSeat)jsWatchSessionResultForSeat.ReadObject(new MemoryStream(watchBuf, 0, watchCount))).data;
                                        if (seat != null)
                                        {
                                            keepSeatIntervalSec = Math.Min((int)seat.keepIntervalSec, 1000);
                                            keepSeatTick = Environment.TickCount & int.MaxValue;
                                        }
                                    }
                                    break;
                                case "serverTime":
                                    Trace.WriteLine("serverTime");
                                    {
                                        var serverTime = ((WatchSessionResultForServerTime)jsWatchSessionResultForServerTime.ReadObject(new MemoryStream(watchBuf, 0, watchCount))).data;
                                        if (serverTime != null && serverTime.currentMs != null)
                                        {
                                            DateTime d;
                                            if (DateTime.TryParse(serverTime.currentMs, CultureInfo.InvariantCulture, out d))
                                            {
                                                serverUnixTime = d.ToUniversalTime() - new DateTime(1970, 1, 1);
                                                serverUnixTimeTick = Environment.TickCount & int.MaxValue;
                                                mixingInfo.nicovideoServerUnixTime = serverUnixTime;
                                                mixingInfo.nicovideoServerUnixTimeTick = serverUnixTimeTick;
                                            }
                                        }
                                    }
                                    break;
                            }
                            watchCount = 0;
                        }

                        if (chunkedMessage != null)
                        {
                            if (chunkedMessage.message != null && chunkedMessage.message.chat != null &&
                                chunkedMessage.meta != null && chunkedMessage.meta.at != null &&
                                serverUnixTime > TimeSpan.Zero)
                            {
                                TimeSpan at = chunkedMessage.meta.at.Value.ToUniversalTime() - new DateTime(1970, 1, 1);

                                if (!wroteLiveChat && at >= serverUnixTime)
                                {
                                    if (wroteFirstChat)
                                    {
                                        // 適当なタグをでっちあげて過去のコメントの出力終了を通知
                                        ResponseLines.Add("-<x_past_chat_end />");
                                    }
                                    wroteFirstChat = true;
                                    wroteLiveChat = true;
                                }
                                else if (!wroteFirstChat)
                                {
                                    // 適当なタグをでっちあげて過去のコメントの出力開始を通知
                                    ResponseLines.Add("-<x_past_chat_begin />");
                                    wroteFirstChat = true;
                                }
                                // 混合時は不整合を避けるため片方のサーバ時刻をdate属性値に使う
                                if (wroteLiveChat && mixingInfo.refugeServerUnixTime > TimeSpan.Zero)
                                {
                                    at = mixingInfo.nicovideoServerUnixTime +
                                         TimeSpan.FromMilliseconds(((Environment.TickCount & int.MaxValue) - mixingInfo.nicovideoServerUnixTimeTick) & int.MaxValue);
                                }
                                var chat = chunkedMessage.message.chat;
                                string mail = "";
                                if (chat.modifier != null)
                                {
                                    if (chat.modifier.full_color != null)
                                    {
                                        mail += " #" + ((chat.modifier.full_color.r << 16 |
                                                         chat.modifier.full_color.g << 8 |
                                                         chat.modifier.full_color.b) & 0xFFFFFF).ToString("x6");
                                    }
                                    else if (chat.modifier.named_color != default)
                                    {
                                        mail += " " + chat.modifier.named_color;
                                    }
                                    if (chat.modifier.position != default)
                                    {
                                        mail += " " + chat.modifier.position;
                                    }
                                    if (chat.modifier.size != default)
                                    {
                                        mail += " " + chat.modifier.size;
                                    }
                                    if (chat.modifier.font != default)
                                    {
                                        mail += " " + chat.modifier.font;
                                    }
                                    if (chat.modifier.opacity != default)
                                    {
                                        mail += " " + chat.modifier.opacity;
                                    }
                                }

                                // xml形式に変換(もっと賢い方法ありそうだが属性の順序など維持したいので…)
                                ResponseLines.Add(("-<chat" +
                                    " thread=\"" + lvId + "_" + (long)vposBaseUnixTime.TotalSeconds + "\"" +
                                    " no=\"" + chat.no +
                                    "\" vpos=\"" + chat.vpos +
                                    "\" date=\"" + (long)at.TotalSeconds +
                                    "\" date_usec=\"" + (at.Milliseconds * 1000 + at.Microseconds) + "\"" +
                                    (mail.Length > 0 ? " mail=\"" + HtmlEncodeAmpLtGt(mail.Substring(1).ToLowerInvariant(), true) + "\"" : "") +
                                    (chat.hashed_user_id == hashedUserId ? " yourpost=\"1\"" : "") +
                                    " user_id=\"" + HtmlEncodeAmpLtGt(chat.raw_user_id == 0 ? chat.hashed_user_id : chat.raw_user_id.ToString(), true) + "\"" +
                                    (chat.account_status == dwango.nicolive.chat.data.Chat.AccountStatus.Premium ? " premium=\"1\"" : "") +
                                    (chat.raw_user_id == 0 ? " anonymity=\"1\"" : "") +
                                    ">" + HtmlEncodeAmpLtGt(chat.content) + "</chat>").Replace("\n", "&#10;").Replace("\r", "&#13;"));
                            }
                            // TODO: nicoadなどは落ち着いたら対応
                        }
                    }
                }
                catch (Exception e)
                {
                    ct.ThrowIfCancellationRequested();
                    Trace.WriteLine(e.ToString());
                    return "!";
                }
                finally
                {
                    // タスクをすべて回収
                    closeCts.Cancel();
                    try
                    {
                        await Task.WhenAll((new Task[] { pollingTask, watchRecvTask, entryTask, segmentTask, prefetchTask }).Where(a => a != null));
                    }
                    catch { }

                    // HTTPストリームはusingしていないのでここで閉じる
                    if (prefetchStream != null)
                    {
                        prefetchStream.Close();
                    }
                    if (segmentStream != null)
                    {
                        segmentStream.Close();
                    }
                    if (entryStream != null)
                    {
                        entryStream.Close();
                    }
                }
            }
            return ".";
        }

        /// <summary>実況ストリーム(避難所)</summary>
        static async Task<string> GetRefugeStreamAsync(string webSocketUrl, BlockingCollection<string> commands, StreamMixingInfo mixingInfo, CancellationToken ct)
        {
            // メソッド実装にあたり特に https://github.com/tsukumijima/TVRemotePlus および https://github.com/asannou/namami を参考にした。

            if (!webSocketUrl.StartsWith("wss://", StringComparison.Ordinal))
            {
                return "!";
            }

            // NX-Jikkyoかどうか。chatタグの形式を微妙に変えるだけなので判定はラフ
            bool isNxJikkyo = webSocketUrl.IndexOf("nx-jikkyo", StringComparison.Ordinal) >= 0;

            using (var watchSession = new ClientWebSocket())
            using (var commentSession = new ClientWebSocket())
            using (var closeCts = new CancellationTokenSource())
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, closeCts.Token))
            {
                Task pollingTask = null;
                Task<WebSocketReceiveResult> watchRecvTask = null;
                Task<WebSocketReceiveResult> commentRecvTask = null;
                try
                {
                    // Frameworkはここで例外になる
                    watchSession.Options.SetRequestHeader("User-Agent", Settings.Instance.useragent ?? UserAgent);
                    commentSession.Options.SetRequestHeader("User-Agent", Settings.Instance.useragent ?? UserAgent);
                    commentSession.Options.AddSubProtocol("msg.nicovideo.jp#json");
                    // 視聴セッションに接続
                    await DoWebSocketAction(async ct => await watchSession.ConnectAsync(new Uri(webSocketUrl), ct), ct);
                    await DoWebSocketAction(async ct => await watchSession.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
                        "{\"type\":\"startWatching\",\"data\":{\"room\":{\"protocol\":\"webSocket\",\"commentable\":true},\"reconnect\":false}}")),
                        WebSocketMessageType.Text, true, ct), ct);

                    var vposBaseUnixTime = TimeSpan.Zero;
                    int keepSeatIntervalSec = 0;
                    int keepSeatTick = 0;
                    int commentKeepTick = 0;
                    var watchBuf = new byte[MaxAcceptableWebSocketPayloadSize];
                    int watchCount = 0;
                    var commentBuf = new byte[MaxAcceptableWebSocketPayloadSize];
                    int commentCount = 0;
                    bool commentConnected = false;
                    var serverUnixTime = TimeSpan.Zero;
                    int serverUnixTimeTick = 0;
                    bool wroteFirstChat = false;
                    bool wroteLiveChat = false;

                    var jsWatchSessionPost = new DataContractJsonSerializer(typeof(WatchSessionPost));
                    var jsWatchSessionResult = new DataContractJsonSerializer(typeof(WatchSessionResult));
                    var jsWatchSessionResultForError = new DataContractJsonSerializer(typeof(WatchSessionResultForError));
                    var jsWatchSessionResultForRoom = new DataContractJsonSerializer(typeof(WatchSessionResultForRoom));
                    var jsWatchSessionResultForSeat = new DataContractJsonSerializer(typeof(WatchSessionResultForSeat));
                    var jsWatchSessionResultForServerTime = new DataContractJsonSerializer(typeof(WatchSessionResultForServerTime));
                    var jsCommentSessionResult = new DataContractJsonSerializer(typeof(CommentSessionResult));

                    bool closed = false;
                    while (!closed)
                    {
                        var gracefulClose = async () =>
                        {
                            if (!closed)
                            {
                                if (commentRecvTask != null)
                                {
                                    await DoWebSocketAction(async ct => await commentSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct), ct);
                                }
                                await DoWebSocketAction(async ct => await watchSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct), ct);
                                closed = true;
                            }
                        };
                        bool watchReceived = false;
                        bool commentReceived = false;
                        {
                            ct.ThrowIfCancellationRequested();
                            int keepSeatElapsed = ((Environment.TickCount & int.MaxValue) - keepSeatTick) & int.MaxValue;
                            int commentKeepElapsed = ((Environment.TickCount & int.MaxValue) - commentKeepTick) & int.MaxValue;
                            if (keepSeatIntervalSec > 0 && keepSeatElapsed > keepSeatIntervalSec * 1000)
                            {
                                // 座席を維持
                                Trace.WriteLine("keepSeat");
                                await DoWebSocketAction(async ct => await watchSession.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
                                                        "{\"type\":\"keepSeat\"}")), WebSocketMessageType.Text, true, ct), ct);
                                keepSeatTick = Environment.TickCount & int.MaxValue;
                            }
                            else if (commentConnected && commentKeepElapsed > 60000)
                            {
                                // 60秒ごとに接続を維持
                                Trace.WriteLine("commentKeep");
                                await DoWebSocketAction(async ct => await commentSession.SendAsync(new ArraySegment<byte>(new byte[0]), WebSocketMessageType.Text, true, ct), ct);
                                commentKeepTick = Environment.TickCount & int.MaxValue;
                            }

                            pollingTask = pollingTask ?? Task.Delay(200, linkedCts.Token);
                            watchRecvTask = watchRecvTask ?? watchSession.ReceiveAsync(new ArraySegment<byte>(watchBuf, watchCount, watchBuf.Length - watchCount), linkedCts.Token);
                            if (commentConnected)
                            {
                                commentRecvTask = commentRecvTask ?? commentSession.ReceiveAsync(new ArraySegment<byte>(commentBuf, commentCount, commentBuf.Length - commentCount), linkedCts.Token);
                            }
                            Task completedTask = await Task.WhenAny((new Task[] { pollingTask, watchRecvTask, commentRecvTask }).Where(a => a != null));
                            if (completedTask == pollingTask)
                            {
                                // 定期的に入力をチェック
                                await pollingTask;
                                pollingTask = null;
                                string comm;
                                while (commands.TryTake(out comm))
                                {
                                    if (comm.FirstOrDefault() == 'c')
                                    {
                                        // 閉じる
                                        await gracefulClose();
                                        break;
                                    }
                                    string dest_, color_, font_, position_, size_, text_;
                                    bool isAnonymous_;
                                    if (comm.FirstOrDefault() == '+' &&
                                        ParsePostComment(comm.Substring(1), out dest_, out color_, out font_, out isAnonymous_, out position_, out size_, out text_) &&
                                        (dest_ == "refuge" || !mixingInfo.ignoreUnspecifiedDestinationPost))
                                    {
                                        // コメント投稿
                                        if ((dest_ == "refuge" || dest_ == null) && vposBaseUnixTime > TimeSpan.Zero && serverUnixTime >= vposBaseUnixTime)
                                        {
                                            // vposは10msec単位。内部時計のずれに影響されないようにサーバ時刻を基準に補正
                                            int vpos = (int)(serverUnixTime - vposBaseUnixTime).TotalSeconds * 100 + (((Environment.TickCount & int.MaxValue) - serverUnixTimeTick) & int.MaxValue) / 10;
                                            var ms = new MemoryStream();
                                            jsWatchSessionPost.WriteObject(ms, new WatchSessionPost()
                                            {
                                                data = new WatchSessionPostData()
                                                {
                                                    color = color_,
                                                    font = font_,
                                                    isAnonymous = isAnonymous_,
                                                    position = position_,
                                                    size = size_,
                                                    text = text_,
                                                    vpos = vpos
                                                },
                                                type = "postComment"
                                            });
                                            byte[] post = ms.ToArray();
                                            Trace.WriteLine(Encoding.UTF8.GetString(post));
                                            await DoWebSocketAction(async ct => await watchSession.SendAsync(new ArraySegment<byte>(post), WebSocketMessageType.Text, true, ct), ct);
                                        }
                                        else
                                        {
                                            // 投稿拒否
                                            ResponseLines.Add("-<chat_result status=\"1\" x_refuge=\"1\" />");
                                        }
                                    }
                                }
                            }
                            else if (completedTask == watchRecvTask)
                            {
                                WebSocketReceiveResult ret = await watchRecvTask;
                                watchRecvTask = null;
                                if (ret.MessageType != WebSocketMessageType.Text || watchCount + ret.Count >= watchBuf.Length)
                                {
                                    // 終了または処理できないフレーム。閉じる
                                    await gracefulClose();
                                }
                                else
                                {
                                    watchCount += ret.Count;
                                    watchReceived = ret.EndOfMessage;
                                }
                            }
                            else
                            {
                                WebSocketReceiveResult ret = await commentRecvTask;
                                commentRecvTask = null;
                                if (ret.MessageType != WebSocketMessageType.Text || commentCount + ret.Count >= commentBuf.Length)
                                {
                                    // 終了または処理できないフレーム。閉じる
                                    await gracefulClose();
                                }
                                else
                                {
                                    commentCount += ret.Count;
                                    commentReceived = ret.EndOfMessage;
                                }
                            }
                        }

                        if (watchReceived)
                        {
                            var message = (WatchSessionResult)jsWatchSessionResult.ReadObject(new MemoryStream(watchBuf, 0, watchCount));
                            switch (message.type)
                            {
                                case "disconnect":
                                case "reconnect":
                                    Trace.WriteLine(message.type);
                                    // とりあえず再接続要求も切断扱い
                                    await gracefulClose();
                                    break;
                                case "error":
                                    Trace.WriteLine("error");
                                    {
                                        var error = ((WatchSessionResultForError)jsWatchSessionResultForError.ReadObject(new MemoryStream(watchBuf, 0, watchCount))).data;
                                        if (error != null)
                                        {
                                            Trace.WriteLine(Encoding.UTF8.GetString(watchBuf, 0, watchCount));
                                            if (error.code == "INVALID_MESSAGE")
                                            {
                                                ResponseLines.Add("-<chat_result status=\"1\" x_refuge=\"1\" />");
                                            }
                                            else if (error.code == "COMMENT_POST_NOT_ALLOWED")
                                            {
                                                ResponseLines.Add("-<chat_result status=\"4\" x_refuge=\"1\" />");
                                            }
                                        }
                                    }
                                    break;
                                case "ping":
                                    Trace.WriteLine("ping-pong");
                                    await DoWebSocketAction(async ct => await watchSession.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
                                                            "{\"type\":\"pong\"}")), WebSocketMessageType.Text, true, ct), ct);
                                    break;
                                case "postCommentResult":
                                    Trace.WriteLine("postCommentResult");
                                    // コメント投稿に成功
                                    ResponseLines.Add("-<chat_result status=\"0\" x_refuge=\"1\" />");
                                    break;
                                case "room":
                                    Trace.WriteLine("room");
                                    if (!commentConnected)
                                    {
                                        var room = ((WatchSessionResultForRoom)jsWatchSessionResultForRoom.ReadObject(new MemoryStream(watchBuf, 0, watchCount))).data;
                                        if (room != null)
                                        {
                                            // 適当なタグをでっちあげてxmlに変換
                                            ResponseLines.Add(("-<x_room" +
                                                (room.threadId != null ? " thread_id=\"" + HtmlEncodeAmpLtGt(room.threadId, true) + "\"" : "") +
                                                (room.yourPostKey != null ? " your_post_key=\"" + HtmlEncodeAmpLtGt(room.yourPostKey, true) + "\"" : "") +
                                                " refuge=\"1\"" +
                                                " />").Replace("\n", "&#10;").Replace("\r", "&#13;"));

                                            if (room.vposBaseTime != null && vposBaseUnixTime <= TimeSpan.Zero)
                                            {
                                                DateTime d;
                                                if (DateTime.TryParse(room.vposBaseTime, CultureInfo.InvariantCulture, out d))
                                                {
                                                    vposBaseUnixTime = d.ToUniversalTime() - new DateTime(1970, 1, 1);
                                                }
                                            }

                                            if (room.threadId != null && room.messageServer != null && room.messageServer.uri != null &&
                                                room.messageServer.uri.StartsWith("wss://", StringComparison.Ordinal))
                                            {
                                                var js = new DataContractJsonSerializer(typeof(List<CommentSessionOpen>));
                                                var ms = new MemoryStream();
                                                js.WriteObject(ms, new List<CommentSessionOpen>()
                                                {
                                                    new CommentSessionOpen() { ping = new ContentContainer() { content = "rs: 0" } },
                                                    new CommentSessionOpen() { ping = new ContentContainer() { content = "ps: 0" } },
                                                    new CommentSessionOpen() { thread = new CommentSessionOpenThread()
                                                    {
                                                        thread = room.threadId,
                                                        threadkey = room.yourPostKey,
                                                        user_id = "guest",
                                                        nicoru = 0, res_from = -10, scores = 1, version = "20061206", with_global = 1
                                                    } },
                                                    new CommentSessionOpen() { ping = new ContentContainer() { content = "pf: 0" } },
                                                    new CommentSessionOpen() { ping = new ContentContainer() { content = "rf: 0" } }
                                                });
                                                // コメントセッションに接続
                                                await DoWebSocketAction(async ct => await commentSession.ConnectAsync(new Uri(room.messageServer.uri), ct), ct);
                                                await DoWebSocketAction(async ct => await commentSession.SendAsync(new ArraySegment<byte>(ms.ToArray()), WebSocketMessageType.Text, true, ct), ct);
                                                commentConnected = true;
                                                commentKeepTick = Environment.TickCount & int.MaxValue;
                                            }
                                        }
                                    }
                                    break;
                                case "seat":
                                    Trace.WriteLine("seat");
                                    {
                                        var seat = ((WatchSessionResultForSeat)jsWatchSessionResultForSeat.ReadObject(new MemoryStream(watchBuf, 0, watchCount))).data;
                                        if (seat != null)
                                        {
                                            keepSeatIntervalSec = Math.Min((int)seat.keepIntervalSec, 1000);
                                            keepSeatTick = Environment.TickCount & int.MaxValue;
                                        }
                                    }
                                    break;
                                case "serverTime":
                                    Trace.WriteLine("serverTime");
                                    {
                                        var serverTime = ((WatchSessionResultForServerTime)jsWatchSessionResultForServerTime.ReadObject(new MemoryStream(watchBuf, 0, watchCount))).data;
                                        if (serverTime != null && serverTime.currentMs != null)
                                        {
                                            DateTime d;
                                            if (DateTime.TryParse(serverTime.currentMs, CultureInfo.InvariantCulture, out d))
                                            {
                                                serverUnixTime = d.ToUniversalTime() - new DateTime(1970, 1, 1);
                                                serverUnixTimeTick = Environment.TickCount & int.MaxValue;
                                                mixingInfo.refugeServerUnixTime = serverUnixTime;
                                                mixingInfo.refugeServerUnixTimeTick = serverUnixTimeTick;
                                            }
                                        }
                                    }
                                    break;
                            }
                            watchCount = 0;
                        }

                        if (commentReceived)
                        {
                            // jsonをxmlに変換(もっと賢い方法ありそうだが属性の順序など維持したいので…)
                            var message = (CommentSessionResult)jsCommentSessionResult.ReadObject(new MemoryStream(commentBuf, 0, commentCount));
                            if (message.chat != null && serverUnixTime > TimeSpan.Zero)
                            {
                                TimeSpan at = TimeSpan.FromSeconds((long)message.chat.date) + TimeSpan.FromMicroseconds((long)message.chat.date_usec);

                                if (!wroteLiveChat && at >= serverUnixTime)
                                {
                                    if (wroteFirstChat)
                                    {
                                        // 適当なタグをでっちあげて過去のコメントの出力終了を通知
                                        ResponseLines.Add("-<x_past_chat_end refuge=\"1\" />");
                                        if (!mixingInfo.dropForwardedChat)
                                        {
                                            // インポートされたコメントにたいする通知も必要
                                            ResponseLines.Add("-<x_past_chat_end />");
                                        }
                                    }
                                    wroteFirstChat = true;
                                    wroteLiveChat = true;
                                }
                                else if (!wroteFirstChat)
                                {
                                    // 適当なタグをでっちあげて過去のコメントの出力開始を通知
                                    ResponseLines.Add("-<x_past_chat_begin refuge=\"1\" />");
                                    if (!mixingInfo.dropForwardedChat)
                                    {
                                        // インポートされたコメントにたいする通知も必要
                                        ResponseLines.Add("-<x_past_chat_begin />");
                                    }
                                    wroteFirstChat = true;
                                }
                                // 混合時は不整合を避けるため片方のサーバ時刻をdate属性値に使う
                                if (wroteLiveChat && mixingInfo.nicovideoServerUnixTime > TimeSpan.Zero)
                                {
                                    at = mixingInfo.nicovideoServerUnixTime +
                                         TimeSpan.FromMilliseconds(((Environment.TickCount & int.MaxValue) - mixingInfo.nicovideoServerUnixTimeTick) & int.MaxValue);
                                }
                                // インポートされたコメントを区別する
                                int userIdPos = message.chat.user_id == null ? -1 :
                                                message.chat.user_id.StartsWith("nicolive:", StringComparison.Ordinal) ? 9 : 0;
                                if (userIdPos != 9 || !mixingInfo.dropForwardedChat)
                                {
                                    ResponseLines.Add(("-<chat" +
                                        (message.chat.thread != null ? " thread=\"" + HtmlEncodeAmpLtGt(message.chat.thread, true) + "\"" : "") +
                                        " no=\"" + (long)message.chat.no +
                                        "\" vpos=\"" + (long)message.chat.vpos +
                                        "\" date=\"" + (long)at.TotalSeconds +
                                        "\" date_usec=\"" + (at.Milliseconds * 1000 + at.Microseconds) + "\"" +
                                        (message.chat.mail != null && message.chat.mail.Length > 0 ? " mail=\"" + HtmlEncodeAmpLtGt(message.chat.mail, true) + "\"" : "") +
                                        (message.chat.yourpost != 0 ? " yourpost=\"" + (long)message.chat.yourpost + "\"" : "") +
                                        (userIdPos >= 0 ? " user_id=\"" + HtmlEncodeAmpLtGt(message.chat.user_id.Substring(userIdPos), true) + "\"" : "") +
                                        (message.chat.premium != 0 ? " premium=\"" + (long)message.chat.premium + "\"" : "") +
                                        (message.chat.anonymity != 0 ? " anonymity=\"" + (long)message.chat.anonymity + "\"" : "") +
                                        // NX-Jikkyoは過去ログAPIの形式にする。他はさしあたりx_refuge属性をつける
                                        (userIdPos > 0 ? "" : isNxJikkyo ? " nx_jikkyo=\"1\"" : " x_refuge=\"1\"") +
                                        ">" + HtmlEncodeAmpLtGt(message.chat.content ?? "") + "</chat>").Replace("\n", "&#10;").Replace("\r", "&#13;"));
                                }
                            }
                            if (message.thread != null)
                            {
                                ResponseLines.Add(("-<thread resultcode=\"" + (long)message.thread.resultcode + "\"" +
                                    (message.thread.thread != null ? " thread=\"" + HtmlEncodeAmpLtGt(message.thread.thread, true) + "\"" : "") +
                                    " revision=\"" + (long)message.thread.revision +
                                    "\" server_time=\"" + (long)message.thread.server_time +
                                    "\" last_res=\"" + (long)message.thread.last_res + "\"" +
                                    (message.thread.ticket != null ? " ticket=\"" + HtmlEncodeAmpLtGt(message.thread.ticket, true) + "\"" : "") +
                                    " x_refuge=\"1\"" +
                                    ">" + HtmlEncodeAmpLtGt(message.thread.content ?? "") + "</thread>").Replace("\n", "&#10;").Replace("\r", "&#13;"));
                                serverUnixTime = TimeSpan.FromSeconds((long)message.thread.server_time);
                                serverUnixTimeTick = Environment.TickCount & int.MaxValue;
                                mixingInfo.refugeServerUnixTime = serverUnixTime;
                                mixingInfo.refugeServerUnixTimeTick = serverUnixTimeTick;
                            }
                            commentCount = 0;
                        }
                    }
                }
                catch (Exception e)
                {
                    ct.ThrowIfCancellationRequested();
                    Trace.WriteLine(e.ToString());
                    return "!";
                }
                finally
                {
                    // タスクをすべて回収
                    closeCts.Cancel();
                    try
                    {
                        await Task.WhenAll((new Task[] { pollingTask, watchRecvTask, commentRecvTask }).Where(a => a != null));
                    }
                    catch { }
                }
            }
            return ".";
        }

        /// <summary>.nicovideo.jpにログインする</summary>
        static async Task NicovideoLoginAsync(CancellationToken ct)
        {
            _nicovideoLoginChecked = false;
            try
            {
                await GetNicovideoLoginCookieAsync(ct);
            }
            catch (Exception e)
            {
                ct.ThrowIfCancellationRequested();
                Trace.WriteLine(e.ToString());
                ResponseLines.Add("!");
                return;
            }
            ResponseLines.Add(Settings.Instance.nicovideo_cookie != null ? "." : "!");
        }

        /// <summary>.nicovideo.jpからログアウトする</summary>
        static async Task NicovideoLogoutAsync(CancellationToken ct)
        {
            if (Settings.Instance.nicovideo_cookie != null)
            {
                try
                {
                    await HttpClientGetStringAsync("https://account.nicovideo.jp/logout?site=niconico", Settings.Instance.nicovideo_cookie, ct);
                }
                catch (Exception e)
                {
                    ct.ThrowIfCancellationRequested();
                    Trace.WriteLine(e.ToString());
                    ResponseLines.Add("!");
                    return;
                }
                Settings.Instance.nicovideo_cookie = null;
                Settings.Instance.Save();
            }
            ResponseLines.Add(".");
        }

        /// <summary>ログインしていなければ.nicovideo.jpにログインしてセッションクッキーを取得する</summary>
        static async Task<string> GetNicovideoLoginCookieAsync(CancellationToken ct)
        {
            // メソッド実装にあたり nicologin (www.axfc.netの/u/4052467) および https://github.com/tsukumijima/NDGRClient を参考にした。

            if (Settings.Instance.mail == null || Settings.Instance.password == null)
            {
                // ログイン情報が設定されていない
                return null;
            }
            if (_nicovideoLoginChecked)
            {
                // チェックを省略
                return Settings.Instance.nicovideo_cookie ?? "";
            }
            _nicovideoLoginChecked = true;

            // クッキーを取得するため
            var clientHandler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };

            using (var client = new HttpClient(clientHandler) { Timeout = TimeSpan.FromSeconds(HttpGetTimeoutSec) })
            {
                client.DefaultRequestHeaders.Add("User-Agent", Settings.Instance.useragent ?? UserAgent);
                // Add()だと値に無駄なスペースが挿入されるため
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                // ユーザーがブラウザで直接アクセスするようなコンテキスト
                client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
                client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
                if (Settings.Instance.nicovideo_cookie != null)
                {
                    client.DefaultRequestHeaders.Add("Cookie", Settings.Instance.nicovideo_cookie);
                }
                HttpResponseMessage getResponse = await client.GetAsync("https://account.nicovideo.jp/login?site=niconico", ct);
                if (getResponse.Headers.Contains("x-niconico-id"))
                {
                    // ログイン済み
                    return Settings.Instance.nicovideo_cookie;
                }
                if (Settings.Instance.nicovideo_cookie != null)
                {
                    Settings.Instance.nicovideo_cookie = null;
                    Settings.Instance.Save();
                }

                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("User-Agent", Settings.Instance.useragent ?? UserAgent);
                // Add()だと値に無駄なスペースが挿入されるため
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                // ログインページからフェッチするようなコンテキスト
                client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
                client.DefaultRequestHeaders.Add("Origin", "https://account.nicovideo.jp");
                client.DefaultRequestHeaders.Add("Referer", "https://account.nicovideo.jp/login?site=niconico");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
                client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");

                // ログイン
                var content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
                {
                    new KeyValuePair<string, string>("mail_tel", Settings.Instance.mail),
                    new KeyValuePair<string, string>("password", Settings.Instance.password)
                });
                HttpResponseMessage postResponse = await client.PostAsync("https://account.nicovideo.jp/login/redirector?site=niconico", content, ct);
                if (postResponse.IsSuccessStatusCode && postResponse.Headers.Contains("x-niconico-id"))
                {
                    // ログイン成功
                    string cookie = "";
                    foreach (Cookie item in clientHandler.CookieContainer.GetCookies(new Uri("https://live.nicovideo.jp/")))
                    {
                        if (item.Name == "nicosid" || item.Name == "user_session" || item.Name == "user_session_secure")
                        {
                            cookie += "; " + item;
                        }
                    }
                    if (cookie.Length > 0)
                    {
                        Settings.Instance.nicovideo_cookie = cookie.Substring(2);
                        Settings.Instance.Save();
                    }
                }
            }
            return Settings.Instance.nicovideo_cookie ?? "";
        }

        static async Task<string> HttpClientGetStringAsync(string requestUri, string cookie, CancellationToken ct)
        {
            HttpClientInstance.DefaultRequestHeaders.Clear();
            HttpClientInstance.DefaultRequestHeaders.ConnectionClose = true;
            HttpClientInstance.DefaultRequestHeaders.Add("User-Agent", Settings.Instance.useragent ?? UserAgent);
            // Add()だと値に無駄なスペースが挿入されるため
            HttpClientInstance.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            // ユーザーがブラウザで直接アクセスするようなコンテキスト
            HttpClientInstance.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
            HttpClientInstance.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            HttpClientInstance.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            HttpClientInstance.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            HttpClientInstance.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            if (cookie.Length > 0)
            {
                HttpClientInstance.DefaultRequestHeaders.Add("Cookie", cookie);
            }
            return await HttpClientInstance.GetStringAsync(requestUri, ct);
        }

        static string HtmlEncodeAmpLtGt(string s, bool encodeQuot = false)
        {
            s = s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            return encodeQuot ? s.Replace("\"", "&quot;") : s;
        }

        static async Task<MemoryStream> ReadProtoBufChunkAsync(Stream s, MemoryStream ms, byte[] buf, CancellationToken ct)
        {
            ms.SetLength(0);
            buf[0] = 255;
            // 可変長整数の終わりまで読む
            for (int i = 0; i < 5 && buf[0] >= 128 && await s.ReadAsync(buf, 0, 1, ct) > 0; i++)
            {
                ms.WriteByte(buf[0]);
            }
            if (buf[0] >= 128)
            {
                // 終端または値が大きすぎる
                return null;
            }
            ms.Position = 0;
            int len = ProtoBuf.ProtoReader.DirectReadVarintInt32(ms);
            if (len < 0 || len > MaxAcceptableProtoBufChunkSize)
            {
                // 値が大きすぎる
                return null;
            }
            ms.SetLength(0);
            while (len > 0)
            {
                int readLen = await s.ReadAsync(buf, 0, Math.Min(len, buf.Length), ct);
                if (readLen <= 0)
                {
                    // 終端
                    return null;
                }
                ms.Write(buf, 0, readLen);
                len -= readLen;
            }
            ms.Position = 0;
            return ms;
        }

        static async Task DoWebSocketAction(Func<CancellationToken, Task> actionAsync, CancellationToken ct)
        {
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                linkedCts.CancelAfter(WebSocketTimeoutSec * 1000);
                // タイムアウト時はここでOperationCanceledExceptionなどが飛ぶ
                // TimeoutExceptionあたりに変換したほうが分かりやすいが今のところ不都合はないのでそのまま
                await actionAsync(linkedCts.Token);
            }
        }

        static bool ParsePostComment(string comm, out string dest, out string color, out string font, out bool isAnonymous, out string position, out string size, out string text)
        {
            text = null;
            string mail = "";
            int mailEnd = comm.IndexOf(']');
            if (mailEnd > 0 && comm.FirstOrDefault() == '[')
            {
                mail = " " + comm.Substring(1, mailEnd - 1) + " ";
                // レコードセパレータ->改行
                text = comm.Substring(mailEnd + 1).Replace('\x1e', '\n');
            }
            // mail欄を解釈
            Match m = Regex.Match(mail, " (nico|refuge) ");
            dest = m.Success ? m.Groups[1].Value : null;
            m = Regex.Match(mail, " (white|red|pink|orange|yellow|green|cyan|blue|purple|black|" +
                                  "white2|niconicowhite|red2|truered|pink2|orange2|passionorange|yellow2|madyellow|green2|" +
                                  "elementalgreen|cyan2|blue2|marineblue|purple2|nobleviolet|black2|#[0-9A-Fa-f]{6}) ");
            color = m.Success ? m.Groups[1].Value : null;
            m = Regex.Match(mail, " (defont|mincho|gothic) ");
            font = m.Success ? m.Groups[1].Value : null;
            isAnonymous = Regex.IsMatch(mail, " 184 ");
            m = Regex.Match(mail, " (ue|naka|shita) ");
            position = m.Success ? m.Groups[1].Value : null;
            m = Regex.Match(mail, " (big|medium|small) ");
            size = m.Success ? m.Groups[1].Value : null;
            return text != null;
        }
    }
}
