using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }
    public string LastErrorMessage { get; private set; }

    const RelayProtocol k_RelayProtocol = RelayProtocol.Default;
    const int k_ConnectionTimeoutMilliseconds = 15000;

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

    /// <summary>
    /// Host creates a relay allocation and starts as host
    /// Returns the join code for other players
    /// </summary>
    public async Task<string> CreateRelay(int maxPlayers = 3)
    {
        try
        {
            LastErrorMessage = string.Empty;

            // Create allocation (maxPlayers excludes the host)
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);

            // Get join code to share with others
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            Debug.Log($"[Relay] Created relay. Join code: {joinCode}");

            // Configure transport
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.UseWebSockets = k_RelayProtocol == RelayProtocol.WSS;
            transport.SetRelayServerData(allocation.ToRelayServerData(k_RelayProtocol));

            if (!NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.StartHost();
            }

            return joinCode;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay] Failed to create relay: {e.Message}");
            LastErrorMessage = "Could not create Relay allocation. Check services, internet, and Unity dashboard setup.";
            return null;
        }
    }

    /// <summary>
    /// Client joins using the join code
    /// </summary>
    public async Task<bool> JoinRelay(string joinCode)
    {
        try
        {
            LastErrorMessage = string.Empty;
            Debug.Log($"[Relay] Joining with code: {joinCode}");

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                LastErrorMessage = "Network bootstrap is missing.";
                return false;
            }

            UnityTransport transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                LastErrorMessage = "Unity Transport is missing from the network bootstrap.";
                return false;
            }

            if (networkManager.IsListening || networkManager.ShutdownInProgress)
            {
                LastErrorMessage = "A network session is already running.";
                return false;
            }

            transport.UseWebSockets = k_RelayProtocol == RelayProtocol.WSS;
            transport.SetRelayServerData(joinAllocation.ToRelayServerData(k_RelayProtocol));

            var connectionResult = new TaskCompletionSource<bool>();

            void OnConnected(ulong clientId)
            {
                if (clientId == networkManager.LocalClientId)
                {
                    connectionResult.TrySetResult(true);
                }
            }

            void OnDisconnected(ulong clientId)
            {
                connectionResult.TrySetResult(false);
            }

            networkManager.OnClientConnectedCallback += OnConnected;
            networkManager.OnClientDisconnectCallback += OnDisconnected;

            try
            {
                if (!networkManager.StartClient())
                {
                    LastErrorMessage = "Could not start the multiplayer client.";
                    return false;
                }

                Task completed = await Task.WhenAny(
                    connectionResult.Task,
                    Task.Delay(k_ConnectionTimeoutMilliseconds));

                if (completed != connectionResult.Task || !connectionResult.Task.Result)
                {
                    bool timedOut = completed != connectionResult.Task;
                    string disconnectReason = networkManager.DisconnectReason;
                    if (!string.IsNullOrWhiteSpace(disconnectReason))
                    {
                        LastErrorMessage = disconnectReason;
                    }
                    else if (timedOut)
                    {
                        LastErrorMessage =
                            "Relay connection timed out. Ask the host to create a fresh lobby.";
                    }
                    else
                    {
                        LastErrorMessage =
                            "The host rejected the connection. Restart both game instances " +
                            "so they use the same project version.";
                    }

                    // A disconnect callback can resume this method while NGO is already
                    // tearing down. Calling Shutdown again from that path re-enters UTP
                    // and can produce a misleading Relay "not connected" error.
                    if (networkManager.IsListening && !networkManager.ShutdownInProgress)
                    {
                        networkManager.Shutdown();
                    }

                    string failureStage = timedOut ? "timed out" : "was disconnected";
                    Debug.LogWarning(
                        $"[Relay] Client {failureStage} before approval: {LastErrorMessage}");
                    return false;
                }

                Debug.Log("[Relay] Client connection approved by host.");
                return true;
            }
            finally
            {
                networkManager.OnClientConnectedCallback -= OnConnected;
                networkManager.OnClientDisconnectCallback -= OnDisconnected;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay] Failed to join relay: {e.Message}");
            LastErrorMessage = "Could not connect to Relay. The lobby may have closed or the join code expired.";
            return false;
        }
    }
}
