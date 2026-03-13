---
name: dev-status
description: Check the current state of the dev environment — SpacetimeDB running, Godot running, server reachable.
user-invocable: true
allowed-tools: mcp__game-dev__server_status, mcp__game-dev__read_logs
---

1. Call server_status to check SpacetimeDB and Godot process status.
2. Call read_logs with lines=20 to show the last 20 lines of Godot output.
3. Summarise what's running and highlight any errors in the logs.
