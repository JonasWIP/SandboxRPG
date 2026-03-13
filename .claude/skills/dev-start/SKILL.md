---
name: dev-start
description: Start the full SandboxRPG dev environment — SpacetimeDB server, publish module, and launch Godot.
user-invocable: true
allowed-tools: mcp__game-dev__start_server, mcp__game-dev__start_godot, mcp__game-dev__screenshot
---

1. Call start_server — this kills any old instance, wipes data, starts fresh, logs in, and publishes the module.
2. Call start_godot — pass editor=true if the user said "editor", otherwise launch the game directly.
3. Wait 3 seconds then take a screenshot to confirm everything started correctly.
4. Report what happened including any errors.
