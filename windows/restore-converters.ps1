param([string]$Destination = (Join-Path $PSScriptRoot 'bin\Converters'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$cache = Join-Path $PSScriptRoot '.tools\conversion-downloads'
New-Item -ItemType Directory -Path $cache,$Destination -Force | Out-Null
$root = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
$items = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'conversion-dependencies.lock.json') -Raw | ConvertFrom-Json
foreach ($item in $items) {
    $path = Join-Path $cache $item.archive
    if (-not (Test-Path -LiteralPath $path)) { Invoke-WebRequest -Uri $item.url -OutFile $path -TimeoutSec 90 }
    if ((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $item.sha256) { throw ('Dependency checksum mismatch: ' + $item.id) }
    $zip = $null
    try {
        if (-not $item.plain) { $zip = [IO.Compression.ZipFile]::OpenRead($path) }
        foreach ($asset in $item.files) {
            $target = [IO.Path]::GetFullPath((Join-Path $root $asset.target))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Dependency target escaped its directory.' }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            if ($item.plain) { Copy-Item -LiteralPath $path -Destination $target -Force }
            else {
                $entry = $zip.GetEntry($asset.entry)
                if ($null -eq $entry) { throw ('Missing dependency entry: ' + $asset.entry) }
                $entryStream = $entry.Open()
                try { $output = [IO.File]::Create($target); try { $entryStream.CopyTo($output) } finally { $output.Dispose() } }
                finally { $entryStream.Dispose() }
            }
        }
    } finally { if ($zip) { $zip.Dispose() } }
}
$uiLicenses = @('QingIcon-LICENSE.txt','QingIcon-NOTICE.txt')
foreach ($name in $uiLicenses) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('ui\' + $name)) -Destination (Join-Path $root ('licenses\' + $name)) -Force }
$macros = New-Object 'System.Collections.Generic.List[string]'
$macros.Add('!macro InstallConverters')
foreach ($item in $items) { foreach ($asset in $item.files) {
    $relative = $asset.target.Replace('/', '\')
    $parent = [IO.Path]::GetDirectoryName($relative)
    $macros.Add('  SetOutPath "$INSTDIR\Converters' + $(if ($parent) { '\' + $parent } else { '' }) + '"')
    $macros.Add('  File "${PAYLOAD_DIR}\Converters\' + $relative + '"')
} }
$macros.Add('  SetOutPath "$INSTDIR\Converters\licenses"')
foreach ($name in $uiLicenses) { $macros.Add('  File "${PAYLOAD_DIR}\Converters\licenses\' + $name + '"') }
$macros.Add('  SetOutPath "$INSTDIR"')
$macros.Add('!macroend')
$macros.Add('!macro UninstallConverters')
foreach ($item in $items) { foreach ($asset in $item.files) { $macros.Add('  Delete "$INSTDIR\Converters\' + $asset.target.Replace('/', '\') + '"') } }
$macros.Add('  RMDir "$INSTDIR\Converters\webp"')
foreach ($name in $uiLicenses) { $macros.Add('  Delete "$INSTDIR\Converters\licenses\' + $name + '"') }
$macros.Add('  RMDir "$INSTDIR\Converters\licenses"')
$macros.Add('  RMDir "$INSTDIR\Converters"')
$macros.Add('!macroend')
[IO.File]::WriteAllLines((Join-Path $PSScriptRoot '.tools\converters-files.nsh'), $macros, (New-Object Text.UTF8Encoding($true)))
