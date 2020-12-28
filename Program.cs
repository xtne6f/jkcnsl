using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
//using System.Net.Sockets;
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

        // HttpClientは使いまわす。クッキーは共有しない
        static HttpClient _httpClientInstance;
        static HttpClient HttpClientInstance
        {
            get
            {
                if (_httpClientInstance == null)
                {
                    _httpClientInstance = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(8) };
                }
                return _httpClientInstance;
            }
        }

        static BlockingCollection<string> ResponseLines = new BlockingCollection<string>();

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
                            case 'G':
                                {
                                    string[] arg = comm.Substring(1).Split(new char[] { ' ' }, 2);
                                    if (arg.Length < 1)
                                    {
                                        ResponseLines.Add("!");
                                        break;
                                    }
                                    await GetHttpGetStringAsync(arg[0], arg.Length >= 2 ? arg[1] : "", quitCts.Token);
                                }
                                break;
                            case 'L':
                                {
                                    string[] arg = comm.Substring(1).Split(new char[] { ' ' }, 2);
                                    if (arg.Length < 1)
                                    {
                                        ResponseLines.Add("!");
                                        break;
                                    }
                                    await GetStreamAsync(arg[0], arg.Length >= 2 ? arg[1] : "", commands, quitCts.Token);
                                }
                                break;
                            /********* 旧実況テストコードここから **********
                            case 'H':
                                {
                                    string[] arg = comm.Substring(1).Split(new char[] { ' ' }, 2);
                                    if (arg.Length < 1)
                                    {
                                        ResponseLines.Add("!");
                                        break;
                                    }
                                    await GetChannelsV2Async(arg[0], arg.Length >= 2 ? arg[1] : "", quitCts.Token);
                                }
                                break;
                            case 'F':
                                {
                                    string[] arg = comm.Substring(1).Split(new char[] { ' ' }, 3);
                                    if (arg.Length < 2)
                                    {
                                        ResponseLines.Add("!");
                                        break;
                                    }
                                    await GetPermalinkV2Async(arg[0], arg[1], arg.Length >= 3 ? arg[2] : "", quitCts.Token);
                                }
                                break;
                            case 'P':
                                {
                                    string[] arg = comm.Substring(1).Split(new char[] { ' ' }, 4);
                                    if (arg.Length < 3)
                                    {
                                        ResponseLines.Add("!");
                                        break;
                                    }
                                    await GetPostkeyV2Async(arg[0], arg[1], arg[2], arg.Length >= 4 ? arg[3] : "", quitCts.Token);
                                }
                                break;
                            case 'S':
                                {
                                    string[] arg = comm.Substring(1).Split(new char[] { ' ' }, 3);
                                    int port;
                                    if (arg.Length < 3 || !int.TryParse(arg[1], out port))
                                    {
                                        ResponseLines.Add("!");
                                        break;
                                    }
                                    await GetStreamOldAsync(arg[0], port, arg[2], commands, quitCts.Token);
                                }
                                break;
                            ********** 旧実況テストコードここまで *********/
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

        /// <summary>実況ストリーム(.nicovideo.jp)</summary>
        static async Task GetStreamAsync(string lvId, string cookie, BlockingCollection<string> commands, CancellationToken ct)
        {
            // メソッド実装にあたり特に https://github.com/tsukumijima/TVRemotePlus および https://github.com/asannou/namami を参考にした。

            WatchEmbedded embedded = null;
            if (Regex.IsMatch(lvId, "^(?:ch|lv)[0-9]+$"))
            {
                // 視聴セッション情報を取得
                try
                {
                    string ret = await HttpClientGetStringAsync("https://live2.nicovideo.jp/watch/" + lvId, cookie, ct);
                    Match match = Regex.Match(ret, "<script(?= )([^>]*? id=\"embedded-data\"[^>]*)>");
                    if (match.Success)
                    {
                        match = Regex.Match(match.Groups[1].Value, " data-props=\"([^\"]*)\"");
                        if (match.Success)
                        {
                            var js = new DataContractJsonSerializer(typeof(WatchEmbedded));
                            var _embedded = (WatchEmbedded)js.ReadObject(new MemoryStream(Encoding.UTF8.GetBytes(HttpUtility.HtmlDecode(match.Groups[1].Value))));
                            // 一応ドメインを検査しておく(スクレイピングなので)
                            if (_embedded.site != null && _embedded.site.relive != null &&
                                Regex.IsMatch(_embedded.site.relive.webSocketUrl ?? "", @"^wss://[0-9A-Za-z.-]+\.nicovideo\.jp/"))
                            {
                                embedded = _embedded;
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
            if (embedded == null)
            {
                ResponseLines.Add("!");
                return;
            }

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
                    // TODO: Frameworkはここで例外になる("error"が返るのでUAは必須)
                    watchSession.Options.SetRequestHeader("User-Agent", UserAgent);
                    commentSession.Options.SetRequestHeader("User-Agent", UserAgent);
                    commentSession.Options.AddSubProtocol("msg.nicovideo.jp#json");
                    // 視聴セッションに接続
                    await watchSession.ConnectAsync(new Uri(embedded.site.relive.webSocketUrl), ct);
                    await watchSession.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
                        "{\"type\":\"startWatching\",\"data\":{\"stream\":{\"quality\":\"super_high\",\"protocol\":\"hls\",\"latency\":\"low\",\"chasePlay\":false}," +
                        "\"room\":{\"protocol\":\"webSocket\",\"commentable\":true},\"reconnect\":false}}")), WebSocketMessageType.Text, true, ct);

                    int keepSeatIntervalSec = 0;
                    int keepSeatTick = 0;
                    int commentKeepTick = 0;
                    var watchBuf = new byte[MaxAcceptableWebSocketPayloadSize];
                    int watchCount = 0;
                    var commentBuf = new byte[MaxAcceptableWebSocketPayloadSize];
                    int commentCount = 0;
                    bool commentConnected = false;
                    long serverUnixTime = 0;
                    int serverUnixTimeTick = 0;

                    var jsWatchSessionPost = new DataContractJsonSerializer(typeof(WatchSessionPost));
                    var jsWatchSessionResult = new DataContractJsonSerializer(typeof(WatchSessionResult));
                    var jsWatchSessionResultForError = new DataContractJsonSerializer(typeof(WatchSessionResultForError));
                    var jsWatchSessionResultForRoom = new DataContractJsonSerializer(typeof(WatchSessionResultForRoom));
                    var jsWatchSessionResultForSeat = new DataContractJsonSerializer(typeof(WatchSessionResultForSeat));
                    var jsCommentSessionResult = new DataContractJsonSerializer(typeof(CommentSessionResult));

                    for (; ; )
                    {
                        bool closed = false;
                        bool watchReceived = false;
                        bool commentReceived = false;
                        for (; ; )
                        {
                            ct.ThrowIfCancellationRequested();
                            int keepSeatElapsed = ((Environment.TickCount & int.MaxValue) - keepSeatTick) & int.MaxValue;
                            int commentKeepElapsed = ((Environment.TickCount & int.MaxValue) - commentKeepTick) & int.MaxValue;
                            if (keepSeatIntervalSec > 0 && keepSeatElapsed > keepSeatIntervalSec * 1000)
                            {
                                // 座席を維持
                                Trace.WriteLine("keepSeat");
                                await watchSession.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"keepSeat\"}")), WebSocketMessageType.Text, true, ct);
                                keepSeatTick = Environment.TickCount & int.MaxValue;
                            }
                            else if (commentConnected && commentKeepElapsed > 60000)
                            {
                                // 60秒ごとに接続を維持
                                Trace.WriteLine("commentKeep");
                                await commentSession.SendAsync(new ArraySegment<byte>(new byte[0]), WebSocketMessageType.Text, true, ct);
                                commentKeepTick = Environment.TickCount & int.MaxValue;
                            }

                            pollingTask = pollingTask ?? Task.Delay(TimeSpan.FromMilliseconds(200), linkedCts.Token);
                            watchRecvTask = watchRecvTask ?? watchSession.ReceiveAsync(new ArraySegment<byte>(watchBuf, watchCount, watchBuf.Length - watchCount), linkedCts.Token);
                            if (commentConnected)
                            {
                                commentRecvTask = commentRecvTask ?? commentSession.ReceiveAsync(new ArraySegment<byte>(commentBuf, commentCount, commentBuf.Length - commentCount), linkedCts.Token);
                            }
                            Task completedTask = await Task.WhenAny((new Task[] { pollingTask, watchRecvTask, commentRecvTask }).Where(a => a != null));
                            if (completedTask == pollingTask)
                            {
                                // 定期的に入力をチェック
                                pollingTask = null;
                                string comm;
                                while (commands.TryTake(out comm))
                                {
                                    if (comm.FirstOrDefault() == 'c')
                                    {
                                        // 閉じる
                                        if (commentRecvTask != null)
                                        {
                                            await commentSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct);
                                        }
                                        await watchSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct);
                                        closed = true;
                                        break;
                                    }
                                    if (comm.StartsWith("+[", StringComparison.Ordinal))
                                    {
                                        // コメント投稿
                                        string[] posts = comm.Substring(2).Split(new char[] { ']' }, 2);
                                        if (posts.Length == 2 &&
                                            embedded.program != null &&
                                            (long)embedded.program.vposBaseTime > 0 &&
                                            serverUnixTime >= (long)embedded.program.vposBaseTime)
                                        {
                                            // コメント欄を解釈
                                            string mail = " " + posts[0] + " ";
                                            Match matchColor = Regex.Match(mail, " (white|red|pink|orange|yellow|green|cyan|blue|purple|black|" +
                                                "white2|niconicowhite|red2|truered|pink2|orange2|passionorange|yellow2|madyellow|green2|" +
                                                "elementalgreen|cyan2|blue2|marineblue|purple2|nobleviolet|black2) ");
                                            bool isAnonymous = Regex.IsMatch(mail, " 184 ");
                                            Match matchPosition = Regex.Match(mail, " (ue|naka|shita) ");
                                            Match matchSize = Regex.Match(mail, " (big|medium|small) ");

                                            // レコードセパレータ->改行
                                            string text = posts[1].Replace('\x1e', '\n');
                                            // vposは10msec単位。内部時計のずれに影響されないようにサーバ時刻を基準に補正
                                            int vpos = (int)(serverUnixTime - (long)embedded.program.vposBaseTime) * 100 + (((Environment.TickCount & int.MaxValue) - serverUnixTimeTick) & int.MaxValue) / 10;
                                            var ms = new MemoryStream();
                                            jsWatchSessionPost.WriteObject(ms, new WatchSessionPost()
                                            {
                                                data = new WatchSessionPostData()
                                                {
                                                    color = matchColor.Success ? matchColor.Groups[1].Value : null,
                                                    isAnonymous = isAnonymous,
                                                    position = matchPosition.Success ? matchPosition.Groups[1].Value : null,
                                                    size = matchSize.Success ? matchSize.Groups[1].Value : null,
                                                    text = text,
                                                    vpos = vpos
                                                },
                                                type = "postComment"
                                            });
                                            byte[] post = ms.ToArray();
                                            Trace.WriteLine(Encoding.UTF8.GetString(post));
                                            await watchSession.SendAsync(new ArraySegment<byte>(post), WebSocketMessageType.Text, true, ct);
                                        }
                                        else
                                        {
                                            // 投稿拒否
                                            ResponseLines.Add("-<chat_result status=\"1\" />");
                                        }
                                    }
                                }
                                if (closed)
                                {
                                    break;
                                }
                            }
                            else if (completedTask == watchRecvTask)
                            {
                                WebSocketReceiveResult ret = watchRecvTask.Result;
                                watchRecvTask = null;
                                if (ret.MessageType != WebSocketMessageType.Text || watchCount + ret.Count >= watchBuf.Length)
                                {
                                    // 終了または処理できないフレーム。閉じる
                                    if (commentRecvTask != null)
                                    {
                                        await commentSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct);
                                    }
                                    await watchSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct);
                                    closed = true;
                                    break;
                                }
                                watchCount += ret.Count;
                                if (ret.EndOfMessage)
                                {
                                    watchReceived = true;
                                    break;
                                }
                            }
                            else
                            {
                                WebSocketReceiveResult ret = commentRecvTask.Result;
                                commentRecvTask = null;
                                if (ret.MessageType != WebSocketMessageType.Text || commentCount + ret.Count >= commentBuf.Length)
                                {
                                    // 終了または処理できないフレーム。閉じる
                                    await commentSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct);
                                    await watchSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct);
                                    closed = true;
                                    break;
                                }
                                commentCount += ret.Count;
                                if (ret.EndOfMessage)
                                {
                                    commentReceived = true;
                                    break;
                                }
                            }
                        }

                        if (closed)
                        {
                            break;
                        }

                        if (watchReceived)
                        {
                            var message = (WatchSessionResult)jsWatchSessionResult.ReadObject(new MemoryStream(watchBuf, 0, watchCount));
                            switch (message.type)
                            {
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
                                case "ping":
                                    Trace.WriteLine("ping-pong");
                                    await watchSession.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"pong\"}")), WebSocketMessageType.Text, true, ct);
                                    break;
                                case "postCommentResult":
                                    Trace.WriteLine("postCommentResult");
                                    // コメント投稿に成功
                                    ResponseLines.Add("-<chat_result status=\"0\" />");
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
                                                (embedded.user != null && embedded.user.id != null ? " user_id=\"" + HtmlEncodeAmpLtGt(embedded.user.id, true) + "\"" : "") +
                                                (embedded.user != null && embedded.user.nickname != null ? " nickname=\"" + HtmlEncodeAmpLtGt(embedded.user.nickname, true) + "\"" : "") +
                                                (embedded.user != null && embedded.user.isLoggedIn ? " is_logged_in=\"1\"" : "") + " />").Replace("\n", "&#10;").Replace("\r", "&#13;"));

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
                                                        user_id = embedded.user != null && embedded.user.id != null ? embedded.user.id : "guest",
                                                        nicoru = 0, res_from = -10, scores = 1, version = "20061206", with_global = 1
                                                    } },
                                                    new CommentSessionOpen() { ping = new ContentContainer() { content = "pf: 0" } },
                                                    new CommentSessionOpen() { ping = new ContentContainer() { content = "rf: 0" } }
                                                });
                                                // コメントセッションに接続
                                                await commentSession.ConnectAsync(new Uri(room.messageServer.uri), ct);
                                                await commentSession.SendAsync(new ArraySegment<byte>(ms.ToArray()), WebSocketMessageType.Text, true, ct);
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
                            }
                            watchCount = 0;
                            watchReceived = false;
                        }

                        if (commentReceived)
                        {
                            // jsonをxmlに変換(もっと賢い方法ありそうだが属性の順序など維持したいので…)
                            var message = (CommentSessionResult)jsCommentSessionResult.ReadObject(new MemoryStream(commentBuf, 0, commentCount));
                            if (message.chat != null)
                            {
                                ResponseLines.Add(("-<chat" +
                                    (message.chat.thread != null ? " thread=\"" + HtmlEncodeAmpLtGt(message.chat.thread, true) + "\"" : "") +
                                    " no=\"" + (long)message.chat.no +
                                    "\" vpos=\"" + (long)message.chat.vpos +
                                    "\" date=\"" + (long)message.chat.date +
                                    "\" date_usec=\"" + (long)message.chat.date_usec + "\"" +
                                    (message.chat.mail != null ? " mail=\"" + HtmlEncodeAmpLtGt(message.chat.mail, true) + "\"" : "") +
                                    (message.chat.yourpost != 0 ? " yourpost=\"" + (long)message.chat.yourpost + "\"" : "") +
                                    (message.chat.user_id != null ? " user_id=\"" + HtmlEncodeAmpLtGt(message.chat.user_id, true) + "\"" : "") +
                                    (message.chat.premium != 0 ? " premium=\"" + (long)message.chat.premium + "\"" : "") +
                                    (message.chat.anonymity != 0 ? " anonymity=\"" + (long)message.chat.anonymity + "\"" : "") +
                                    ">" + HtmlEncodeAmpLtGt(message.chat.content ?? "") + "</chat>").Replace("\n", "&#10;").Replace("\r", "&#13;"));
                            }
                            if (message.ping != null)
                            {
                                ResponseLines.Add(("-<ping>" + HtmlEncodeAmpLtGt(message.ping.content ?? "") + "</ping>").Replace("\n", "&#10;").Replace("\r", "&#13;"));
                            }
                            if (message.thread != null)
                            {
                                serverUnixTime = (long)message.thread.server_time;
                                serverUnixTimeTick = Environment.TickCount & int.MaxValue;
                                ResponseLines.Add(("-<thread resultcode=\"" + (long)message.thread.resultcode + "\"" +
                                    (message.thread.thread != null ? " thread=\"" + HtmlEncodeAmpLtGt(message.thread.thread, true) + "\"" : "") +
                                    " revision=\"" + (long)message.thread.revision +
                                    "\" server_time=\"" + serverUnixTime +
                                    "\" last_res=\"" + (long)message.thread.last_res + "\"" +
                                    (message.thread.ticket != null ? " ticket=\"" + HtmlEncodeAmpLtGt(message.thread.ticket, true) + "\"" : "") +
                                    ">" + HtmlEncodeAmpLtGt(message.thread.content ?? "") + "</thread>").Replace("\n", "&#10;").Replace("\r", "&#13;"));
                            }
                            commentCount = 0;
                            commentReceived = false;
                        }
                    }
                }
                catch (Exception e)
                {
                    ct.ThrowIfCancellationRequested();
                    Trace.WriteLine(e.ToString());
                    ResponseLines.Add("!");
                    return;
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
            ResponseLines.Add(".");
        }

        static async Task<string> HttpClientGetStringAsync(string requestUri, string cookie, CancellationToken ct)
        {
            HttpClientInstance.DefaultRequestHeaders.Clear();
            HttpClientInstance.DefaultRequestHeaders.ConnectionClose = true;
            HttpClientInstance.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            if (cookie.Length > 0)
            {
                HttpClientInstance.DefaultRequestHeaders.Add("Cookie", cookie);
            }
            Task<string> task = HttpClientInstance.GetStringAsync(requestUri);
            using (var delayCts = new CancellationTokenSource())
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, delayCts.Token))
            {
                Task delayTask = Task.Delay(-1, linkedCts.Token);
                try
                {
                    // FrameworkのGetStringAsync()はキャンセルトークンを渡せないため
                    await Task.WhenAny(task, delayTask);
                    ct.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    // taskを回収
                    HttpClientInstance.CancelPendingRequests();
                    await task;
                    throw;
                }
                finally
                {
                    // delayTaskを回収
                    try
                    {
                        delayCts.Cancel();
                        await delayTask;
                    }
                    catch { }
                }
            }
            return task.Result;
        }

        static string HtmlEncodeAmpLtGt(string s, bool encodeQuot = false)
        {
            s = s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            return encodeQuot ? s.Replace("\"", "&quot;") : s;
        }

        /********* 旧実況テストコードここから **********

        const int TcpClientSendTimeoutMsec = 8000;

        /// <summary>チャンネルリスト(v2)</summary>
        static async Task GetChannelsV2Async(string host, string cookie, CancellationToken ct)
        {
            string ret;
            try
            {
                ret = await HttpClientGetStringAsync("http://" + host + "/api/v2_app/getchannels", cookie, ct);
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

        /// <summary>パーマリンク(v2)</summary>
        static async Task GetPermalinkV2Async(string jkId, string host, string cookie, CancellationToken ct)
        {
            string ret;
            try
            {
                ret = await HttpClientGetStringAsync("http://" + host + "/api/v2/getflv?v=" + jkId, cookie, ct);
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

        /// <summary>ポストキー(v2)</summary>
        static async Task GetPostkeyV2Async(string threadId, string blockNo, string host, string cookie, CancellationToken ct)
        {
            string ret;
            try
            {
                ret = await HttpClientGetStringAsync("http://" + host + "/api/v2/getpostkey?thread=" + threadId + "&block_no=" + blockNo, cookie, ct);
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

        /// <summary>実況ストリーム(旧)</summary>
        static async Task GetStreamOldAsync(string ip, int port, string request, BlockingCollection<string> commands, CancellationToken ct)
        {
            using (var client = new TcpClient() { SendTimeout = TcpClientSendTimeoutMsec })
            {
                try
                {
                    client.Connect(ip, port);
                }
                catch
                {
                    ResponseLines.Add("!");
                    return;
                }
                ct.ThrowIfCancellationRequested();

                var sw = new StreamWriter(client.GetStream());
                var sr = new StreamReader(client.GetStream());
                try
                {
                    sw.Write(request.Replace("&#10;", "\n").Replace("&#13;", "\r") + "\0");
                    sw.Flush();
                }
                catch
                {
                    ResponseLines.Add("!");
                    return;
                }
                ct.ThrowIfCancellationRequested();

                Task<int> recvTask = null;
                var recvBuf = new char[8192];
                string ret = "";
                for (; ; )
                {
                    bool recvCompleted;
                    try
                    {
                        string comm;
                        if (commands.TryTake(out comm))
                        {
                            if (comm.FirstOrDefault() == 'c')
                            {
                                break;
                            }
                            if (comm.FirstOrDefault() == '+')
                            {
                                await sw.WriteAsync(comm.Substring(1).Replace("&#10;", "\n").Replace("&#13;", "\r") + "\0");
                                await sw.FlushAsync();
                            }
                        }
                        if (recvTask == null)
                        {
                            recvTask = sr.ReadAsync(recvBuf, 0, recvBuf.Length);
                        }
                        recvCompleted = await Task.WhenAny(recvTask, Task.Delay(10)) == recvTask;
                    }
                    catch
                    {
                        break;
                    }
                    ct.ThrowIfCancellationRequested();

                    if (recvCompleted)
                    {
                        if (recvTask.Result == 0)
                        {
                            break;
                        }
                        int j = 0;
                        for (int i = 0; i < recvTask.Result; i++)
                        {
                            if (recvBuf[i] == '\0')
                            {
                                ret += new string(recvBuf, j, i - j);
                                ResponseLines.Add("-" + ret.Replace("\n", "&#10;").Replace("\r", "&#13;"));
                                ret = "";
                                j = i + 1;
                            }
                        }
                        ret += new string(recvBuf, j, recvTask.Result - j);
                        recvTask = null;
                    }
                }
            }
            ResponseLines.Add(".");
        }

        ********** 旧実況テストコードここまで *********/
    }
}
