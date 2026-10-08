param(
    [Parameter(Mandatory=$true)][string]$Installer,
    [Parameter(Mandatory=$true)][string]$ProductionInstaller,
    [Parameter(Mandatory=$true)][string]$Payload,
    [Parameter(Mandatory=$true)][string]$Root
)
$ErrorActionPreference = 'Stop'
$expectedRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'output\installer'))
if ([System.IO.Path]::GetFullPath($Root) -ne $expectedRoot) { throw 'Installer tests must stay within qa/output/installer.' }
$installDir = Join-Path $Root 'Install 用户 空格'
$desktopLink = Join-Path $Root 'Desktop\Snapline.lnk'
$menuLink = Join-Path $Root 'Start Menu\Snapline\Snapline.lnk'
$dataDir = Join-Path $Root 'LocalAppData\Snapline'
$results = New-Object System.Collections.Generic.List[object]
$shell = New-Object -ComObject WScript.Shell

function Check([bool]$ok, [string]$name) {
    $results.Add([pscustomobject]@{ check=$name; passed=$ok })
    if (-not $ok) { throw ('FAIL: ' + $name) }
    Write-Output ('PASS: ' + $name)
}
function Install([string[]]$options = @()) {
    $arguments = @('/S') + $options + ('/D=' + $installDir)
    $process = Start-Process -FilePath $Installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    Check ($process.ExitCode -eq 0) 'Actual QA installer exits successfully'
}
function Uninstall {
    $process = Start-Process -FilePath (Join-Path $installDir 'Uninstall.exe') -ArgumentList '/S' -WindowStyle Hidden -Wait -PassThru
    Check ($process.ExitCode -eq 0) 'Actual QA uninstaller exits successfully'
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Test-Path -LiteralPath (Join-Path $installDir 'Snapline.exe')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    Check (-not (Test-Path -LiteralPath (Join-Path $installDir 'Snapline.exe'))) 'Uninstall removes program executable'
}
function VerifyShortcut([string]$path, [string]$target) {
    Check (Test-Path -LiteralPath $path) ('Shortcut exists: ' + [System.IO.Path]::GetFileName($path))
    $shortcut = $shell.CreateShortcut($path)
    Check ($shortcut.TargetPath -eq $target) 'Shortcut targets the installed executable, including Unicode and spaces'
    Check ($shortcut.WorkingDirectory -eq $installDir) 'Shortcut working directory matches the installation'
    Check ($shortcut.IconLocation -eq ($target + ',0')) 'Shortcut uses the installed Snapline icon'
}

New-Item -ItemType Directory -Force -Path (Join-Path $dataDir 'Inbox'), $installDir | Out-Null
$settingsPath = Join-Path $dataDir 'settings.json'
$imagePath = Join-Path $dataDir 'Inbox\retained.png'
[System.IO.File]::WriteAllText($settingsPath, '{"CollectionPaused":true,"HotkeyKey":119}')
[System.IO.File]::WriteAllBytes($imagePath, [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl1sAAAAASUVORK5CYII='))
$settingsHash = (Get-FileHash -LiteralPath $settingsPath).Hash
$imageHash = (Get-FileHash -LiteralPath $imagePath).Hash
$foreignFile = Join-Path $installDir 'user-file-keep.txt'
[System.IO.File]::WriteAllText($foreignFile, 'Keep files not owned by the installer.')
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupBefore = (Get-ItemProperty -LiteralPath $runKey -Name Snapline -ErrorAction SilentlyContinue).Snapline
$actualDesktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Snapline.lnk'
$actualDesktopBefore = if (Test-Path -LiteralPath $actualDesktopLink) { (Get-FileHash -LiteralPath $actualDesktopLink).Hash } else { '' }
$testAppKey = 'HKCU:\Software\Snapline-InstallerTests\App'
$testRunKey = 'HKCU:\Software\Snapline-InstallerTests\Run'
Check (-not (Test-Path -LiteralPath $testAppKey)) 'Private QA uninstall registration is initially absent'
$productionBytes = [System.IO.File]::ReadAllBytes($ProductionInstaller)
$productionText = [Text.Encoding]::UTF8.GetString($productionBytes)
Check ($productionText -match 'requestedExecutionLevel[^>]+asInvoker') 'Production installer manifest requests current-user execution'
Check (-not $productionText.Contains($Root) -and -not [Text.Encoding]::Unicode.GetString($productionBytes).Contains($Root)) 'Production installer excludes QA directory redirection'

Install
foreach ($file in Get-ChildItem -LiteralPath $Payload -File) {
    $installedPath = Join-Path $installDir $file.Name
    Check ((Get-FileHash -LiteralPath $installedPath).Hash -eq (Get-FileHash -LiteralPath $file.FullName).Hash) ('Installed payload matches verified portable file: ' + $file.Name)
}
Check (Test-Path -LiteralPath (Join-Path $installDir 'INSTALLER.zh-CN.md')) 'Installer usage and attribution guide is included'
VerifyShortcut $desktopLink (Join-Path $installDir 'Snapline.exe')
VerifyShortcut $menuLink (Join-Path $installDir 'Snapline.exe')
Check (Test-Path -LiteralPath (Join-Path $Root 'Start Menu\Snapline\卸载 Snapline.lnk')) 'Start Menu includes an uninstall entry'
$uninstallRegistration = Get-ItemProperty -LiteralPath $testAppKey
Check ($uninstallRegistration.UninstallString -eq ('"' + (Join-Path $installDir 'Uninstall.exe') + '"')) 'Uninstall registration quotes paths containing spaces and Unicode'
Check ($uninstallRegistration.QuietUninstallString -eq ($uninstallRegistration.UninstallString + ' /S')) 'Silent uninstall registration is valid'
Check ($uninstallRegistration.InstallLocation -eq $installDir) 'Uninstall registration points to the installed directory'
Check (-not (Test-Path -LiteralPath $testRunKey)) 'Installation does not enable startup'
New-Item -Path $testRunKey -Force | Out-Null
New-ItemProperty -LiteralPath $testRunKey -Name Snapline -Value ('"' + (Join-Path $installDir 'Snapline.exe') + '" --data-dir "' + $dataDir + '"') -PropertyType String -Force | Out-Null
Uninstall
Check (-not (Test-Path -LiteralPath $desktopLink)) 'Uninstall removes the desktop shortcut it created'
Check (-not (Test-Path -LiteralPath $menuLink)) 'Uninstall removes its Start Menu shortcuts'
Check (Test-Path -LiteralPath $foreignFile) 'Uninstall preserves unrelated files inside the installation directory'
Check ((Get-FileHash -LiteralPath $settingsPath).Hash -eq $settingsHash -and (Get-FileHash -LiteralPath $imagePath).Hash -eq $imageHash) 'Uninstall preserves existing screenshot data and settings'
Check (-not (Test-Path -LiteralPath $testAppKey)) 'Uninstall removes its own application registration'
Check (-not (Get-ItemProperty -LiteralPath $testRunKey -Name Snapline -ErrorAction SilentlyContinue).Snapline) 'Uninstall removes startup pointing to the installed executable'

Install @('/NODESKTOP')
Check (-not (Test-Path -LiteralPath $desktopLink)) 'Opting out does not create a desktop shortcut'
Check (Test-Path -LiteralPath $menuLink) 'Opting out retains the Start Menu entry'
Install
Check (Test-Path -LiteralPath $desktopLink) 'Reinstall can enable the desktop shortcut'
Install @('/NODESKTOP')
Check (-not (Test-Path -LiteralPath $desktopLink)) 'Reinstall can remove a previously created desktop shortcut'
Check ((Get-FileHash -LiteralPath $settingsPath).Hash -eq $settingsHash -and (Get-FileHash -LiteralPath $imagePath).Hash -eq $imageHash) 'Reinstall preserves existing screenshot data and settings'
$otherStartup = '"D:\Other Portable App\Snapline.exe" --data-dir "D:\Other Data"'
New-ItemProperty -LiteralPath $testRunKey -Name Snapline -Value $otherStartup -PropertyType String -Force | Out-Null
Uninstall
Check (Test-Path -LiteralPath $foreignFile) 'Final uninstall preserves unrelated files'
Check ((Get-ItemProperty -LiteralPath $testRunKey -Name Snapline).Snapline -eq $otherStartup) 'Uninstall preserves a startup entry pointing to another portable copy'
Remove-Item -LiteralPath 'HKCU:\Software\Snapline-InstallerTests' -Recurse -Force
$startupAfter = (Get-ItemProperty -LiteralPath $runKey -Name Snapline -ErrorAction SilentlyContinue).Snapline
Check ($startupBefore -eq $startupAfter) 'QA install and uninstall do not change the real startup setting'
$actualDesktopAfter = if (Test-Path -LiteralPath $actualDesktopLink) { (Get-FileHash -LiteralPath $actualDesktopLink).Hash } else { '' }
Check ($actualDesktopBefore -eq $actualDesktopAfter) 'QA does not create or modify the real desktop shortcut'
$report = [ordered]@{
    scope='Actual install and uninstall with redirected shortcuts and private test registry keys.'
    productionInstaller=[System.IO.Path]::GetFileName($ProductionInstaller)
    productionSHA256=(Get-FileHash -LiteralPath $ProductionInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    allPassed=$true
    checks=$results.ToArray()
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $Root 'installer-results.json'), (ConvertTo-Json -InputObject $report -Depth 5) + "`n", $utf8)
Write-Output ('PASS ' + $results.Count + ' installer checks')
