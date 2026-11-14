; Installatore Windows di IP-DESK-CTRL (Inno Setup 6)
; Uso: ISCC /DAppVersion=0.1.0 /DArch=x64|x86|arm64 /DSourceDir=<cartella con IP-DESK-CTRL.exe> /DOutDir=<uscita> IP-DESK-CTRL.iss

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
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "servizio"; Description: "Accesso completo: richieste UAC, schermata di blocco, programmi da amministratore e Ctrl+Alt+Canc (servizio di Windows; serve l'installazione come amministratore)"; GroupDescription: "Accesso:"; Check: IsAdminInstallMode
Name: "ffmpeg"; Description: "Scarica ffmpeg per il video compresso H.264, H.265 e AV1 (circa 150 MB da GitHub; senza, si usano i riquadri JPEG)"; GroupDescription: "Video:"
Name: "autostart"; Description: "Avvia IP-DESK-CTRL all'accesso a Windows (così il PC è sempre raggiungibile col suo numero)"; GroupDescription: "Avvio:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\IP-DESK-CTRL.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\IP-DESK-CTRL"; Filename: "{app}\IP-DESK-CTRL.exe"
Name: "{autodesktop}\IP-DESK-CTRL"; Filename: "{app}\IP-DESK-CTRL.exe"; Tasks: desktopicon
Name: "{userstartup}\IP-DESK-CTRL"; Filename: "{app}\IP-DESK-CTRL.exe"; Parameters: "--nascosto"; Tasks: autostart

[Registry]
; Ctrl+Alt+Canc generato dal servizio (SendSAS): 1 = permesso ai servizi
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"; ValueType: dword; ValueName: "SoftwareSASGeneration"; ValueData: "1"; Tasks: servizio; Check: IsAdminInstallMode

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create IPDeskCtrl binPath= ""\""{app}\IP-DESK-CTRL.exe\"" --service"" start= auto DisplayName= ""IP-DESK-CTRL accesso completo"""; Flags: runhidden; Tasks: servizio
Filename: "{sys}\sc.exe"; Parameters: "description IPDeskCtrl ""Permette a IP-DESK-CTRL di mostrare e comandare le richieste UAC, la schermata di blocco e i programmi da amministratore durante i collegamenti accettati."""; Flags: runhidden; Tasks: servizio
Filename: "{app}\IP-DESK-CTRL.exe"; Parameters: "--scarica-ffmpeg ""{app}"""; StatusMsg: "Scarico ffmpeg per il video compresso (circa 150 MB)..."; Flags: runhidden waituntilterminated; Tasks: ffmpeg; Check: NoFfmpeg
Filename: "{sys}\sc.exe"; Parameters: "start IPDeskCtrl"; Flags: runhidden; Tasks: servizio
Filename: "{app}\IP-DESK-CTRL.exe"; Description: "{cm:LaunchProgram,IP-DESK-CTRL}"; Flags: nowait postinstall skipifsilent
; aggiornamento dal programma (/AGGIORNA): lo si riapre da solo, come l'utente di prima, per riprendere i collegamenti
Filename: "{app}\IP-DESK-CTRL.exe"; Parameters: "--dopo-aggiornamento"; Flags: nowait runasoriginaluser; Check: IsAutoUpdate

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop IPDeskCtrl"; Flags: runhidden; RunOnceId: "FermaServizio"
Filename: "{sys}\sc.exe"; Parameters: "delete IPDeskCtrl"; Flags: runhidden; RunOnceId: "TogliServizio"

[UninstallDelete]
Type: files; Name: "{app}\ffmpeg.exe"

[Code]
function NoFfmpeg(): Boolean;
begin
  Result := not FileExists(ExpandConstant('{app}\ffmpeg.exe'));
end;

function IsAutoUpdate: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/AGGIORNA') = 0 then Result := True;
end;

// aggiornamento: il servizio usa l'eseguibile, va fermato (e tolto: lo ricrea il setup se richiesto)
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop IPDeskCtrl', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete IPDeskCtrl', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(1500);
  Result := '';
end;
