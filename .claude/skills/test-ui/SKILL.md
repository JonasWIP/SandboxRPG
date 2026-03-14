# /test-ui — Automated UI system test

Walk through every major UI feature in SandboxRPG, taking a screenshot after each step and reporting pass/fail.

## What this tests

The centralised UIManager stack, modal panels, ESC ownership, hotbar, inventory/crafting panel, chat, and build mode guard.

---

## Test sequence

Run each step **in order**. After every action wait for the result then screenshot. Keep a running pass/fail log.

### 0 — Baseline
- Screenshot the current state. Note what screen we're on (main menu or in-world).

### 1 — Enter the game
- If on the main menu: click the **"New Character"** button (approx centre of screen).
  - Alternatively "Continue" if a character exists.
- Screenshot after ~2 s. **Expected**: character setup screen OR world loaded with HUD visible.

### 2 — Character setup (if shown)
- If the character setup panel is visible: type a name (send_key `TestPlayer`) then click the confirm/play button.
- Screenshot. **Expected**: world loaded, HUD visible, hotbar at bottom.

### 3 — HUD / Hotbar visible
- Screenshot (no input). **Expected**: hotbar 8 slots visible at bottom, health/stamina bars visible, no inventory panel open.

### 4 — Open Inventory+Crafting panel (I key)
- send_key `i`
- Screenshot. **Expected**: side-by-side inventory (left grid) + crafting recipes (right list) panel visible over the world. Mouse should be free.

### 5 — Close Inventory panel (I key again)
- send_key `i`
- Screenshot. **Expected**: panel gone, back to HUD only.

### 6 — Open via C key
- send_key `c`
- Screenshot. **Expected**: same inventory+crafting panel opens (C is an alias).

### 7 — Close via ESC
- send_key `{ESC}`
- Screenshot. **Expected**: panel closed (ESC consumed by UIManager before escape menu, since a panel was open).

### 8 — Open Escape Menu (ESC with nothing open)
- send_key `{ESC}`
- Screenshot. **Expected**: escape menu visible (Resume / Settings / Leave / Quit buttons).

### 9 — Open Settings from Escape Menu
- Click the "Settings" button in the escape menu.
- Screenshot. **Expected**: settings panel pushed on top of escape menu (settings visible).

### 10 — Back from Settings
- send_key `{ESC}` OR click the "Back" button.
- Screenshot. **Expected**: escape menu visible again (settings popped, escape menu still on stack).

### 11 — Resume from Escape Menu
- Click "Resume" button.
- Screenshot. **Expected**: escape menu closed, back to world + HUD.

### 12 — Chat (T key)
- send_key `t`
- Screenshot. **Expected**: chat input box focused/visible at bottom.

### 13 — Close Chat (ESC)
- send_key `{ESC}`
- Screenshot. **Expected**: chat closed, mouse locked again.

### 14 — Hotbar slot switching (keys 1–3)
- send_key `1`, screenshot. send_key `2`, screenshot. send_key `3`, screenshot.
- **Expected**: active hotbar slot highlight moves accordingly.

### 15 — Build mode guard (if relevant)
- If a build item is slotted, entering build mode should be blocked when UI is open.
- Open inventory (send_key `i`), then try send_key `b` (or relevant build key).
- Screenshot. **Expected**: build ghost does NOT appear while inventory is open.
- Close inventory.

---

## Reporting

After all steps, output a summary table:

| Step | Action | Expected | Result |
|---|---|---|---|
| 0 | Baseline screenshot | Any state | ✓/✗ |
| 1 | Enter game | World or char setup | ✓/✗ |
| ... | ... | ... | ... |

Mark ✓ for pass, ✗ for fail. For each failure, describe the actual behaviour and suggest a likely cause.

If you can't tell pass/fail from a screenshot alone (e.g. mouse mode), note it as "⚠ uncertain".
