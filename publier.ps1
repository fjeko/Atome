# Fabrique la version "utilisable au quotidien" de l'application dans app\publish.
#
#   .\publier.ps1              -> compile en mode Release dans app\publish
#   .\publier.ps1 -Raccourci   -> cree en plus un raccourci "Revisions BTS SIO" sur le Bureau
#
# La version publiee a besoin du runtime .NET 10 Desktop (deja present avec le SDK) et de WebView2 (livre avec Windows 11).
# (Pas d'accents dans ce fichier : Windows PowerShell 5.1 les lit mal dans un script sans BOM.)
param([switch]$Raccourci)

$ErrorActionPreference = "Stop"
$racine = $PSScriptRoot
$sortie = Join-Path $racine "publish"

# L'application ne doit pas tourner pendant qu'on remplace ses fichiers.
Get-Process Revisions -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet publish (Join-Path $racine "src\Revisions.App") -c Release -o $sortie --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "La publication a echoue." }

$exe = Join-Path $sortie "Revisions.exe"
Write-Host "Application publiee : $exe"

if ($Raccourci) {
    # [char]0xE9 = e accent aigu, pour que le raccourci s'appelle bien "Revisions" avec son accent.
    $nom = "R$([char]0xE9)visions BTS SIO"
    $lien = Join-Path ([Environment]::GetFolderPath("Desktop")) "$nom.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $raccourci = $shell.CreateShortcut($lien)
    $raccourci.TargetPath = $exe
    $raccourci.WorkingDirectory = $sortie
    $raccourci.Description = $nom
    $raccourci.Save()
    Write-Host "Raccourci cree : $lien"
}
