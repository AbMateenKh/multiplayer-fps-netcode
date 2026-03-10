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

    public event Action<int> OnPlayerCountChanged;

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

    /// <summary>
    /// Create a private lobby (requires code to join)
    /// </summary>
    public async Task<Lobby> CreatePrivateLobby(string lobbyName, int maxPlayers = 4)
    {
        try
        {
            string relayJoinCode = await RelayManager.Instance.CreateRelay(maxPlayers - 1);
            if (relayJoinCode == null) return null;

            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = true,
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
            return m_CurrentLobby;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to create private lobby: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Create a public lobby (anyone can quick join)
    /// </summary>
    public async Task<Lobby> CreatePublicLobby(string lobbyName, int maxPlayers = 4)
    {
        try
        {
            string relayJoinCode = await RelayManager.Instance.CreateRelay(maxPlayers - 1);
            if (relayJoinCode == null) return null;

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

            Debug.Log($"[Lobby] Public lobby created. Name: {m_CurrentLobby.Name}");
            return m_CurrentLobby;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Failed to create public lobby: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Join lobby by code (for private lobbies)
    /// </summary>
    public async Task<bool> JoinLobbyByCode(string lobbyCode)
    {
        try
        {
            m_CurrentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);
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

    /// <summary>
    /// Quick join any available public lobby
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
            Lobby updated = await LobbyService.Instance.GetLobbyAsync(m_CurrentLobby.Id);
            if (updated.Players.Count != m_CurrentLobby.Players.Count)
            {
                OnPlayerCountChanged?.Invoke(updated.Players.Count);
            }
            m_CurrentLobby = updated;
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