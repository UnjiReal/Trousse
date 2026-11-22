param(
    [string]$OutputFile = (Join-Path $PSScriptRoot 'Trousse.exe'),
    [switch]$PublicRelease
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Versione.ps1')
$version = Get-TrousseBuildVersion $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$xaml = Join-Path $PSScriptRoot 'src\MainWindow.xaml'
$references = @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll', 'System.Xaml.dll') |
    ForEach-Object {
        $path = Join-Path $wpf $_
        if (!(Test-Path -LiteralPath $path)) { $path = Join-Path $framework $_ }
        "/r:$path"
    }
$icon = Join-Path $PSScriptRoot 'assets\trousse.ico'
$iconPreview = Join-Path $PSScriptRoot 'assets\trousse-icon.png'
$splash = Join-Path $PSScriptRoot 'assets\market-splash.jpg'
$executable = [IO.Path]::GetFullPath($OutputFile)
if (!$PublicRelease -and (Test-Path -LiteralPath $executable) -and
    (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion -eq $version) {
    return
}
$compilerArgs = @(
    '/nologo', '/target:winexe', '/platform:x64', '/codepage:65001', '/optimize+', "/out:$executable",
    '/r:System.Xml.Linq.dll', '/r:System.Runtime.Serialization.dll', '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', "/resource:$xaml,MainWindow.xaml",
    "/win32icon:$icon", "/resource:$iconPreview,TrousseIcon.png", "/resource:$splash,MarketSplash.jpg"
) + $references + $sources
$media = @{
    '192x192.png' = 'GogLogo.png'
    'steam-logo.png' = 'SteamLogo.png'
    'specialstandard.png' = 'SkyrimStandard.png'
    'anniversary.png' = 'SkyrimAnniversary.png'
    'vr.png' = 'SkyrimVr.png'
    'app background music.mp3' = 'BackgroundMusic.mp3'
    'CRASH.mp3' = 'CrashEffect.mp3'
    'PACKAGE COMPLETE.mp3' = 'PackageEffect.mp3'
}
foreach ($asset in $media.GetEnumerator() | Sort-Object Name) {
    $path = Join-Path $PSScriptRoot ('assets\' + $asset.Key)
    if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -gt 0) {
        $compilerArgs += "/resource:$path,$($asset.Value)"
    }
}
if ($PublicRelease) { $compilerArgs += '/define:TROUSSE_PUBLIC' }
$metadata = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllText($metadata, @"
[assembly: System.Reflection.AssemblyVersion("$version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$version")]
"@)
    & (Join-Path $framework 'csc.exe') @compilerArgs $metadata
    exit $LASTEXITCODE
} finally { [IO.File]::Delete($metadata) }
