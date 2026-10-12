<#
.SYNOPSIS
  Rewrites legal/THIRD-PARTY-NOTICES.txt from the packages GameRadar.exe ships.

.DESCRIPTION
  The MIT and BSD licenses of the components bundled in the self-contained
  exe (Avalonia, SkiaSharp, the .NET runtime...) ask for their copyright and
  license text to come with every copy. This script lists what the Windows
  64-bit exe really contains, from a restore of the app (project.assets.json),
  and reads each package's license in the local NuGet cache.

  Run it after changing a package or the .NET SDK: the test
  "The_third_party_notices_name_every_shipped_package" fails until then.
  Needs the runtime pack in the NuGet cache: publish the app once first.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Update-ThirdPartyNotices.ps1
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'src\GameRadar.App'
$output = Join-Path $root 'legal\THIRD-PARTY-NOTICES.txt'
$cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }

# A private restore: the app's own obj\ folder is left alone (the app may be running).
$temp = Join-Path ([IO.Path]::GetTempPath()) ('gameradar-notices-' + [Guid]::NewGuid().ToString('N'))
dotnet restore $app --artifacts-path $temp | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
$assets = Get-Content -Raw -Encoding UTF8 (Join-Path $temp 'obj\GameRadar.App\project.assets.json') | ConvertFrom-Json
Remove-Item -Recurse -Force $temp

# Shipped = a package with a real (not "_._") managed or native file, or a
# native file for win-x64. Build-only packages and the Linux/macOS/WebAssembly
# native packages are left out: the Windows exe does not contain them.
function Test-Shipped($library) {
    foreach ($group in 'runtime', 'native') {
        if ($library.$group) {
            foreach ($file in $library.$group.PSObject.Properties.Name) {
                if (-not $file.EndsWith('_._')) { return $true }
            }
        }
    }
    if ($library.runtimeTargets) {
        foreach ($file in $library.runtimeTargets.PSObject.Properties) {
            if ($file.Value.rid -eq 'win-x64' -and -not $file.Name.EndsWith('_._')) { return $true }
        }
    }
    return $false
}

$target = $assets.targets.PSObject.Properties | Where-Object { $_.Name -notlike '*/*' } | Select-Object -First 1
$packages = New-Object System.Collections.Generic.List[object]
foreach ($entry in $target.Value.PSObject.Properties) {
    if ($entry.Value.type -eq 'package' -and (Test-Shipped $entry.Value)) {
        $id, $version = $entry.Name -split '/'
        $packages.Add([pscustomobject]@{ Id = $id; Version = $version })
    }
}

# The .NET runtime itself (self-contained exe): the version bundled with the SDK.
$runtimeVersion = (dotnet msbuild $app -getProperty:BundledNETCoreAppPackageVersion).Trim()
$packages.Add([pscustomobject]@{ Id = 'Microsoft.NETCore.App.Runtime.win-x64'; Version = $runtimeVersion })

$mit = New-Object System.Collections.Generic.List[string]
$licenseFiles = New-Object System.Collections.Generic.List[object]
$notices = [ordered]@{}   # SHA-256 of a notices file -> @{ Text; Packages }
$components = New-Object System.Text.StringBuilder

foreach ($package in $packages | Sort-Object Id) {
    $folder = Join-Path $cache (Join-Path $package.Id.ToLowerInvariant() $package.Version)
    if (-not (Test-Path $folder)) { throw "$($package.Id) $($package.Version) is not in the NuGet cache ($folder): publish the app once first." }
    $nuspec = Get-ChildItem $folder -Filter *.nuspec | Select-Object -First 1
    [xml]$xml = Get-Content -Raw -Encoding UTF8 $nuspec.FullName
    $meta = $xml.package.metadata

    $name = "$($package.Id) $($package.Version)"
    if ($meta.license -and $meta.license.type -eq 'expression') {
        $license = $meta.license.'#text'
        if ($license -eq 'MIT') { $mit.Add($name) }
    }
    elseif ($meta.license -and $meta.license.type -eq 'file') {
        $text = Get-Content -Raw -Encoding UTF8 (Join-Path $folder $meta.license.'#text')
        # Named when recognized (the app shows this name in its list): ANGLE's
        # license is the 3-clause BSD one.
        $license = if ($text -match 'Redistribution and use in source and binary forms' -and $text -match 'Neither the name') { 'BSD-3-Clause' } else { 'see its license below' }
        $licenseFiles.Add(@{ Title = "License of $name"; Text = $text.Trim() })
    }
    else {
        $license = "see $($meta.licenseUrl)"
    }

    $url = if ($meta.projectUrl) { ($meta.projectUrl -split '\?')[0] } else { '' }
    [void]$components.AppendLine($name)
    if ($meta.copyright) { [void]$components.AppendLine("  $($meta.copyright)") }
    [void]$components.AppendLine("  License: $license")
    if ($url) { [void]$components.AppendLine("  $url") }
    [void]$components.AppendLine()

    # Notices of what the package itself bundles (Skia, zlib, libpng...), once each.
    foreach ($file in Get-ChildItem $folder -File | Where-Object { $_.Name -match '(?i)^third.?party.?notices' }) {
        $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
        if (-not $notices.Contains($hash)) {
            $notices[$hash] = @{ Text = (Get-Content -Raw -Encoding UTF8 $file.FullName).Trim(); Packages = New-Object System.Collections.Generic.List[string] }
        }
        $notices[$hash].Packages.Add($name)
    }
}

$rule = '=' * 78
$text = New-Object System.Text.StringBuilder
[void]$text.AppendLine('GameRadar — Third-party notices / Avis des composants tiers')
[void]$text.AppendLine($rule)
[void]$text.AppendLine()
[void]$text.AppendLine('EN: GameRadar contains the open-source components below. Their licenses ask')
[void]$text.AppendLine('for these notices to be reproduced; they apply to these components, not to')
[void]$text.AppendLine('GameRadar itself (see LICENSE.txt).')
[void]$text.AppendLine('FR : GameRadar contient les composants open source ci-dessous. Leurs licences')
[void]$text.AppendLine('demandent de reproduire ces mentions ; elles valent pour ces composants, pas')
[void]$text.AppendLine('pour GameRadar lui-même (voir LICENSE.txt).')
[void]$text.AppendLine()
[void]$text.AppendLine('Generated by tools/Update-ThirdPartyNotices.ps1: do not edit by hand.')
[void]$text.AppendLine()
[void]$text.AppendLine($rule)
[void]$text.AppendLine('Components')
[void]$text.AppendLine($rule)
[void]$text.AppendLine()
[void]$text.Append($components.ToString())
[void]$text.AppendLine($rule)
[void]$text.AppendLine('MIT License')
[void]$text.AppendLine($rule)
[void]$text.AppendLine()
[void]$text.AppendLine('Applies to: ' + ($mit -join ', ') + '.')
[void]$text.AppendLine('Copyright: the holders listed for each component above.')
[void]$text.AppendLine()
[void]$text.AppendLine(@'
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
'@)
# Every section has the same shape (rule, title, rule, text): the app cuts
# the file at these rules to show each section folded (LegalDocuments).
foreach ($license in $licenseFiles) {
    [void]$text.AppendLine()
    [void]$text.AppendLine($rule)
    [void]$text.AppendLine($license.Title)
    [void]$text.AppendLine($rule)
    [void]$text.AppendLine()
    [void]$text.AppendLine($license.Text)
}
foreach ($notice in $notices.Values) {
    [void]$text.AppendLine()
    [void]$text.AppendLine($rule)
    [void]$text.AppendLine('Third-party notices bundled with ' + ($notice.Packages -join ', '))
    [void]$text.AppendLine($rule)
    [void]$text.AppendLine()
    [void]$text.AppendLine($notice.Text)
}

# UTF-8 without BOM, LF line endings, like the rest of the repository.
# (This script itself is saved WITH a BOM: Windows PowerShell 5.1 reads a
# script without one as ANSI, and its accents came out broken.)
$content = $text.ToString() -replace "`r`n", "`n"
[IO.File]::WriteAllText($output, $content, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "$($packages.Count) packages -> $output ($([math]::Round($content.Length / 1KB)) KB)"
