param([string]$Version = "0.4.6", [switch]$Pack)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "publish"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
$google = Join-Path $root "build\google-oauth.json"
if (-not (Test-Path $google)) {
  $dl = Get-ChildItem (Join-Path $env:USERPROFILE "Downloads") -Filter "client_secret_*.apps.googleusercontent.com*.json" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($dl) { Copy-Item $dl.FullName $google -Force }
}
if (-not (Test-Path $google)) {
  $cfgPath = Join-Path $env:APPDATA "kyzWheel\config.json"
  if (Test-Path $cfgPath) {
    $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
    if ($cfg.GoogleClientId -and $cfg.GoogleClientSecret) {
      @{ installed = @{ client_id = $cfg.GoogleClientId; client_secret = $cfg.GoogleClientSecret } } | ConvertTo-Json | Set-Content $google -Encoding UTF8
    }
  }
}
if (Test-Path $google) { Copy-Item $google (Join-Path $root "src\KyzWheel.App\Assets\google.json") -Force; Write-Host "google client bundled" } else { Write-Host "google client NOT bundled" }
dotnet publish "$root\src\KyzWheel.App\KyzWheel.App.csproj" -c Release -r win-x64 --self-contained -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish "$root\src\KyzWheel.Guard\KyzWheel.Guard.csproj" -c Release -r win-x64 --self-contained -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$wa = Join-Path $root "sidecar\whatsapp"
Push-Location $wa
if (-not (Test-Path "node_modules")) { npm ci --no-audit --no-fund }
node build.mjs
Pop-Location
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item (Join-Path $wa "dist\wa-sidecar.exe") $out -Force
if ($Pack) {
  $vpk = if (Get-Command vpk -ErrorAction SilentlyContinue) { "vpk" } else { Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe" }
  & $vpk pack -u kyzWheel -v $Version -p $out -e KyzWheel.Guard.exe -i "$root\src\KyzWheel.App\Assets\kyzWheel.ico" --packTitle kyzWheel --packAuthors yokyznt --shortcuts "Desktop,StartMenuRoot" -r win-x64 -o "$root\Releases"
}
