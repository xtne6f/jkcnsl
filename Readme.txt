jkcnsl

■概要
おもにニコニコ実況のコメントを取得する非公式のコマンドラインツールです。

■使い方など
.NETアプリなのでビルドは各種のシェルでプロジェクトフォルダに移動し、
> dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true
などとしてください。
動作環境はWindowsではWindows10以降と思います。

jkcnslを起動して、
> Lch???<改行> (←???は実況の番号)
などと打ち込めば、ニコニコ実況から取得したコメントが流れます。終了は c<改行> や
q<改行> と打ち込んでください。
> R1 wss://{有志の視聴セッションのアドレス}<改行>
などと打ち込めば、有志の開設した避難所に接続できます。

ニコニコ実況にログインする場合はまず
> Smail {メールアドレス}<改行>
> Spassword {パスワード}<改行>
と打ち込んでログイン情報を設定ファイル"jkcnsl.json"に保存します。
つづいて
> Ai<改行>
と打ち込んで"."が出力されればログイン成功です("!"は失敗)。
2段階認証を設定している場合はワンタイムパスワードの入力を促されるので、ニコニコ
から送られた確認コードを
> +123456<改行>
のように打ち込んでください。

> Ao<改行>
とすればログアウトできます。

ログイン情報が設定されていれば次回のニコニコ実況への初回接続時に自動でログインが
試みられるので、ログインが不要な場合はログアウト後に
> Smail<改行>
> Spassword<改行>
と打ち込んで(mailかpasswordどちらか片方でもOK、"jkcnsl.json"の削除でもOK)ログイン
情報を削除してください。

2段階認証画面の「端末名」や「このデバイスを信頼する」はそれぞれ
> Sdevice_name 端末名<改行>
> Strust_device false<改行>
のようにして設定できます。

> S<改行>
と打ち込めば現在のすべての設定情報を出力できます。

■ライセンス
MITとします。

■ソース
https://github.com/xtne6f/jkcnsl

dwangoフォルダ以下のファイルは
https://github.com/n-air-app/nicolive-comment-protobuf/tree/bf66a84370db5785cd3685b3072ef08ae888284e
の.protoをもとにprotogen 3.2.42を使って以下のpowershellコマンドで作成しました。
> ls dwango\nicolive\chat\data\*.proto, dwango\nicolive\chat\data\atoms\*.proto, dwango\nicolive\chat\service\edge\payload.proto | Resolve-Path -Relative | %{protogen --csharp_out=. +names=original "$_"}

■謝辞
実装にあたり特に https://github.com/tsukumijima/TVRemotePlus および
https://github.com/asannou/namami を参考にしました。とりわけ変数名など多くのアイ
デアをTVRemotePlusから借用しています。

2024年以降の新方式のニコニコ実況への対応にあたり特に
https://github.com/tsukumijima/NDGRClient および
https://github.com/noriokun4649/TVTComment を参考にしました。

ログイン機能の実装にあたりnicologin (www.axfc.netの/u/4052467)を参考にしました。
