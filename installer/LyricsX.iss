; 需要 Inno Setup 6.1 或更高版本（依赖 DownloadTemporaryFile 自动下载并静默安装运行库）。
#define MyAppName "TaskbarInfo"
#define MyAppVersion "1.1.7"
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
OutputBaseFilename=TaskbarInfo-Setup-v{#MyAppVersion}
SetupIconFile=..\src\icons\LyricsX.ico
Compression={#SetupCompression}
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
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
  DotNetRuntimeFileName = 'dotnet9-windowsdesktop-runtime-x64.exe';
  WindowsAppRuntimeFileName = 'windowsappruntimeinstall-x64.exe';
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

// 下载并静默安装指定的运行库。
// 成功返回 True；下载失败、无法启动或退出码异常时返回 False。
function DownloadAndInstallRuntime(const AUrl, AFileName, AArguments, ADescription: String): Boolean;
var
  ErrorCode: Integer;
begin
  Result := False;

  MsgBox(
    '检测到缺少 ' + ADescription + '，安装程序将自动从微软官方下载并安装。' + #13#10 + #13#10 +
    '请保持网络连接，视网络情况可能需要数分钟。',
    mbInformation, MB_OK);

  try
    DownloadTemporaryFile(AUrl, AFileName, '', nil);
  except
    Exit;
  end;

  if not Exec(ExpandConstant('{tmp}\' + AFileName), AArguments, '', SW_SHOW, ewWaitUntilTerminated, ErrorCode) then
    Exit;

  // 退出码 3010 表示安装成功但系统需要重启，同样视为成功
  if (ErrorCode <> 0) and (ErrorCode <> 3010) then
    Exit;

  Result := True;
end;

// 在文件复制前自动检测并补齐缺失的运行库
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorCode: Integer;
begin
  Result := '';

  if not IsDotNet9DesktopRuntimeInstalled then
  begin
    if not DownloadAndInstallRuntime(DotNetDesktopRuntimeUrl, DotNetRuntimeFileName, '/install /quiet /norestart', '.NET 9 Desktop Runtime (x64)') then
    begin
      if MsgBox(
        '自动下载并安装 .NET 9 Desktop Runtime (x64) 失败。' + #13#10 + #13#10 +
        '是否打开微软官网手动下载？安装完成后请重新运行 TaskbarInfo 安装程序。',
        mbConfirmation, MB_YESNO) = IDYES then
      begin
        ShellExec('open', DotNetDesktopRuntimeUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      end;

      Result := '缺少 .NET 9 Desktop Runtime (x64)，安装已中止。';
      Exit;
    end;

    if not IsDotNet9DesktopRuntimeInstalled then
    begin
      MsgBox(
        '.NET 9 Desktop Runtime 安装程序已执行，但系统仍未检测到该运行库。' + #13#10 +
        '请从微软官网手动安装后重新运行安装程序。',
        mbError, MB_OK);
      Result := '缺少 .NET 9 Desktop Runtime (x64)，安装已中止。';
      Exit;
    end;
  end;

  if not IsWindowsAppRuntime18Installed then
  begin
    if not DownloadAndInstallRuntime(WindowsAppRuntimeUrl, WindowsAppRuntimeFileName, '--quiet', 'Windows App Runtime 1.8 (x64)') then
    begin
      if MsgBox(
        '自动下载并安装 Windows App Runtime 1.8 (x64) 失败。' + #13#10 + #13#10 +
        '是否打开微软官网手动下载？安装完成后请重新运行 TaskbarInfo 安装程序。',
        mbConfirmation, MB_YESNO) = IDYES then
      begin
        ShellExec('open', WindowsAppRuntimeUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      end;

      Result := '缺少 Windows App Runtime 1.8 (x64)，安装已中止。';
      Exit;
    end;

    if not IsWindowsAppRuntime18Installed then
    begin
      MsgBox(
        'Windows App Runtime 1.8 安装程序已执行，但系统仍未检测到该运行库。' + #13#10 +
        '请从微软官网手动安装后重新运行安装程序。',
        mbError, MB_OK);
      Result := '缺少 Windows App Runtime 1.8 (x64)，安装已中止。';
      Exit;
    end;
  end;
end;
