using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

namespace SandboxRPG;

public class PlayerSpawner
{
    private readonly Node3D _parent;
    private readonly GameManager _gm;
    private readonly Dictionary<string, RemotePlayer> _remotePlayers = new();

    private PlayerController? _localPlayer;

    public PlayerSpawner(Node3D parent, GameManager gm)
    {
        _parent = parent;
        _gm     = gm;
    }

    public void SpawnAll()
    {
        SpawnLocalPlayer();

        foreach (var player in _gm.GetAllPlayers())
        {
            if (player.Identity != _gm.LocalIdentity && player.IsOnline && !_gm.IsServiceIdentity(player.Identity))
                SpawnOrUpdateRemotePlayer(player);
        }
    }

    public void OnUpdated(string identityHex)
    {
        foreach (var player in _gm.GetAllPlayers())
        {
            if (player.Identity.ToString() != identityHex) continue;

            if (player.Identity == _gm.LocalIdentity)
            {
                _localPlayer?.ApplyColor(player.ColorHex ?? PlayerPrefs.LoadColorHex());
                return;
            }

            if (player.IsOnline && !_gm.IsServiceIdentity(player.Identity))
                SpawnOrUpdateRemotePlayer(player);
            else
                RemoveRemotePlayer(identityHex);
            break;
        }
    }

    public void OnRemoved(string identityHex) => RemoveRemotePlayer(identityHex);

    private void SpawnLocalPlayer()
    {
        var p = _gm.GetLocalPlayer();
        if (p == null) return;

        _localPlayer = new PlayerController { Name = "LocalPlayer" };
        _parent.AddChild(_localPlayer);
        _localPlayer.GlobalPosition = new Vector3(p.PosX, p.PosY, p.PosZ);
        _localPlayer.Rotation = new Vector3(0, p.RotY, 0);
        _localPlayer.ApplyColor(p.ColorHex ?? PlayerPrefs.LoadColorHex());

        GD.Print($"[PlayerSpawner] Local player spawned at ({p.PosX}, {p.PosY}, {p.PosZ})");
    }

    private void SpawnOrUpdateRemotePlayer(Player player)
    {
        string id       = player.Identity.ToString();
        string colorHex = player.ColorHex ?? "#E6804D";

        if (_remotePlayers.TryGetValue(id, out var existing))
        {
            existing.UpdateFromServer(player.PosX, player.PosY, player.PosZ, player.RotY, player.Name, colorHex);
        }
        else
        {
            var remote = new RemotePlayer
            {
                Name        = $"Remote_{id[..8]}",
                IdentityHex = id,
                PlayerName  = player.Name,
                ColorHex    = colorHex,
            };
            _parent.AddChild(remote);
            remote.GlobalPosition = new Vector3(player.PosX, player.PosY, player.PosZ);
            remote.Rotation = new Vector3(0, player.RotY, 0);
            _remotePlayers[id] = remote;
            GD.Print($"[PlayerSpawner] Remote player spawned: {player.Name}");
        }
    }

    private void RemoveRemotePlayer(string identityHex)
    {
        if (_remotePlayers.TryGetValue(identityHex, out var remote))
        {
            remote.QueueFree();
            _remotePlayers.Remove(identityHex);
        }
    }
}
