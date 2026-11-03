@echo off
cd /d "%~dp0.."
echo Building Release...
dotnet build ChessAnalysis\ChessAnalysis.csproj -c Release
if %ERRORLEVEL% equ 0 (
    echo Starting ChessAnalysis...
    start "" "ChessAnalysis\bin\Release\net10.0-windows\ChessAnalysis.exe"
) else (
    echo Build failed!
    pause
)
