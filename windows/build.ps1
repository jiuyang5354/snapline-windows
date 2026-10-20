param([switch]$Test, [switch]$Features, [switch]$Editor, [switch]$Announcements, [switch]$Bindings, [switch]$Materials, [switch]$Storage, [switch]$Inbox)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$compilerPath = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Install .NET Framework 4.8 Developer Pack to build Snapline.' }
$binaryRoot = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Force -Path $binaryRoot | Out-Null
$iconPath = Join-Path $projectRoot 'Snapline.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { & (Join-Path $projectRoot 'make-icon.ps1') }
& (Join-Path $projectRoot 'compile-shader.ps1')
$references = @('System.dll', 'System.Core.dll', 'System.Net.Http.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll', 'System.Xaml.dll', 'Microsoft.VisualBasic.dll') |
    ForEach-Object { '/reference:' + (Join-Path $frameworkRoot $_) }
$references += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll') |
    ForEach-Object { '/reference:' + (Join-Path $frameworkRoot ('WPF\' + $_)) }
$sourcePaths = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$common = @('/nologo', '/optimize+', '/platform:anycpu', '/langversion:5', '/warn:4', '/utf8output',
    ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')), ('/win32icon:' + $iconPath), ('/resource:' + $iconPath + ',Snapline.ico')) + $references
foreach ($name in @('Theme', 'Settings')) {
    $common += '/resource:' + (Join-Path $projectRoot ('ui\' + $name + '.xaml')) + ',Snapline.' + $name + '.xaml'
}
$common += '/resource:' + (Join-Path $projectRoot 'ANNOUNCEMENT.zh-CN.txt') + ',Snapline.Announcement'
$common += '/resource:' + (Join-Path $projectRoot 'ui\SettingsGlass.ps') + ',Snapline.SettingsGlass.ps'
& $compilerPath @common '/target:winexe' ('/out:' + (Join-Path $binaryRoot 'Snapline.exe')) @sourcePaths
if ($LASTEXITCODE -ne 0) { throw 'Snapline compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination $binaryRoot
foreach ($licenseFile in @('LICENSE', 'UPSTREAM-LICENSE.txt', 'NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path (Split-Path $projectRoot -Parent) $licenseFile) -Destination (Join-Path $binaryRoot $licenseFile)
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.zh-CN.md') -Destination (Join-Path $binaryRoot 'README.zh-CN.md')
Write-Output ('Built: ' + (Join-Path $binaryRoot 'Snapline.exe'))
if ($Storage) {
    $probe = Join-Path $binaryRoot 'StartupMemoryProbe.exe'
    & $compilerPath '/nologo' '/optimize+' '/platform:anycpu' '/langversion:5' '/warn:4' '/utf8output' '/target:exe' '/reference:System.Drawing.dll' ('/out:' + $probe) (Join-Path $projectRoot 'qa\StartupMemoryProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Startup memory probe compilation failed.' }
    $storageOutput = Join-Path $projectRoot ('qa\output\startup-memory-v' + [Reflection.AssemblyName]::GetAssemblyName((Join-Path $binaryRoot 'Snapline.exe')).Version.ToString(3))
    New-Item -ItemType Directory -Force -Path $storageOutput | Out-Null
    & $probe (Join-Path $binaryRoot 'Snapline.exe') (Join-Path $projectRoot 'qa\fixtures\startup-memory') (Join-Path $storageOutput ('run-' + [Guid]::NewGuid().ToString('N'))) | Tee-Object -FilePath (Join-Path $storageOutput 'memory-results.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Startup memory regression failed.' }
    $memoryCandidate = Join-Path $binaryRoot 'Snapline.exe'
    & $probe --tall $memoryCandidate (Join-Path $storageOutput 'generated-tall') (Join-Path $storageOutput ('tall-' + [Guid]::NewGuid().ToString('N'))) | Tee-Object -FilePath (Join-Path $storageOutput 'memory-results.txt') -Append
    if ($LASTEXITCODE -ne 0) { throw 'Tall screenshot memory regression failed.' }
    & $probe --formats $memoryCandidate (Join-Path $projectRoot 'qa\fixtures\startup-memory\formats') (Join-Path $storageOutput ('formats-' + [Guid]::NewGuid().ToString('N'))) | Tee-Object -FilePath (Join-Path $storageOutput 'memory-results.txt') -Append
    if ($LASTEXITCODE -ne 0) { throw 'Image codec digest regression failed.' }
    & $probe --diagnostics $memoryCandidate (Join-Path $storageOutput ('reports-' + [Guid]::NewGuid().ToString('N'))) | Tee-Object -FilePath (Join-Path $storageOutput 'memory-results.txt') -Append
    if ($LASTEXITCODE -ne 0) { throw 'Failure diagnostic reporting regression failed.' }
}
if ($Test -or $Features -or $Editor -or $Announcements -or $Bindings -or $Materials -or $Inbox) {
    & $compilerPath @common '/target:exe' '/main:Snapline.Tests' ('/out:' + (Join-Path $binaryRoot 'Snapline.Tests.exe')) @sourcePaths (Join-Path $projectRoot 'qa\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination (Join-Path $binaryRoot 'Snapline.Tests.exe.config')
    if ($Inbox) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --inbox (Join-Path $projectRoot 'qa\output\inbox-location') }
    elseif ($Materials) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --materials (Join-Path $projectRoot ('qa\output\materials-v' + [Reflection.AssemblyName]::GetAssemblyName((Join-Path $binaryRoot 'Snapline.exe')).Version.ToString(3))) }
    elseif ($Bindings) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --bindings (Join-Path $projectRoot 'qa\output') }
    elseif ($Editor) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --editor (Join-Path $projectRoot 'qa\output\editor-v1.4.1') }
    elseif ($Announcements) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --announcements (Join-Path $projectRoot 'qa\output') }
    elseif ($Features) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --features (Join-Path $projectRoot 'qa\output') }
    else { & (Join-Path $binaryRoot 'Snapline.Tests.exe') (Join-Path $projectRoot 'qa\output') }
    if ($LASTEXITCODE -ne 0) { throw 'Snapline tests failed.' }
}
