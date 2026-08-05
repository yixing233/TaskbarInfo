#define MyAppName "TaskbarInfo"
#define MyAppVersion "1.1.4"
#define MyAppPublisher "TaskbarInfo"
#define MyAppExeName "TaskbarInfo.exe"

#ifndef PublishDirectory
  #define PublishDirectory "..\publish\win-x64"
#endif

#ifndef SetupOutputDirectory
  #define SetupOutputDirectory "output"
#endif

#ifndef SetupCompression
  #define SetupCompression "lzma2/ultra64"
#endif

[Setup]
AppId={{A0EE30DD-EAA6-4D9A-A6A9-0EE5E0E9FE10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#SetupOutputDirectory}
OutputBaseFilename=TaskbarInfo-Setup
SetupIconFile=..\src\icons\LyricsX.ico
Compression={#SetupCompression}
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项"; Flags: unchecked

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetDesktopRuntimeUrl = 'https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe';
  WindowsAppRuntimeUrl = 'https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe';
  AppModelPackageRepositoryKey = 'SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Packages';

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

function IsWindowsAppRuntime18Installed: Boolean;
var
  Subkeys: TArrayOfString;
  Index: Integer;
begin
  Result := False;
  if not RegGetSubkeyNames(HKLM64, AppModelPackageRepositoryKey, Subkeys) then
    Exit;

  for Index := 0 to GetArrayLength(Subkeys) - 1 do
  begin
    if (Pos('Microsoft.WindowsAppRuntime.1.8_', Subkeys[Index]) = 1) and
       (Pos('_x64__8wekyb3d8bbwe', Subkeys[Index]) > 0) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := IsDotNet9DesktopRuntimeInstalled;
  if not Result then
  begin
    if MsgBox(
      'TaskbarInfo 需要 .NET 9 Desktop Runtime (x64) 才能运行。' + #13#10 + #13#10 +
      '是否现在打开微软官方下载页？安装运行库后，请重新运行 TaskbarInfo 安装程序。',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', DotNetDesktopRuntimeUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;

    Exit;
  end;

  Result := IsWindowsAppRuntime18Installed;
  if Result then
    Exit;

  if MsgBox(
    'TaskbarInfo 的设置窗口需要 Windows App Runtime 1.8 (x64)。' + #13#10 + #13#10 +
    '是否现在打开微软官方下载页？安装运行库后，请重新运行 TaskbarInfo 安装程序。',
    mbConfirmation, MB_YESNO) = IDYES then
  begin
    ShellExec('open', WindowsAppRuntimeUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;

  Result := False;
end;
