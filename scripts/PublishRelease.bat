@echo off
setlocal
cd /d "%~dp0.."

echo =======================================================
echo    ChessAnalysis - Building Standalone Release (.EXE)
echo =======================================================
echo.

echo 1. Building Single-File Release (.EXE) [dist\ChessAnalysis_SingleFile]...
dotnet publish ChessAnalysis\ChessAnalysis.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist\ChessAnalysis_SingleFile
if errorlevel 1 goto ERROR

if exist "dist\ChessAnalysis_SingleFile\*.pdb" del /f /q "dist\ChessAnalysis_SingleFile\*.pdb"
if exist "dist\ChessAnalysis_SingleFile\stockfish" rmdir /s /q "dist\ChessAnalysis_SingleFile\stockfish"

echo.
echo =======================================================
echo [SUCCESS] Single-File Release successfully created!
echo.
echo Executable: dist\ChessAnalysis_SingleFile\ChessAnalysis.exe
echo.
echo Stockfish engine excluded (download from https://stockfishchess.org/download/ to C:\Stockfish\)
echo =======================================================
goto END

:ERROR
echo.
echo =======================================================
echo [ERROR] Build failed. Please review error output above.
echo =======================================================

:END
if "%1"=="--no-pause" goto NO_PAUSE
pause
:NO_PAUSE
endlocal
