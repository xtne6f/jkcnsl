jkcnsl

■概要
おもにニコニコ実況のコメントを取得する非公式のコマンドラインツールです。

■使い方など
.NET Coreアプリなので(FrameworkでもビルドできますがClientWebSocketに決定的なバグ
があり使えません)、ビルドは各種のシェルでプロジェクトフォルダに移動し、
> dotnet publish -c Release -r win10-x86 /p:PublishSingleFile=true /p:PublishTrimmed=true
などとしてください。
動作環境はWindowsではWindows7以降と思います。
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

■謝辞
実装にあたり特に https://github.com/tsukumijima/TVRemotePlus および
https://github.com/asannou/namami を参考にしました。とりわけ変数名など多くのアイ
デアをTVRemotePlusから借用しています。
