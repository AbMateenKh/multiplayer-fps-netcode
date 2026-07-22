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
    int m_LobbyGeneration;
    bool m_PollInFlight;
    bool m_IsLeavingLobby;
    const float HeartbeatInterval = 15f;
    const float PollInterval = 1.5f;
    const string PlayerNameKey = "PlayerName";
    const string ReadyKey = "Ready";

    public string LastErrorMessage { get; private set; }

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
            LastErrorMessage = string.Empty;
            string relayJoinCode = await RelayManager.Instance.CreateRelay(maxPlayers - 1);
            if (relayJoinCode == null)
            {
                LastErrorMessage = RelayManager.Instance != null
                    ? RelayManager.Instance.LastErrorMessage
                    : "Could not create Relay allocation.";
                return null;
            }

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

            SetCurrentLobby(await LobbyService.Instance.CreateLobbyAsync(
                lobbyName, maxPlayers, options));

            Debug.Log($"[Lobby] Public lobby created: {m_CurrentLobby.Name}");
            NotifyPlayersChanged();
            return m_CurrentLobby;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to create public lobby: {e.Message}");
            LastErrorMessage = "Could not create public lobby. Check Unity Services sign-in and network connection.";
            return null;
        }
    }

    public async Task<Lobby> CreatePrivateLobby(string lobbyName, string playerName, int maxPlayers = 4)
    {
        try
        {
            LastErrorMessage = string.Empty;
            string relayJoinCode = await RelayManager.Instance.CreateRelay(maxPlayers - 1);
            if (relayJoinCode == null)
            {
                LastErrorMessage = RelayManager.Instance != null
                    ? RelayManager.Instance.LastErrorMessage
                    : "Could not create Relay allocation.";
                return null;
            }

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

            SetCurrentLobby(await LobbyService.Instance.CreateLobbyAsync(
                lobbyName, maxPlayers, options));

            Debug.Log($"[Lobby] Private lobby created. Code: {m_CurrentLobby.LobbyCode}");
            NotifyPlayersChanged();
            return m_CurrentLobby;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to create private lobby: {e.Message}");
            LastErrorMessage = "Could not create private lobby. Check Unity Services sign-in and network connection.";
            return null;
        }
    }

    public async Task<bool> JoinLobbyByCode(string lobbyCode, string playerName)
    {
        try
        {
            LastErrorMessage = string.Empty;
            JoinLobbyByCodeOptions options = new JoinLobbyByCodeOptions
            {
                Player = CreatePlayerData(playerName)
            };

            SetCurrentLobby(await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, options));
            Debug.Log($"[Lobby] Joined: {m_CurrentLobby.Name}");

            string relayJoinCode = m_CurrentLobby.Data["RelayJoinCode"].Value;
            bool joinedRelay = await RelayManager.Instance.JoinRelay(relayJoinCode);
            if (!joinedRelay)
            {
                LastErrorMessage = RelayManager.Instance.LastErrorMessage;
            }

            return joinedRelay;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to join by code: {e.Message}");
            LastErrorMessage = "Could not join lobby. Check the code, or ask the host to create a fresh lobby.";
            return false;
        }
    }

    public async Task<bool> QuickJoin(string playerName)
    {
        try
        {
            LastErrorMessage = string.Empty;
            QuickJoinLobbyOptions options = new QuickJoinLobbyOptions
            {
                Player = CreatePlayerData(playerName)
            };

            SetCurrentLobby(await LobbyService.Instance.QuickJoinLobbyAsync(options));
            Debug.Log($"[Lobby] Quick joined: {m_CurrentLobby.Name}");

            string relayJoinCode = m_CurrentLobby.Data["RelayJoinCode"].Value;
            bool joinedRelay = await RelayManager.Instance.JoinRelay(relayJoinCode);
            if (!joinedRelay)
            {
                LastErrorMessage = RelayManager.Instance.LastErrorMessage;
            }

            return joinedRelay;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] No lobbies available: {e.Message}");
            LastErrorMessage = "No public lobbies are available right now.";
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
                    PlayerNameKey, new PlayerDataObject(
                        PlayerDataObject.VisibilityOptions.Member,
                        playerName)
                },
                {
                    ReadyKey, new PlayerDataObject(
                        PlayerDataObject.VisibilityOptions.Member,
                        "false")
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
            SendHeartbeat(m_CurrentLobby.Id, m_LobbyGeneration);
        }
    }

    void HandlePollForUpdates()
    {
        if (m_CurrentLobby == null || m_IsLeavingLobby || m_PollInFlight) return;

        m_PollTimer += Time.deltaTime;
        if (m_PollTimer >= PollInterval)
        {
            m_PollTimer = 0f;
            PollLobby(m_CurrentLobby.Id, m_LobbyGeneration);
        }
    }

    async void PollLobby(string lobbyId, int generation)
    {
        m_PollInFlight = true;
        try
        {
            Lobby currentLobby = m_CurrentLobby;
            if (!IsCurrentLobbyRequest(lobbyId, generation) || currentLobby == null)
                return;

            string previousState = BuildLobbyPlayersState(currentLobby);
            Lobby updated = await LobbyService.Instance.GetLobbyAsync(lobbyId);

            // The player can leave while the request is in flight.
            if (!IsCurrentLobbyRequest(lobbyId, generation))
                return;

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
            if (!IsCurrentLobbyRequest(lobbyId, generation))
                return;

            Debug.LogError($"[Lobby] Poll failed: {e.Message}");
            LastErrorMessage = "Lost lobby connection. Return to menu and try again.";
            ClearCurrentLobby();
        }
        finally
        {
            m_PollInFlight = false;
        }
    }

    async void SendHeartbeat(string lobbyId, int generation)
    {
        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
        }
        catch (Exception e)
        {
            if (IsCurrentLobbyRequest(lobbyId, generation))
            {
                Debug.LogWarning($"[Lobby] Heartbeat failed: {e.Message}");
            }
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
            players.Add(new PlayerLobbyData
            {
                Name = name,
                IsHost = isHost,
                IsReady = isHost || GetPlayerReady(p)
            });
        }

        OnLobbyPlayersChanged?.Invoke(players);
    }

    public void RefreshPlayersSnapshot()
    {
        NotifyPlayersChanged();
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
            builder.Append(':');
            builder.Append(GetPlayerReady(player));
        }

        return builder.ToString();
    }

    string GetPlayerName(Player player)
    {
        if (player.Data != null && player.Data.ContainsKey(PlayerNameKey))
        {
            return player.Data[PlayerNameKey].Value;
        }

        return "Unknown";
    }

    bool GetPlayerReady(Player player)
    {
        if (player.Data != null && player.Data.ContainsKey(ReadyKey))
        {
            return string.Equals(player.Data[ReadyKey].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public bool IsLocalPlayerReady()
    {
        if (m_CurrentLobby == null)
            return false;

        string localPlayerId = AuthenticationService.Instance.PlayerId;
        for (int i = 0; i < m_CurrentLobby.Players.Count; i++)
        {
            Player player = m_CurrentLobby.Players[i];
            if (player.Id == localPlayerId)
            {
                return player.Id == m_CurrentLobby.HostId || GetPlayerReady(player);
            }
        }

        return false;
    }

    public bool AreAllPlayersReady()
    {
        if (m_CurrentLobby == null || m_CurrentLobby.Players == null || m_CurrentLobby.Players.Count == 0)
            return false;

        for (int i = 0; i < m_CurrentLobby.Players.Count; i++)
        {
            Player player = m_CurrentLobby.Players[i];
            if (player.Id == m_CurrentLobby.HostId)
                continue;

            if (!GetPlayerReady(player))
                return false;
        }

        return true;
    }

    public async Task<bool> SetLocalReady(bool isReady)
    {
        if (m_CurrentLobby == null)
            return false;

        try
        {
            LastErrorMessage = string.Empty;
            UpdatePlayerOptions options = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    {
                        ReadyKey, new PlayerDataObject(
                            PlayerDataObject.VisibilityOptions.Member,
                            isReady ? "true" : "false")
                    }
                }
            };

            string lobbyId = m_CurrentLobby.Id;
            int generation = m_LobbyGeneration;
            Lobby updated = await LobbyService.Instance.UpdatePlayerAsync(
                lobbyId,
                AuthenticationService.Instance.PlayerId,
                options);
            if (!IsCurrentLobbyRequest(lobbyId, generation))
                return false;

            m_CurrentLobby = updated;
            NotifyPlayersChanged();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to update ready state: {e.Message}");
            LastErrorMessage = "Could not update ready state. Check the lobby connection.";
            return false;
        }
    }

    public bool IsHost()
    {
        return m_CurrentLobby != null &&
               m_CurrentLobby.HostId == AuthenticationService.Instance.PlayerId;
    }

    public Lobby GetCurrentLobby() => m_CurrentLobby;
    public int GetMaxPlayers() => m_CurrentLobby?.MaxPlayers ?? 4;
    public int GetCurrentPlayerCount() => m_CurrentLobby?.Players?.Count ?? 0;

    public async Task LeaveLobby()
    {
        Lobby leavingLobby = m_CurrentLobby;
        if (leavingLobby == null || m_IsLeavingLobby)
            return;

        bool localPlayerIsHost = leavingLobby.HostId == AuthenticationService.Instance.PlayerId;
        m_IsLeavingLobby = true;
        ClearCurrentLobby();

        try
        {
            // A host ending a match closes its lobby. Connected clients are then
            // disconnected by Netcode instead of inheriting a dead Relay session.
            if (localPlayerIsHost)
            {
                await LobbyService.Instance.DeleteLobbyAsync(leavingLobby.Id);
            }
            else
            {
                await LobbyService.Instance.RemovePlayerAsync(
                    leavingLobby.Id,
                    AuthenticationService.Instance.PlayerId);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] Leave request did not complete: {e.Message}");
        }
        finally
        {
            m_IsLeavingLobby = false;
        }
    }

    void SetCurrentLobby(Lobby lobby)
    {
        m_CurrentLobby = lobby;
        m_LobbyGeneration++;
        m_IsLeavingLobby = false;
        m_PollTimer = 0f;
        m_HeartbeatTimer = 0f;
    }

    void ClearCurrentLobby()
    {
        m_CurrentLobby = null;
        m_LobbyGeneration++;
        m_PollTimer = 0f;
        m_HeartbeatTimer = 0f;
    }

    bool IsCurrentLobbyRequest(string lobbyId, int generation)
    {
        return !m_IsLeavingLobby &&
               m_CurrentLobby != null &&
               m_LobbyGeneration == generation &&
               m_CurrentLobby.Id == lobbyId;
    }
}

public struct PlayerLobbyData
{
    public string Name;
    public bool IsHost;
    public bool IsReady;
}
