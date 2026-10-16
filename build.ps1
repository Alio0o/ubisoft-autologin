#Requires -Version 5.1
<#
.SYNOPSIS
  Builds, tests and publishes Ubisoft Auto-Login as self-contained Windows x64 executables.
.EXAMPLE
  ./build.ps1              # test + publish to ./publish
  ./build.ps1 -SkipTests
#>
param(
    [string]$Configuration = 'Release',
    [string]$Output = (Join-Path $PSScriptRoot 'publish'),
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

if (-not $SkipTests) {
    dotnet test UbisoftAutoLogin.sln -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

$common = @('-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
            '-p:DebugType=none', '--nologo')

dotnet publish src/UbisoftAutoLogin.Tray/UbisoftAutoLogin.Tray.csproj @common -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Publishing the tray app failed.' }

dotnet publish src/UbisoftAutoLogin.Diagnostics/UbisoftAutoLogin.Diagnostics.csproj @common -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Publishing the diagnostics tool failed.' }

Get-ChildItem -LiteralPath $Output -Filter *.exe | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    '{0}  {1:N1} MB  SHA256 {2}' -f $_.Name, ($_.Length / 1MB), $hash
}
