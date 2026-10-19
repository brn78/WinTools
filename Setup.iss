; Setup di WinTools (Inno Setup 6/7). Lo compila Build.ps1, che passa la versione: ISCC /DAppVersion=1.2.0 Setup.iss
; Installazione per utente, senza amministratore, in %LOCALAPPDATA%\Programs\WinTools: così l'aggiornamento
; automatico dell'app (che sostituisce il proprio exe) continua a funzionare e i programmi avviati dai tasti
; non partono elevati.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "WinTools"
#define AppPublisher "brn78"
#define AppURL "https://github.com/brn78/WinTools"
#define AppExe "WinTools.exe"

[Setup]
AppId={{5E2F1C8A-7D3B-4B9E-9C41-3F0A6D2B8E71}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
AppCopyright=© 2026 brn78
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Setup di {#AppName}
DefaultDirName={userpf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
OutputDir=.
OutputBaseFilename=WinTools-Setup
SetupIconFile=WinTools.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
ShowLanguageDialog=auto
CloseApplications=no

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
italian.AutoStart=Avvia WinTools con Windows (consigliato)
english.AutoStart=Start WinTools with Windows (recommended)
italian.StartGroup=Avvio:
english.StartGroup=Startup:
italian.RemoveData=Eliminare anche le impostazioni, i tasti e l'indice dei file di WinTools?%n%n(cartella %1)
english.RemoveData=Also delete WinTools settings, buttons and file index?%n%n(folder %1)

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:StartGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; La stessa voce che scrive l'app da Impostazioni → Generale; sparisce alla disinstallazione
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"" --autostart"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; L'app riceve la scelta sull'avvio automatico, così la ricorda nelle sue impostazioni
Filename: "{app}\{#AppExe}"; Parameters: "{code:LaunchParams}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure KillApp();
var
  ResultCode: Integer;
begin
  // L'app in esecuzione tiene bloccato il proprio exe: la si chiude (salva le impostazioni a ogni modifica)
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM WinTools.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(600);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillApp();
  Result := '';
end;

function LaunchParams(Param: String): String;
begin
  if WizardIsTaskSelected('autostart') then Result := '--set-autostart=1' else Result := '--set-autostart=0';
end;

function InitializeUninstall(): Boolean;
begin
  KillApp();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // La voce di avvio, anche se l'ha scritta l'app e non il setup
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WinTools');
    DataDir := ExpandConstant('{localappdata}\WinTools');
    if DirExists(DataDir) then
    begin
      if MsgBox(FmtMessage(CustomMessage('RemoveData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
