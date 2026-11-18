; KeySwitch per-user installer (Inno Setup 6). Built in CI on Windows by tools/build-installer.ps1:
;   ISCC /DAppVersion=1.3.2 /DSourceExe=<path to KeySwitch.exe> /DNeedsRuntime=1 /O<dist> /FKeySwitch-Setup KeySwitch.iss
; No administrator rights: files go to %LocalAppData%\Programs\KeySwitch, the shortcut to the user's Start menu,
; autostart to HKCU\...\Run (the same value the tray item "Запускать при входе в Windows" uses).
; NeedsRuntime: the framework-dependent build; Setup checks for the .NET 8 Desktop Runtime first.
; This file is UTF-8 with BOM (Russian texts).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\..\dist\small\KeySwitch.exe"
#endif

[Setup]
AppId={{5E0B7C1A-2F4D-4C63-9A8E-7B1D6C3F9A20}
AppName=KeySwitch
AppVersion={#AppVersion}
AppVerName=KeySwitch {#AppVersion}
AppPublisher=Denis Studio
AppPublisherURL=https://github.com/vadimpokrov396-ship-it/keyswitch
AppSupportURL=https://github.com/vadimpokrov396-ship-it/keyswitch
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\KeySwitch
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\..\dist
OutputBaseFilename=KeySwitch-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
UninstallDisplayName=KeySwitch
UninstallDisplayIcon={app}\KeySwitch.exe
CloseApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "autostart"; Description: "Запускать KeySwitch при входе в Windows"; GroupDescription: "Дополнительно:"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "KeySwitch.exe"; Flags: ignoreversion
Source: "..\..\README_RU.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\KeySwitch"; Filename: "{app}\KeySwitch.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KeySwitch"; ValueData: """{app}\KeySwitch.exe"""; Tasks: autostart

[Run]
Filename: "{app}\KeySwitch.exe"; Description: "Запустить KeySwitch"; Flags: nowait postinstall skipifsilent

[Code]
const
  FullSetupUrl = 'https://github.com/vadimpokrov396-ship-it/keyswitch/releases/latest/download/KeySwitch-Setup-full.exe';
  DotNetUrl = 'https://dotnet.microsoft.com/en-us/download/dotnet/8.0';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

procedure StopKeySwitch;
var
  ResultCode: Integer;
begin
  // A running copy locks KeySwitch.exe; its settings are saved on every change, so nothing is lost.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM KeySwitch.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

#ifdef NeedsRuntime
function HasDesktopRuntime8: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          Result := True;
      until Result or not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;
#endif

function InitializeSetup: Boolean;
#ifdef NeedsRuntime
var
  Answer, ErrorCode: Integer;
#endif
begin
  Result := True;
#ifdef NeedsRuntime
  if not HasDesktopRuntime8 then
  begin
    Log('.NET 8 Desktop Runtime (x64) was not found.');
    Answer := SuppressibleMsgBox(
      'Этой (лёгкой) версии KeySwitch нужен .NET 8 Desktop Runtime x64, а на компьютере он не найден.' + #13#10 + #13#10 +
      'Да — скачать полный установщик KeySwitch (в нём .NET уже есть, ничего ставить не нужно).' + #13#10 +
      'Нет — открыть страницу загрузки .NET 8 Desktop Runtime и после его установки запустить этот установщик ещё раз.' + #13#10 +
      'Отмена — выйти.',
      mbConfirmation, MB_YESNOCANCEL, IDCANCEL);
    if Answer = IDYES then
      ShellExec('open', FullSetupUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode)
    else if Answer = IDNO then
      ShellExec('open', DotNetUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end;
#endif
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopKeySwitch;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopKeySwitch;
    // Autostart may have been switched on from the tray menu rather than by Setup.
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'KeySwitch');
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    if SuppressibleMsgBox('Удалить также настройки KeySwitch (исключения, горячие клавиши)?',
      mbConfirmation, MB_YESNO, IDYES) = IDYES then
      DelTree(ExpandConstant('{userappdata}\KeySwitch'), True, True, True);
  end;
end;
