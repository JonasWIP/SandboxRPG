#!/usr/bin/env node
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { CallToolRequestSchema, ListToolsRequestSchema } from "@modelcontextprotocol/sdk/types.js";
import { execFileSync, execSync, spawn } from "child_process";
import { readFileSync, writeFileSync, unlinkSync, existsSync, createWriteStream } from "fs";
import { tmpdir } from "os";
import { join } from "path";

// Screenshot output width — coordinates you pass to click() must be in this space
const SS_TARGET_WIDTH = 1280;

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

// Win32 types for window capture + input without focus
const PS_WINCAP = `
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Drawing;
public class WinCap {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
}
'@
Add-Type -AssemblyName System.Drawing`;

// Track the PID of the Godot process we launched
let _godotPid = null;

// Returns PS snippet that sets $hw to the Godot window handle (or empty-handle if not found)
function psGetWindow() {
  return _godotPid
    ? `$_proc = Get-Process -Id ${_godotPid} -ErrorAction SilentlyContinue`
    : `$_proc = Get-Process | Where-Object { $_.MainWindowTitle -like '*SandboxRPG*' } | Select-Object -First 1`;
}

// ── Tools ─────────────────────────────────────────────────────────────────────
const TOOLS = [
  {
    name: "screenshot",
    description: "Capture a screenshot of the Godot game window only (no focus needed — works in background).",
    inputSchema: { type: "object", properties: {} },
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
    description: "Send keyboard input to the Godot window without focusing it. Supports single keys ('w', 'esc', 'space', 'enter', 'tab', '1'-'9'), modifier combos ('^c' Ctrl+C, '+i' Shift+I, '%f4' Alt+F4), and arrow/function keys ('left','right','up','down','f1'-'f12','back','delete').",
    inputSchema: {
      type: "object",
      properties: {
        keys: { type: "string", description: "Key string e.g. 'w', 'esc', '^c' (Ctrl+C), '+i' (Shift+I), '%f4' (Alt+F4), 'f1', 'left'" },
        delay_ms: { type: "number", description: "Wait before sending (ms, default 0)" },
      },
      required: ["keys"],
    },
  },
  {
    name: "click",
    description: "Send a mouse click to the Godot window without focusing it. Coordinates are in the game's client area scaled to 1280px wide (same space as screenshot).",
    inputSchema: {
      type: "object",
      properties: {
        x: { type: "number", description: "X coordinate in screenshot space (0-1280)" },
        y: { type: "number", description: "Y coordinate in screenshot space" },
        button: { type: "string", enum: ["left", "right", "middle"], description: "Mouse button (default left)" },
      },
      required: ["x", "y"],
    },
  },
];

// ── Tool handlers ─────────────────────────────────────────────────────────────
function handleScreenshot(_args) {
  const imgPath = `${process.env.TEMP}\\claude_ss.png`;
  const tw = SS_TARGET_WIDTH;
  const imgPathPs = imgPath.replace(/\\/g, "\\\\");

  ps(`
${PS_WINCAP}
${psGetWindow()}
if ($_proc -and $_proc.MainWindowHandle -ne [IntPtr]::Zero) {
  $hw = $_proc.MainWindowHandle
  # Capture full window via PrintWindow (works even when behind other windows)
  $wr = New-Object WinCap+RECT
  [WinCap]::GetWindowRect($hw, [ref]$wr) | Out-Null
  $winW = $wr.R - $wr.L; $winH = $wr.B - $wr.T
  $wbmp = New-Object System.Drawing.Bitmap $winW, $winH
  $wg   = [System.Drawing.Graphics]::FromImage($wbmp)
  $hdc  = $wg.GetHdc()
  [WinCap]::PrintWindow($hw, $hdc, 2) | Out-Null   # 2 = PW_RENDERFULLCONTENT
  $wg.ReleaseHdc($hdc); $wg.Dispose()
  # Crop to client area only (strips title bar / window chrome)
  $cr  = New-Object WinCap+RECT
  [WinCap]::GetClientRect($hw, [ref]$cr) | Out-Null
  $pt  = New-Object WinCap+POINT; $pt.X = 0; $pt.Y = 0
  [WinCap]::ClientToScreen($hw, [ref]$pt) | Out-Null
  $ox  = $pt.X - $wr.L; $oy = $pt.Y - $wr.T
  $cw  = $cr.R - $cr.L; $ch  = $cr.B - $cr.T
  $src = New-Object System.Drawing.Rectangle $ox, $oy, $cw, $ch
  $cbmp = $wbmp.Clone($src, $wbmp.PixelFormat); $wbmp.Dispose()
  # Scale to SS_TARGET_WIDTH
  $th  = [int]($ch * ${tw} / $cw)
  $out = New-Object System.Drawing.Bitmap ${tw}, $th
  $sg  = [System.Drawing.Graphics]::FromImage($out)
  $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $sg.DrawImage($cbmp, 0, 0, ${tw}, $th)
  $sg.Dispose(); $cbmp.Dispose()
  $out.Save('${imgPathPs}', [System.Drawing.Imaging.ImageFormat]::Png)
  $out.Dispose()
} else {
  # Fallback: full-screen capture if window not found
  Add-Type -AssemblyName System.Windows.Forms
  $s   = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object System.Drawing.Bitmap $s.Width, $s.Height
  $g   = [System.Drawing.Graphics]::FromImage($bmp)
  $null = $g.CopyFromScreen($s.Location, [System.Drawing.Point]::Empty, $s.Size)
  $g.Dispose()
  $th  = [int]($s.Height * ${tw} / $s.Width)
  $out = New-Object System.Drawing.Bitmap ${tw}, $th
  $sg  = [System.Drawing.Graphics]::FromImage($out)
  $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $sg.DrawImage($bmp, 0, 0, ${tw}, $th)
  $sg.Dispose(); $bmp.Dispose()
  $out.Save('${imgPathPs}', [System.Drawing.Imaging.ImageFormat]::Png)
  $out.Dispose()
}`);

  const data = readFileSync(imgPath).toString("base64");
  return [{ type: "image", data, mimeType: "image/png" }];
}

function handleServerStatus() {
  const out = ps(`
$stdb  = Get-Process spacetimedb-standalone -ErrorAction SilentlyContinue
$godot = Get-Process | Where-Object { $_.MainWindowTitle -like '*SandboxRPG*' } | Select-Object -First 1
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
  _godotPid = proc.pid;
  proc.unref();

  return [{ type: "text", text: `Godot started (pid ${proc.pid}). Log: ${GODOT_LOG}` }];
}

function handleStopGodot() {
  const out = ps(`
$procs = Get-Process | Where-Object { $_.MainWindowTitle -like '*SandboxRPG*' }
if ($procs) {
  $procs | ForEach-Object { Stop-Process -Id $_.Id -Force; "Stopped Godot pid $($_.Id)" }
} else { "Godot was not running" }
`);
  _godotPid = null;
  return [{ type: "text", text: out }];
}

function handleReadLogs({ lines = 80 }) {
  if (!existsSync(GODOT_LOG)) return [{ type: "text", text: "(no log file yet — start the game first)" }];
  const all = readFileSync(GODOT_LOG, "utf8").split("\n");
  const tail = all.slice(-lines).join("\n");
  return [{ type: "text", text: tail || "(log is empty)" }];
}

// VK code map for common keys used in game testing
const VK = {
  'esc':0x1B,'{esc}':0x1B,'escape':0x1B,
  'enter':0x0D,'{enter}':0x0D,
  'tab':0x09,'{tab}':0x09,
  'space':0x20,'{space}':0x20,
  'back':0x08,'{back}':0x08,'backspace':0x08,
  'delete':0x2E,'{delete}':0x2E,'{del}':0x2E,
  'left':0x25,'{left}':0x25,
  'right':0x27,'{right}':0x27,
  'up':0x26,'{up}':0x26,
  'down':0x28,'{down}':0x28,
  'f1':0x70,'f2':0x71,'f3':0x72,'f4':0x73,'f5':0x74,'f6':0x75,
  'f7':0x76,'f8':0x77,'f9':0x78,'f10':0x79,'f11':0x7A,'f12':0x7B,
  'a':0x41,'b':0x42,'c':0x43,'d':0x44,'e':0x45,'f':0x46,'g':0x47,'h':0x48,
  'i':0x49,'j':0x4A,'k':0x4B,'l':0x4C,'m':0x4D,'n':0x4E,'o':0x4F,'p':0x50,
  'q':0x51,'r':0x52,'s':0x53,'t':0x54,'u':0x55,'v':0x56,'w':0x57,'x':0x58,'y':0x59,'z':0x5A,
  '0':0x30,'1':0x31,'2':0x32,'3':0x33,'4':0x34,'5':0x35,'6':0x36,'7':0x37,'8':0x38,'9':0x39,
};

// Send one VK code via PostMessage (no focus needed)
function psPostKey(vk, delay_ms) {
  return `
${PS_WINCAP}
${psGetWindow()}
if ($_proc -and $_proc.MainWindowHandle -ne [IntPtr]::Zero) {
  $hw = $_proc.MainWindowHandle
  if (${delay_ms} -gt 0) { Start-Sleep -Milliseconds ${delay_ms} }
  [WinCap]::PostMessage($hw, 0x0100, [IntPtr]${vk}, [IntPtr]0x00010001) | Out-Null
  Start-Sleep -Milliseconds 40
  [WinCap]::PostMessage($hw, 0x0101, [IntPtr]${vk}, [IntPtr]0xC0010001) | Out-Null
}`;
}

// Send modifier + key via PostMessage (no focus needed)
// modVk: 0x11 Ctrl, 0x10 Shift, 0x12 Alt
function psPostModKey(modVk, keyVk, delay_ms) {
  return `
${PS_WINCAP}
${psGetWindow()}
if ($_proc -and $_proc.MainWindowHandle -ne [IntPtr]::Zero) {
  $hw = $_proc.MainWindowHandle
  if (${delay_ms} -gt 0) { Start-Sleep -Milliseconds ${delay_ms} }
  [WinCap]::PostMessage($hw, 0x0100, [IntPtr]${modVk}, [IntPtr]0x00010001) | Out-Null
  Start-Sleep -Milliseconds 20
  [WinCap]::PostMessage($hw, 0x0100, [IntPtr]${keyVk}, [IntPtr]0x00010001) | Out-Null
  Start-Sleep -Milliseconds 40
  [WinCap]::PostMessage($hw, 0x0101, [IntPtr]${keyVk}, [IntPtr]0xC0010001) | Out-Null
  Start-Sleep -Milliseconds 20
  [WinCap]::PostMessage($hw, 0x0101, [IntPtr]${modVk}, [IntPtr]0xC0010001) | Out-Null
}`;
}

function handleSendKey({ keys, delay_ms = 0 }) {
  const k = keys.toLowerCase().trim();

  // Direct key match
  const vk = VK[k];
  if (vk) {
    ps(psPostKey(vk, delay_ms));
    return [{ type: "text", text: `Sent key: ${keys}` }];
  }

  // Modifier prefix: ^=Ctrl, +=Shift, %=Alt (SendKeys notation)
  const modMap = { '^': [0x11, 'Ctrl'], '+': [0x10, 'Shift'], '%': [0x12, 'Alt'] };
  const modEntry = modMap[k[0]];
  if (modEntry) {
    const bare = k.slice(1).replace(/[{}]/g, ''); // strip { } from e.g. ^{f4}
    const baseVk = VK[bare] ?? VK[`{${bare}}`];
    if (baseVk) {
      ps(psPostModKey(modEntry[0], baseVk, delay_ms));
      return [{ type: "text", text: `Sent ${modEntry[1]}+${bare.toUpperCase()}` }];
    }
  }

  return [{ type: "text", text: `Unknown key: '${keys}' — not in VK map. Use a key from: esc, enter, space, tab, a-z, 0-9, f1-f12, left/right/up/down, back, delete, ^key, +key, %key` }];
}

function handleClick({ x, y, button = "left" }) {
  // x,y are in SS_TARGET_WIDTH coordinate space mapped to the game client area.
  // Scale factor: clientWidth / SS_TARGET_WIDTH (same as screenshot, aspect-ratio-preserving)
  // WM messages: left=0x201/0x202, right=0x204/0x205, middle=0x207/0x208
  const [downMsg, upMsg, btnFlag] =
    button === "right"  ? [0x204, 0x205, 0x0002] :
    button === "middle" ? [0x207, 0x208, 0x0010] :
                          [0x201, 0x202, 0x0001];

  ps(`
${PS_WINCAP}
${psGetWindow()}
if ($_proc -and $_proc.MainWindowHandle -ne [IntPtr]::Zero) {
  $hw = $_proc.MainWindowHandle
  $cr = New-Object WinCap+RECT
  [WinCap]::GetClientRect($hw, [ref]$cr) | Out-Null
  $cw = $cr.R - $cr.L; $ch = $cr.B - $cr.T
  # Same scale factor used by screenshot (SS_TARGET_WIDTH / clientWidth)
  $scale = $cw / ${SS_TARGET_WIDTH}
  $cx = [int](${x} * $scale)
  $cy = [int](${y} * $scale)
  $lp = [IntPtr](($cy -shl 16) -bor ($cx -band 0xFFFF))
  [WinCap]::PostMessage($hw, 0x${downMsg.toString(16)}, [IntPtr]${btnFlag}, $lp) | Out-Null
  Start-Sleep -Milliseconds 60
  [WinCap]::PostMessage($hw, 0x${upMsg.toString(16)}, [IntPtr]0, $lp) | Out-Null
}`);
  return [{ type: "text", text: `Clicked ${button} at (${x},${y}) in window-client space` }];
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
