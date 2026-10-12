Unicode true
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"

!ifndef VERSION
!error "VERSION must be provided"
!endif
!ifndef PAYLOAD
!error "PAYLOAD must be provided"
!endif
!ifndef OUTPUT
!error "OUTPUT must be provided"
!endif

Name "Cut the Save ${VERSION}"
OutFile "${OUTPUT}"
InstallDir "$PROGRAMFILES64\tianqistudio\Cut the Save"
RequestExecutionLevel admin
SetCompressor /SOLID zlib
ManifestDPIAware true
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=2052 "ProductName" "Cut the Save"
VIAddVersionKey /LANG=2052 "CompanyName" "tianqistudio"
VIAddVersionKey /LANG=2052 "FileDescription" "Cut the Save 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${VERSION}.0"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright tianqistudio"
ShowInstDetails show
ShowUninstDetails show
BrandingText "tianqistudio"
!define MUI_ABORTWARNING
; makensis normally changes to this script's directory, including build-source/installer.
; Callers using /NOCD can supply /DCTS_ICON=<absolute icon path> explicitly.
!ifndef CTS_ICON
!define CTS_ICON "..\assets\icons\cut-the-save.ico"
!endif
!define MUI_ICON "${CTS_ICON}"
!define MUI_UNICON "${CTS_ICON}"
!insertmacro MUI_PAGE_WELCOME
Page custom ShortcutPage ShortcutPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"

Var DesktopChoice
Var StartMenuChoice
Var DesktopCheck
Var StartMenuCheck
Var Dialog
Var PowerShell

Function .onInit
    ${IfNot} ${RunningX64}
        MessageBox MB_ICONSTOP "Cut the Save 需要 64 位 Windows。"
        Abort
    ${EndIf}
    SetRegView 64
    SetShellVarContext all
    StrCpy $PowerShell "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
    StrCpy $DesktopChoice 1
    StrCpy $StartMenuChoice 1
    ClearErrors
    ReadRegDWORD $0 HKLM "SOFTWARE\tianqistudio\CutTheSave" "DesktopShortcut"
    ${IfNot} ${Errors}
        ${If} $0 == 0
        ${OrIf} $0 == 1
            StrCpy $DesktopChoice $0
        ${EndIf}
    ${EndIf}
    ClearErrors
    ReadRegDWORD $0 HKLM "SOFTWARE\tianqistudio\CutTheSave" "StartMenuShortcut"
    ${IfNot} ${Errors}
        ${If} $0 == 0
        ${OrIf} $0 == 1
            StrCpy $StartMenuChoice $0
        ${EndIf}
    ${EndIf}
    ${GetParameters} $1
    ClearErrors
    ${GetOptions} $1 "/DESKTOP=" $0
    ${IfNot} ${Errors}
        ${If} $0 != 0
        ${AndIf} $0 != 1
            MessageBox MB_ICONSTOP "/DESKTOP 只接受 0 或 1。"
            Abort
        ${EndIf}
        StrCpy $DesktopChoice $0
    ${EndIf}
    ClearErrors
    ${GetOptions} $1 "/STARTMENU=" $0
    ${IfNot} ${Errors}
        ${If} $0 != 0
        ${AndIf} $0 != 1
            MessageBox MB_ICONSTOP "/STARTMENU 只接受 0 或 1。"
            Abort
        ${EndIf}
        StrCpy $StartMenuChoice $0
    ${EndIf}
FunctionEnd

Function ShortcutPage
    !insertmacro MUI_HEADER_TEXT "快捷方式" "安装与修复会保存并尊重以下选择。"
    nsDialogs::Create 1018
    Pop $Dialog
    ${If} $Dialog == error
        Abort
    ${EndIf}
    ${NSD_CreateLabel} 0 0 100% 28u "程序将安装到：$PROGRAMFILES64\tianqistudio\Cut the Save。$\r$\n用户配置、存档、备份与 RTSS 恢复数据均保留。"
    Pop $0
    ${NSD_CreateCheckbox} 0 42u 100% 12u "创建桌面快捷方式"
    Pop $DesktopCheck
    ${NSD_SetState} $DesktopCheck $DesktopChoice
    ${NSD_CreateCheckbox} 0 64u 100% 12u "创建开始菜单快捷方式（tianqistudio）"
    Pop $StartMenuCheck
    ${NSD_SetState} $StartMenuCheck $StartMenuChoice
    nsDialogs::Show
FunctionEnd

Function ShortcutPageLeave
    ${NSD_GetState} $DesktopCheck $DesktopChoice
    ${NSD_GetState} $StartMenuCheck $StartMenuChoice
FunctionEnd

Section "安装或修复"
    InitPluginsDir
    SetOutPath "$PLUGINSDIR\payload"
    File /r "${PAYLOAD}\*"
    WriteUninstaller "$PLUGINSDIR\Uninstall.exe"
    DetailPrint "正在校验文件、检查占用并准备可回滚的安装。"
    nsExec::ExecToStack '"$PowerShell" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\payload\Maintenance.ps1" -Mode Install -PayloadDirectory "$PLUGINSDIR\payload" -UninstallerPath "$PLUGINSDIR\Uninstall.exe" -Version "${VERSION}" -DesktopShortcut $DesktopChoice -StartMenuShortcut $StartMenuChoice'
    Pop $0
    Pop $1
    DetailPrint "$1"
    ${If} $0 != 0
        SetErrorLevel 1
        MessageBox MB_ICONSTOP "安装未完成。请查看详细信息。$\r$\n$1" /SD IDOK
        Abort
    ${EndIf}
SectionEnd

Function un.onInit
    ${IfNot} ${RunningX64}
        Abort
    ${EndIf}
    SetRegView 64
    SetShellVarContext all
    StrCpy $PowerShell "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
FunctionEnd

Section "Uninstall"
    InitPluginsDir
    SetOutPath "$PLUGINSDIR"
    File /oname=Maintenance.ps1 "${PAYLOAD}\Maintenance.ps1"
    DetailPrint "仅移除本产品登记的程序文件及快捷方式；保留所有用户数据。"
    nsExec::ExecToStack '"$PowerShell" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Maintenance.ps1" -Mode Uninstall'
    Pop $0
    Pop $1
    DetailPrint "$1"
    ${If} $0 != 0
        SetErrorLevel 1
        MessageBox MB_ICONSTOP "卸载未完成。请查看详细信息。$\r$\n$1" /SD IDOK
        Abort
    ${EndIf}
SectionEnd
