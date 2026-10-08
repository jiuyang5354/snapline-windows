param(
    [Parameter(Mandatory=$true)][string]$PortableZip,
    [Parameter(Mandatory=$true)][string]$ChecksumFile,
    [Parameter(Mandatory=$true)][ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')][string]$ReleaseTag,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit,
    [string]$NuGetPath = 'nuget.exe',
    [switch]$Prerelease
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$version = $ReleaseTag.Substring(1)
$packageVersion = $version
if ($Prerelease) { $packageVersion += '-preview' }
$zipName = 'Snapline-Windows-' + $ReleaseTag + '.zip'
$checksumLines = @(Get-Content -LiteralPath $ChecksumFile | Where-Object { $_ -match ('^[0-9a-fA-F]{64}  ' + [regex]::Escape($zipName) + '$') })
if ($checksumLines.Count -ne 1) { throw 'Release checksum entry is missing or duplicated.' }
$expectedHash = $checksumLines[0].Substring(0, 64)
if ((Get-FileHash -LiteralPath $PortableZip -Algorithm SHA256).Hash -ine $expectedHash) { throw 'Portable ZIP SHA-256 does not match the release.' }
$outputRoot = Join-Path $PSScriptRoot 'dist\nuget'
$stageRoot = Join-Path $outputRoot ('stage-' + [guid]::NewGuid().ToString('N'))
$portableRoot = Join-Path $stageRoot 'portable'
New-Item -ItemType Directory -Force -Path $portableRoot | Out-Null
$portableFiles = @('Snapline.exe', 'Snapline.exe.config', 'README.zh-CN.md', 'LICENSE', 'UPSTREAM-LICENSE.txt', 'NOTICE.md', 'QA.md', 'preview.png')
try {
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PortableZip).Path)
    try {
        $files = @($archive.Entries | Where-Object { $_.Name.Length -gt 0 })
        if ($files.Count -ne $portableFiles.Count) { throw 'Unexpected files in the portable release; update the package allowlist before publishing.' }
        foreach ($name in $portableFiles) {
            $matchingEntries = @($files | Where-Object { $_.FullName.Replace('\', '/') -ceq ('Snapline/' + $name) })
            if ($matchingEntries.Count -ne 1) { throw ('Missing or duplicated portable file: ' + $name) }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($matchingEntries[0], (Join-Path $portableRoot $name))
        }
    } finally { $archive.Dispose() }
    $exeVersion = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $portableRoot 'Snapline.exe')).Version.ToString(3)
    if ($exeVersion -cne $version) { throw 'Executable version does not match the release tag.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PACKAGE-README.md') -Destination $stageRoot
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Snapline.nuspec') -Raw
    $manifest.package.metadata.version = $packageVersion
    $manifest.package.metadata.repository.commit = $SourceCommit
    $manifestPath = Join-Path $stageRoot 'Snapline.nuspec'
    $manifest.Save($manifestPath)
    & $NuGetPath pack $manifestPath -BasePath $stageRoot -OutputDirectory $outputRoot -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw 'NuGet packaging failed.' }
    $packagePath = Join-Path $outputRoot ('jiuyang5354.Snapline.Windows.' + $packageVersion + '.nupkg')
    $packageArchive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        foreach ($name in $portableFiles) {
            $entry = $packageArchive.GetEntry('tools/Snapline/' + $name)
            if ($null -eq $entry) { throw ('Missing packaged file: ' + $name) }
            $stream = $entry.Open()
            try { $entryHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose() }
            if ($entryHash -cne (Get-FileHash -LiteralPath (Join-Path $portableRoot $name) -Algorithm SHA256).Hash) { throw ('Packaged file changed: ' + $name) }
        }
    } finally { $hasher.Dispose(); $packageArchive.Dispose() }
    Write-Output ('Verified ' + $portableFiles.Count + ' portable files: ' + $packagePath)
} finally {
    $absoluteStage = [System.IO.Path]::GetFullPath($stageRoot)
    $absoluteOutput = [System.IO.Path]::GetFullPath($outputRoot)
    if ($absoluteStage.StartsWith($absoluteOutput + '\', [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $absoluteStage)) {
        Remove-Item -LiteralPath $absoluteStage -Recurse -Force
    }
}
