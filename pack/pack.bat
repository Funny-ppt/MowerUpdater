@echo off
copy /Y "..\bin\Release\*" "bin\"

del archive.7z
del MowerUpdater.exe

cd bin
"../7zr.exe" a ../archive.7z *
cd ..

copy /Y /B 7zSD.sfx + config.txt + archive.7z MowerUpdater.exe
pause >nul