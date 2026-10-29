param([switch]$Test, [switch]$Features, [switch]$Editor, [switch]$Announcements, [switch]$Bindings, [switch]$Materials, [switch]$Storage, [switch]$Inbox, [switch]$Clipboard, [switch]$Shelf, [switch]$FileTools, [switch]$ShelfPresentation, [switch]$LineGroups, [switch]$CardPhysics, [switch]$PdfTools, [switch]$ToolsOpening, [switch]$ToolsDesign)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$compilerPath = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Install .NET Framework 4.8 Developer Pack to build Snapline.' }
$binaryRoot = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Force -Path $binaryRoot | Out-Null
$iconPath = Join-Path $projectRoot 'Snapline.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { & (Join-Path $projectRoot 'make-icon.ps1') }
$references = @('System.dll', 'System.Core.dll', 'System.Net.Http.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll', 'System.Xaml.dll', 'Microsoft.VisualBasic.dll', 'System.IO.Compression.dll') |
    ForEach-Object { '/reference:' + (Join-Path $frameworkRoot $_) }
$references += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll') |
    ForEach-Object { '/reference:' + (Join-Path $frameworkRoot ('WPF\' + $_)) }
& (Join-Path $projectRoot 'restore-converters.ps1')
$references += @('PdfSharp-gdi.dll', 'PdfSharp.System.dll', 'PdfSharp.Shared.dll', 'PdfSharp.Cryptography.dll') |
    ForEach-Object { '/reference:' + (Join-Path $binaryRoot ('Converters\' + $_)) }
$standard = Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\netstandard') -Filter 'netstandard.dll' -Recurse | Select-Object -First 1
if (-not $standard) { throw 'Install .NET Framework 4.8 to provide its netstandard facade.' }
$references += '/reference:' + $standard.FullName
$sourcePaths = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$common = @('/nologo', '/optimize+', '/platform:anycpu', '/langversion:5', '/warn:4', '/utf8output',
    ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')), ('/win32icon:' + $iconPath), ('/resource:' + $iconPath + ',Snapline.ico')) + $references
foreach ($name in @('Theme', 'Settings', 'ToolTheme', 'ToolIcons')) {
    $common += '/resource:' + (Join-Path $projectRoot ('ui\' + $name + '.xaml')) + ',Snapline.' + $name + '.xaml'
}
$shaderPath = Join-Path $projectRoot 'ui\ToolSheen.ps'
if (-not (Test-Path -LiteralPath $shaderPath) -or (Get-Item (Join-Path $projectRoot 'ui\ToolSheen.hlsl')).LastWriteTimeUtc -gt (Get-Item $shaderPath).LastWriteTimeUtc) {
    & (Join-Path $projectRoot 'compile-tool-shader.ps1')
}
$common += '/resource:' + (Join-Path $projectRoot 'ui\ToolSheen.ps') + ',Snapline.ToolSheen.ps'
$common += '/resource:' + (Join-Path $projectRoot 'ANNOUNCEMENT.zh-CN.txt') + ',Snapline.Announcement'
& $compilerPath @common '/target:winexe' ('/out:' + (Join-Path $binaryRoot 'Snapline.exe')) @sourcePaths
if ($LASTEXITCODE -ne 0) { throw 'Snapline compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination $binaryRoot
foreach ($licenseFile in @('LICENSE', 'UPSTREAM-LICENSE.txt', 'NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path (Split-Path $projectRoot -Parent) $licenseFile) -Destination (Join-Path $binaryRoot $licenseFile)
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.zh-CN.md') -Destination (Join-Path $binaryRoot 'README.zh-CN.md')
Write-Output ('Built: ' + (Join-Path $binaryRoot 'Snapline.exe'))
if ($ToolsDesign) {
    foreach ($entry in @('ToolsDesignProbe', 'ToolIntegrationProbe')) {
        $probe = Join-Path $binaryRoot ('Snapline.' + $entry + '.exe')
        & $compilerPath @common '/target:exe' ('/main:Snapline.' + $entry) ('/out:' + $probe) @sourcePaths (Join-Path $projectRoot ('qa\' + $entry + '.cs'))
        if ($LASTEXITCODE -ne 0) { throw 'Tool design/integration probe compilation failed.' }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination ($probe + '.config')
        $probeOutput = Join-Path $projectRoot ('qa\output\' + $entry)
        New-Item -ItemType Directory -Force -Path $probeOutput | Out-Null
        & $probe $probeOutput
        if ($LASTEXITCODE -ne 0) { throw 'Production tool design/integration checks failed.' }
    }
}
if ($ToolsOpening) {
    $openingProbe = Join-Path $binaryRoot 'Snapline.ToolsOpeningProbe.exe'
    & $compilerPath @common '/target:exe' '/main:Snapline.ToolsOpeningProbe' ('/out:' + $openingProbe) @sourcePaths (Join-Path $projectRoot 'qa\ToolsOpeningProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Tools opening probe compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination ($openingProbe + '.config')
    $openingOutput = Join-Path $projectRoot 'qa\output\tools-opening'
    New-Item -ItemType Directory -Force -Path $openingOutput | Out-Null
    foreach ($mode in @('pdf', 'convert')) {
        & $openingProbe $mode $openingOutput 200 --check | Tee-Object -FilePath (Join-Path $openingOutput ($mode + '-results.txt'))
        if ($LASTEXITCODE -ne 0) { throw 'Tools opening regression failed.' }
    }
}
if ($PdfTools) {
    $pdfProbe = Join-Path $binaryRoot 'Snapline.PdfToolsProbe.exe'
    & $compilerPath @common '/target:exe' '/main:Snapline.PdfToolsProbe' ('/out:' + $pdfProbe) @sourcePaths (Join-Path $projectRoot 'qa\PdfToolsProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'PDF tools probe compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination ($pdfProbe + '.config')
    & $pdfProbe (Join-Path $projectRoot 'qa\output\pdf-tools')
    if ($LASTEXITCODE -ne 0) { throw 'PDF/WebP checks failed.' }
    foreach ($entry in @('PdfToolsPreview', 'ImageConvertPreview')) {
        $preview = Join-Path $binaryRoot ('Snapline.' + $entry + '.exe')
        & $compilerPath @common '/target:winexe' ('/main:Snapline.' + $entry) ('/out:' + $preview) @sourcePaths (Join-Path $projectRoot 'qa\PdfToolsProbe.cs')
        if ($LASTEXITCODE -ne 0) { throw 'PDF/image preview compilation failed.' }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination ($preview + '.config')
    }
}
if ($CardPhysics) {
    $physicsProbe = Join-Path $binaryRoot 'Snapline.CardPhysicsProbe.exe'
    $physicsSources = @((Join-Path $projectRoot 'qa\CardPhysicsProbe.cs'), (Join-Path $projectRoot 'qa\LineGroupsProbe.cs'))
    & $compilerPath @common '/target:exe' '/main:Snapline.CardPhysicsProbe' ('/out:' + $physicsProbe) @sourcePaths @physicsSources
    if ($LASTEXITCODE -ne 0) { throw 'Card physics probe compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination ($physicsProbe + '.config')
    & $physicsProbe (Join-Path $projectRoot 'qa\output\card-physics')
    if ($LASTEXITCODE -ne 0) { throw 'Card physics checks failed.' }
    $physicsPreview = Join-Path $binaryRoot 'Snapline.CardPhysicsPreview.exe'
    & $compilerPath @common '/target:winexe' '/main:Snapline.CardPhysicsPreview' ('/out:' + $physicsPreview) @sourcePaths @physicsSources
    if ($LASTEXITCODE -ne 0) { throw 'Card physics preview compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination ($physicsPreview + '.config')
}
if ($LineGroups) {
    $groupsProbe = Join-Path $binaryRoot 'Snapline.LineGroupsProbe.exe'
    & $compilerPath @common '/target:exe' '/main:Snapline.LineGroupsProbe' ('/out:' + $groupsProbe) @sourcePaths (Join-Path $projectRoot 'qa\LineGroupsProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Screenshot groups probe compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination ($groupsProbe + '.config')
    & $groupsProbe (Join-Path $projectRoot 'qa\output\line-groups')
    if ($LASTEXITCODE -ne 0) { throw 'Screenshot groups checks failed.' }
    $groupsPreview = Join-Path $binaryRoot 'Snapline.LineGroupsPreview.exe'
    & $compilerPath @common '/target:winexe' '/main:Snapline.LineGroupsProbe' ('/out:' + $groupsPreview) @sourcePaths (Join-Path $projectRoot 'qa\LineGroupsProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Screenshot groups preview compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination ($groupsPreview + '.config')
}
if ($ShelfPresentation) {
    $presentation = Join-Path $binaryRoot 'Snapline.ShelfPresentationProbe.exe'
    & $compilerPath @common '/target:exe' '/main:Snapline.ShelfPresentationProbe' ('/out:' + $presentation) @sourcePaths (Join-Path $projectRoot 'qa\ShelfPresentationProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Shelf presentation probe compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination ($presentation + '.config')
    & $presentation (Join-Path $projectRoot 'qa\output\shelf-presentation')
    if ($LASTEXITCODE -ne 0) { throw 'Production shelf presentation checks failed.' }
}
if ($Storage -or $Clipboard) {
    $probe = Join-Path $binaryRoot 'StartupMemoryProbe.exe'
    & $compilerPath '/nologo' '/optimize+' '/platform:anycpu' '/langversion:5' '/warn:4' '/utf8output' '/target:exe' '/reference:System.Drawing.dll' ('/out:' + $probe) (Join-Path $projectRoot 'qa\StartupMemoryProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Startup memory probe compilation failed.' }
}
if ($Storage) {
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
if ($Test -or $Features -or $Editor -or $Announcements -or $Bindings -or $Materials -or $Inbox -or $Clipboard -or $Shelf -or $FileTools) {
    & $compilerPath @common '/target:exe' '/main:Snapline.Tests' ('/out:' + (Join-Path $binaryRoot 'Snapline.Tests.exe')) @sourcePaths (Join-Path $projectRoot 'qa\Tests.cs') (Join-Path $projectRoot 'qa\FileShelfTests.cs') (Join-Path $projectRoot 'qa\FileToolsTests.cs') (Join-Path $projectRoot 'qa\SettingsFeedbackTests.cs') (Join-Path $projectRoot 'qa\ShotDragTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination (Join-Path $binaryRoot 'Snapline.Tests.exe.config')
    if ($FileTools) {
        & $compilerPath @common '/target:winexe' '/main:Snapline.FileToolsPreview' ('/out:' + (Join-Path $binaryRoot 'Snapline.FileToolsPreview.exe')) @sourcePaths (Join-Path $projectRoot 'qa\FileToolsTests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'File tools preview compilation failed.' }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'Snapline.exe.config') -Destination (Join-Path $binaryRoot 'Snapline.FileToolsPreview.exe.config')
        & (Join-Path $binaryRoot 'Snapline.Tests.exe') --file-tools (Join-Path $projectRoot 'qa\output\file-tools')
    }
    elseif ($Shelf) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --shelf (Join-Path $projectRoot 'qa\output\file-shelf') }
    elseif ($Clipboard) {
        & (Join-Path $binaryRoot 'Snapline.Tests.exe') --clipboard (Join-Path $projectRoot 'qa\output\clipboard-safety')
        if ($LASTEXITCODE -ne 0) { throw 'Clipboard safety checks failed.' }
        $nativeProbe = Join-Path $binaryRoot 'ClipboardNativeProbe.exe'
        & $compilerPath '/nologo' '/optimize+' '/platform:anycpu' '/langversion:5' '/warn:4' '/utf8output' '/target:exe' '/reference:System.Core.dll' ('/out:' + $nativeProbe) (Join-Path $projectRoot 'qa\ClipboardNativeProbe.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Native clipboard probe compilation failed.' }
        & $nativeProbe (Join-Path $binaryRoot 'Snapline.exe') | Tee-Object -FilePath (Join-Path $projectRoot 'qa\output\clipboard-safety\native-clipboard-results.txt')
        if ($LASTEXITCODE -ne 0) { throw 'Native clipboard screening failed.' }
        & $probe --clipboard (Join-Path $binaryRoot 'Snapline.exe') (Join-Path $projectRoot 'qa\fixtures\startup-memory') (Join-Path $projectRoot ('qa\output\clipboard-memory/run-' + [Guid]::NewGuid().ToString('N'))) | Tee-Object -FilePath (Join-Path $projectRoot 'qa\output\clipboard-safety\clipboard-memory-results.txt')
    }
    elseif ($Inbox) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --inbox (Join-Path $projectRoot 'qa\output\inbox-location') }
    elseif ($Materials) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --materials (Join-Path $projectRoot ('qa\output\materials-v' + [Reflection.AssemblyName]::GetAssemblyName((Join-Path $binaryRoot 'Snapline.exe')).Version.ToString(3))) }
    elseif ($Bindings) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --bindings (Join-Path $projectRoot 'qa\output') }
    elseif ($Editor) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --editor (Join-Path $projectRoot 'qa\output\editor-v1.4.1') }
    elseif ($Announcements) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --announcements (Join-Path $projectRoot 'qa\output') }
    elseif ($Features) { & (Join-Path $binaryRoot 'Snapline.Tests.exe') --features (Join-Path $projectRoot 'qa\output') }
    else { & (Join-Path $binaryRoot 'Snapline.Tests.exe') (Join-Path $projectRoot 'qa\output') }
    if ($LASTEXITCODE -ne 0) { throw 'Snapline tests failed.' }
}
