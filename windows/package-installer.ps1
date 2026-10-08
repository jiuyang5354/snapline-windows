param(
    [string]$PortableZip = (Join-Path $PSScriptRoot 'Snapline-Windows-v1.2.1.zip'),
    [string]$ChecksumFile = (Join-Path (Split-Path $PSScriptRoot -Parent) 'SHA256SUMS.txt'),
    [string]$NsisPath = (Join-Path $PSScriptRoot '.tools\nsis-3.13\makensis.exe'),
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $NsisPath)) { throw 'Download NSIS 3.13 from https://nsis.sourceforge.io/Download or pass -NsisPath.' }
$portableName = [System.IO.Path]::GetFileName($PortableZip)
$checksumLine = @(Get-Content -LiteralPath $ChecksumFile | Where-Object { $_ -match ('^[0-9a-fA-F]{64}\s+\*?' + [regex]::Escape($portableName) + '$') })
if ($checksumLine.Count -ne 1) { throw 'The portable package must have exactly one SHA-256 entry.' }
$expectedHash = $checksumLine[0].Substring(0, 64).ToLowerInvariant()
if ((Get-FileHash -LiteralPath $PortableZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) { throw 'Portable package SHA-256 mismatch.' }
$stageRoot = Join-Path $PSScriptRoot 'dist\installer'
$payloadParent = Join-Path $stageRoot 'payload'
New-Item -ItemType Directory -Force -Path $payloadParent | Out-Null
Expand-Archive -LiteralPath $PortableZip -DestinationPath $payloadParent -Force
$payload = Join-Path $payloadParent 'Snapline'
$appVersion = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $payload 'Snapline.exe')).Version.ToString(3)
$outputFile = Join-Path $stageRoot ('Snapline-Windows-Setup-v' + $appVersion + '.exe')
$compilerArguments = @('/V2', '/WX', '/INPUTCHARSET', 'UTF8', ('/DAPP_VERSION=' + $appVersion), ('/DPAYLOAD_DIR=' + $payload),
    ('/DAPP_ICON=' + (Join-Path $PSScriptRoot 'Snapline.ico')),
    ('/DINSTALLER_README=' + (Join-Path $PSScriptRoot 'INSTALLER.zh-CN.md')))
& $NsisPath @compilerArguments ('/DOUTPUT_FILE=' + $outputFile) (Join-Path $PSScriptRoot 'installer.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
$setupHash = (Get-FileHash -LiteralPath $outputFile -Algorithm SHA256).Hash.ToLowerInvariant()
if ($Test) {
    $qaRoot = Join-Path $PSScriptRoot 'qa\output\installer'
    New-Item -ItemType Directory -Force -Path $qaRoot | Out-Null
    $qaSetup = Join-Path $qaRoot 'Snapline-Setup-QA.exe'
    & $NsisPath @compilerArguments ('/DOUTPUT_FILE=' + $qaSetup) ('/DQA_ROOT=' + $qaRoot) (Join-Path $PSScriptRoot 'installer.nsi')
    if ($LASTEXITCODE -ne 0) { throw 'QA installer compilation failed.' }
    & (Join-Path $PSScriptRoot 'qa\InstallerTests.ps1') -Installer $qaSetup -ProductionInstaller $outputFile -Payload $payload -Root $qaRoot
}
$sourceRoot = Join-Path $stageRoot 'Snapline-Windows-Installer-Source'
$absoluteStage = [System.IO.Path]::GetFullPath($stageRoot)
$absoluteSource = [System.IO.Path]::GetFullPath($sourceRoot)
if (-not $absoluteSource.StartsWith($absoluteStage + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Installer source staging escaped dist.' }
if (Test-Path -LiteralPath $absoluteSource) { Remove-Item -LiteralPath $absoluteSource -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $sourceRoot 'windows\qa') | Out-Null
foreach ($name in @('README.md', 'LICENSE', 'UPSTREAM-LICENSE.txt', 'NOTICE.md', 'SHA256SUMS.txt')) {
    Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) $name) -Destination $sourceRoot
}
foreach ($name in @('installer.nsi', 'package-installer.ps1', 'INSTALLER.zh-CN.md', 'Snapline.ico')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $sourceRoot 'windows')
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'qa\InstallerTests.ps1') -Destination (Join-Path $sourceRoot 'windows\qa')
$qaReport = Join-Path $PSScriptRoot 'qa\output\installer\installer-results.json'
if (Test-Path -LiteralPath $qaReport) {
    $results = Get-Content -LiteralPath $qaReport -Raw | ConvertFrom-Json
    if ($results.allPassed -and $results.productionSHA256 -eq $setupHash) {
        Copy-Item -LiteralPath $qaReport -Destination (Join-Path $sourceRoot 'windows\qa\installer-results.json')
    }
}
$sourceZip = Join-Path $stageRoot ('Snapline-Windows-Installer-Source-v' + $appVersion + '.zip')
Compress-Archive -LiteralPath $sourceRoot -DestinationPath $sourceZip -Force
$sourceHash = (Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText((Join-Path $stageRoot 'SHA256SUMS-Setup.txt'),
    $setupHash + '  ' + [System.IO.Path]::GetFileName($outputFile) + "`n" + $sourceHash + '  ' + [System.IO.Path]::GetFileName($sourceZip) + "`n", $utf8)
Get-Item -LiteralPath $outputFile, $sourceZip | Select-Object FullName, Length
