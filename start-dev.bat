@echo off
SET CLI=%USERPROFILE%\.local\bin\spacetimedb-cli.exe
SET GODOT="C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe"

echo Stopping old server...
taskkill /IM spacetimedb-standalone.exe /F >nul 2>&1
rmdir /s /q "%LOCALAPPDATA%\SpacetimeDB\data" >nul 2>&1

echo Starting SpacetimeDB...
start "SpacetimeDB" "%CLI%" start --in-memory

:WAIT
timeout /t 1 /nobreak >nul
"%SystemRoot%\System32\curl.exe" -sf http://127.0.0.1:3000/v1/ping >nul 2>&1
if errorlevel 1 goto WAIT

echo Publishing module...
"%CLI%" logout >nul 2>&1
"%CLI%" login --server-issued-login local --no-browser
cd server && "%CLI%" publish -b bin\Release\net8.0\wasi-wasm\AppBundle\StdbModule.wasm sandbox-rpg && cd ..

echo Opening Godot...
start "Godot" %GODOT% --path "C:\Users\Jonas\Documents\GodotGame\client" --editor
