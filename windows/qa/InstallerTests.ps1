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
function Install([string[]]$options = @(), [bool]$useRegisteredDirectory = $false) {
    $arguments = @('/S') + $options
    if (-not $useRegisteredDirectory) { $arguments += '/D=' + $installDir }
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
Add-Type -AssemblyName System.Drawing
$image = New-Object System.Drawing.Bitmap(2, 2)
try { $image.SetPixel(0, 0, [Drawing.Color]::OrangeRed); $image.Save($imagePath, [Drawing.Imaging.ImageFormat]::Png) }
finally { $image.Dispose() }
$settings = [ordered]@{ CollectionPaused=$true; ListenClipboard=$false; AutoCheckUpdates=$false; WatchFolder=''; HotkeyKey=119; HotkeyModifiers=3; Paths=@($imagePath) }
[System.IO.File]::WriteAllText($settingsPath, (ConvertTo-Json -InputObject $settings -Compress))
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
$startupValue = '"' + (Join-Path $installDir 'Snapline.exe') + '" --data-dir "' + $dataDir + '"'
New-ItemProperty -LiteralPath $testRunKey -Name Snapline -Value $startupValue -PropertyType String -Force | Out-Null
$shortcutArguments = '--data-dir "' + $dataDir + '"'
foreach ($path in @($desktopLink, $menuLink)) { $shortcut = $shell.CreateShortcut($path); $shortcut.Arguments = $shortcutArguments; $shortcut.Save() }
$desktopHash = (Get-FileHash -LiteralPath $desktopLink).Hash
$menuHash = (Get-FileHash -LiteralPath $menuLink).Hash

$previousZip = Join-Path (Split-Path $PSScriptRoot -Parent) 'Snapline-Windows-v1.3.0.zip'
Check ((Get-FileHash -LiteralPath $previousZip -Algorithm SHA256).Hash.ToLowerInvariant() -eq 'b34973a5eeefedb4fe1e4022efe9921f585c9338d1e2d69c5e075bd5ebc14a46') 'Upgrade fixture is the publicly released v1.3.0 portable package'
$previousRoot = Join-Path $Root 'Previous Portable'
Expand-Archive -LiteralPath $previousZip -DestinationPath $previousRoot -Force
$oldExecutable = Join-Path $previousRoot 'Snapline\Snapline.exe'
$oldVersion = [Reflection.AssemblyName]::GetAssemblyName($oldExecutable).Version
$newVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $Payload 'Snapline.exe')).Version
Check ($oldVersion -lt $newVersion) 'Upgrade fixture has an older executable version than the new payload'
Copy-Item -LiteralPath $oldExecutable -Destination (Join-Path $installDir 'Snapline.exe') -Force
$otherDir = Join-Path $Root 'Other Portable'
$secondData = Join-Path $Root 'Second Data'
$otherData = Join-Path $Root 'Other Data'
New-Item -ItemType Directory -Force -Path $otherDir, $secondData, $otherData | Out-Null
Copy-Item -LiteralPath $oldExecutable -Destination (Join-Path $otherDir 'Snapline.exe') -Force
Copy-Item -LiteralPath (Join-Path $Payload 'Snapline.exe.config') -Destination $otherDir -Force
foreach ($directory in @($secondData, $otherData)) { Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $directory 'settings.json') -Force }
$oldProcesses = @()
$otherProcess = $null
try {
    foreach ($directory in @($dataDir, $secondData)) {
        $oldProcesses += Start-Process -FilePath (Join-Path $installDir 'Snapline.exe') -ArgumentList ('--data-dir "' + $directory + '"') -WindowStyle Hidden -PassThru
        $oldProcesses[-1].WaitForInputIdle(5000) | Out-Null
        Check (-not $oldProcesses[-1].HasExited) 'Released old executable starts from the registered installation directory'
    }
    $otherProcess = Start-Process -FilePath (Join-Path $otherDir 'Snapline.exe') -ArgumentList ('--data-dir "' + $otherData + '"') -WindowStyle Hidden -PassThru
    $otherProcess.WaitForInputIdle(5000) | Out-Null
    Check (-not $otherProcess.HasExited) 'A separate portable copy is running during the upgrade'
    Install @() $true
    foreach ($process in $oldProcesses) { Check ($process.WaitForExit(5000) -and $process.ExitCode -eq 0) 'Upgrade gracefully exits a running old instance before replacing its executable' }
    Check (-not $otherProcess.HasExited) 'Upgrade leaves the running portable copy in another directory alone'
    Check ((Get-FileHash -LiteralPath (Join-Path $otherDir 'Snapline.exe')).Hash -eq (Get-FileHash -LiteralPath $oldExecutable).Hash) 'Upgrade does not overwrite another portable copy'
    Check (-not (Test-Path -LiteralPath (Join-Path $Root 'Default Install'))) 'Upgrade automatically reuses the registered custom installation directory'
    Check ((Get-FileHash -LiteralPath (Join-Path $installDir 'Snapline.exe')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $Payload 'Snapline.exe')).Hash) 'Upgrade replaces the old executable with the verified new payload'
    $registration = Get-ItemProperty -LiteralPath $testAppKey
    Check ($registration.InstallLocation -eq $installDir -and $registration.DisplayVersion -eq $newVersion.ToString(3)) 'Upgrade updates one application registration at the original location'
    VerifyShortcut $desktopLink (Join-Path $installDir 'Snapline.exe')
    VerifyShortcut $menuLink (Join-Path $installDir 'Snapline.exe')
    Check ((Get-FileHash -LiteralPath $desktopLink).Hash -eq $desktopHash -and (Get-FileHash -LiteralPath $menuLink).Hash -eq $menuHash) 'In-place upgrade preserves existing shortcut files and their custom data-dir arguments'
    Check (@(Get-ChildItem -LiteralPath (Join-Path $Root 'Desktop') -Filter 'Snapline*.lnk').Count -eq 1) 'Upgrade keeps a single desktop shortcut'
    Check ((Get-ItemProperty -LiteralPath $testRunKey -Name Snapline).Snapline -eq $startupValue) 'Upgrade preserves the enabled startup command and its custom data directory'
    Check ((Get-FileHash -LiteralPath $settingsPath).Hash -eq $settingsHash -and (Get-FileHash -LiteralPath $imagePath).Hash -eq $imageHash) 'Running-instance upgrade preserves screenshots and custom settings byte for byte'
    Check (Test-Path -LiteralPath $foreignFile) 'Upgrade preserves unrelated files in the original installation directory'
} finally {
    foreach ($process in @($oldProcesses) + @($otherProcess)) {
        if ($null -ne $process) { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }; $process.Dispose() }
    }
}
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
    scope='Actual install, running v1.3.0 upgrade and uninstall with redirected shortcuts and private test registry keys.'
    previousVersion=$oldVersion.ToString(3)
    productionInstaller=[System.IO.Path]::GetFileName($ProductionInstaller)
    productionSHA256=(Get-FileHash -LiteralPath $ProductionInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    allPassed=$true
    checks=$results.ToArray()
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $Root 'installer-results.json'), (ConvertTo-Json -InputObject $report -Depth 5) + "`n", $utf8)
Write-Output ('PASS ' + $results.Count + ' installer checks')
