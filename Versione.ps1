function Get-TrousseBuildVersion([string]$Root) {
    $Root = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $scope = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Root.ToUpperInvariant()))).Replace('-', '').Substring(0, 24)
        $mutex = [Threading.Mutex]::new($false, "Local\TrousseBuild-$scope")
        try {
            try { $locked = $mutex.WaitOne(30000) }
            catch [Threading.AbandonedMutexException] { $locked = $true }
            if (!$locked) { throw 'The Trousse build version is in use.' }
            try {
                $stamp = Join-Path $Root 'version.json'
                $current = Get-Content -LiteralPath $stamp -Raw | ConvertFrom-Json
                if ($current.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric X.Y.Z version in version.json.' }
                $dirs = @('src', 'assets', 'release', 'vortex') | ForEach-Object { Join-Path $Root $_ }
                $files = @(Get-ChildItem -LiteralPath $dirs -File -Recurse)
                $files += Get-Item -LiteralPath (Join-Path $Root 'Compila.ps1'), (Join-Path $Root 'Pubblica.ps1'), (Join-Path $Root 'Versione.ps1')
                [string[]]$paths = $files | ForEach-Object FullName
                [Array]::Sort($paths, [StringComparer]::Ordinal)
                $content = foreach ($path in $paths) {
                    $path.Substring($Root.Length + 1).Replace('\', '/') + ':' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
                }
                $fingerprint = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($content -join "`n")))).Replace('-', '')
                if ($current.fingerprint -ne $fingerprint) {
                    if ($current.fingerprint) {
                        $old = [Version]$current.version
                        $current.version = '{0}.{1}.{2}' -f $old.Major, $old.Minor, ($old.Build + 1)
                    }
                    $current.fingerprint = $fingerprint
                    $temporary = $stamp + '.' + [Guid]::NewGuid().ToString('N')
                    try {
                        [IO.File]::WriteAllText($temporary, ($current | ConvertTo-Json))
                        [IO.File]::Replace($temporary, $stamp, [NullString]::Value)
                    } finally {
                        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
                    }
                }
                return $current.version
            } finally { $mutex.ReleaseMutex() }
        } finally { $mutex.Dispose() }
    } finally { $sha.Dispose() }
}
