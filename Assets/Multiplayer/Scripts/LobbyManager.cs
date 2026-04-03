using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    Lobby m_CurrentLobby;
    float m_HeartbeatTimer;
    float m_PollTimer;
    const float HeartbeatInterval = 15f;
    const float PollInterval = 1.5f;

    public event Action<List<PlayerLobbyData>> OnLobbyPlayersChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        HandleHeartbeat();
        HandlePollForUpdates();
    }

    public async Task<Lobby> CreatePublicLobby(string lobbyName, string playerName, int maxPlayers = 4)
    {
        try
        {
            string relayJoinCode = await RelayManager.Instance.CreateRelay(maxPlayers - 1);
            if (relayJoinCode == null) return null;

            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = CreatePlayerData(playerName),
                Data = new Dictionary<string, DataObject>
                {
                    {
                        "RelayJoinCode", new DataObject(
                            DataObject.VisibilityOptions.Member,
                            relayJoinCode)
                    }
                }
            };

            m_CurrentLobby = await LobbyService.Instance.CreateLobbyAsync(
                lobbyName, maxPlayers, options);

            Debug.Log($"[Lobby] Public lobby created: {m_CurrentLobby.Name}");
            NotifyPlayersChanged();
            return m_CurrentLobby;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to create public lobby: {e.Message}");
            return null;
        }
    }

    public async Task<Lobby> CreatePrivateLobby(string lobbyName, string playerName, int maxPlayers = 4)
    {
        try
        {
            string relayJoinCode = await RelayManager.Instance.CreateRelay(maxPlayers - 1);
            if (relayJoinCode == null) return null;

            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = true,
                Player = CreatePlayerData(playerName),
                Data = new Dictionary<string, DataObject>
                {
                    {
                        "RelayJoinCode", new DataObject(
                            DataObject.VisibilityOptions.Member,
                            relayJoinCode)
                    }
                }
            };

            m_CurrentLobby = await LobbyService.Instance.CreateLobbyAsync(
                lobbyName, maxPlayers, options);

            Debug.Log($"[Lobby] Private lobby created. Code: {m_CurrentLobby.LobbyCode}");
            NotifyPlayersChanged();
            return m_CurrentLobby;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to create private lobby: {e.Message}");
            return null;
        }
    }

    public async Task<bool> JoinLobbyByCode(string lobbyCode, string playerName)
    {
        try
        {
            JoinLobbyByCodeOptions options = new JoinLobbyByCodeOptions
            {
                Player = CreatePlayerData(playerName)
            };

            m_CurrentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, options);
            Debug.Log($"[Lobby] Joined: {m_CurrentLobby.Name}");

            string relayJoinCode = m_CurrentLobby.Data["RelayJoinCode"].Value;
            return await RelayManager.Instance.JoinRelay(relayJoinCode);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to join by code: {e.Message}");
            return false;
        }
    }

    public async Task<bool> QuickJoin(string playerName)
    {
        try
        {
            QuickJoinLobbyOptions options = new QuickJoinLobbyOptions
            {
                Player = CreatePlayerData(playerName)
            };

            m_CurrentLobby = await LobbyService.Instance.QuickJoinLobbyAsync(options);
            Debug.Log($"[Lobby] Quick joined: {m_CurrentLobby.Name}");

            string relayJoinCode = m_CurrentLobby.Data["RelayJoinCode"].Value;
            return await RelayManager.Instance.JoinRelay(relayJoinCode);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] No lobbies available: {e.Message}");
            return false;
        }
    }

    Player CreatePlayerData(string playerName)
    {
        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                {
                    "PlayerName", new PlayerDataObject(
                        PlayerDataObject.VisibilityOptions.Member,
                        playerName)
                }
            }
        };
    }

    void HandleHeartbeat()
    {
        if (m_CurrentLobby == null) return;
        if (!IsHost()) return;

        m_HeartbeatTimer += Time.deltaTime;
        if (m_HeartbeatTimer >= HeartbeatInterval)
        {
            m_HeartbeatTimer = 0f;
            LobbyService.Instance.SendHeartbeatPingAsync(m_CurrentLobby.Id);
        }
    }

    void HandlePollForUpdates()
    {
        if (m_CurrentLobby == null) return;

        m_PollTimer += Time.deltaTime;
        if (m_PollTimer >= PollInterval)
        {
            m_PollTimer = 0f;
            PollLobby();
        }
    }

    async void PollLobby()
    {
        try
        {
            string previousState = BuildLobbyPlayersState(m_CurrentLobby);
            Lobby updated = await LobbyService.Instance.GetLobbyAsync(m_CurrentLobby.Id);
            string updatedState = BuildLobbyPlayersState(updated);
            bool playersChanged = previousState != updatedState;
            m_CurrentLobby = updated;

            if (playersChanged)
            {
                NotifyPlayersChanged();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Poll failed: {e.Message}");
            m_CurrentLobby = null;
        }
    }

    void NotifyPlayersChanged()
    {
        if (m_CurrentLobby == null) return;

        List<PlayerLobbyData> players = new List<PlayerLobbyData>();
        for (int i = 0; i < m_CurrentLobby.Players.Count; i++)
        {
            var p = m_CurrentLobby.Players[i];
            string name = GetPlayerName(p);

            bool isHost = p.Id == m_CurrentLobby.HostId;
            players.Add(new PlayerLobbyData { Name = name, IsHost = isHost });
        }

        OnLobbyPlayersChanged?.Invoke(players);
    }

    string BuildLobbyPlayersState(Lobby lobby)
    {
        if (lobby == null || lobby.Players == null)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        builder.Append(lobby.HostId);

        for (int i = 0; i < lobby.Players.Count; i++)
        {
            var player = lobby.Players[i];
            builder.Append('|');
            builder.Append(player.Id);
            builder.Append(':');
            builder.Append(GetPlayerName(player));
        }

        return builder.ToString();
    }

    string GetPlayerName(Player player)
    {
        if (player.Data != null && player.Data.ContainsKey("PlayerName"))
        {
            return player.Data["PlayerName"].Value;
        }

        return "Unknown";
    }

    public bool IsHost()
    {
        return m_CurrentLobby != null &&
               m_CurrentLobby.HostId == AuthenticationService.Instance.PlayerId;
    }

    public Lobby GetCurrentLobby() => m_CurrentLobby;
    public int GetMaxPlayers() => m_CurrentLobby?.MaxPlayers ?? 4;
    public int GetCurrentPlayerCount() => m_CurrentLobby?.Players?.Count ?? 0;

    public async void LeaveLobby()
    {
        try
        {
            if (m_CurrentLobby != null)
            {
                await LobbyService.Instance.RemovePlayerAsync(
                    m_CurrentLobby.Id,
                    AuthenticationService.Instance.PlayerId);
                m_CurrentLobby = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to leave: {e.Message}");
        }
    }
}

public struct PlayerLobbyData
{
    public string Name;
    public bool IsHost;
}
