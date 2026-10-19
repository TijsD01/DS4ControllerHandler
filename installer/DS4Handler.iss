; Installer for DS4 Handler (Inno Setup 6).
; Built by .github/workflows/release.yml, which passes:
;   /DAppVersion=1.2.3          version shown in Windows "Apps"
;   /DSourceDir=...             folder with the single-file DS4Handler.exe
;   /DDriverDir=...             folder with the ViGEmBus and HidHide setups
;   /DOutputDir=...             where DS4Handler-Setup-<version>.exe is written
;
; Installs per user (no admin needed for the app itself). The ViGEmBus and HidHide setups are
; bundled and only started when that driver is missing; they ask for admin rights themselves.
;
; Updating: a newer setup with the same AppId installs over the old version in the same folder
; (it closes a running DS4 Handler first), so there is always one version installed. Profiles,
; calibration and settings live in %APPDATA%\DS4ControllerHandler, outside the install folder;
; installing and updating never touch them. Uninstalling only changes the uninstalling account's
; settings.json (hidden-controller list, HidHideActivatedByApp) and appends to log.txt, via --unhide;
; an all-users uninstall only reads other users' files.
; Uninstalling stops the app and runs DS4Handler.exe --unhide, so controllers HidHide hid stay usable.
; The app starts the setup itself with /SILENT /CLOSEAPPLICATIONS /UPDATE=1 for one-click updates;
; /UPDATE=1 skips the driver choices (the app warns itself when a driver is missing) and starts the
; new version again when the setup is done.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#ifndef DriverDir
  #define DriverDir "..\drivers"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{6C1E3B52-7A4D-4F0B-9C2E-5D8A1F3E9B47}
AppName=DS4 Handler
AppVersion={#AppVersion}
AppPublisher=TijsD01
AppPublisherURL=https://github.com/TijsD01/DS4ControllerHandler
AppSupportURL=https://github.com/TijsD01/DS4ControllerHandler/issues
AppUpdatesURL=https://github.com/TijsD01/DS4ControllerHandler/releases
DefaultDirName={autopf}\DS4 Handler
DefaultGroupName=DS4 Handler
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=DS4Handler-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\DS4Handler.exe
UninstallDisplayName=DS4 Handler
CloseApplications=yes
CloseApplicationsFilter=DS4Handler.exe

[Languages]
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
dutch.DriversGroup=Drivers (nodig om games je controller te laten zien):
dutch.InstallViGEm=ViGEmBus installeren (nodig: maakt de virtuele Xbox-controller)
dutch.InstallHidHide=HidHide installeren (aanbevolen: voorkomt dubbele input in games)
dutch.DesktopIcon=Snelkoppeling op het bureaublad
dutch.RunApp=DS4 Handler nu starten
dutch.RebootHint=Na het installeren van HidHide moet Windows soms opnieuw opstarten voordat het werkt.
english.DriversGroup=Drivers (needed so games can see your controller):
english.InstallViGEm=Install ViGEmBus (required: creates the virtual Xbox controller)
english.InstallHidHide=Install HidHide (recommended: prevents double input in games)
english.DesktopIcon=Desktop shortcut
english.RunApp=Start DS4 Handler now
english.RebootHint=After installing HidHide, Windows sometimes needs a restart before it works.

[Tasks]
Name: "vigem"; Description: "{cm:InstallViGEm}"; GroupDescription: "{cm:DriversGroup}"; Check: (not ViGEmInstalled) and (not StartedByUpdater)
Name: "hidhide"; Description: "{cm:InstallHidHide}"; GroupDescription: "{cm:DriversGroup}"; Check: (not HidHideInstalled) and (not StartedByUpdater)
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\DS4Handler.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DriverDir}\ViGEmBus_Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: vigem
Source: "{#DriverDir}\HidHide_Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: hidhide

[Icons]
Name: "{autoprograms}\DS4 Handler"; Filename: "{app}\DS4Handler.exe"
Name: "{autodesktop}\DS4 Handler"; Filename: "{app}\DS4Handler.exe"; Tasks: desktopicon

[Run]
; shellexec so the driver setups can show their own admin (UAC) prompt.
Filename: "{tmp}\ViGEmBus_Setup.exe"; StatusMsg: "ViGEmBus installeren..."; Flags: shellexec waituntilterminated; Tasks: vigem
Filename: "{tmp}\HidHide_Setup.exe"; StatusMsg: "HidHide installeren..."; Flags: shellexec waituntilterminated; Tasks: hidhide
Filename: "{app}\DS4Handler.exe"; Description: "{cm:RunApp}"; Flags: nowait postinstall skipifsilent
; Update started from the app: start the new version again (as the normal user, not as admin).
Filename: "{app}\DS4Handler.exe"; Flags: nowait runasoriginaluser; Check: StartedByUpdater

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM DS4Handler.exe /F"; Flags: runhidden; RunOnceId: "StopApp"
; taskkill skips the app's own exit, so HidHide would keep the controllers hidden: undo that.
; An all-users uninstall runs as administrator, whose %APPDATA% is not the user's, and Inno cannot run
; uninstall entries as the original user. --all-users makes the app read the hidden-controller list from
; every user profile instead (it can, as administrator).
; Keep the two RunOnceIds different (and UnhideControllers unchanged): an all-users install upgraded from
; 1.2.0 still has the old plain --unhide entry in its uninstall log; with one shared id Inno would run only
; one of the two, possibly the old one.
Filename: "{app}\DS4Handler.exe"; Parameters: "--unhide"; Flags: runhidden waituntilterminated; RunOnceId: "UnhideControllers"; Check: not IsAdminInstallMode
Filename: "{app}\DS4Handler.exe"; Parameters: "--unhide --all-users"; Flags: runhidden waituntilterminated; RunOnceId: "UnhideControllersAllUsers"; Check: IsAdminInstallMode

[Registry]
; Remove the "Start met Windows" entry the app may have created.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "DS4Handler"; Flags: uninsdeletevalue dontcreatekey

[Code]
function ServiceExists(const Name: String): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\' + Name);
end;

function ViGEmInstalled: Boolean;
begin
  Result := ServiceExists('ViGEmBus');
end;

function HidHideInstalled: Boolean;
begin
  Result := ServiceExists('HidHide');
end;

function StartedByUpdater: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('hidhide') and (not WizardSilent) then
    MsgBox(CustomMessage('RebootHint'), mbInformation, MB_OK);
end;
