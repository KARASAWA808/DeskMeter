# README 用のスキンのプレビュー (previews\<スキン名>.png) を撮り直す。
# 作業用のフォルダーに exe・skins・help を複製して動かすので、手元の settings.json は触らない。
#   powershell -ExecutionPolicy Bypass -File tools\make-previews.ps1            # 組み込み 12 種 + skins\example-*.json
#   powershell -ExecutionPolicy Bypass -File tools\make-previews.ps1 Nord Frost # 指定したものだけ
# 透明モードや配置プリセット、マスコット画像、切り離しなどの特別なプレビューは撮り直さない (そのときの設定が要るため)。
param([string[]]$Skins)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'DeskMeter.exe'
if (-not (Test-Path $exe)) { throw "先に build.ps1 でビルドしてください: $exe" }
$stage = Join-Path $env:TEMP 'dm-previews'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $exe $stage
Copy-Item (Join-Path $root 'skins') (Join-Path $stage 'skins') -Recurse
Copy-Item (Join-Path $root 'help') (Join-Path $stage 'help') -Recurse

if (-not $Skins) {
    $Skins = @('Classic', 'Cyberpunk', 'Dracula', 'Frost', 'KawaiiMint', 'KawaiiPink', 'Light', 'Matrix', 'Nord', 'Notebook', 'Retro', 'Synthwave')
    $Skins += Get-ChildItem (Join-Path $root 'skins') -Filter 'example-*.json' | ForEach-Object { $_.BaseName }
}
$utf8 = New-Object Text.UTF8Encoding($false)
$outDir = Join-Path $root 'previews'
foreach ($skin in $Skins) {
    # 既定の配置で、IP だけ隠す (公開する画像なので)
    [IO.File]::WriteAllText((Join-Path $stage 'settings.json'), ('{ "Skin": "' + $skin + '", "Mode": "normal", "ShowIp": false }'), $utf8)
    Remove-Item (Join-Path $stage 'deskmeter.log') -ErrorAction SilentlyContinue
    $out = Join-Path $outDir ($skin + '.png')
    Start-Process -FilePath (Join-Path $stage 'DeskMeter.exe') -ArgumentList @('--snapshot', $out, '--delay', '8') -WorkingDirectory $stage -Wait
    if (-not (Test-Path $out)) { throw "撮れませんでした: $skin" }
    Write-Host ("{0,-24} {1,8:n0} bytes" -f $skin, (Get-Item $out).Length)
    if (Test-Path (Join-Path $stage 'deskmeter.log')) { Get-Content (Join-Path $stage 'deskmeter.log') -Encoding UTF8 | ForEach-Object { Write-Host "   $_" } }
}
Write-Host "done: $outDir"
