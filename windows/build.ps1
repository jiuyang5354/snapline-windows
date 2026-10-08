param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$compilerPath = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Install .NET Framework 4.8 Developer Pack to build Snapline.' }
$binaryRoot = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Force -Path $binaryRoot | Out-Null
$iconPath = Join-Path $projectRoot 'Snapline.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { & (Join-Path $projectRoot 'make-icon.ps1') }
$references = @('System.dll', 'System.Core.dll', 'System.Net.Http.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll', 'System.Xaml.dll', 'Microsoft.VisualBasic.dll') |
    ForEach-Object { '/reference:' + (Join-Path $frameworkRoot $_) }
$references += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll') |
    ForEach-Object { '/reference:' + (Join-Path $frameworkRoot ('WPF\' + $_)) }
$sourcePaths = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$common = @('/nologo', '/optimize+', '/platform:anycpu', '/langversion:5', '/warn:4', '/utf8output',
    ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')), ('/win32icon:' + $iconPath), ('/resource:' + $iconPath + ',Snapline.ico')) + $references
& $compilerPath @common '/target:winexe' ('/out:' + (Join-Path $binaryRoot 'Snapline.exe')) @sourcePaths
if ($LASTEXITCODE -ne 0) { throw 'Snapline compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination $binaryRoot
foreach ($licenseFile in @('LICENSE', 'UPSTREAM-LICENSE.txt', 'NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path (Split-Path $projectRoot -Parent) $licenseFile) -Destination (Join-Path $binaryRoot $licenseFile)
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.zh-CN.md') -Destination (Join-Path $binaryRoot 'README.zh-CN.md')
Write-Output ('Built: ' + (Join-Path $binaryRoot 'Snapline.exe'))
if ($Test) {
    & $compilerPath @common '/target:exe' '/main:Snapline.Tests' ('/out:' + (Join-Path $binaryRoot 'Snapline.Tests.exe')) @sourcePaths (Join-Path $projectRoot 'qa\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination (Join-Path $binaryRoot 'Snapline.Tests.exe.config')
    & (Join-Path $binaryRoot 'Snapline.Tests.exe') (Join-Path $projectRoot 'qa\output')
    if ($LASTEXITCODE -ne 0) { throw 'Snapline tests failed.' }
}
