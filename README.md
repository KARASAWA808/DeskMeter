# DeskMeter

Rainmeter 風の、デスクトップ常駐リソースモニターです。Windows 10 / 11 で、インストール不要・追加ランタイム不要（.NET Framework 4.8 同梱のものを使用）の単一 exe として動きます。

![Classic スキン](previews/Classic.png)

## 表示できるもの

| パネル | 内容 |
| --- | --- |
| CPU Meter | 全体と論理プロセッサごとの使用率、実クロック、プロセス/スレッド/ハンドル数、物理コア数、L3。HWiNFO64 連携で温度・電力・ファン |
| Memory Meter | 使用中（圧縮）、使用可能、コミット、キャッシュ、ページ/非ページプール、ハードウェア予約、ページファイル、メモリ構成（使用中/変更済み/スタンバイ/空き）、種類・速度・スロット数 |
| GPU Meter | 使用率、温度、VRAM、クロック、消費電力、ファン（NVIDIA、`nvidia-smi` 経由） |
| Network Meter | 上り/下り速度、ピーク、Ping、起動後と累計の通信量、内部 IP |
| Drives Meter | 容量と使用率、読み書き速度 |
| Top CPU / Top Memory Meter | プロセス別の上位 10 件 |
| Ring Gauges | CPU / RAM / GPU / VRAM の円形ゲージ |
| 時計・カレンダー・日付タイル | 稼働時間、新年までのカウントダウン、日本の祝日 |
| Weather Meter | 現在の天気と 3 日予報（Open-Meteo、API キー不要）、月齢 |

## 見た目のカスタマイズ

- **スキン** 12 種（Classic / Cyberpunk / Retro / Light / Kawaii Pink / Kawaii Mint / Synthwave / Matrix / Nord / Dracula / Frost / Notebook）。`skins\*.json` で自作もできます（`help\custom-skin.html`）。
- **配置の編集**: 右クリック →「配置の編集」でパネルをドラッグして並べ替え。列は最大 6 つ。
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

プレビューの撮り方: `DeskMeter.exe --snapshot out.png --delay 6`（`--edit` を付けると配置編集中の見た目）。設定は exe と同じフォルダーの `settings.json` を使います。

## 使い方

1. [Releases](../../releases) から zip をダウンロードして展開し、`DeskMeter.exe` を実行します。
   初回は SmartScreen の警告が出ることがあります。「詳細情報」→「実行」で起動できます。
2. 右クリックでメニューが開きます。左ドラッグで移動します。タスクトレイのアイコンからも同じメニューを開けます。
3. 設定は exe と同じフォルダーの `settings.json` に保存されます（メニューの「設定ファイルを開く」で編集。保存すると自動で反映されます）。

主なメニュー: スキン / 表示モード（デスクトップに固定・常に最前面・通常）/ モニターへ移動 / サイズ / 背景の不透明度 / 更新間隔 / グラフの時間幅 / 表示する項目 / 配置の編集 / パネルを切り離す / 配置プリセット / テーマ / 画像 / ホットキー / Windows 起動時に開始 / ヘルプ

## 動作環境

- Windows 10 / 11（64 ビット）
- GPU パネルは NVIDIA GPU（ドライバー付属の `nvidia-smi.exe`）が必要です。無い環境では「取得できません」と表示されるだけで他は動きます。
- CPU 温度などは Windows 標準では取れないため、HWiNFO64 の「Shared Memory Support」を有効にしたときだけ表示されます（`help\hwinfo.html`）。
- **Smart App Control** が有効な PC では、署名のない exe は起動できません。その場合は下記の方法でその PC 上でビルドしてください。

## 外部への通信

- 天気: Open-Meteo（`api.open-meteo.com`）に設定した緯度・経度を送ります。初期値は東京駅付近です。`settings.json` の `WeatherEnabled` を `false` にすると通信しません。
- 場所の検索: メニューの「天気の場所を設定」で地名を検索したときだけ、入力した地名を Open-Meteo のジオコーディング（`geocoding-api.open-meteo.com`）に送ります。
- Ping: `settings.json` の `PingHost`（既定 `1.1.1.1`）へ 5 秒おき。空文字で無効。
- 天気パネルをダブルクリックすると、設定した緯度・経度を付けた windy.com を既定のブラウザーで開きます（アプリ自身は通信しません）。
- それ以外の通信はありません。

## ビルド方法（SDK 不要）

Windows に同梱されている C# コンパイラでビルドします。Visual Studio や .NET SDK は不要です。

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

`DeskMeter.exe` がフォルダー直下にできます。アイコンを変えたいときは `make-icon.ps1` で `app.ico` を作り直してからビルドします。

## 常駐時の負荷の目安

1 秒更新で 1 コアの 2% 前後（28 コア機で全体の 0.1% 程度）、メモリ約 100 MB（ソフトウェア描画、GPU デバイスを作りません）。再描画は計測が終わるたびに 1 回だけで、ウィジェットを隠している間とロック中は計測も `nvidia-smi` も止まります。

負荷の内訳を調べたいときは、環境変数 `DESKMETER_PROFILE=1` を付けて起動すると、描画と計測にかかった時間が 30 秒ごとに `deskmeter.log` に書かれます。

## ライセンス

[MIT License](LICENSE)
