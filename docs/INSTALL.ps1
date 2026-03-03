Param(
    [switch]$BuildRelease
)

$ErrorActionPreference = "Stop"

# Always execute from repository root, even if user launches script from System32.
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
Set-Location $RepoRoot

Write-Host "== Call Shield installer script =="
Write-Host "Repository root: $RepoRoot"

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    Write-Error @"
No se encontró el comando 'dotnet'.
Instala .NET 8 SDK desde: https://aka.ms/dotnet/download
Después reinicia PowerShell y ejecuta:
  cd '$RepoRoot'
  .\docs\INSTALL.ps1
"@
}

$dotnetInfo = & dotnet --list-sdks
if (-not $dotnetInfo) {
    Write-Error @"
No hay SDKs de .NET instalados (solo runtime o nada).
Instala .NET 8 SDK desde: https://aka.ms/dotnet/download
"@
}

if ($BuildRelease) {
    & dotnet publish .\src\CallShield.App\CallShield.App.csproj -c Release -r win-x64 --self-contained false
} else {
    & dotnet build .\CallShield.sln
}

Write-Host "Done. Run application with:"
Write-Host "  dotnet run --project .\src\CallShield.App\CallShield.App.csproj"
