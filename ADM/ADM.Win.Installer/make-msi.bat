@echo off
setlocal
if not defined BUILD_VER set "BUILD_VER=1.0.4"
if not defined PRODUCT_UPGRADE_CODE set "PRODUCT_UPGRADE_CODE=741CBBE2-3911-4192-A051-AFF85038EFD7"

DEL /s /q *.wixobj 2>NUL
DEL /s /q net4.7.2.wxs 2>NUL
RMDIR /S /Q BIN\NET472 2>NUL
RMDIR /S /Q WIXOBJ\NET472 2>NUL

MKDIR BIN 2>NUL
MKDIR BIN\NET472 2>NUL
MKDIR BIN\NET472\ADM.App.Host 2>NUL
MKDIR WIXOBJ\NET472 2>NUL

dotnet build ..\ADM.Wpf.UI\ADM.Wpf.UI.csproj -c Release -f net4.7.2 -o BIN\NET472 || exit /b 1
dotnet build ..\ADM.WinForms.IntegrationUI\ADM.WinForms.IntegrationUI.csproj -c Release -f net4.7.2 -o BIN\NET472 || exit /b 1
dotnet build ..\ADM.App.Host\ADM.App.Host.csproj -c Release -f net4.7.2 -o BIN\NET472\ADM.App.Host || exit /b 1

heat dir BIN\NET472 -o net4.7.2.wxs -scom -frag -srd -sreg -gg -cg NET4 -dr INSTALLFOLDER || exit /b 1
candle product.wxs net4.7.2.wxs -o WIXOBJ\NET472\ || exit /b 1
light -ext WixUIExtension -ext WixUtilExtension -cultures:en-us WIXOBJ\NET472\product.wixobj WIXOBJ\NET472\net4.7.2.wixobj -b BIN\NET472 -out admsetup-%BUILD_VER%.msi || exit /b 1

endlocal
