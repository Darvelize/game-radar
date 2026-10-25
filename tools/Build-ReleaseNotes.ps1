<#
.SYNOPSIS
  Builds the notes of a GitHub release: French then English, installation, SHA-256.

.DESCRIPTION
  The notes of a version are the sections of CHANGELOG.md and CHANGELOG.en.md
  (the only source: the app shows the same files in its "Nouveautés" page),
  with their "###" parts turned into "####" so that GitHub shows them as
  sub-headings of "Au programme" / "What's inside". The installation text and
  the SHA-256 of the exe come after them. Same layout as the manual releases
  (docs/release.md).

  Fails with a clear message when a changelog has no section for the version,
  or when the section is empty: a release is never published without notes.

.PARAMETER Version
  The version to publish, as in the "## X.Y.Z" heading (no leading "v").

.PARAMETER ExePath
  The published GameRadar.exe, whose SHA-256 goes in the notes.

.PARAMETER OutFile
  Where to write the notes (UTF-8, no BOM). Without it, they are printed.

.PARAMETER RequireDate
  A real release: the sections must be dated ("## X.Y.Z — AAAA-MM-JJ"), the date
  being written just before it (docs/release.md). A dry run may come before.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Build-ReleaseNotes.ps1 -Version 1.3.0 -ExePath publish\GameRadar.exe -OutFile publish\release-notes-1.3.0.md
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $ExePath,
    [string] $OutFile,
    [switch] $RequireDate
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

# The body of the "## <version>" section, up to the next "## " heading.
function Get-Notes([string] $Path, [string] $Version) {
    $lines = @(Get-Content -Encoding UTF8 -LiteralPath $Path)
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match ('^## ' + [regex]::Escape($Version) + '(\s|$)')) { $start = $i + 1; break }
    }
    if ($start -lt 0) { throw "No '## $Version' section in $(Split-Path -Leaf $Path)." }
    if ($RequireDate -and $lines[$start - 1] -notmatch ('^## ' + [regex]::Escape($Version) + ' [—–-] \d{4}-\d{2}-\d{2}')) {
        throw "The '## $Version' section of $(Split-Path -Leaf $Path) has no date (## $Version — YYYY-MM-DD)."
    }

    $body = @()
    for ($i = $start; $i -lt $lines.Count -and $lines[$i] -notmatch '^## '; $i++) { $body += $lines[$i] }
    $text = ($body -join "`n").Trim()
    if (-not $text) { throw "The '## $Version' section of $(Split-Path -Leaf $Path) is empty." }
    # "###" parts become "####": sub-headings of "Au programme" / "What's inside".
    return $text -replace '(?m)^### ', '#### '
}

if (-not (Test-Path -LiteralPath $ExePath)) { throw "No exe at $ExePath." }
$french = Get-Notes (Join-Path $root 'CHANGELOG.md') $Version
$english = Get-Notes (Join-Path $root 'CHANGELOG.en.md') $Version
$sha = (Get-FileHash -Algorithm SHA256 -LiteralPath $ExePath).Hash

$template = @'
**🇫🇷 Français** · [🇬🇧 English below](#english)

## Au programme

{FR}

## Installation

1. Télécharge **`GameRadar.exe`** ci-dessous (Windows 10/11, 64 bits, rien à installer). Si tu avais une version précédente, remplace simplement l'ancien fichier : tes comptes et réglages sont conservés.
2. Au premier lancement, Windows affiche « Windows a protégé votre ordinateur » : clique sur **Informations complémentaires** puis **Exécuter quand même** (l'appli n'est pas signée).
3. Connecte ton compte **Xbox**, **Steam** et/ou **PlayStation** dans Réglages › Comptes, et choisis ton abonnement **Game Pass** et/ou **PS Plus**. Pas de compte ? Ajoute tes jeux à la main depuis l'Historique.

En utilisant GameRadar, tu acceptes ses [conditions d'utilisation](https://github.com/Darvelize/game-radar/blob/main/legal/LICENSE.txt).

---

<a id="english"></a>
**🇬🇧 English**

## What's inside

{EN}

## Installation

1. Download **`GameRadar.exe`** below (Windows 10/11, 64-bit, nothing to install). If you had an earlier version, just replace the old file: your accounts and settings are kept.
2. On first launch, Windows shows "Windows protected your PC": click **More info**, then **Run anyway** (the app is not signed).
3. Connect your **Xbox**, **Steam** and/or **PlayStation** account in Settings › Accounts, and choose your **Game Pass** and/or **PS Plus** plan. No account? Add your games by hand from the History.

By using GameRadar, you accept its [terms of use](https://github.com/Darvelize/game-radar/blob/main/legal/LICENSE.txt).

---

## SHA-256

```
{SHA}
```
PowerShell : `Get-FileHash .\GameRadar.exe`

[VirusTotal](https://www.virustotal.com/gui/file/{SHA})

Construit par GitHub à partir du code de ce dépôt (licence MIT), avec une attestation de provenance / Built by GitHub from the code of this repository (MIT license), with a provenance attestation:
```
gh attestation verify GameRadar.exe -R Darvelize/game-radar
```
'@

$notes = $template.Replace('{FR}', $french).Replace('{EN}', $english).Replace('{SHA}', $sha).Replace("`r`n", "`n")

if ($OutFile) {
    [IO.File]::WriteAllText($OutFile, $notes + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Release notes of $Version written to $OutFile"
} else {
    $notes
}
