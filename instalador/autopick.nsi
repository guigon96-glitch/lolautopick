; Instalador do AutoPick LoL (NSIS). Compilar: makensis -DVERSAO=1.0.0 instalador/autopick.nsi
; Instala só para o usuário (sem administrador), em %LOCALAPPDATA%\Programs\AutoPickLoL.
; Modo silencioso (/S) é usado pelo próprio app para se atualizar: fecha o app, troca o .exe e abre de novo.
Unicode true
!ifndef VERSAO
  !define VERSAO "0.0.0"
!endif
!define NOME "AutoPick LoL"
!define CHAVE "Software\Microsoft\Windows\CurrentVersion\Uninstall\AutoPickLoL"

Name "${NOME} ${VERSAO}"
OutFile "../AutoPickLoL-Setup.exe"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\AutoPickLoL"
InstallDirRegKey HKCU "Software\AutoPickLoL" "Pasta"
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!define MUI_FINISHPAGE_RUN "$INSTDIR\AutoPickLoL.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Abrir o AutoPick LoL"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "PortugueseBR"

Section "AutoPick LoL"
  ; fecha o app se estiver aberto (na atualização o .exe precisa ser trocado)
  nsExec::Exec 'taskkill /IM AutoPickLoL.exe /F'
  Sleep 500
  SetOutPath "$INSTDIR"
  File "../AutoPickLoL.exe"
  WriteUninstaller "$INSTDIR\Desinstalar.exe"
  CreateShortCut "$SMPROGRAMS\${NOME}.lnk" "$INSTDIR\AutoPickLoL.exe"
  CreateShortCut "$DESKTOP\${NOME}.lnk" "$INSTDIR\AutoPickLoL.exe"
  WriteRegStr HKCU "Software\AutoPickLoL" "Pasta" "$INSTDIR"
  WriteRegStr HKCU "${CHAVE}" "DisplayName" "${NOME}"
  WriteRegStr HKCU "${CHAVE}" "DisplayVersion" "${VERSAO}"
  WriteRegStr HKCU "${CHAVE}" "Publisher" "AutoPick LoL"
  WriteRegStr HKCU "${CHAVE}" "DisplayIcon" "$INSTDIR\AutoPickLoL.exe"
  WriteRegStr HKCU "${CHAVE}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${CHAVE}" "UninstallString" '"$INSTDIR\Desinstalar.exe"'
  WriteRegDWORD HKCU "${CHAVE}" "NoModify" 1
  WriteRegDWORD HKCU "${CHAVE}" "NoRepair" 1
  ; atualização automática: reabre o app (no modo normal a página final oferece abrir)
  IfSilent 0 +2
    Exec '"$INSTDIR\AutoPickLoL.exe"'
SectionEnd

Section "Uninstall"
  nsExec::Exec 'taskkill /IM AutoPickLoL.exe /F'
  Sleep 500
  Delete "$INSTDIR\AutoPickLoL.exe"
  Delete "$INSTDIR\Desinstalar.exe"
  Delete "$INSTDIR\autopick.log"
  RMDir /r "$INSTDIR\icones"
  ; config.json (suas listas e runas) fica, para não perder nada se reinstalar
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${NOME}.lnk"
  Delete "$DESKTOP\${NOME}.lnk"
  DeleteRegKey HKCU "${CHAVE}"
  DeleteRegKey HKCU "Software\AutoPickLoL"
SectionEnd
