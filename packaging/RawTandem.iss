; RawTandem - installeur Windows (Inno Setup)
; Compilé en CI par .github/workflows/release.yml :
;   iscc /DAppVersion=x.y.z packaging\RawTandem.iss
; Attend la sortie publish single-file dans publish\windows\ (depuis la
; racine du dépôt). Les chemins sont relatifs à ce fichier .iss.

#define AppName "RawTandem"
#define AppExeName "RawTandem.UI.exe"
#ifndef AppVersion
#define AppVersion "0.0.0"
#endif

; GUID stable : identifie l'application pour les mises à jour
; (ne jamais le changer, sinon l'installeur crée une deuxième entrée).
#define AppId "{{9F4C2E1A-6B7D-4A3E-8C5B-0E2F1A3D5B7C}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=RawTandem-{#AppVersion}-setup
SetupIconFile=..\src\RawTandem.UI\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LanguageDetectionMethod=locale

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
