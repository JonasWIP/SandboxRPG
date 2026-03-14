# /interact — Send input to the game window

Send a click or keypress to the SandboxRPG Godot window, then take a screenshot.

## Argument parsing

The user passes arguments after `/interact`. Parse like this:

- `click X Y` — call `mcp__game-dev__click` with `x=X, y=Y` (left click)
- `click X Y right` — right click
- `key K` — call `mcp__game-dev__send_key` with `keys=K`
- `type TEXT` — call `mcp__game-dev__send_key` with `keys=TEXT` (literal text input)
- No argument — just take a screenshot

### Common key strings
Single letters or names: `i`, `c`, `t`, `b`, `e`, `esc`, `enter`, `tab`, `space`, `1`–`8`.

`send_key` uses PostMessage directly to the SandboxRPG window — **no window focus required**.
For complex combos (Ctrl+Z etc.) it falls back to SendKeys and does need focus.

### Click coordinates
Coordinates are in **1280-wide screenshot space** — use the pixel position you see in the screenshot image.
The tool auto-scales them to the actual physical screen resolution.

## Steps

1. Parse the argument to determine the action.
2. Execute the action (click or send_key).
3. Wait ~300ms implicitly (the MCP tool does this), then call `mcp__game-dev__screenshot`.
4. Describe what happened — did the UI respond as expected? Note any glitches.
