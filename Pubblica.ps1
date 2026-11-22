$ErrorActionPreference = 'Stop'

$vortex = Join-Path $PSScriptRoot 'vortex'
$info = Get-Content -LiteralPath (Join-Path $vortex 'info.json') -Raw | ConvertFrom-Json

$output = Join-Path $PSScriptRoot 'dist'
$staging = Join-Path $PSScriptRoot 'bin\public-release'
New-Item -ItemType Directory -Path $output, $staging -Force | Out-Null
$exe = Join-Path $staging 'Trousse.exe'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Compila.ps1') -OutputFile $exe -PublicRelease
if ($LASTEXITCODE -ne 0) { throw 'The public build failed.' }
$version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
$info | Add-Member -NotePropertyName version -NotePropertyValue $version -Force
$manifest = Join-Path $staging 'info.json'
[IO.File]::WriteAllText($manifest, ($info | ConvertTo-Json))

Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path $PSScriptRoot 'assets\trousse-icon.png'
$toolIcon = Join-Path $staging 'trousse-tool.png'
$sourceIcon = [Drawing.Image]::FromFile($iconPath)
try {
    $thumbnail = [Drawing.Bitmap]::new($sourceIcon, 64, 64)
    try { $thumbnail.Save($toolIcon, [Drawing.Imaging.ImageFormat]::Png) }
    finally { $thumbnail.Dispose() }
} finally { $sourceIcon.Dispose() }

$guide = Join-Path $PSScriptRoot 'release\README.txt'
$portable = Join-Path $output "Trousse-$version-Windows-x64.zip"
$extension = Join-Path $output "Trousse-$version-Vortex-Extension.zip"
Compress-Archive -LiteralPath $exe, $guide -DestinationPath $portable -Force
$extensionFiles = @($exe, $guide, $toolIcon, (Join-Path $vortex 'index.js'), $manifest)
Compress-Archive -LiteralPath $extensionFiles -DestinationPath $extension -Force
Get-Item -LiteralPath $portable, $extension
