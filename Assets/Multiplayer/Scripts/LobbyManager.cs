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

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Update()
    {
        HandleHeartbeat();
        HandlePollForUpdates();
    }

    /// <summary>
    /// Host creates a lobby and relay, stores join code in lobby data
    /// </summary>
    public async Task<Lobby> CreateLobby(string lobbyName, int maxPlayers = 4)
    {
        try
        {
            // Create relay first, get the join code
            string relayJoinCode = await RelayManager.Instance.CreateRelay(maxPlayers - 1);

            if (relayJoinCode == null) return null;

            // Create lobby with relay join code stored in data
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false,
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

            Debug.Log($"[Lobby] Created: {m_CurrentLobby.Name}, " +
                      $"Code: {m_CurrentLobby.LobbyCode}, " +
                      $"Players: {m_CurrentLobby.Players.Count}/{m_CurrentLobby.MaxPlayers}");

            return m_CurrentLobby;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to create: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Client joins lobby by code, reads relay join code, connects
    /// </summary>
    public async Task<bool> JoinLobbyByCode(string lobbyCode)
    {
        try
        {
            m_CurrentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);

            Debug.Log($"[Lobby] Joined: {m_CurrentLobby.Name}");

            // Get relay join code from lobby data
            string relayJoinCode = m_CurrentLobby.Data["RelayJoinCode"].Value;

            // Connect via relay
            return await RelayManager.Instance.JoinRelay(relayJoinCode);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to join: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Quick join any available lobby
    /// </summary>
    public async Task<bool> QuickJoin()
    {
        try
        {
            m_CurrentLobby = await LobbyService.Instance.QuickJoinLobbyAsync();

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

    /// <summary>
    /// Keep lobby alive (host must send heartbeat every 30s)
    /// </summary>
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

    /// <summary>
    /// Poll for lobby updates (player list changes)
    /// </summary>
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
            m_CurrentLobby = await LobbyService.Instance.GetLobbyAsync(m_CurrentLobby.Id);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Poll failed: {e.Message}");
            m_CurrentLobby = null;
        }
    }

    bool IsHost()
    {
        return m_CurrentLobby != null &&
               m_CurrentLobby.HostId == AuthenticationService.Instance.PlayerId;
    }

    public Lobby GetCurrentLobby() => m_CurrentLobby;

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