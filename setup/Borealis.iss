; Installer für Borealis (Inno Setup 7). Bauen mit setup\build_installer.ps1 (baut erst dist\Borealis).
; Richtet ein: Programmdateien, Dienst BorealisService (startet mit Windows), Tray-App beim Anmelden (Run +
; StartupApproved, damit Task-Manager und Einstellungen sie zeigen), Startmenü, Eintrag unter „Installierte Apps“.
; Lädt bei Bedarf die .NET-10-Desktop-Laufzeit (Microsoft) und auf Wunsch PawnIO 2.2.0 (feste Prüfsumme) nach.

#define DistDir AddBackslash(SourcePath) + "..\dist\Borealis"
#define AppVersion GetVersionNumbersString(DistDir + "\Borealis.exe")

[Setup]
AppId={{37A8C1A9-A922-421D-A6FB-A817768C4B2B}
AppName=Borealis
AppVersion={#AppVersion}
AppVerName=Borealis {#AppVersion}
AppPublisher=Borealis
AppPublisherURL=https://github.com/Th3Tyr0x/borealis
AppSupportURL=https://github.com/Th3Tyr0x/borealis/issues
AppUpdatesURL=https://github.com/Th3Tyr0x/borealis/releases
AppComments={cm:AppComments}
DefaultDirName={autopf}\Borealis
; Installationsordner immer anzeigen (Standard „auto“ überspringt ihn bei Updates)
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=Borealis-Setup-{#AppVersion}
SetupIconFile=..\src\Borealis.App\Assets\Borealis.ico
WizardSmallImageFile=wizard-small.png
UninstallDisplayIcon={app}\Borealis.exe
UninstallDisplayName=Borealis
WizardStyle=modern dynamic
Compression=lzma2/max
SolidCompression=yes
; Dienst und App beendet der Installer selbst (PrepareToInstall)
CloseApplications=no
RestartApplications=no
; Der Autostart der App gilt für den Benutzer, der installiert
UsedUserAreasWarning=no
#ifdef SignThumbprint
; Setup und Deinstaller signieren (build_installer.ps1 -CertThumbprint …)
SignTool=borealis
SignedUninstaller=yes
#endif

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
de.AppComments=Beleuchtung und Lüfter ohne Armoury Crate
en.AppComments=Lighting and fans without Armoury Crate
de.AutostartTask=Borealis bei der Anmeldung im Infobereich starten
en.AutostartTask=Start Borealis in the notification area at sign-in
de.PrereqTitle=Voraussetzungen
en.PrereqTitle=Requirements
de.PrereqSubtitle=Was Borealis außer seinen eigenen Dateien braucht.
en.PrereqSubtitle=What Borealis needs besides its own files.
de.DotNetPresent=.NET 10 Desktop-Laufzeit: bereits installiert (%1).
en.DotNetPresent=.NET 10 Desktop Runtime: already installed (%1).
de.DotNetMissing=.NET 10 Desktop-Laufzeit: fehlt. Setup lädt sie von Microsoft (ca. 57 MB) und installiert sie.
en.DotNetMissing=.NET 10 Desktop Runtime: missing. Setup downloads it from Microsoft (about 57 MB) and installs it.
de.PawnIoInfo=PawnIO ist ein offener, von Microsoft signierter Treiber (pawnio.eu). Borealis braucht ihn für Lüfter, Temperaturen und RAM-Beleuchtung; Mainboard- und ARGB-Beleuchtung gehen auch ohne. Er lässt sich später auch in Borealis unter Einstellungen einrichten.
en.PawnIoInfo=PawnIO is an open driver signed by Microsoft (pawnio.eu). Borealis needs it for fans, temperatures and memory lighting; motherboard and ARGB lighting also work without it. You can also set it up later in Borealis under Settings.
de.PawnIoOption=Treiber PawnIO 2.2.0 einrichten (wird von GitHub geladen, ca. 3 MB)
en.PawnIoOption=Set up the PawnIO 2.2.0 driver (downloaded from GitHub, about 3 MB)
de.PawnIoPresent=PawnIO ist bereits eingerichtet
en.PawnIoPresent=PawnIO is already set up
de.MemoPrereq=Voraussetzungen:
en.MemoPrereq=Requirements:
de.MemoDotNetPresent=.NET 10 Desktop-Laufzeit vorhanden
en.MemoDotNetPresent=.NET 10 Desktop Runtime present
de.MemoDotNetInstall=.NET 10 Desktop-Laufzeit herunterladen und installieren
en.MemoDotNetInstall=Download and install the .NET 10 Desktop Runtime
de.MemoPawnIoPresent=PawnIO vorhanden
en.MemoPawnIoPresent=PawnIO present
de.MemoPawnIoInstall=PawnIO herunterladen und einrichten
en.MemoPawnIoInstall=Download and set up PawnIO
de.MemoPawnIoSkip=PawnIO nicht einrichten
en.MemoPawnIoSkip=Do not set up PawnIO
de.Options=Optionen:
en.Options=Options:
de.DownloadFailed=Download fehlgeschlagen: %1
en.DownloadFailed=Download failed: %1
de.InstallingDotNet=.NET 10 Desktop-Laufzeit wird installiert …
en.InstallingDotNet=Installing the .NET 10 Desktop Runtime …
de.InstallingPawnIo=PawnIO wird eingerichtet …
en.InstallingPawnIo=Setting up PawnIO …
de.InstallingService=Borealis-Dienst wird eingerichtet …
en.InstallingService=Setting up the Borealis service …
de.DotNetFailed=Die .NET-10-Desktop-Laufzeit wurde nicht installiert (Code %1). Borealis läuft ohne sie nicht; bitte von dotnet.microsoft.com installieren und Setup erneut ausführen.
en.DotNetFailed=The .NET 10 Desktop Runtime was not installed (code %1). Borealis cannot run without it; please install it from dotnet.microsoft.com and run setup again.
de.PawnIoFailed=PawnIO wurde nicht eingerichtet (Code %1). Lüfter, Temperaturen und RAM-Beleuchtung lassen sich später in Borealis unter Einstellungen einrichten.
en.PawnIoFailed=PawnIO was not set up (code %1). You can set up fans, temperatures and memory lighting later in Borealis under Settings.
de.ServiceFailed=Der Borealis-Dienst wurde nicht eingerichtet (Code %1). Protokoll: C:\ProgramData\Borealis\logs
en.ServiceFailed=The Borealis service was not set up (code %1). Log: C:\ProgramData\Borealis\logs
de.RemoveSettings=Auch Einstellungen, Profile und Protokolle von Borealis löschen?
en.RemoveSettings=Also delete Borealis settings, profiles and logs?

[Tasks]
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:Options}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#DistDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Borealis"; Filename: "{app}\Borealis.exe"
Name: "{autodesktop}\Borealis"; Filename: "{app}\Borealis.exe"; Tasks: desktopicon

[Registry]
; Autostart der Tray-App; ohne StartupApproved fehlt sie im Task-Manager unter „Autostart-Apps“ (02 = aktiviert)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Borealis"; \
  ValueData: """{app}\Borealis.exe"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueType: binary; \
  ValueName: "Borealis"; ValueData: "02 00 00 00 00 00 00 00 00 00 00 00"; Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Borealis"; Flags: deletevalue; Tasks: not autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueName: "Borealis"; Flags: deletevalue; Tasks: not autostart

[Run]
Filename: "{app}\Borealis.exe"; Description: "{cm:LaunchProgram,Borealis}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM Borealis.exe"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{app}\Borealis.Service.exe"; Parameters: "uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveService"

[Code]
const
  DotNetUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  DotNetFile = 'windowsdesktop-runtime-10-win-x64.exe';
  PawnIoUrl = 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe';
  PawnIoFile = 'PawnIO_setup.exe';
  PawnIoSha256 = '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032';

var
  DownloadPage: TDownloadWizardPage;
  PrereqPage: TInputOptionWizardPage;
  NeedDotNet, NeedPawnIo, RebootNeeded: Boolean;

{ Installierte .NET-10-Desktop-Laufzeit (Versionsordner, z. B. „10.0.14“), sonst leer }
function DotNetDesktop10Version: String;
var
  FindRec: TFindRec;
begin
  Result := '';
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then begin
    Result := FindRec.Name;
    FindClose(FindRec);
  end;
end;

function DotNetDesktop10Installed: Boolean;
begin
  Result := DotNetDesktop10Version <> '';
end;

function PawnIoInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

procedure InitializeWizard;
var
  DotNetText: String;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;

  { Seite „Voraussetzungen“ nach der Ordnerwahl: Stand von .NET, Wahl für PawnIO }
  if DotNetDesktop10Installed then
    DotNetText := FmtMessage(CustomMessage('DotNetPresent'), [DotNetDesktop10Version])
  else
    DotNetText := CustomMessage('DotNetMissing');
  PrereqPage := CreateInputOptionPage(wpSelectDir, CustomMessage('PrereqTitle'), CustomMessage('PrereqSubtitle'),
    DotNetText + #13#10#13#10 + CustomMessage('PawnIoInfo'), False, False);
  PrereqPage.Add(CustomMessage('PawnIoOption'));
  PrereqPage.Values[0] := True;
  if PawnIoInstalled then begin
    PrereqPage.CheckListBox.ItemCaption[0] := CustomMessage('PawnIoPresent');
    PrereqPage.CheckListBox.ItemEnabled[0] := False;
  end;
end;

{ PawnIO einrichten? Im stillen Modus abschaltbar mit /PAWNIO=0 }
function WantPawnIo: Boolean;
begin
  if PawnIoInstalled then
    Result := False
  else if WizardSilent then
    Result := ExpandConstant('{param:PAWNIO|1}') <> '0'
  else
    Result := PrereqPage.Values[0];
end;

{ Zusammenfassung vor dem Installieren um die Voraussetzungen ergänzen }
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  DotNetLine, PawnIoLine: String;
begin
  if DotNetDesktop10Installed then DotNetLine := CustomMessage('MemoDotNetPresent') else DotNetLine := CustomMessage('MemoDotNetInstall');
  if PawnIoInstalled then PawnIoLine := CustomMessage('MemoPawnIoPresent')
  else if WantPawnIo then PawnIoLine := CustomMessage('MemoPawnIoInstall')
  else PawnIoLine := CustomMessage('MemoPawnIoSkip');
  Result := MemoDirInfo + NewLine + NewLine +
    CustomMessage('MemoPrereq') + NewLine + Space + DotNetLine + NewLine + Space + PawnIoLine;
  if MemoTasksInfo <> '' then Result := Result + NewLine + NewLine + MemoTasksInfo;
end;

{ Laufenden Dienst und App beenden, damit ihre Dateien ersetzt werden können }
procedure StopBorealis;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop BorealisService', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(1500);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Borealis.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Borealis.Service.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
end;

{ .NET und PawnIO nur laden, wenn sie fehlen; PawnIO mit fester Prüfsumme }
function DownloadPrerequisites: String;
begin
  Result := '';
  NeedDotNet := not DotNetDesktop10Installed;
  NeedPawnIo := WantPawnIo;
  if not (NeedDotNet or NeedPawnIo) then Exit;
  try
    if WizardSilent then begin
      if NeedDotNet then DownloadTemporaryFile(DotNetUrl, DotNetFile, '', nil);
      if NeedPawnIo then DownloadTemporaryFile(PawnIoUrl, PawnIoFile, PawnIoSha256, nil);
    end else begin
      DownloadPage.Clear;
      if NeedDotNet then DownloadPage.Add(DotNetUrl, DotNetFile, '');
      if NeedPawnIo then DownloadPage.Add(PawnIoUrl, PawnIoFile, PawnIoSha256);
      DownloadPage.Show;
      try
        DownloadPage.Download;
      finally
        DownloadPage.Hide;
      end;
    end;
  except
    Result := FmtMessage(CustomMessage('DownloadFailed'), [GetExceptionMessage]);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := DownloadPrerequisites;
  if Result = '' then StopBorealis;
end;

{ Installer still ausführen; 0 = ok, 3010 = Neustart nötig, 1638 = neuere Version schon da }
function RunSetup(const FileName, Params, Status: String; var Code: Integer): Boolean;
begin
  WizardForm.StatusLabel.Caption := Status;
  Result := Exec(ExpandConstant('{tmp}\' + FileName), Params, '', SW_HIDE, ewWaitUntilTerminated, Code)
    and ((Code = 0) or (Code = 3010) or (Code = 1638));
  if Code = 3010 then RebootNeeded := True;
  Log(Format('%s %s: Code %d', [FileName, Params, Code]));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  if NeedDotNet and not RunSetup(DotNetFile, '/install /quiet /norestart', CustomMessage('InstallingDotNet'), Code) then begin
    SuppressibleMsgBox(FmtMessage(CustomMessage('DotNetFailed'), [IntToStr(Code)]), mbCriticalError, MB_OK, IDOK);
    Exit;
  end;
  if NeedPawnIo and not RunSetup(PawnIoFile, '-install -silent', CustomMessage('InstallingPawnIo'), Code) then
    SuppressibleMsgBox(FmtMessage(CustomMessage('PawnIoFailed'), [IntToStr(Code)]), mbError, MB_OK, IDOK);
  WizardForm.StatusLabel.Caption := CustomMessage('InstallingService');
  if not Exec(ExpandConstant('{app}\Borealis.Service.exe'), 'install', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    SuppressibleMsgBox(FmtMessage(CustomMessage('ServiceFailed'), [IntToStr(Code)]), mbError, MB_OK, IDOK);
end;

function NeedRestart: Boolean;
begin
  Result := RebootNeeded;
end;

{ Beim Entfernen fragen, ob auch die Einstellungen weg sollen (Standard: behalten) }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and
     (SuppressibleMsgBox(CustomMessage('RemoveSettings'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES) then begin
    DelTree(ExpandConstant('{commonappdata}\Borealis'), True, True, True);
    DelTree(ExpandConstant('{userappdata}\Borealis'), True, True, True);
  end;
end;
