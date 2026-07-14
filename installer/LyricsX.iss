#define MyAppName "LyricsX"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "LyricsX"
#define MyAppExeName "LyricsX.exe"

[Setup]
AppId={{A0EE30DD-EAA6-4D9A-A6A9-0EE5E0E9FE10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=LyricsX-Setup
SetupIconFile=..\src\icons\LyricsX.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "default"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetDesktopRuntimeUrl = 'https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe';

function IsDotNet9DesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
  RuntimePath: String;
begin
  Result := False;
  RuntimePath := ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App\9.*');

  if FindFirst(RuntimePath, FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := IsDotNet9DesktopRuntimeInstalled;
  if Result then
    Exit;

  if MsgBox(
    'LyricsX 需要 .NET 9 Desktop Runtime (x64) 才能运行。' + #13#10 + #13#10 +
    '是否现在打开微软官方下载页？安装运行库后，请重新运行 LyricsX 安装程序。',
    mbConfirmation, MB_YESNO) = IDYES then
  begin
    ShellExec('open', DotNetDesktopRuntimeUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;

  Result := False;
end;
