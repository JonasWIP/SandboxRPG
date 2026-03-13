#!/usr/bin/env node
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { CallToolRequestSchema, ListToolsRequestSchema } from "@modelcontextprotocol/sdk/types.js";
import { execFileSync, execSync, spawn } from "child_process";
import { readFileSync, writeFileSync, unlinkSync, existsSync, createWriteStream } from "fs";
import { tmpdir } from "os";
import { join } from "path";

// ── Paths ─────────────────────────────────────────────────────────────────────
const HOME        = process.env.USERPROFILE;
const APPDATA     = process.env.APPDATA;
const LOCALAPPDATA = process.env.LOCALAPPDATA;
const PROJECT     = "C:\\Users\\Jonas\\Documents\\GodotGame";
const CLIENT      = `${PROJECT}\\client`;
const SERVER_DIR  = `${PROJECT}\\server`;
const CLI         = `${HOME}\\.local\\bin\\spacetimedb-cli.exe`;
const GODOT       = `${HOME}\\AppData\\Local\\Microsoft\\WinGet\\Packages\\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\\Godot_v4.6.1-stable_mono_win64\\Godot_v4.6.1-stable_mono_win64.exe`;
const WASM        = `${SERVER_DIR}\\bin\\Release\\net8.0\\wasi-wasm\\AppBundle\\StdbModule.wasm`;
const GODOT_LOG   = `${PROJECT}\\tools\\game-mcp\\godot.log`;
const STDB_DATA   = `${LOCALAPPDATA}\\SpacetimeDB\\data`;

// ── PowerShell runner ─────────────────────────────────────────────────────────
function ps(script) {
  const tmp = join(tmpdir(), `mcp_${Date.now()}.ps1`);
  writeFileSync(tmp, script, "utf8");
  try {
    return execFileSync("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", tmp], {
      encoding: "utf8",
      timeout: 15000,
    }).trim();
  } finally {
    try { unlinkSync(tmp); } catch {}
  }
}

// ── Shared PS snippets ────────────────────────────────────────────────────────
const PS_WIN32 = `
Add-Type @'
using System; using System.Runtime.InteropServices;
public class WinAPI {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
}
'@`;

function psGodotFocus() {
  return `${PS_WIN32}
$p = Get-Process | Where-Object { $_.ProcessName -like "*Godot*" } | Select-Object -First 1
if ($p -and $p.MainWindowHandle -ne [IntPtr]::Zero) {
  [WinAPI]::ShowWindow($p.MainWindowHandle, 9)
  [WinAPI]::SetForegroundWindow($p.MainWindowHandle)
  Start-Sleep -Milliseconds 400
}`;
}

// ── Tools ─────────────────────────────────────────────────────────────────────
const TOOLS = [
  {
    name: "screenshot",
    description: "Capture a screenshot of the screen. Pass focus_godot=true to focus the Godot window first.",
    inputSchema: {
      type: "object",
      properties: {
        focus_godot: { type: "boolean", description: "Focus the Godot window before capturing" },
      },
    },
  },
  {
    name: "server_status",
    description: "Check whether SpacetimeDB and Godot are currently running.",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "start_server",
    description: "Kill any old SpacetimeDB, wipe data, start fresh, login, and publish the module.",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "stop_server",
    description: "Kill the running SpacetimeDB server.",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "publish_module",
    description: "Build the server WASM module and publish it to the running SpacetimeDB.",
    inputSchema: {
      type: "object",
      properties: {
        build: { type: "boolean", description: "Run spacetime build first (default true)" },
      },
    },
  },
  {
    name: "start_godot",
    description: "Launch Godot. Optionally run a specific scene directly (skips editor).",
    inputSchema: {
      type: "object",
      properties: {
        scene: { type: "string", description: "Scene path e.g. res://scenes/Main.tscn. Omit to open editor." },
        editor: { type: "boolean", description: "Open the Godot editor (default false = run game)" },
      },
    },
  },
  {
    name: "stop_godot",
    description: "Kill all running Godot processes.",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "read_logs",
    description: "Read recent Godot game output captured since the last start_godot call.",
    inputSchema: {
      type: "object",
      properties: {
        lines: { type: "number", description: "Number of most-recent lines to return (default 80)" },
      },
    },
  },
  {
    name: "send_key",
    description: "Send keyboard input to the Godot window. Focuses it first.",
    inputSchema: {
      type: "object",
      properties: {
        keys: { type: "string", description: "SendKeys string e.g. 'w', '{ESC}', '^c' (Ctrl+C), '+i' (Shift+I)" },
        delay_ms: { type: "number", description: "Wait before sending (ms, default 0)" },
      },
      required: ["keys"],
    },
  },
  {
    name: "click",
    description: "Move the mouse to screen coordinates and click. Focuses Godot first.",
    inputSchema: {
      type: "object",
      properties: {
        x: { type: "number", description: "Screen X coordinate" },
        y: { type: "number", description: "Screen Y coordinate" },
        button: { type: "string", enum: ["left", "right", "middle"], description: "Mouse button (default left)" },
      },
      required: ["x", "y"],
    },
  },
];

// ── Tool handlers ─────────────────────────────────────────────────────────────
function handleScreenshot({ focus_godot }) {
  const script = `
${focus_godot ? psGodotFocus() : ""}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$s = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = [System.Drawing.Bitmap]::new($s.Width, $s.Height)
$g   = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($s.Location, [System.Drawing.Point]::Empty, $s.Size)
$path = "$env:TEMP\\claude_ss.png"
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output $path`;

  const path = ps(script);
  const data = readFileSync(path.trim()).toString("base64");
  return [{ type: "image", data, mimeType: "image/png" }];
}

function handleServerStatus() {
  const out = ps(`
$stdb  = Get-Process spacetimedb-standalone -ErrorAction SilentlyContinue
$godot = Get-Process | Where-Object { $_.ProcessName -like "*Godot*" } -ErrorAction SilentlyContinue
"SpacetimeDB : $(if($stdb)  {"RUNNING  pid=$($stdb.Id)"}  else {"STOPPED"})"
"Godot       : $(if($godot) {"RUNNING  pid=$($godot.Id)"} else {"STOPPED"})"
try {
  $r = Invoke-WebRequest http://127.0.0.1:3000/v1/ping -TimeoutSec 2 -UseBasicParsing
  "Server ping : OK ($($r.StatusCode))"
} catch { "Server ping : FAILED" }
`);
  return [{ type: "text", text: out }];
}

function handleStartServer() {
  // Kill + wipe
  ps(`
$p = Get-Process spacetimedb-standalone -ErrorAction SilentlyContinue
if ($p) { $p | Stop-Process -Force; Start-Sleep 1 }
Remove-Item -Recurse -Force "${STDB_DATA}" -ErrorAction SilentlyContinue
`);

  // Start server detached
  spawn(CLI, ["start", "--in-memory"], { detached: true, stdio: "ignore" }).unref();

  // Poll until ready (max 15s)
  const deadline = Date.now() + 15000;
  while (Date.now() < deadline) {
    try { execFileSync("curl", ["-sf", "http://127.0.0.1:3000/v1/ping"], { stdio: "ignore", timeout: 2000 }); break; }
    catch { execSync("ping -n 2 127.0.0.1 >nul", { shell: true }); }
  }

  // Login
  try { execFileSync(CLI, ["logout"], { stdio: "ignore" }); } catch {}
  const loginOut = execFileSync(CLI, ["login", "--server-issued-login", "local", "--no-browser"], { encoding: "utf8" });

  // Publish
  const pubOut = execFileSync(CLI, ["publish", "-b", WASM, "sandbox-rpg"], {
    cwd: SERVER_DIR, encoding: "utf8",
  });

  return [{ type: "text", text: `${loginOut}\n${pubOut}`.trim() }];
}

function handleStopServer() {
  const out = ps(`
$p = Get-Process spacetimedb-standalone -ErrorAction SilentlyContinue
if ($p) { $p | Stop-Process -Force; "Stopped SpacetimeDB (pid $($p.Id))" }
else     { "SpacetimeDB was not running" }
`);
  return [{ type: "text", text: out }];
}

function handlePublishModule({ build = true }) {
  let out = "";
  if (build) {
    out += execFileSync(CLI, ["build"], { cwd: SERVER_DIR, encoding: "utf8" });
  }
  out += execFileSync(CLI, ["publish", "-b", WASM, "sandbox-rpg"], {
    cwd: SERVER_DIR, encoding: "utf8",
  });
  return [{ type: "text", text: out.trim() }];
}

function handleStartGodot({ scene, editor }) {
  // Clear log
  writeFileSync(GODOT_LOG, "", "utf8");

  const args = ["--path", CLIENT];
  if (editor) args.push("--editor");
  else if (scene) args.push(scene);

  const log = createWriteStream(GODOT_LOG, { flags: "a" });
  const proc = spawn(GODOT, args, { detached: true, stdio: ["ignore", "pipe", "pipe"] });
  proc.stdout.pipe(log);
  proc.stderr.pipe(log);
  proc.unref();

  return [{ type: "text", text: `Godot started (pid ${proc.pid}). Log: ${GODOT_LOG}` }];
}

function handleStopGodot() {
  const out = ps(`
$procs = Get-Process | Where-Object { $_.ProcessName -like "*Godot*" }
if ($procs) {
  $procs | ForEach-Object { Stop-Process -Id $_.Id -Force; "Stopped Godot pid $($_.Id)" }
} else { "Godot was not running" }
`);
  return [{ type: "text", text: out }];
}

function handleReadLogs({ lines = 80 }) {
  if (!existsSync(GODOT_LOG)) return [{ type: "text", text: "(no log file yet — start the game first)" }];
  const all = readFileSync(GODOT_LOG, "utf8").split("\n");
  const tail = all.slice(-lines).join("\n");
  return [{ type: "text", text: tail || "(log is empty)" }];
}

function handleSendKey({ keys, delay_ms = 0 }) {
  const escaped = keys.replace(/'/g, "''");
  ps(`
${psGodotFocus()}
Add-Type -AssemblyName System.Windows.Forms
if (${delay_ms} -gt 0) { Start-Sleep -Milliseconds ${delay_ms} }
[System.Windows.Forms.SendKeys]::SendWait('${escaped}')
`);
  return [{ type: "text", text: `Sent keys: ${keys}` }];
}

function handleClick({ x, y, button = "left" }) {
  const btn = button === "right" ? "RightButton" : button === "middle" ? "MiddleButton" : "LeftButton";
  ps(`
${psGodotFocus()}
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new(${x}, ${y})
Start-Sleep -Milliseconds 100
$t = [System.Windows.Forms.MouseEventArgs]
Add-Type @'
using System; using System.Runtime.InteropServices;
public class Mouse {
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int x, int y, int d, int e);
}
'@
$down = $(if("${btn}" -eq "RightButton"){8}elseif("${btn}" -eq "MiddleButton"){32}else{2})
$up   = $(if("${btn}" -eq "RightButton"){16}elseif("${btn}" -eq "MiddleButton"){64}else{4})
[Mouse]::mouse_event($down, 0, 0, 0, 0)
Start-Sleep -Milliseconds 50
[Mouse]::mouse_event($up, 0, 0, 0, 0)
`);
  return [{ type: "text", text: `Clicked ${button} at (${x}, ${y})` }];
}

// ── MCP Server ────────────────────────────────────────────────────────────────
const server = new Server(
  { name: "sandboxrpg-game-dev", version: "1.0.0" },
  { capabilities: { tools: {} } }
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({ tools: TOOLS }));

server.setRequestHandler(CallToolRequestSchema, async (req) => {
  const { name, arguments: args = {} } = req.params;
  try {
    let content;
    switch (name) {
      case "screenshot":     content = handleScreenshot(args);      break;
      case "server_status":  content = handleServerStatus();        break;
      case "start_server":   content = handleStartServer();         break;
      case "stop_server":    content = handleStopServer();          break;
      case "publish_module": content = handlePublishModule(args);   break;
      case "start_godot":    content = handleStartGodot(args);      break;
      case "stop_godot":     content = handleStopGodot();           break;
      case "read_logs":      content = handleReadLogs(args);        break;
      case "send_key":       content = handleSendKey(args);         break;
      case "click":          content = handleClick(args);           break;
      default: return { content: [{ type: "text", text: `Unknown tool: ${name}` }], isError: true };
    }
    return { content };
  } catch (err) {
    return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
  }
});

const transport = new StdioServerTransport();
await server.connect(transport);
