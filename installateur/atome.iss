; Script Inno Setup : fabrique "Atome-Setup-<version>.exe".
; On ne le lance pas a la main : utiliser creer-setup.ps1 (il passe la version et le dossier a emballer).

#ifndef Version
  #define Version "1.0.0"
#endif
#ifndef Source
  #define Source "..\publish-setup"
#endif

[Setup]
AppId={{6C1D7B52-3F0A-4B8E-9A55-A70E00000001}
AppName=Atome
AppVersion={#Version}
AppPublisher=fjeko
DefaultDirName={autopf}\Atome
DefaultGroupName=Atome
; Installation pour l'utilisateur seul : pas besoin d'etre administrateur.
PrivilegesRequired=lowest
OutputDir=sortie
OutputBaseFilename=Atome-Setup-{#Version}
SetupIconFile=..\src\Revisions.App\Ressources\icone.ico
UninstallDisplayIcon={app}\Atome.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "bureau"; Description: "Creer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"

[Files]
; Tout le contenu publie, sauf les fichiers de debogage (.pdb) qui contiennent des chemins du PC de developpement.
Source: "{#Source}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Atome"; Filename: "{app}\Atome.exe"
Name: "{autodesktop}\Atome"; Filename: "{app}\Atome.exe"; Tasks: bureau

[Run]
Filename: "{app}\Atome.exe"; Description: "Lancer Atome"; Flags: nowait postinstall skipifsilent
