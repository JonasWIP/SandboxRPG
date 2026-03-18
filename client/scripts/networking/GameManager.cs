using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

namespace SandboxRPG;

// =========================================================================
// GAME STATE
// =========================================================================

public enum GameState
{
	Disconnected,
	Connecting,
	Connected,
	CharacterSetup,
	InGame,
}

/// <summary>
/// Autoload singleton managing the SpacetimeDB connection and game state machine.
/// UI reacts to StateChanged / ConnectionFailed signals — no direct coupling.
/// </summary>
public partial class GameManager : Node
{
	// === Configuration ===
	[Export] public string ServerUrl = "http://127.0.0.1:3000";
	[Export] public string ModuleName = "sandbox-rpg";

	// === Singleton ===
	public static GameManager Instance { get; private set; } = null!;

	// === Connection ===
	public DbConnection? Conn { get; private set; }
	public Identity? LocalIdentity { get; private set; }
	public new bool IsConnected { get; private set; }

	// === State machine ===
	public GameState State { get; private set; } = GameState.Disconnected;

	// === Signals ===
	[Signal] public delegate void StateChangedEventHandler(int state);   // cast to GameState
	[Signal] public delegate void ConnectionFailedEventHandler(string reason);
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
	[Signal] public delegate void WorldObjectUpdatedEventHandler(long id, bool removed);
	[Signal] public delegate void TerrainConfigChangedEventHandler();
	[Signal] public delegate void AccessControlChangedEventHandler();
	[Signal] public delegate void ContainerSlotChangedEventHandler();

	// =========================================================================
	// GODOT LIFECYCLE
	// =========================================================================

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		// Do NOT auto-connect — MainMenu drives the connection.
		SetState(GameState.Disconnected);
	}

	public override void _Process(double delta)
	{
		Conn?.FrameTick();
	}

	// =========================================================================
	// CONNECTION — PUBLIC API
	// =========================================================================

	/// <summary>Connect using the saved auth token (same identity as last session).</summary>
	public void Connect()
	{
		if (State == GameState.Connecting) return;
		SetState(GameState.Connecting);
		BuildConnection(LoadAuthToken());
	}

	/// <summary>Connect as a brand-new identity (delete saved token first).</summary>
	public void ConnectFresh()
	{
		if (State == GameState.Connecting) return;
		DeleteAuthToken();
		SetState(GameState.Connecting);
		BuildConnection(null);
	}

	public bool HasSavedToken() => FileAccess.FileExists(AuthTokenPath);

	// =========================================================================
	// REDUCER CALLS — CLIENT → SERVER
	// =========================================================================

	public void SetPlayerName(string name)    => Conn?.Reducers.SetName(name);
	public void SetPlayerColor(string hex)    => Conn?.Reducers.SetColor(hex);
	public void SendMovePlayer(float x, float y, float z, float rotY) => Conn?.Reducers.MovePlayer(x, y, z, rotY);
	public void SendChatMessage(string text)  => Conn?.Reducers.SendChat(text);
	public void PickupWorldItem(ulong id)     => Conn?.Reducers.PickupItem(id);
	public void DropInventoryItem(ulong id, uint qty) => Conn?.Reducers.DropItem(id, qty);
	public void CraftRecipe(ulong id, string station = "") => Conn?.Reducers.CraftItem(id, station);
	public void PlaceBuildStructure(string type, float x, float y, float z, float rotY) => Conn?.Reducers.PlaceStructure(type, x, y, z, rotY);
	public void RemoveBuildStructure(ulong id) => Conn?.Reducers.RemoveStructure(id);
	public void MoveItemSlot(ulong id, int slot) => Conn?.Reducers.MoveItemToSlot(id, slot);
	public void HarvestWorldObject(ulong id, string toolType) => Conn?.Reducers.HarvestWorldObject(id, toolType);
	public void ToggleAccess(ulong entityId, string entityTable) => Conn?.Reducers.ToggleAccessControl(entityId, entityTable);
	public void ContainerDeposit(ulong containerId, string containerTable, ulong inventoryItemId, int toSlot, uint quantity)
		=> Conn?.Reducers.ContainerDeposit(containerId, containerTable, inventoryItemId, toSlot, quantity);
	public void ContainerWithdraw(ulong containerId, string containerTable, int fromSlot, uint quantity)
		=> Conn?.Reducers.ContainerWithdraw(containerId, containerTable, fromSlot, quantity);
	public void ContainerTransfer(ulong containerId, string containerTable, int fromSlot, int toSlot)
		=> Conn?.Reducers.ContainerTransfer(containerId, containerTable, fromSlot, toSlot);

	// =========================================================================
	// DATA ACCESS — READ FROM STDB CLIENT CACHE
	// =========================================================================

	public IEnumerable<Player> GetAllPlayers()
	{
		if (Conn == null) yield break;
		foreach (var p in Conn.Db.Player.Iter()) yield return p;
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
			if (item.OwnerId == LocalIdentity.Value)
				yield return item;
	}

	public IEnumerable<WorldItem>      GetAllWorldItems()    { if (Conn != null) foreach (var i in Conn.Db.WorldItem.Iter())        yield return i; }
	public IEnumerable<PlacedStructure> GetAllStructures()  { if (Conn != null) foreach (var s in Conn.Db.PlacedStructure.Iter()) yield return s; }
	public IEnumerable<CraftingRecipe> GetAllRecipes()      { if (Conn != null) foreach (var r in Conn.Db.CraftingRecipe.Iter())   yield return r; }
	public IEnumerable<WorldObject>    GetAllWorldObjects() { if (Conn != null) foreach (var o in Conn.Db.WorldObject.Iter())      yield return o; }
	public WorldObject? GetWorldObject(ulong id) => Conn?.Db.WorldObject.Id.Find(id);
	public TerrainConfig? GetTerrainConfig() => Conn?.Db.TerrainConfig.Id.Find(0);
	public IEnumerable<AccessControl> GetAllAccessControls() { if (Conn != null) foreach (var a in Conn.Db.AccessControl.Iter()) yield return a; }
	public IEnumerable<ContainerSlot> GetContainerSlots(ulong containerId)
	{
		if (Conn == null) yield break;
		foreach (var cs in Conn.Db.ContainerSlot.Iter())
			if (cs.ContainerId == containerId) yield return cs;
	}
	public AccessControl? GetAccessControl(ulong entityId, string entityTable)
	{
		if (Conn == null) return null;
		foreach (var ac in Conn.Db.AccessControl.Iter())
			if (ac.EntityId == entityId && ac.EntityTable == entityTable) return ac;
		return null;
	}

	// =========================================================================
	// PRIVATE — CONNECTION IMPLEMENTATION
	// =========================================================================

	private void BuildConnection(string? savedToken)
	{
		try
		{
			var builder = DbConnection.Builder()
				.WithUri(ServerUrl)
				.WithDatabaseName(ModuleName)
				.OnConnect(OnConnected)
				.OnConnectError(OnConnectError)
				.OnDisconnect(OnDisconnected);

			if (!string.IsNullOrEmpty(savedToken))
				builder = builder.WithToken(savedToken);

			Conn = builder.Build();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GameManager] Failed to create connection: {ex.Message}");
			SetState(GameState.Disconnected);
			EmitSignal(SignalName.ConnectionFailed, ex.Message);
		}
	}

	private void OnConnected(DbConnection conn, Identity identity, string authToken)
	{
		GD.Print($"[GameManager] Connected! Identity: {identity}");
		LocalIdentity = identity;
		IsConnected = true;
		SaveAuthToken(authToken);
		SetState(GameState.Connected);

		RegisterCallbacks(conn);

		conn.SubscriptionBuilder()
			.OnApplied(OnSubscriptionApplied)
			.OnError((ctx, err) => GD.PrintErr($"[GameManager] Subscription error: {err}"))
			.SubscribeToAllTables();

		EmitSignal(SignalName.Connected);
	}

	private void OnConnectError(Exception error)
	{
		GD.PrintErr($"[GameManager] Connection error: {error.Message}");
		IsConnected = false;
		SetState(GameState.Disconnected);
		EmitSignal(SignalName.ConnectionFailed, error.Message);
	}

	private void OnDisconnected(DbConnection conn, Exception? error)
	{
		GD.Print("[GameManager] Disconnected from server.");
		IsConnected = false;
		SetState(GameState.Disconnected);
		EmitSignal(SignalName.Disconnected);
	}

	private void OnSubscriptionApplied(SubscriptionEventContext ctx)
	{
		GD.Print("[GameManager] Subscription applied - all data synced!");

		var player = GetLocalPlayer();
		var nextState = (player == null || player.Name.StartsWith("Player_"))
			? GameState.CharacterSetup
			: GameState.InGame;

		SetState(nextState);
		EmitSignal(SignalName.SubscriptionApplied);
	}

	private void SetState(GameState newState)
	{
		if (State == newState) return;
		State = newState;
		GD.Print($"[GameManager] State → {newState}");
		EmitSignal(SignalName.StateChanged, (int)newState);
	}

	// =========================================================================
	// TABLE CALLBACKS
	// =========================================================================

	private void RegisterCallbacks(DbConnection conn)
	{
		conn.Db.Player.OnInsert += (ctx, player) =>
		{
			GD.Print($"[GameManager] Player joined: {player.Name}");
			CallDeferred(nameof(EmitPlayerUpdated), player.Identity.ToString());
		};
		conn.Db.Player.OnUpdate += (ctx, _, newPlayer) =>
			CallDeferred(nameof(EmitPlayerUpdated), newPlayer.Identity.ToString());
		conn.Db.Player.OnDelete += (ctx, player) =>
			CallDeferred(nameof(EmitPlayerRemoved), player.Identity.ToString());

		conn.Db.ChatMessage.OnInsert += (ctx, msg) =>
			CallDeferred(nameof(EmitChatMessage), msg.SenderName, msg.Text);

		conn.Db.InventoryItem.OnInsert += (ctx, _) => CallDeferred(nameof(EmitInventoryChanged));
		conn.Db.InventoryItem.OnUpdate += (ctx, _, _) => CallDeferred(nameof(EmitInventoryChanged));
		conn.Db.InventoryItem.OnDelete += (ctx, _) => CallDeferred(nameof(EmitInventoryChanged));

		conn.Db.WorldItem.OnInsert += (ctx, _) => CallDeferred(nameof(EmitWorldItemChanged));
		conn.Db.WorldItem.OnDelete += (ctx, _) => CallDeferred(nameof(EmitWorldItemChanged));

		conn.Db.PlacedStructure.OnInsert += (ctx, _) => CallDeferred(nameof(EmitStructureChanged));
		conn.Db.PlacedStructure.OnUpdate += (ctx, _, _) => CallDeferred(nameof(EmitStructureChanged));
		conn.Db.PlacedStructure.OnDelete += (ctx, _) => CallDeferred(nameof(EmitStructureChanged));

		conn.Db.CraftingRecipe.OnInsert += (ctx, _) => CallDeferred(nameof(EmitRecipesLoaded));

		conn.Db.WorldObject.OnInsert += (ctx, o) => CallDeferred(nameof(EmitWorldObjectUpdated), (long)o.Id, false);
		conn.Db.WorldObject.OnDelete += (ctx, o) => CallDeferred(nameof(EmitWorldObjectUpdated), (long)o.Id, true);

		conn.Db.TerrainConfig.OnInsert += (ctx, _) => CallDeferred(nameof(EmitTerrainConfigChanged));
		conn.Db.TerrainConfig.OnUpdate += (ctx, _, _) => CallDeferred(nameof(EmitTerrainConfigChanged));

		conn.Db.AccessControl.OnInsert += (ctx, _) => CallDeferred(nameof(EmitAccessControlChanged));
		conn.Db.AccessControl.OnUpdate += (ctx, _, _) => CallDeferred(nameof(EmitAccessControlChanged));
		conn.Db.AccessControl.OnDelete += (ctx, _) => CallDeferred(nameof(EmitAccessControlChanged));

		conn.Db.ContainerSlot.OnInsert += (ctx, _) => CallDeferred(nameof(EmitContainerSlotChanged));
		conn.Db.ContainerSlot.OnUpdate += (ctx, _, _) => CallDeferred(nameof(EmitContainerSlotChanged));
		conn.Db.ContainerSlot.OnDelete += (ctx, _) => CallDeferred(nameof(EmitContainerSlotChanged));
	}

	// Deferred signal emitters (thread-safe hop back to main thread)
	private void EmitPlayerUpdated(string id)  => EmitSignal(SignalName.PlayerUpdated, id);
	private void EmitPlayerRemoved(string id)  => EmitSignal(SignalName.PlayerRemoved, id);
	private void EmitChatMessage(string n, string t) => EmitSignal(SignalName.ChatMessageReceived, n, t);
	private void EmitInventoryChanged()        => EmitSignal(SignalName.InventoryChanged);
	private void EmitWorldItemChanged()        => EmitSignal(SignalName.WorldItemChanged);
	private void EmitStructureChanged()        => EmitSignal(SignalName.StructureChanged);
	private void EmitRecipesLoaded()           => EmitSignal(SignalName.RecipesLoaded);
	private void EmitWorldObjectUpdated(long id, bool removed) => EmitSignal(SignalName.WorldObjectUpdated, id, removed);
	private void EmitTerrainConfigChanged() => EmitSignal(SignalName.TerrainConfigChanged);
	private void EmitAccessControlChanged() => EmitSignal(SignalName.AccessControlChanged);
	private void EmitContainerSlotChanged() => EmitSignal(SignalName.ContainerSlotChanged);

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

	private void DeleteAuthToken()
	{
		if (FileAccess.FileExists(AuthTokenPath))
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(AuthTokenPath));
	}
}
