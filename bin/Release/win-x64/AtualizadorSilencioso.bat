@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM Testa conexão com internet
ping -n 1 8.8.8.8 >nul 2>&1
if errorlevel 1 exit /b 0

REM 0 Verificar sincronização da biblioteca Power BI
echo [0/8] Verificando pasta Power BI local...

REM Detectar raiz do OneDrive
set "ODROOT="
if defined OneDriveCommercial set "ODROOT=%OneDriveCommercial%"
if not defined ODROOT if defined OneDrive       set "ODROOT=%OneDrive%"
if not defined ODROOT                           set "ODROOT=%USERPROFILE%\OneDrive - ONE ENGENHARIA INDUSTRIA E COMERCIO LTDA"

REM Possíveis caminhos da biblioteca
set "PBI1=%ODROOT%\ONE Engenharia\Power BI"
set "PBI2=%ODROOT%\OneEngenharia\Power BI"
set "PBI3=%USERPROFILE%\ONE ENGENHARIA INDUSTRIA E COMERCIO LTDA\ONE Engenharia - Power BI"

if exist "%PBI1%" (
  set "PBI_FOLDER=%PBI1%"
) else if exist "%PBI2%" (
  set "PBI_FOLDER=%PBI2%"
) else if exist "%PBI3%" (
  set "PBI_FOLDER=%PBI3%"
) else (
  set "PBI_FOLDER=%PBI1%"
)

echo Pasta final: "%PBI_FOLDER%"
echo.

REM 1 Buscar metadados da release
echo [1/8] Buscando metadados da release...
set "API_URL=https://api.github.com/repos/loboczss/OrganizadorArquivosWPF/releases/latest"
set "TEMP_DIR=%TEMP%\OneEngUpdater"
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"
mkdir "%TEMP_DIR%"
curl -s -H "User-Agent:OneEngUpdater" "%API_URL%" > "%TEMP_DIR%\release.json"
if not exist "%TEMP_DIR%\release.json" exit /b 1

REM 2 Extrair versão remota
for /f "usebackq delims=" %%V in (
  `powershell -NoProfile -Command "(Get-Content '%TEMP_DIR%\release.json' | ConvertFrom-Json).tag_name.TrimStart('v')"`
) do set "REMOTE_VER=%%V"

REM 3 Extrair versão local
set "INSTALL_DIR=%LocalAppData%\OneEngRenamer\OrganizadorArquivosWPF"
set "APP=OrganizadorArquivosWPF.exe"
if exist "%INSTALL_DIR%\%APP%" (
  for /f "usebackq delims=" %%L in (
    `powershell -NoProfile -Command "[System.Reflection.AssemblyName]::GetAssemblyName('%INSTALL_DIR%\%APP%').Version.ToString()"`
  ) do set "LOCAL_VER=%%L"
) else (
  set "LOCAL_VER=0.0.0.0"
)

echo Versao instalada: v!LOCAL_VER!
echo Versao remota   : v!REMOTE_VER!
echo.

REM Remover espaços extras
for %%# in ("!LOCAL_VER!")  do set "LOCAL_VER=%%~#"
for %%# in ("!REMOTE_VER!") do set "REMOTE_VER=%%~#"

REM Comparar versões (0=igual ou acima, 10=nova disponível, 1=erro)
powershell -NoProfile -Command ^
  "$l='!LOCAL_VER!';$r='!REMOTE_VER!';try{$lv=[Version]$l;$rv=[Version]$r}catch{exit 1};if($lv -lt $rv){exit 10}else{exit 0}"
set "cmp=%errorlevel%"

if "%cmp%"=="1" exit /b 1
if "%cmp%"=="0" exit /b 0

REM 4 Download dinâmico
echo [2/8] Baixando release v!REMOTE_VER!...
for /f "usebackq delims=" %%A in (
  `powershell -NoProfile -Command "(Get-Content '%TEMP_DIR%\release.json' | ConvertFrom-Json).assets | Where-Object{ $_.name -like '*-full.zip' } | Select-Object -First 1 | ForEach-Object{ $_.browser_download_url }"`
) do set "ZIP_URL=%%A"
if not defined ZIP_URL exit /b 1
set "ZIP_FILE=%TEMP_DIR%\update.zip"
curl -L -o "%ZIP_FILE%" "%ZIP_URL%"
if errorlevel 1 exit /b 1

REM 5 Extrair release
echo [3/8] Extraindo...
set "EXTRACT_DIR=%TEMP_DIR%\extracted"
if exist "%EXTRACT_DIR%" rmdir /s /q "%EXTRACT_DIR%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%ZIP_FILE%' -DestinationPath '%EXTRACT_DIR%' -Force"
if errorlevel 1 exit /b 1

REM 6 Instalar em AppData
echo [4/8] Instalando em AppData...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
robocopy "%EXTRACT_DIR%" "%INSTALL_DIR%" /MIR /R:3 /W:1 >nul

REM 7 Limpar temporários
echo [5/8] Limpando temporários...
rd /s /q "%EXTRACT_DIR%"
del /q "%ZIP_FILE%"
del /q "%TEMP_DIR%\release.json"

REM 8 Atualizar atalho
echo [6/8] Atualizando atalho...
set "SHORTCUT_DIR=%USERPROFILE%\Documents\CompillerLog"
set "SHORTCUT_LOG=%SHORTCUT_DIR%\shortcut.log"
set "SHORTCUT_NAME=CompillerLog.lnk"
if exist "%SHORTCUT_LOG%" (
  set /p OLD_NAME=<"%SHORTCUT_LOG%"
  if exist "%USERPROFILE%\Desktop\!OLD_NAME!" del /q "%USERPROFILE%\Desktop\!OLD_NAME!"
)
if not exist "%SHORTCUT_DIR%" mkdir "%SHORTCUT_DIR%"
for /f "usebackq delims=" %%D in (
  `powershell -NoProfile -Command "[Environment]::GetFolderPath('Desktop')"`
) do set "DESKTOP=%%D"
set "LINK_LNK=%DESKTOP%\%SHORTCUT_NAME%"
set "TARGET=%INSTALL_DIR%\%APP%"
powershell -NoProfile -Command "try{$s=New-Object -ComObject WScript.Shell;$l=$s.CreateShortcut('%LINK_LNK%');$l.TargetPath='%TARGET%';$l.WorkingDirectory='%INSTALL_DIR%';$l.Save();exit 0}catch{exit 1}"
if errorlevel 1 (
  (
    echo [InternetShortcut]
    echo URL=file:///%TARGET:\=/% 
    echo IconFile=%TARGET%
    echo IconIndex=0
  )>"%DESKTOP%\CompillerLog.url"
)
echo %SHORTCUT_NAME%>"%SHORTCUT_LOG%"

REM 9 Iniciar aplicativo e fechar
start "" "%TARGET%"
exit /b 0
