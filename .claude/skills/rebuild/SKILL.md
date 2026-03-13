---
name: rebuild
description: Rebuild and republish the SpacetimeDB server module without restarting the server.
user-invocable: true
allowed-tools: mcp__game-dev__publish_module, mcp__game-dev__server_status
---

1. Call server_status to confirm SpacetimeDB is running. If it's stopped, tell the user to run start_server first.
2. Call publish_module with build=true to rebuild the WASM and publish it.
3. Report success or any errors from the build output.
