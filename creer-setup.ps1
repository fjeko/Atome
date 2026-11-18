# Fabrique l'installateur Atome-Setup-<version>.exe dans installateur\sortie.
#
#   .\creer-setup.ps1              -> version lue dans Revisions.App.csproj
#   .\creer-setup.ps1 -Version 1.1.0  -> ce numero est donne a l'application ET au setup
#                                        (c'est ce que fait le workflow de release, avec le numero du tag)
#
# 1) publie l'application en mode "autonome" (le runtime .NET est inclus : rien a installer pour l'utilisateur)
#    dans publish-setup (separe de publish, utilise au quotidien) ;
# 2) lance Inno Setup pour emballer le tout.
# (Pas d'accents dans ce fichier : Windows PowerShell 5.1 les lit mal dans un script sans BOM.)
param([string]$Version)

$ErrorActionPreference = "Stop"
$racine = $PSScriptRoot
$projet = Join-Path $racine "src\Revisions.App"

if (-not $Version) {
    $csproj = [xml](Get-Content (Join-Path $projet "Revisions.App.csproj"))
    $Version = @($csproj.Project.PropertyGroup.Version | Where-Object { $_ })[0]
}

# Recherche d'Inno Setup (installation pour l'utilisateur ou pour tout le PC).
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup est introuvable. Installe-le depuis https://jrsoftware.org/isdl.php" }

$sortie = Join-Path $racine "publish-setup"
if (Test-Path $sortie) { Remove-Item $sortie -Recurse -Force }

Write-Host "Publication autonome (version $Version)..."
# -p:Version impose le numero a l'application : avec le workflow de release, c'est celui du tag (v0.3.0 -> 0.3.0).
# Plus besoin de modifier <Version> dans le .csproj a chaque version (il ne sert qu'en developpement).
# PublishSingleFile : le code .NET tient dans un seul Atome.exe au lieu de ~250 DLL. Au premier lancement apres une
# installation ou une mise a jour, Windows (antivirus) analyse chaque fichier neuf : ~10 s d'attente avant que la
# fenetre apparaisse avec 250 DLL, contre ~0,1 s avec un seul fichier (mesure du journal, section Chrono).
dotnet publish $projet -c Release -r win-x64 --self-contained true -o $sortie --nologo -v q "-p:Version=$Version" -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "La publication a echoue." }

Write-Host "Creation de l'installateur..."
& $iscc "/DVersion=$Version" (Join-Path $racine "installateur\atome.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup a echoue." }

$setup = Join-Path $racine "installateur\sortie\Atome-Setup-$Version.exe"
Write-Host "Termine : $setup"

# Empreinte SHA-256 du setup, au format de sha256sum ("empreinte  nom"), publiee avec la Release.
# Atome refuse d'installer une mise a jour si ce fichier manque ou ne correspond pas.
# Ecrite en ASCII sans BOM (identique avec Windows PowerShell 5.1 et PowerShell 7).
$empreinte = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
$ligne = "$empreinte  Atome-Setup-$Version.exe`n"
[System.IO.File]::WriteAllText("$setup.sha256", $ligne, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Empreinte : $setup.sha256 ($empreinte)"

# Version portable : le meme dossier, sans les fichiers de debogage (.pdb), en ZIP.
$zip = Join-Path $racine "installateur\sortie\Atome-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
$tmp = Join-Path $env:TEMP "atome-portable"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
Copy-Item $sortie $tmp -Recurse
Get-ChildItem $tmp -Recurse -Filter *.pdb | Remove-Item -Force
Compress-Archive -Path "$tmp\*" -DestinationPath $zip
Remove-Item $tmp -Recurse -Force
Write-Host "Version portable : $zip"
