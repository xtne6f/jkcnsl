jkcnsl

■概要
おもにニコニコ実況のコメントを取得する非公式のコマンドラインツールです。

■使い方など
.NETアプリなのでビルドは各種のシェルでプロジェクトフォルダに移動し、
> dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true
などとしてください。
動作環境はWindowsではWindows10以降と思います。
2024年7月時点の.NET 8では /p:PublishTrimmed=true をつけるとビルドはできるが正常
動作しないバイナリができるので、その場合はこのオプションを取り除いてください。

jkcnslを起動して、
> Lch???<改行> (←???は実況の番号)
などと打ち込めば、取得したコメントが流れます。終了は c<改行> や q<改行> と打ち込
んでください。
> R1 wss://{有志の視聴セッションのアドレス}<改行>
などと打ち込めば、有志の開設した避難所に接続できます。

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
