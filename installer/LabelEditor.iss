; Label Editor installer (Inno Setup 7).
;
; Build with build/package.ps1, which publishes the app and passes:
;   /DAppVersion=1.2.3      product version (from Directory.Build.props)
;   /DSourceDir=<publish>   folder with LabelEditor.exe and Defaults\
;   /DOutputDir=<folder>    where LabelEditor-Setup-<version>.exe is written
;
; Two modes, chosen on the first page (or /PORTABLE=1 on the command line):
;   * Install on this PC: all users, in Program Files (C:\Program Files\Label Editor), with Start menu entry
;     and an entry in Windows "Installed apps" (uninstaller). Needs administrator rights: setup starts without
;     them and, when this mode is chosen, restarts itself elevated (UAC prompt) directly into the PC install.
;   * Portable: extracts to any folder (USB stick, network share, fixed folder). Writes the "portable"
;     marker so settings and labels stay in that folder. No uninstall entry, no registry, no shortcuts,
;     and no administrator rights needed.
; Running a newer setup against an existing location upgrades it in place and keeps settings and labels.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define AppName "Label Editor"
#define AppExe "LabelEditor.exe"
#define AppFileVersion GetVersionNumbersString(SourceDir + "\" + AppExe)

[Setup]
; Never change AppId: it is how upgrades find the existing installation.
AppId={{8F3C1C2E-5B7A-4D3B-9E1A-2C6F4B9D7A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Label Editor
VersionInfoVersion={#AppFileVersion}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=yes
; Start unelevated so portable extraction never needs admin; the PC install relaunches with /ALLUSERS,
; which makes {autopf} = C:\Program Files and puts the uninstall entry in HKLM (all users).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Portable extraction must leave no trace in Windows.
Uninstallable=not IsPortable
CreateUninstallRegKey=not IsPortable
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; Close a running editor before replacing its files.
CloseApplications=yes
RestartApplications=no
SetupIconFile=..\src\LabelEditor.App\Assets\app.ico
WizardStyle=modern
OutputDir={#OutputDir}
OutputBaseFilename=LabelEditor-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "sv"; MessagesFile: "compiler:Languages\Swedish.isl"

[CustomMessages]
en.ModeTitle=Installation type
en.ModeDescription=How do you want to use {#AppName}?
en.ModeSub=Both can be updated later by running a newer setup and choosing the same location.
en.ModePc=Install on this PC (Start menu, entry in Windows "Installed apps")
en.ModePortable=Portable: extract to a folder or USB stick (no Windows entry; settings stay in the folder)
en.UpgradeQuestion=This folder contains {#AppName} %1.%n%nUpdate it to %2? Settings and labels are kept.
en.SameVersionQuestion=This folder already contains {#AppName} %1.%n%nReinstall (repair) it?
en.DowngradeQuestion=This folder contains a NEWER version (%1) than this setup (%2).%n%nInstall the older version anyway?
en.NotEmptyQuestion=The folder is not empty and does not contain {#AppName}.%n%nExtract into it anyway?
en.PortableMarker=Label Editor portable mode: settings and labels are stored in the UserData folder next to the program. Delete this file to use the Windows user profile instead.
en.DesktopIcon=Create a desktop shortcut
en.ElevationFailed=Installing on this PC needs administrator rights (it installs to Program Files for all users).%n%nChoose Portable to extract without administrator rights.
sv.ModeTitle=Installationstyp
sv.ModeDescription=Hur vill du använda {#AppName}?
sv.ModeSub=Båda kan uppdateras senare genom att köra en nyare installation och välja samma plats.
sv.ModePc=Installera på den här datorn (Start-menyn, post i Windows "Installerade appar")
sv.ModePortable=Portabel: packa upp i en mapp eller på ett USB-minne (ingen post i Windows; inställningar sparas i mappen)
sv.UpgradeQuestion=Mappen innehåller {#AppName} %1.%n%nUppdatera till %2? Inställningar och etiketter behålls.
sv.SameVersionQuestion=Mappen innehåller redan {#AppName} %1.%n%nInstallera om (reparera)?
sv.DowngradeQuestion=Mappen innehåller en NYARE version (%1) än den här installationen (%2).%n%nInstallera den äldre versionen ändå?
sv.NotEmptyQuestion=Mappen är inte tom och innehåller inte {#AppName}.%n%nPacka upp i den ändå?
sv.PortableMarker=Label Editor portabelt läge: inställningar och etiketter sparas i mappen UserData bredvid programmet. Ta bort den här filen för att använda Windows-användarprofilen i stället.
sv.DesktopIcon=Skapa genväg på skrivbordet
sv.ElevationFailed=Installation på den här datorn kräver administratörsbehörighet (den installeras i Program Files för alla användare).%n%nVälj Portabel för att packa upp utan administratörsbehörighet.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked; Check: not IsPortable

[InstallDelete]
; Packaged LEdit defaults are replaced as a whole so files removed in a new version do not linger.
Type: filesandordirs; Name: "{app}\Defaults"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "\UserData,\portable,\*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Check: not IsPortable
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; Check: not IsPortable

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  ModePage: TInputOptionWizardPage;
  PcDir: String;
  Relaunched: Boolean;

function IsPortable: Boolean;
begin
  if ModePage = nil then
    Result := ExpandConstant('{param:PORTABLE|0}') = '1'
  else
    Result := ModePage.Values[1];
end;

{ Set by the elevated restart: the PC install was already chosen, so skip the mode page. }
function PcModePreselected: Boolean;
begin
  Result := CompareText(ExpandConstant('{param:MODE|}'), 'pc') = 0;
end;

{ Restarts this setup with administrator rights in all-users (Program Files) mode. }
function RelaunchElevated: Boolean;
var
  Params: String;
  ResultCode: Integer;
begin
  Params := '/ALLUSERS /MODE=pc /LANG=' + ActiveLanguage;
  if WizardSilent then
    Params := Params + ' /VERYSILENT /SUPPRESSMSGBOXES /NORESTART';
  Result := ShellExec('runas', ExpandConstant('{srcexe}'), Params, '', SW_SHOW, ewNoWait, ResultCode);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (ModePage <> nil) and (PageID = ModePage.ID) and PcModePreselected;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  { Closing after handing over to the elevated setup is not a cancellation the user must confirm. }
  if Relaunched then
    Confirm := False;
end;

{ Compares dotted versions numerically: -1 if A < B, 0 if equal, 1 if A > B. }
function CompareVersions(A, B: String): Integer;
var
  PartA, PartB: Integer;
  DotA, DotB: Integer;
begin
  Result := 0;
  while (Result = 0) and ((A <> '') or (B <> '')) do
  begin
    DotA := Pos('.', A);
    DotB := Pos('.', B);
    if DotA = 0 then begin PartA := StrToIntDef(A, 0); A := ''; end
    else begin PartA := StrToIntDef(Copy(A, 1, DotA - 1), 0); Delete(A, 1, DotA); end;
    if DotB = 0 then begin PartB := StrToIntDef(B, 0); B := ''; end
    else begin PartB := StrToIntDef(Copy(B, 1, DotB - 1), 0); Delete(B, 1, DotB); end;
    if PartA < PartB then Result := -1
    else if PartA > PartB then Result := 1;
  end;
end;

procedure InitializeWizard;
begin
  ModePage := CreateInputOptionPage(wpWelcome, CustomMessage('ModeTitle'), CustomMessage('ModeDescription'),
    CustomMessage('ModeSub'), True, False);
  ModePage.Add(CustomMessage('ModePc'));
  ModePage.Add(CustomMessage('ModePortable'));
  ModePage.Values[0] := PcModePreselected or (ExpandConstant('{param:PORTABLE|0}') <> '1');
  ModePage.Values[1] := not ModePage.Values[0];
  PcDir := WizardForm.DirEdit.Text;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir, Exe, Installed: String;
  FindRec: TFindRec;
  HasFiles: Boolean;
begin
  Result := True;

  { Switching mode suggests a matching folder; the user can still browse anywhere (e.g. a USB stick).
    Never in silent mode or when /DIR= was given: then the folder is exactly what was asked for. }
  { Install on this PC goes to Program Files for all users: hand over to an elevated setup. }
  if (CurPageID = ModePage.ID) and not IsPortable and not IsAdminInstallMode then
  begin
    Result := False;
    if RelaunchElevated then
    begin
      Relaunched := True;
      WizardForm.Close;
    end
    else if not WizardSilent then
      MsgBox(CustomMessage('ElevationFailed'), mbError, MB_OK);
    Exit;
  end;

  if (CurPageID = ModePage.ID) and not WizardSilent and (ExpandConstant('{param:DIR|}') = '') then
  begin
    if IsPortable then
    begin
      if CompareText(WizardForm.DirEdit.Text, PcDir) = 0 then
        WizardForm.DirEdit.Text := ExpandConstant('{userdocs}\Label Editor Portable');
    end
    else
      WizardForm.DirEdit.Text := PcDir;
  end;

  { Detect an existing copy in the chosen folder and confirm update / repair / downgrade. }
  if CurPageID = wpSelectDir then
  begin
    Dir := WizardDirValue;
    Exe := AddBackslash(Dir) + '{#AppExe}';
    if FileExists(Exe) then
    begin
      if not GetVersionNumbersString(Exe, Installed) then
        Installed := '0.0.0.0';
      case CompareVersions(Installed, '{#AppFileVersion}') of
        -1: Result := WizardSilent or (MsgBox(FmtMessage(CustomMessage('UpgradeQuestion'), [Installed, '{#AppVersion}']), mbConfirmation, MB_YESNO) = IDYES);
        0: Result := WizardSilent or (MsgBox(FmtMessage(CustomMessage('SameVersionQuestion'), [Installed]), mbConfirmation, MB_YESNO) = IDYES);
        1: Result := WizardSilent or (MsgBox(FmtMessage(CustomMessage('DowngradeQuestion'), [Installed, '{#AppVersion}']), mbError, MB_YESNO or MB_DEFBUTTON2) = IDYES);
      end;
    end
    else if IsPortable and DirExists(Dir) then
    begin
      HasFiles := FindFirst(AddBackslash(Dir) + '*', FindRec);
      if HasFiles then
      try
        HasFiles := False;
        repeat
          if (FindRec.Name <> '.') and (FindRec.Name <> '..') then HasFiles := True;
        until HasFiles or not FindNext(FindRec);
      finally
        FindClose(FindRec);
      end;
      if HasFiles and not WizardSilent then
        Result := MsgBox(CustomMessage('NotEmptyQuestion'), mbConfirmation, MB_YESNO) = IDYES;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Marker: String;
begin
  if CurStep = ssPostInstall then
  begin
    Marker := ExpandConstant('{app}\portable');
    if IsPortable then
      SaveStringToFile(Marker, CustomMessage('PortableMarker'), False)
    else if FileExists(Marker) then
      DeleteFile(Marker); { a PC install keeps its data in the user profile }
  end;
end;
