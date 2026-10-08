$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$distributionRoot = Join-Path $projectRoot 'dist'
$portableRoot = Join-Path $distributionRoot 'Snapline'
$sourceRoot = Join-Path $distributionRoot 'Snapline-Windows-Source'
foreach ($stageRoot in @($portableRoot, $sourceRoot)) {
    $absoluteStage = [System.IO.Path]::GetFullPath($stageRoot)
    $absoluteDistribution = [System.IO.Path]::GetFullPath($distributionRoot)
    if (-not $absoluteStage.StartsWith($absoluteDistribution + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Package staging path escaped dist.' }
    if (Test-Path -LiteralPath $absoluteStage) { Remove-Item -LiteralPath $absoluteStage -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $portableRoot, (Join-Path $sourceRoot 'windows') | Out-Null
$releaseFiles = @('Snapline.exe', 'Snapline.exe.config', 'README.zh-CN.md', 'LICENSE', 'UPSTREAM-LICENSE.txt', 'NOTICE.md')
foreach ($name in $releaseFiles) {
    Copy-Item -LiteralPath (Join-Path $projectRoot ('bin\' + $name)) -Destination (Join-Path $portableRoot $name)
}
Copy-Item -LiteralPath (Join-Path (Split-Path $projectRoot -Parent) 'docs\preview.png') -Destination (Join-Path $portableRoot 'preview.png')
Copy-Item -LiteralPath (Join-Path $projectRoot 'QA.md') -Destination (Join-Path $portableRoot 'QA.md')
$sourceFiles = @('.gitignore', 'build.ps1', 'package.ps1', 'make-icon.ps1', 'app.manifest', 'Snapline.exe.config', 'Snapline.ico', 'README.zh-CN.md', 'QA.md')
foreach ($name in $sourceFiles) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination (Join-Path $sourceRoot ('windows\' + $name))
}
New-Item -ItemType Directory -Force -Path (Join-Path $sourceRoot 'windows\src'), (Join-Path $sourceRoot 'windows\qa'), (Join-Path $sourceRoot 'windows\ui') | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $sourceRoot 'windows\src')
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'qa\Tests.cs') -Destination (Join-Path $sourceRoot 'windows\qa\Tests.cs')
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'ui') -Filter '*.xaml' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $sourceRoot 'windows\ui')
}
foreach ($repositoryFile in @('README.md', 'LICENSE', 'UPSTREAM-LICENSE.txt', 'NOTICE.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path (Split-Path $projectRoot -Parent) $repositoryFile) -Destination (Join-Path $sourceRoot $repositoryFile)
}
New-Item -ItemType Directory -Force -Path (Join-Path $sourceRoot 'docs') | Out-Null
Copy-Item -LiteralPath (Join-Path (Split-Path $projectRoot -Parent) 'docs\preview.png') -Destination (Join-Path $sourceRoot 'docs\preview.png')
$releaseTag = 'v' + [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $projectRoot 'bin\Snapline.exe')).Version.ToString(3)
$portableZip = Join-Path $projectRoot ('Snapline-Windows-' + $releaseTag + '.zip')
$sourceZip = Join-Path $projectRoot ('Snapline-Windows-Source-' + $releaseTag + '.zip')
Compress-Archive -LiteralPath $portableRoot -DestinationPath $portableZip -Force
Compress-Archive -LiteralPath $sourceRoot -DestinationPath $sourceZip -Force
$repositoryRoot = Split-Path $projectRoot -Parent
$portableHash = (Get-FileHash -LiteralPath $portableZip -Algorithm SHA256).Hash.ToLowerInvariant()
$sourceHash = (Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant()
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $repositoryRoot 'SHA256SUMS.txt'),
    $portableHash + '  ' + [System.IO.Path]::GetFileName($portableZip) + "`n" + $sourceHash + '  ' + [System.IO.Path]::GetFileName($sourceZip) + "`n", $utf8)
$updateFeed = @([ordered]@{
    tag_name = $releaseTag; draft = $false; prerelease = $true
    body = [System.IO.File]::ReadAllText((Join-Path $repositoryRoot 'CHANGELOG.md'))
    assets = @([ordered]@{
        name = [System.IO.Path]::GetFileName($portableZip); state = 'uploaded'; size = (Get-Item -LiteralPath $portableZip).Length
        digest = 'sha256:' + $portableHash
        browser_download_url = 'https://github.com/jiuyang5354/snapline-windows/releases/download/' + $releaseTag + '/' + [System.IO.Path]::GetFileName($portableZip)
    })
})
[System.IO.File]::WriteAllText((Join-Path $distributionRoot 'update.json'), (ConvertTo-Json -InputObject $updateFeed -Depth 5).Replace("`r`n", "`n") + "`n", $utf8)
Write-Output 'Upload and verify release assets first, then publish dist/update.json as the repository-root update.json.'
Get-Item -LiteralPath $portableZip, $sourceZip | Select-Object Name,Length
