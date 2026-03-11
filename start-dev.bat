@echo off
echo === SandboxRPG Dev Startup ===

echo [1/4] Starting SpacetimeDB (port 3000)...
start "SpacetimeDB" spacetime start --in-memory

echo Waiting for server to start...
timeout /t 3 /nobreak > nul

echo [2/4] Logging in to local server...
spacetime logout 2>nul
spacetime login --server-issued-login local --no-browser

echo [3/4] Publishing server module...
cd server
spacetime publish -b bin\Release\net8.0\wasi-wasm\AppBundle\StdbModule.wasm
cd ..

echo [4/4] Opening Godot editor...
set GODOT="C:\Users\Jonas\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe"
start "Godot" %GODOT% --path "C:\Users\Jonas\Documents\GodotGame\client" --editor

echo.
echo === Dev environment started! ===
echo Server: http://127.0.0.1:3000
echo Module: sandbox-rpg
echo.
echo To rebuild server after changes:
echo   cd server ^&^& spacetime build ^&^& spacetime publish -b bin\Release\net8.0\wasi-wasm\AppBundle\StdbModule.wasm
echo.
echo To regenerate client bindings:
echo   cd server ^&^& spacetime generate --lang csharp --out-dir ..\client\scripts\networking\SpacetimeDB --bin-path bin\Release\net8.0\wasi-wasm\AppBundle\StdbModule.wasm
pause
