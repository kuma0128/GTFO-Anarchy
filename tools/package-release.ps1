param(
    [string]$Source = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory = $true)][string]$Output
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Source = (Resolve-Path -LiteralPath $Source).Path
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw "Output already exists: $Output" }
$manifest = Get-Content -LiteralPath (Join-Path $Source 'profile/manifest.json') -Raw | ConvertFrom-Json
$entries = [ordered]@{}
foreach ($name in 'manifest.json','README.md','CHANGELOG.md','icon.png') {
    $entries[$name] = Join-Path $Source "profile/$name"
}
$entries['VALIDATION.md'] = Join-Path $Source 'VALIDATION.md'
Get-ChildItem -LiteralPath $Source -File -Filter 'GameData_*.json' | ForEach-Object {
    $entries["plugins/Anarchy/$($_.Name)"] = $_.FullName
}
foreach ($folder in 'Custom','PartialData') {
    Get-ChildItem -LiteralPath (Join-Path $Source $folder) -Recurse -File | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($Source, $_.FullName).Replace('\', '/')
        $entries["plugins/Anarchy/$relative"] = $_.FullName
    }
}
$entries['plugins/PersistentData/CleanerRundownMenu/MenuData.json'] = Join-Path $Source 'profile/PersistentData/CleanerRundownMenu/MenuData.json'
foreach ($file in $entries.Values) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing input: $file" }
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Output)) | Out-Null
$archive = [IO.Compression.ZipFile]::Open($Output, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entry in $entries.GetEnumerator()) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $entry.Value, $entry.Key, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($Output)
try {
    if ($archive.Entries.Count -ne $entries.Count) { throw 'Archive entry count mismatch' }
    foreach ($entry in $archive.Entries) {
        if (!$entries.Contains($entry.FullName)) { throw "Unexpected entry: $($entry.FullName)" }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [Convert]::ToHexString($sha.ComputeHash($stream)) }
        finally { $stream.Dispose(); $sha.Dispose() }
        $expected = (Get-FileHash -LiteralPath $entries[$entry.FullName] -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw "Content mismatch: $($entry.FullName)" }
    }
} finally { $archive.Dispose() }
[PSCustomObject]@{
    Version = $manifest.version_number
    Files = $entries.Count
    Bytes = (Get-Item -LiteralPath $Output).Length
    SHA256 = (Get-FileHash -LiteralPath $Output -Algorithm SHA256).Hash
    Path = $Output
} | ConvertTo-Json
