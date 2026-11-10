; Installatore Windows di IP-DESK-CTRL (Inno Setup 6)
; Uso: ISCC /DAppVersion=0.1.0 /DArch=x64|x86 /DSourceDir=<cartella con IP-DESK-CTRL.exe> /DOutDir=<uscita> IP-DESK-CTRL.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

[Setup]
AppId={{3E8B1C55-9A2D-4C71-B0F4-6D2E5A7C9B13}
AppName=IP-DESK-CTRL
AppVersion={#AppVersion}
AppVerName=IP-DESK-CTRL {#AppVersion}
AppPublisher=ic8tem
AppPublisherURL=https://github.com/ic8tem/IP-DESK-CTRL
AppSupportURL=https://github.com/ic8tem/IP-DESK-CTRL
DefaultDirName={autopf}\IP-DESK-CTRL
DefaultGroupName=IP-DESK-CTRL
DisableProgramGroupPage=yes
OutputDir={#OutDir}
OutputBaseFilename=IP-DESK-CTRL-{#AppVersion}-win-{#Arch}-setup
SetupIconFile=..\..\src\IpDeskCtrl\Assets\icon.ico
UninstallDisplayIcon={app}\IP-DESK-CTRL.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequiredOverridesAllowed=dialog
InfoBeforeFile=..\..\THIRD-PARTY-NOTICES.txt
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Avvia IP-DESK-CTRL all'accesso a Windows (così il PC è sempre raggiungibile col suo numero)"; GroupDescription: "Avvio:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\IP-DESK-CTRL.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\IP-DESK-CTRL"; Filename: "{app}\IP-DESK-CTRL.exe"
Name: "{autodesktop}\IP-DESK-CTRL"; Filename: "{app}\IP-DESK-CTRL.exe"; Tasks: desktopicon
Name: "{userstartup}\IP-DESK-CTRL"; Filename: "{app}\IP-DESK-CTRL.exe"; Tasks: autostart

[Run]
Filename: "{app}\IP-DESK-CTRL.exe"; Description: "{cm:LaunchProgram,IP-DESK-CTRL}"; Flags: nowait postinstall skipifsilent
