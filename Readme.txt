jkcnsl

■概要
おもにニコニコ実況のコメントを取得する非公式のコマンドラインツールです。

■注意
これは非公式のツールです。
ニコニコ実況(= https://live.nicovideo.jp/ の特定チャンネル)の仕様変更その他による不具合や不利益を被る可能性があります。
ソースファイルのみ公開しますので、各自の責任で検査しビルドしてください。

■使い方など
ビルドは.NET SDK 10がインストールされた環境でx64build.batを実行してください。
jkcnslとJkcnslLoginWindow.exeがbinフォルダ配下のpublishフォルダに生成されます。
動作環境はWindowsではWindows10以降と思います。
JkcnslLoginWindow.exeは使用しないなら無視で構いません。

Linuxでは以下のようにビルドできます(Ubuntu 24.04の例)。設定ファイルなどの既定の保存先は"/var/local/jkcnsl"です。
Windowsで"-r linux-x64"でビルドしたバイナリを持っていっても動くと思います。ARM向けは"-r linux-arm64"です。
> sudo apt install dotnet-sdk-10.0
> dotnet publish jkcnsl.csproj -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true
> sudo install ./bin/Release/net10.0/linux-x64/publish/jkcnsl /usr/local/bin
> sudo mkdir /var/local/jkcnsl
> sudo chown $USER /var/local/jkcnsl  # パーミッション等は適宜調整

jkcnslを起動して、
> Lch???<改行> (←???は実況の番号)
などと打ち込めば、ニコニコ実況から取得したコメントが流れます。終了は c<改行> や
q<改行> と打ち込んでください。
> R1 wss://{有志の視聴セッションのアドレス}<改行>
などと打ち込めば、有志の開設した避難所に接続できます。

jkcnslはタイムアウト秒数などいくつかの設定情報をjkcnsl.jsonに保存します。
> S<改行>
と打ち込めば現在のすべての設定情報を出力できます。
> Shttp_get_timeout_sec 10<改行>
などと打ち込んで設定を変更できます。設定を初期化したいときは
> Shttp_get_timeout_sec<改行>
などと打ち込んでください。

ニコニコ実況にログインする場合はJkcnslLoginWindow.exeを使用してください。
JkcnslLoginWindow.exeは専用のEdgeブラウザを開いて、同じフォルダにあるjkcnslの
nicovideo_cookie(とオプションでuseragent)設定を保存するGUIアプリです(jkcnslがな
い場合もCookieのコピーはできます)。
JkcnslLoginWindow.exeを起動してログイン・ログアウトを行い「jkcnslに保存」ボタン
で保存するのが基本的な流れです。
いくつかの起動オプションでJkcnslLoginWindow.exeの動作を調整できます。詳細は
App.xaml.csの冒頭を確認してください。

■ライセンス
MITとします。

■ソース
https://github.com/xtne6f/jkcnsl

Windows以外ではトレース出力を抑制していますが /p:AdditionalConstants=DO_NOT_SUPPRESS_TRACE をつけてビルドすると抑制解除します。

dwango,googleフォルダ以下のファイルは
https://github.com/n-air-app/nicolive-comment-protobuf/tree/871fe37c088af7e34fffd93aa7c2c309be5d90d2
https://github.com/protocolbuffers/protobuf/tree/35cd01f9fe9afbeea38cc7b979a3b6bfcde82c03
の.protoをもとにprotogen 3.2.52を使って以下のpowershellコマンドで作成しました。
> ls dwango\nicolive\chat\data\*.proto, dwango\nicolive\chat\data\atoms\*.proto, dwango\nicolive\chat\service\edge\payload.proto | Resolve-Path -Relative | %{protogen --csharp_out=. +names=original "$_"}
> ls google\protobuf\struct.proto | Resolve-Path -Relative | %{protogen --csharp_out=. +names=original "$_"}

■謝辞
実装にあたり特に https://github.com/tsukumijima/TVRemotePlus および
https://github.com/asannou/namami を参考にしました。とりわけ変数名など多くのアイ
デアをTVRemotePlusから借用しています。

2024年以降の新方式のニコニコ実況への対応にあたり特に
https://github.com/tsukumijima/NDGRClient および
https://github.com/noriokun4649/TVTComment を参考にしました。
