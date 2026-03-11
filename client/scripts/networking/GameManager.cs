using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

namespace SandboxRPG;

/// <summary>
/// Autoload singleton managing the SpacetimeDB connection.
/// All game data flows through here - the single source of truth.
/// </summary>
public partial class GameManager : Node
{
	// === Configuration ===
	[Export] public string ServerUrl = "http://127.0.0.1:3000";
	[Export] public string ModuleName = "sandbox-rpg";

	// === Connection State ===
	public static GameManager Instance { get; private set; } = null!;
	public DbConnection? Conn { get; private set; }
	public Identity? LocalIdentity { get; private set; }
	public new bool IsConnected { get; private set; }

	// === Signals for Godot nodes to react ===
	[Signal] public delegate void ConnectedEventHandler();
	[Signal] public delegate void DisconnectedEventHandler();
	[Signal] public delegate void SubscriptionAppliedEventHandler();
	[Signal] public delegate void PlayerUpdatedEventHandler(string identityHex);
	[Signal] public delegate void PlayerRemovedEventHandler(string identityHex);
	[Signal] public delegate void ChatMessageReceivedEventHandler(string senderName, string text);
	[Signal] public delegate void InventoryChangedEventHandler();
	[Signal] public delegate void WorldItemChangedEventHandler();
	[Signal] public delegate void StructureChangedEventHandler();
	[Signal] public delegate void RecipesLoadedEventHandler();

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		GD.Print("[GameManager] Initializing SpacetimeDB connection...");
		Connect();
	}

	public override void _Process(double delta)
	{
		// Process incoming SpacetimeDB messages each frame
		Conn?.FrameTick();
	}

	// =========================================================================
	// CONNECTION
	// =========================================================================

	private void Connect()
	{
		try
		{
			Conn = DbConnection.Builder()
				.WithUri(ServerUrl)
				.WithDatabaseName(ModuleName)
				.OnConnect(OnConnected)
				.OnConnectError(OnConnectError)
				.OnDisconnect(OnDisconnected)
				.Build();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GameManager] Failed to create connection: {ex.Message}");
		}
	}

	private void OnConnected(DbConnection conn, Identity identity, string authToken)
	{
		GD.Print($"[GameManager] Connected! Identity: {identity}");
		LocalIdentity = identity;
		IsConnected = true;

		// Save auth token for reconnection
		SaveAuthToken(authToken);

		// Register table callbacks
		RegisterCallbacks(conn);

		// Subscribe to all public tables
		conn.SubscriptionBuilder()
			.OnApplied(OnSubscriptionApplied)
			.OnError((ctx, err) => GD.PrintErr($"[GameManager] Subscription error: {err}"))
			.SubscribeToAllTables();

		EmitSignal(SignalName.Connected);
	}

	private void OnConnectError(Exception error)
	{
		GD.PrintErr($"[GameManager] Connection error: {error.Message}");
	}

	private void OnDisconnected(DbConnection conn, Exception? error)
	{
		GD.Print("[GameManager] Disconnected from server.");
		IsConnected = false;
		EmitSignal(SignalName.Disconnected);
	}

	private void OnSubscriptionApplied(SubscriptionEventContext ctx)
	{
		GD.Print("[GameManager] Subscription applied - all data synced!");
		EmitSignal(SignalName.SubscriptionApplied);
	}

	// =========================================================================
	// TABLE CALLBACKS
	// =========================================================================

	private void RegisterCallbacks(DbConnection conn)
	{
		// Player table
		conn.Db.Player.OnInsert += (ctx, player) =>
		{
			GD.Print($"[GameManager] Player joined: {player.Name}");
			CallDeferred(nameof(EmitPlayerUpdated), player.Identity.ToString());
		};
		conn.Db.Player.OnUpdate += (ctx, oldPlayer, newPlayer) =>
		{
			CallDeferred(nameof(EmitPlayerUpdated), newPlayer.Identity.ToString());
		};
		conn.Db.Player.OnDelete += (ctx, player) =>
		{
			CallDeferred(nameof(EmitPlayerRemoved), player.Identity.ToString());
		};

		// Chat messages
		conn.Db.ChatMessage.OnInsert += (ctx, msg) =>
		{
			CallDeferred(nameof(EmitChatMessage), msg.SenderName, msg.Text);
		};

		// Inventory
		conn.Db.InventoryItem.OnInsert += (ctx, item) => CallDeferred(nameof(EmitInventoryChanged));
		conn.Db.InventoryItem.OnUpdate += (ctx, _, _) => CallDeferred(nameof(EmitInventoryChanged));
		conn.Db.InventoryItem.OnDelete += (ctx, _) => CallDeferred(nameof(EmitInventoryChanged));

		// World items
		conn.Db.WorldItem.OnInsert += (ctx, _) => CallDeferred(nameof(EmitWorldItemChanged));
		conn.Db.WorldItem.OnDelete += (ctx, _) => CallDeferred(nameof(EmitWorldItemChanged));

		// Structures
		conn.Db.PlacedStructure.OnInsert += (ctx, _) => CallDeferred(nameof(EmitStructureChanged));
		conn.Db.PlacedStructure.OnUpdate += (ctx, _, _) => CallDeferred(nameof(EmitStructureChanged));
		conn.Db.PlacedStructure.OnDelete += (ctx, _) => CallDeferred(nameof(EmitStructureChanged));

		// Recipes
		conn.Db.CraftingRecipe.OnInsert += (ctx, _) => CallDeferred(nameof(EmitRecipesLoaded));
	}

	// Deferred signal emitters (thread-safe)
	private void EmitPlayerUpdated(string id) => EmitSignal(SignalName.PlayerUpdated, id);
	private void EmitPlayerRemoved(string id) => EmitSignal(SignalName.PlayerRemoved, id);
	private void EmitChatMessage(string name, string text) => EmitSignal(SignalName.ChatMessageReceived, name, text);
	private void EmitInventoryChanged() => EmitSignal(SignalName.InventoryChanged);
	private void EmitWorldItemChanged() => EmitSignal(SignalName.WorldItemChanged);
	private void EmitStructureChanged() => EmitSignal(SignalName.StructureChanged);
	private void EmitRecipesLoaded() => EmitSignal(SignalName.RecipesLoaded);

	// =========================================================================
	// REDUCER CALLS (Client → Server)
	// =========================================================================

	public void SetPlayerName(string name)
	{
		Conn?.Reducers.SetName(name);
	}

	public void SendMovePlayer(float x, float y, float z, float rotY)
	{
		Conn?.Reducers.MovePlayer(x, y, z, rotY);
	}

	public void SendChatMessage(string text)
	{
		Conn?.Reducers.SendChat(text);
	}

	public void PickupWorldItem(ulong worldItemId)
	{
		Conn?.Reducers.PickupItem(worldItemId);
	}

	public void DropInventoryItem(ulong inventoryItemId, uint quantity)
	{
		Conn?.Reducers.DropItem(inventoryItemId, quantity);
	}

	public void CraftRecipe(ulong recipeId)
	{
		Conn?.Reducers.CraftItem(recipeId);
	}

	public void PlaceBuildStructure(string structureType, float x, float y, float z, float rotY)
	{
		Conn?.Reducers.PlaceStructure(structureType, x, y, z, rotY);
	}

	public void RemoveBuildStructure(ulong structureId)
	{
		Conn?.Reducers.RemoveStructure(structureId);
	}

	public void MoveItemSlot(ulong itemId, int slot)
	{
		Conn?.Reducers.MoveItemToSlot(itemId, slot);
	}

	// =========================================================================
	// DATA ACCESS (Read from SpacetimeDB client cache)
	// =========================================================================

	public IEnumerable<Player> GetAllPlayers()
	{
		if (Conn == null) yield break;
		foreach (var p in Conn.Db.Player.Iter())
			yield return p;
	}

	public Player? GetLocalPlayer()
	{
		if (Conn == null || LocalIdentity == null) return null;
		return Conn.Db.Player.Identity.Find(LocalIdentity.Value);
	}

	public IEnumerable<InventoryItem> GetMyInventory()
	{
		if (Conn == null || LocalIdentity == null) yield break;
		foreach (var item in Conn.Db.InventoryItem.Iter())
		{
			if (item.OwnerId == LocalIdentity.Value)
				yield return item;
		}
	}

	public IEnumerable<WorldItem> GetAllWorldItems()
	{
		if (Conn == null) yield break;
		foreach (var item in Conn.Db.WorldItem.Iter())
			yield return item;
	}

	public IEnumerable<PlacedStructure> GetAllStructures()
	{
		if (Conn == null) yield break;
		foreach (var s in Conn.Db.PlacedStructure.Iter())
			yield return s;
	}

	public IEnumerable<CraftingRecipe> GetAllRecipes()
	{
		if (Conn == null) yield break;
		foreach (var r in Conn.Db.CraftingRecipe.Iter())
			yield return r;
	}

	// =========================================================================
	// AUTH TOKEN PERSISTENCE
	// =========================================================================

	private const string AuthTokenPath = "user://spacetimedb_auth.token";

	private void SaveAuthToken(string token)
	{
		using var file = FileAccess.Open(AuthTokenPath, FileAccess.ModeFlags.Write);
		file?.StoreString(token);
	}

	private string? LoadAuthToken()
	{
		if (!FileAccess.FileExists(AuthTokenPath)) return null;
		using var file = FileAccess.Open(AuthTokenPath, FileAccess.ModeFlags.Read);
		return file?.GetAsText();
	}
}
