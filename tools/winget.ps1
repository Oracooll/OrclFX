# Writes the winget manifests for a published release and checks them; with -Submit, also proposes them to
# Microsoft's package list (github.com/microsoft/winget-pkgs) as a pull request from your fork.
#   .\tools\winget.ps1 -Version 1.1.012            write + validate into dist\winget
#   .\tools\winget.ps1 -Version 1.1.012 -Submit    ... and open the pull request (needs the GitHub CLI, gh)
# Once the package is listed, anyone can install it with:  winget install Oracooll.OrclFileExplorer
param([Parameter(Mandatory = $true)][string]$Version, [switch]$Submit)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$id = 'Oracooll.OrclFileExplorer'
$repo = 'Oracooll/OrclFX'
$schema = '1.10.0'
$tag = "v$Version"
$url = "https://github.com/$repo/releases/download/$tag/orclfx.exe"

# The checksum must be of the file people will download, so take it from the release itself.
$tmp = Join-Path ([IO.Path]::GetTempPath()) "orclfx-winget-$Version.exe"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
(New-Object Net.WebClient).DownloadFile($url, $tmp)
$sha = (Get-FileHash $tmp -Algorithm SHA256).Hash
$fileVersion = (Get-Item $tmp).VersionInfo.FileVersion
[IO.File]::Delete($tmp)
$parts = $Version.Split('.')
if ($fileVersion -ne ("{0}.{1}.{2}.0" -f [int]$parts[0], [int]$parts[1], [int]$parts[2])) { throw "The release's orclfx.exe is version $fileVersion, not $Version" }
$date = (& gh release view $tag --repo $repo --json publishedAt --jq .publishedAt).Substring(0, 10)

$rel = "manifests/o/Oracooll/OrclFileExplorer/$Version"
$dir = Join-Path $root "dist\winget\$rel"
New-Item -ItemType Directory -Force $dir | Out-Null
$header = "# Created with tools\winget.ps1 from $repo`n"
$files = @{
"$id.yaml" = @"
${header}# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@
"$id.installer.yaml" = @"
${header}# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: exe
Scope: user
InstallModes:
- silent
- silentWithProgress
InstallerSwitches:
  Silent: --install --quiet
  SilentWithProgress: --install --quiet
UpgradeBehavior: install
ReleaseDate: $date
AppsAndFeaturesEntries:
- DisplayName: OrclFX
  Publisher: Orcl
  DisplayVersion: $Version
  ProductCode: OrclFileExplorer
Installers:
- Architecture: neutral
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema
"@
"$id.locale.en-US.yaml" = @"
${header}# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Orcl
PublisherUrl: https://github.com/Oracooll
PublisherSupportUrl: https://github.com/$repo/issues
Author: Oracooll
PackageName: OrclFX
PackageUrl: https://github.com/$repo
License: PolyForm Noncommercial 1.0.0
LicenseUrl: https://github.com/$repo/blob/main/LICENSE.md
Copyright: Copyright (c) 2026 Oracooll
ShortDescription: A light multi-pane file manager that hosts the real Windows Explorer view.
Description: |-
  OrclFX shows one to four file panes side by side, each with its own remembered and lockable
  tabs, and every pane is the real Windows Explorer view (thumbnails, right-click menus, drag and drop).
  It adds a folder tree, a preview pane, a shortcuts strip shared between computers through OneDrive,
  dark and light themes, and optional folder sizes.
Moniker: orclfx
Tags:
- dual-pane
- explorer
- file-explorer
- file-manager
- tabs
ReleaseNotesUrl: https://github.com/$repo/releases/tag/$tag
ManifestType: defaultLocale
ManifestVersion: $schema
"@
}
$utf8 = New-Object Text.UTF8Encoding($false)
foreach ($name in $files.Keys) { [IO.File]::WriteAllText((Join-Path $dir $name), ($files[$name] -replace "`r`n", "`n"), $utf8) }
Write-Host "Manifests for $id $Version written to $dir (SHA-256 $sha)"

& winget validate --manifest $dir
if ($LASTEXITCODE -ne 0) { throw "winget validate failed" }

if (-not $Submit) { return }

# Propose the manifests: fork microsoft/winget-pkgs (once), bring the fork up to date, put the three files on a
# new branch of the fork and open a pull request. Nothing is cloned (the repository is huge).
# gh writes "Not Found" to stderr for the existence checks below; that's an answer, not an error.
$ErrorActionPreference = 'Continue'
$me = & gh api user --jq .login
& gh repo fork microsoft/winget-pkgs --clone=false 2>&1 | Out-Null
$fork = "$me/winget-pkgs"
for ($i = 0; $i -lt 20; $i++) { & gh api "repos/$fork" --silent 2>$null; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep 3 }
& gh api -X POST "repos/$fork/merge-upstream" -f branch=master --silent
$base = & gh api "repos/$fork/git/ref/heads/master" --jq .object.sha
$branch = "$id-$Version"
& gh api -X DELETE "repos/$fork/git/refs/heads/$branch" --silent 2>$null   # left over from an earlier attempt
& gh api -X POST "repos/$fork/git/refs" -f "ref=refs/heads/$branch" -f "sha=$base" --silent
foreach ($name in $files.Keys) {
    $content = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $dir $name)))
    & gh api -X PUT "repos/$fork/contents/$rel/$name" -f "message=$id $Version" -f "content=$content" -f "branch=$branch" --silent
    if ($LASTEXITCODE -ne 0) { throw "Uploading $name failed" }
}
& gh api "repos/microsoft/winget-pkgs/contents/manifests/o/Oracooll/OrclFileExplorer" --silent 2>$null; $isNew = $LASTEXITCODE -ne 0
$title = if ($isNew) { "New package: $id version $Version" } else { "New version: $id version $Version" }
$body = @"
- [ ] Have you signed the [Contributor License Agreement](https://cla.opensource.microsoft.com/microsoft/winget-pkgs)?
- [x] Is there a linked Issue? (none needed)
- [x] Have you checked that there aren't other open [pull requests](https://github.com/microsoft/winget-pkgs/pulls) for the same manifest update/change?
- [x] This PR only modifies one (1) manifest
- [x] Have you [validated](https://github.com/microsoft/winget-pkgs/blob/master/doc/Authoring.md#validation) your manifest locally with ``winget validate --manifest <path>``?
- [ ] Have you tested your manifest locally with ``winget install --manifest <path>``?
- [x] Does your manifest conform to the [$schema schema](https://github.com/microsoft/winget-pkgs/tree/master/doc/manifest/schema/$schema)?

The installer is a per-user, self-installing exe: ``--install --quiet`` copies it to ``%LOCALAPPDATA%\Programs\Orcl File Explorer``, adds a Start menu shortcut and an Apps & Features entry (ProductCode ``OrclFileExplorer``) with a QuietUninstallString. No admin rights.
"@
& gh pr create --repo microsoft/winget-pkgs --head "${me}:$branch" --base master --title $title --body $body
