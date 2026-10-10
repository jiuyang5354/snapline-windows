Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "Sections.nsh"
!include "WinVer.nsh"

Name "Snapline ${APP_VERSION}"
OutFile "${OUTPUT_FILE}"
!ifdef QA_ROOT
  InstallDir "${QA_ROOT}\Default Install"
!else
  InstallDir "$LOCALAPPDATA\Programs\Snapline"
!endif
RequestExecutionLevel user
SetCompressor zlib
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=2052 "ProductName" "Snapline Windows 安装包"
VIAddVersionKey /LANG=2052 "FileDescription" "Snapline 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Windows: jiuyang5354; Tendedero: Alejandro Bujan"

!ifdef QA_ROOT
  !define UNINSTALL_KEY "Software\Snapline-InstallerTests\App"
  !define RUN_KEY "Software\Snapline-InstallerTests\Run"
!else
  !define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Snapline"
  !define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!endif
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安装 Snapline 截图晾衣绳"
!define MUI_WELCOMEPAGE_TEXT "将 Snapline 安装到当前用户的程序文件夹。已安装时默认沿用原目录，自动退出该位置的旧版并覆盖程序文件，保留截图和设置。$\r$\n$\r$\n桌面快捷方式默认勾选，可在下一页取消。开机启动由程序单独控制，默认关闭。$\r$\n$\r$\nTendedero 的非官方 Windows 移植版。原作者：Alejandro Buján；Windows 版：jiuyang5354。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${PAYLOAD_DIR}\LICENSE"
!define MUI_COMPONENTSPAGE_TEXT_TOP "选择快捷方式。Snapline 程序和开始菜单入口为必需项。"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\Snapline.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!define MUI_FINISHPAGE_RUN_TEXT "运行 Snapline"
!insertmacro MUI_PAGE_FINISH
!define MUI_UNCONFIRMPAGE_TEXT_TOP "卸载 Snapline 程序及其快捷方式。截图和设置仍保留在原来的数据目录。请先从托盘退出程序。"
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"

Var DesktopFolder
Var ProgramsFolder
Var PreviousDesktop
Var PreviousInstallDir

Section "Snapline 程序与开始菜单入口" AppSection
  SectionIn RO
  Call CloseRunning
  SetOverwrite on
  SetOutPath "$INSTDIR"
  File "${PAYLOAD_DIR}\Snapline.exe"
  File "${PAYLOAD_DIR}\Snapline.exe.config"
  File "${PAYLOAD_DIR}\README.zh-CN.md"
  File "${PAYLOAD_DIR}\QA.md"
  File "${PAYLOAD_DIR}\preview.png"
  File "${PAYLOAD_DIR}\LICENSE"
  File "${PAYLOAD_DIR}\UPSTREAM-LICENSE.txt"
  File "${PAYLOAD_DIR}\NOTICE.md"
  File "${INSTALLER_README}"
  ReadINIStr $PreviousDesktop "$INSTDIR\installer.ini" "Shortcuts" "Desktop"
  WriteINIStr "$INSTDIR\installer.ini" "Shortcuts" "Desktop" "0"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$ProgramsFolder\Snapline"
  ${If} $PreviousInstallDir != $INSTDIR
  ${OrIfNot} ${FileExists} "$ProgramsFolder\Snapline\Snapline.lnk"
    CreateShortcut "$ProgramsFolder\Snapline\Snapline.lnk" "$INSTDIR\Snapline.exe" "" "$INSTDIR\Snapline.exe" 0
  ${EndIf}
  CreateShortcut "$ProgramsFolder\Snapline\卸载 Snapline.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Snapline 截图晾衣绳"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "jiuyang5354 (Tendedero: Alejandro Bujan)"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Snapline.exe,0"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "https://github.com/jiuyang5354/snapline-windows"
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section "创建桌面快捷方式" DesktopSection
  ClearErrors
  ${If} $PreviousInstallDir != $INSTDIR
  ${OrIfNot} ${FileExists} "$DesktopFolder\Snapline.lnk"
    CreateShortcut "$DesktopFolder\Snapline.lnk" "$INSTDIR\Snapline.exe" "" "$INSTDIR\Snapline.exe" 0
  ${EndIf}
  IfErrors +2
  WriteINIStr "$INSTDIR\installer.ini" "Shortcuts" "Desktop" "1"
SectionEnd

Section "-Finish"
  ReadINIStr $0 "$INSTDIR\installer.ini" "Shortcuts" "Desktop"
  ${If} $PreviousDesktop == "1"
  ${AndIf} $0 == "0"
    Delete "$DesktopFolder\Snapline.lnk"
  ${EndIf}
SectionEnd

Function .onInit
  SetShellVarContext current
  ReadRegStr $PreviousInstallDir HKCU "${UNINSTALL_KEY}" "InstallLocation"
  StrCpy $DesktopFolder "$DESKTOP"
  StrCpy $ProgramsFolder "$SMPROGRAMS"
!ifdef QA_ROOT
  ; The QA build redirects shortcuts and uses private test registry keys.
  StrCpy $DesktopFolder "${QA_ROOT}\Desktop"
  StrCpy $ProgramsFolder "${QA_ROOT}\Start Menu"
  CreateDirectory "$DesktopFolder"
  CreateDirectory "$ProgramsFolder"
!endif
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "Snapline 需要 Windows 10 或 Windows 11。" /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
  ReadRegDWORD $0 HKLM "Software\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 528040
    MessageBox MB_OK|MB_ICONSTOP "请先安装 .NET Framework 4.8 或更高版本，再运行 Snapline 安装包。" /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/NODESKTOP" $1
  ${IfNot} ${Errors}
    !insertmacro UnselectSection ${DesktopSection}
  ${EndIf}
FunctionEnd

Function CloseRunning
  GetFullPathName $8 "$INSTDIR\Snapline.exe"
close_retry:
  StrCpy $0 0
close_next:
  ; Match the full executable path before ending its WPF message loop.
  System::Call 'user32::FindWindowExW(p -3, p r0, p 0, w "Snapline messages") p .r0'
  ${If} $0 == 0
    Return
  ${EndIf}
  System::Call 'user32::GetWindowThreadProcessId(p r0, *i .r1) i .r2'
  System::Call 'kernel32::OpenProcess(i 0x101000, i 0, i r1) p .r3'
  ${If} $3 != 0
    StrCpy $5 ${NSIS_MAX_STRLEN}
    System::Call 'kernel32::QueryFullProcessImageNameW(p r3, i 0, w .r4, *i r5) i .r6'
    ${If} $6 != 0
    ${AndIf} $4 == $8
      System::Call 'user32::PostThreadMessageW(i r2, i 0x0012, p 0, p 0)'
      System::Call 'kernel32::WaitForSingleObject(p r3, i 5000) i .r7'
      System::Call 'kernel32::CloseHandle(p r3)'
      ${If} $7 != 0
        MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "旧版 Snapline 尚未退出。请从托盘退出后重试，或取消本次安装。" /SD IDCANCEL IDRETRY close_retry
        SetErrorLevel 3
        Abort
      ${EndIf}
      ; The window just closed; restart enumeration for other data-dir instances.
      Goto close_retry
    ${EndIf}
    System::Call 'kernel32::CloseHandle(p r3)'
  ${EndIf}
  Goto close_next
FunctionEnd

Function un.onInit
  SetShellVarContext current
  StrCpy $DesktopFolder "$DESKTOP"
  StrCpy $ProgramsFolder "$SMPROGRAMS"
!ifdef QA_ROOT
  StrCpy $DesktopFolder "${QA_ROOT}\Desktop"
  StrCpy $ProgramsFolder "${QA_ROOT}\Start Menu"
!endif
FunctionEnd

Section "Uninstall"
  ReadINIStr $0 "$INSTDIR\installer.ini" "Shortcuts" "Desktop"
  ${If} $0 == "1"
    Delete "$DesktopFolder\Snapline.lnk"
  ${EndIf}
  Delete "$ProgramsFolder\Snapline\Snapline.lnk"
  Delete "$ProgramsFolder\Snapline\卸载 Snapline.lnk"
  RMDir "$ProgramsFolder\Snapline"
  ReadRegStr $0 HKCU "${RUN_KEY}" "Snapline"
  StrLen $1 '"$INSTDIR\Snapline.exe"'
  StrCpy $0 $0 $1
  ${If} $0 == '"$INSTDIR\Snapline.exe"'
    DeleteRegValue HKCU "${RUN_KEY}" "Snapline"
  ${EndIf}
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  Delete "$INSTDIR\Snapline.exe"
  Delete "$INSTDIR\Snapline.exe.config"
  Delete "$INSTDIR\README.zh-CN.md"
  Delete "$INSTDIR\QA.md"
  Delete "$INSTDIR\preview.png"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\UPSTREAM-LICENSE.txt"
  Delete "$INSTDIR\NOTICE.md"
  Delete "$INSTDIR\INSTALLER.zh-CN.md"
  Delete "$INSTDIR\installer.ini"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
