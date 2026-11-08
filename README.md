# DeskMeter

[![build](https://github.com/KARASAWA808/DeskMeter/actions/workflows/build.yml/badge.svg)](https://github.com/KARASAWA808/DeskMeter/actions/workflows/build.yml)

Rainmeter 風の、デスクトップ常駐リソースモニターです。Windows 10 / 11 で、インストール不要・追加ランタイム不要（.NET Framework 4.8 同梱のものを使用）の単一 exe として動きます。

![Classic スキン](previews/Classic.png)

## 表示できるもの

| パネル | 内容 |
| --- | --- |
| CPU Meter | 全体と論理プロセッサごとの使用率、実クロック、プロセス/スレッド/ハンドル数、物理コア数、L3。HWiNFO64 連携で温度・電力・ファン |
| Memory Meter | 使用中（圧縮）、使用可能、コミット、キャッシュ、ページ/非ページプール、ハードウェア予約、ページファイル、メモリ構成（使用中/変更済み/スタンバイ/空き）、種類・速度・スロット数 |
| GPU Meter | 使用率、温度、VRAM、クロック、消費電力、ファン（NVIDIA、`nvidia-smi` 経由）。NVIDIA 以外の GPU は使用率と VRAM のみ（Windows の GPU カウンター） |
| Network Meter | 上り/下り速度、ピーク、Ping（直近 60 回のグラフとロス数）、起動後と累計の通信量、内部 IP |
| Drives Meter | 容量と使用率、全体とドライブごとの読み書き速度 |
| Top CPU / Top Memory Meter | プロセス別の上位 10 件 |
| Ring Gauges | CPU / RAM / GPU / VRAM の円形ゲージ |
| 時計・カレンダー・日付タイル | 稼働時間、新年までのカウントダウン、日本の祝日（その月の祝日名も）、世界時計、タイマー。24/12 時間制、週の開始曜日を選べます |
| Weather Meter | 現在の天気と 3 日予報（Open-Meteo、API キー不要）、月齢。単位は °C/km/h、°C/m/s、°F/mph |
| Battery Meter | 残量、残り時間、AC/充電の状態、残量のグラフ（ノート PC。バッテリーの無い PC では選べません） |
| Notes | メモとカウントダウン（`settings.json` の `Notes` / `Countdowns` に書きます） |

## 見た目のカスタマイズ

- **スキン** 12 種（Classic / Cyberpunk / Retro / Light / Kawaii Pink / Kawaii Mint / Synthwave / Matrix / Nord / Dracula / Frost / Notebook）。`skins\*.json` で自作もできます（`help\custom-skin.html`）。
- **配置の編集**: 右クリック →「配置の編集」でパネルをドラッグして並べ替え。列は最大 6 つ。画面に収まらない移動は元に戻され、「配置を元に戻す」で直前の移動を取り消せます。
- **パネルの切り離し**: パネルごとに独立ウィンドウにして、別モニターにも置けます。
- **配置プリセット** と **テーマ**（スキン・配置・画像などの一式を保存・切替）。
- **透明モード**: 背景の不透明度 0% ＋ 文字の縁取りで、壁紙の上に文字だけを浮かせられます。
- **画像**: 背景画像、マスコット画像（立ち絵を横に並べる配置も可）。

| Kawaii Pink | Synthwave | Frost |
| --- | --- | --- |
| ![](previews/KawaiiPink.png) | ![](previews/Synthwave.png) | ![](previews/Frost.png) |

| 透明モード + リングゲージ | 配置プリセット「ダッシュボード」 | マスコット画像（右に並べる） |
| --- | --- | --- |
| ![](previews/transparent-rings.png) | ![](previews/preset-dashboard.png) | ![](previews/mascot-image-right.png) |

| 配置の編集 | 切り離した時計と天気 |
| --- | --- |
| ![](previews/layout-edit.png) | ![](previews/floating-clock.png) ![](previews/floating-weather.png) |

## 使い方

1. [Releases](../../releases) から zip をダウンロードして展開し、`DeskMeter.exe` を実行します。
   初回は SmartScreen の警告が出ることがあります。「詳細情報」→「実行」で起動できます。
2. 右クリックでメニューが開きます。左ドラッグで移動します。タスクトレイのアイコンからも同じメニューを開けます。初回はトレイの通知で同じ案内が出ます。画面の幅が 1400 px 未満（または高さが 800 px 未満）のときは 80% の大きさで始まります。
3. 最初に決めるもの: 天気の場所（「計測と天気」→「天気の場所を設定...」。既定は東京駅付近）、大きさ（「見た目」→「サイズ」）、出す項目（「表示する項目」。NVIDIA 以外の GPU なら GPU パネルを外すのも手です）。
3. 設定は exe と同じフォルダーの `settings.json` に保存されます（メニューの「設定ファイルを開く」で編集。保存すると自動で反映されます）。

メニューの構成:

| 項目 | 中身 |
| --- | --- |
| スキン / テーマ / 配置プリセット / 表示する項目 / 配置の編集 / パネルを切り離す | 見た目と配置の切り替え |
| 見た目 | サイズ / 背景の不透明度 / 普段は薄く表示 / 文字の影・縁取り / 画像 / 時計・カレンダー（24 時間制・月曜始まり） |
| ウィンドウ | 表示モード（デスクトップに固定・常に最前面・通常）/ モニターへ移動 / 位置を固定 / クリックを透過 / 同じモニターで最大化中のウィンドウがあれば隠す / ホットキー（表示/非表示・配置の編集・クリック透過） |
| 計測と天気 | 更新間隔 / グラフの時間幅 / 天気の場所を設定 / 天気を今すぐ更新 / 天気の単位 / ピークと起動後の通信量をリセット |
| 通知 / タイマー | トレイのバルーンのしきい値 / 5〜60 分のタイマー |
| ウィジェットを隠す / Windows 起動時に開始 | 隠したものはホットキーかトレイのダブルクリックで戻せます |
| 設定とフォルダー | 設定ファイルを開く・再読み込み / 設定一式を書き出す・読み込む / 設定を初期値に戻す / フォルダーとログを開く（CSV 記録のスイッチも） |
| ヘルプ | 各ヘルプ / 版と Releases へのリンク |

- **通知**: CPU やメモリの使用率が高い状態が続いたとき、ドライブの空きが少ないとき、温度が高いとき（CPU は HWiNFO 連携時、GPU は NVIDIA）、バッテリー残量が少ないときに、タスクトレイのバルーンで知らせます。しきい値はメニューの「通知」で選びます（既定はすべて無効）。同じ通知は解消するまで、または 30 分たつまで繰り返しません。
- **設定一式の書き出し・読み込み**: `settings.json`、テーマ、自作スキン、背景・マスコット画像を 1 つの zip にまとめます。別の PC で読み込むと同じ見た目になります（モニターの指定だけは引き継ぎません）。コマンドラインの `--export-settings` / `--import-settings` でも行えます。
- **普段は薄く表示**: 30〜70% の不透明度で薄く置いておき、マウスを載せたときだけはっきり表示します（クリック透過中はマウスが入らないので薄くしません）。
- **最大化中は隠す**: 「常に最前面」や「通常」で使うときに、同じモニターでウィンドウを最大化している間だけウィジェットを隠します（計測は続くので CSV 記録と通知は止まりません。隠れている間にホットキーやトレイで表示すると、前面のウィンドウが変わるまで表示したままにします）。

### 更新するとき

新しい zip の `DeskMeter.exe` と `help\`、`skins\` のファイルを上書きするだけです。`settings.json`、自作のスキン（`skins\` に足したもの）、`themes\` はそのまま使えます。

### アンインストール

1. メニューの「Windows 起動時に開始」のチェックを外します（レジストリ `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` の `DeskMeter` が消えます）。
2. 「終了」で閉じて、フォルダーごと削除します。

書き込む場所はこのフォルダーと上のレジストリ値だけです。このフォルダーに書き込めないとき（Program Files に置いた、ランサムウェア保護のかかったフォルダー、zip の中から直接起動した等）は、代わりに `%APPDATA%\DeskMeter` に保存し、起動時にトレイで知らせます（メニューの「フォルダーとログを開く」に保存先が出ます）。フォルダーには `settings.json`、`deskmeter.log`、壊れた設定の退避 `settings.broken-*.json`、「設定を初期値に戻す」の前のバックアップ `settings.backup-*.json`、保存したテーマ `themes\` ができます。

### ログと不具合の報告

エラーは exe と同じフォルダーの `deskmeter.log` に書かれます（メニューの「フォルダーとログを開く」から開けます）（512 KB を超えると次の書き込み時に消して作り直します。同じ失敗が続いても埋め尽くさないよう、1 時間に 200 行を超えるとしばらく記録を止めます）。設定・スキン・画像の読み込みに問題があったときはタスクトレイの通知でも知らせます。

不具合は [Issues](../../issues) へ。DeskMeter の版（メニューの「ヘルプ」の一番下に出ます。`deskmeter.log` の先頭行にも版と Windows の版が入ります）、`deskmeter.log`、できれば `settings.json`（`Latitude`/`Longitude`/`LocationName` は消して構いません）を添えてもらえると助かります。

版ごとの変更点は [CHANGELOG.md](CHANGELOG.md) にあります。新しい版は「ヘルプ」→「DeskMeter vX.Y.Z - 新しい版を確認する」で Releases ページを開いて確かめられます（自動では確認しません）。

## 動作環境

- Windows 10 / 11（64 ビット）。.NET Framework 4.8 が必要です。Windows 10 バージョン 1903 以降と Windows 11 には最初から入っています。それより古い Windows 10 では、Microsoft の .NET Framework 4.8 インストーラーを先に入れてください。
- GPU パネルの温度・クロック・消費電力・ファンは NVIDIA GPU（ドライバー付属の `nvidia-smi.exe`）が必要です。AMD / Intel の GPU では、タスクマネージャーと同じ Windows の GPU カウンター（Windows 10 1709 以降）から使用率と VRAM 使用量だけを表示します（不要なら「表示する項目」で GPU を外せます）。
- CPU 温度などは Windows 標準では取れないため、HWiNFO64 の「Shared Memory Support」を有効にしたときだけ表示されます（`help\hwinfo.html`）。
- **Smart App Control** が有効な PC では、署名のない exe は起動できません。その場合は下記の方法でその PC 上でビルドしてください。

## 外部への通信

- 天気: Open-Meteo（`api.open-meteo.com`）に設定した緯度・経度を 15 分おきに送ります。初期値は東京駅付近です。メニューの「天気の場所を設定」で「天気を取得しない」にすると通信しません。
- 場所の検索: 「天気の場所を設定」で地名を検索したときだけ、入力した地名を Open-Meteo のジオコーディング（`geocoding-api.open-meteo.com`）に送ります。
- Ping: `settings.json` の `PingHost`（既定 `1.1.1.1`）へ 5 秒おき（更新間隔を 5 秒より長くしたときはその間隔）。空文字で無効。
- 天気パネルをダブルクリックすると、設定した緯度・経度を付けた windy.com を既定のブラウザーで開きます（アプリ自身は通信しません）。
- それ以外の通信はありません。

## ビルド方法（SDK 不要）

Windows に同梱されている C# コンパイラでビルドします。Visual Studio や .NET SDK は不要です。

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

`DeskMeter.exe` がフォルダー直下にできます。アイコンを変えたいときは `make-icon.ps1` で `app.ico` を作り直してからビルドします。

## 常駐時の負荷の目安

既定（1 秒更新、100%、Classic）で 1 コアの 2〜4%（28 コア機で全体の 0.1% 程度）、メモリ約 100 MB（ソフトウェア描画、GPU デバイスを作りません）。再描画は計測が終わるたびに 1 回だけで、ウィジェットを隠している間・ロック中・画面が消えている間・フルスクリーンのアプリ（ゲームや全画面の動画）が DeskMeter のあるモニターを覆っている間（最前面モード以外。別のモニターに見えていれば止まりません）は、計測も `nvidia-smi` も止まります。

タスクマネージャーのメモリ欄は 170 MB 前後に見えますが、そのうち約 150 MB は WPF が初期化する GPU ドライバーの DLL（NVIDIA では nvgpucomp64.dll など）を他のプロセスと共有しているぶんで、このアプリ固有の消費（プライベートバイト）は 90 MB 前後です。

負荷はウィンドウの面積と描画回数に比例します。半透明ウィンドウの合成が主な内訳なので、サイズ 150% やカード付きのスキン（Nord、Frost など）、0.25〜0.5 秒更新では数倍になります（0.25 秒更新・150%・Nord で 1 コアの 10〜15% 程度）。計測が 0.5 秒より速いときは、グラフの点は計測ごとに積みつつ描画だけ 0.5 秒ごとに間引きます。バッテリー駆動中は `nvidia-smi` への問い合わせを 10 秒ごとにします。

負荷の内訳を調べたいときは、環境変数 `DESKMETER_PROFILE=1` を付けて起動すると、描画と計測にかかった時間が 30 秒ごとに `deskmeter.log` に書かれます。`DESKMETER_NO_SMI=1` を付けると `nvidia-smi` を使わず、NVIDIA 以外の環境と同じ表示になります（表示確認用）。`DESKMETER_ALLOW_TEMP=1` を付けると、`%TEMP%` の下から起動しても「zip の中から起動」とは見なしません（テスト用）。

## settings.json の項目

メニューで変えられるものがほとんどですが、`ShowIp` / `ShowForecast` / `ShowHolidays` と `Hotkey` の自由な組み合わせはファイルを直接編集します。範囲外の値は既定値に戻るか、範囲内に丸められます。読めない項目があるときは、その項目だけ既定値にして通知します。

| 項目 | 既定値 | 意味 |
| --- | --- | --- |
| `Skin` | `"Classic"` | 内蔵スキン名、または `skins\*.json` のファイル名（拡張子なし） |
| `Scale` | `1.0` | 表示倍率。0.5〜3.0 |
| `BackgroundOpacity` | `0.82` | 背景の不透明度。0〜1 |
| `TextShadow` | `"auto"` | 文字の影・縁取り。`"auto"`（不透明度 50% 未満のとき）/ `"on"` / `"off"` |
| `Mode` | `"desktop"` | `"desktop"`（デスクトップに固定）/ `"topmost"`（常に最前面）/ `"normal"` |
| `Locked` / `ClickThrough` | `false` | 位置の固定 / クリックの透過 |
| `IntervalMs` | `1000` | 更新間隔（ミリ秒）。250〜10000 |
| `HistorySeconds` | `120` | グラフの時間幅（秒）。30〜1800 |
| `EditHotkey` / `ClickThroughHotkey` | `""` | 配置の編集 / クリック透過を切り替えるホットキー（書き方は `Hotkey` と同じ） |
| `HideWhenMaximized` | `false` | 同じモニターで最大化中のウィンドウが前面の間は隠す |
| `HoverOpacity` | `100` | 普段の不透明度（%）。20〜99 にするとマウスを載せたときだけ 100% になる |
| `AlertCpuPct` / `AlertMemPct` / `AlertDiskFreePct` / `AlertTempC` / `AlertBatteryPct` / `AlertSeconds` | `0` / `0` / `0` / `0` / `0` / `60` | 通知のしきい値（0 で無効）と、CPU・メモリの「続いたら」の秒数 |
| `Clock24h` / `WeekStartMonday` | `true` / `false` | 24 時間制（`false` で 12 時間制 + AM/PM）/ カレンダーを月曜始まりにする |
| `WorldClocks` | `[]` | 時計パネルに足す世界時計。`"Windows のタイムゾーン ID|表示名"` の配列（ID は `tzutil /l` で一覧。例 `"Eastern Standard Time|New York"`） |
| `Notes` / `Countdowns` | `[]` / `[]` | Notes パネルのメモ（文字列の配列）とカウントダウン（`{"Name": "旅行", "Date": "2026-12-24"}` の配列） |
| `WeatherUnits` | `"metric"` | 天気の単位: `metric`（°C, km/h）/ `metric-ms`（°C, m/s）/ `imperial`（°F, mph） |
| `LogCsv` | `false` | 1 分ごとに CPU・メモリ・GPU・通信・ディスク・Ping・温度・バッテリーを `deskmeter-日付.csv` に追記する |
| `Hotkey` | `""` | 表示/非表示のホットキー。`"Ctrl+Alt+D"` のように `Ctrl` / `Alt` / `Shift` / `Win` とキー名（`A`〜`Z`、`0`〜`9`、`F1`〜`F12`、`Space` など）を `+` でつなぐ。空で無効 |
| `Layout` | 標準の 3 列 | 列ごとのパネル ID の配列。ID は `gpu` `cpu` `memory` `network` `drives` `topcpu` `topmem` `rings` `clock` `calendar` `datetile` `weather`。最大 6 列 |
| `CompactCores` | `false` | コアごとの使用率を 2 列に詰める |
| `Floating` / `FloatPositions` | `[]` | 切り離したパネルの ID と、その位置（モニター名と位置・大きさ、作業領域の左上からのピクセル） |
| `Monitor` / `MonitorId` / `MonitorRect` / `AnchorH` / `AnchorV` / `MonX` / `MonY` | メイン / `"right"` / `"top"` / `8` / `8` | 本体の位置。どのモニターの、どの端（`left`/`right`、`top`/`bottom`）から何ピクセルか（`MonX`/`MonY` は 0〜20000）。`MonitorId`（モニター固有の ID）と `MonitorRect`（位置と大きさ）は、モニターの番号が振り直されても同じモニターを探すためのもので、自動で書かれます |
| `BackgroundImage` / `BackgroundImageOpacity` | `""` / `0.35` | 背景画像（絶対パス、または exe のフォルダーからの相対パス）とその濃さ 0〜1 |
| `MascotImage` / `MascotPlacement` / `MascotSize` | `""` / `"free"` / `220` | マスコット画像、置き方（`free` / `right` / `left`）、最大の高さ 60〜1200 px |
| `ShowIp` | `true` | ネットワークパネルに内部 IP を出す。配信や画面共有で隠したいときは `false` |
| `PingHost` | `"1.1.1.1"` | Ping の宛先。空で無効 |
| `WeatherEnabled` | `true` | `false` で天気を取得しない |
| `Latitude` / `Longitude` / `LocationName` | `35.68` / `139.77` / `"東京都 千代田区"` | 天気の場所と表示名（`help\weather-location.html`） |
| `ShowForecast` | `true` | 天気パネルに 3 日予報を出す |
| `ShowHolidays` | `true` | カレンダーに日本の祝日を出す |
| `MemoryPanelAdded` / `Left` / `Top` | | 古い設定からの移行用。触らなくて構いません |

## コマンドライン

| 引数 | 動き |
| --- | --- |
| `--snapshot out.png [--delay 秒] [--edit]` | 描画内容を PNG に保存して終了します（既定 8 秒後。`--edit` で配置編集中の見た目）。README のプレビューはこれで撮っています（`tools\make-previews.ps1` でスキンのプレビューを撮り直せます） |
| `--reload` | 起動中の DeskMeter に設定ファイルの再読み込みを頼んで終了します |
| `--toggle-edit` | 起動中の DeskMeter の配置編集モードを切り替えて終了します |
| `--export-settings out.zip` | 設定一式（settings.json、テーマ、自作スキン、画像）を zip に書き出して終了します |
| `--import-settings in.zip` | zip の設定一式を取り込んで終了します（今の settings.json は `settings.backup-日時.json` に退避。起動中の DeskMeter は自動で読み直します） |

`--reload` と `--toggle-edit` は、起動中の DeskMeter が無いときは何もせず `deskmeter.log` に記録して終了します。

2 つ目の `DeskMeter.exe` を普通に起動すると、先に動いているものを表示して自分は終了します。

## English

DeskMeter is a Rainmeter-style always-on-desktop resource monitor for Windows 10 / 11: a single `DeskMeter.exe` with no installer and no extra runtime (it uses the .NET Framework 4.8 that ships with Windows). It shows CPU (per core, per process), memory, GPU (NVIDIA via `nvidia-smi`; other GPUs via Windows counters), network, drives, clock, calendar with Japanese holidays, and weather (Open-Meteo). It can sit behind other windows or stay on top, comes with 12 skins plus JSON custom skins, supports detached panels on other monitors, and pauses sampling while hidden, locked, the display is off, or a fullscreen app covers it. The UI and documentation are in Japanese. Download the zip from [Releases](../../releases), extract it, and run `DeskMeter.exe`; settings live in `settings.json` next to the exe. Build from source with `build.ps1` (uses the C# compiler bundled with Windows).

## ライセンス

[MIT License](LICENSE)
