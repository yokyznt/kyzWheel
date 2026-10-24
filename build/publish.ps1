param([string]$Version = "0.4.0", [switch]$Pack)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "publish"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
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
