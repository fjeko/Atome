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
; Si Atome est ouvert, l'installateur demande de le fermer (meme nom que le Mutex d'instance unique de App.xaml.cs).
; Lors d'une mise a jour lancee depuis Atome, Atome rend ce Mutex avant de lancer le setup.
AppMutex=Atome-instance-unique-RevisionsBTS
; /CLOSEAPPLICATIONS ferme ce qui utiliserait encore les fichiers ; c'est l'entree [Run] qui relance Atome,
; pas le gestionnaire de redemarrage de Windows (sinon Atome serait lance deux fois).
CloseApplications=yes
RestartApplications=no
; Les donnees de l'utilisateur sont dans %LOCALAPPDATA%\RevisionsBTS : ce script n'y touche JAMAIS
; (ni a l'installation, ni a la mise a jour, ni a la desinstallation). Il ne gere que {app}.

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "bureau"; Description: "Creer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"

[InstallDelete]
; Depuis la 0.6.1, Atome est publie en un seul Atome.exe : on retire les DLL et fichiers .json de l'ancienne
; installation (les DLL natives encore utiles sont recopiees juste apres par [Files]). Rien ne touche aux donnees.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\Atome.deps.json"
Type: files; Name: "{app}\Atome.runtimeconfig.json"

[Files]
; Tout le contenu publie, sauf les fichiers de debogage (.pdb) qui contiennent des chemins du PC de developpement.
Source: "{#Source}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Atome"; Filename: "{app}\Atome.exe"
Name: "{autodesktop}\Atome"; Filename: "{app}\Atome.exe"; Tasks: bureau

[Run]
; Case « Lancer Atome » a la fin de l'assistant. Sans « skipifsilent » : en mode /SILENT (mise a jour lancee
; depuis Atome), l'entree est executee comme si la case etait cochee, donc Atome redemarre tout seul.
Filename: "{app}\Atome.exe"; Description: "Lancer Atome"; Flags: nowait postinstall
