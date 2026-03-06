using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Host creates a relay allocation and starts as host
    /// Returns the join code for other players
    /// </summary>
    public async Task<string> CreateRelay(int maxPlayers = 3)
    {
        try
        {
            // Create allocation (maxPlayers excludes the host)
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);

            // Get join code to share with others
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            Debug.Log($"[Relay] Created relay. Join code: {joinCode}");

            // Configure transport
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            // Start host
            NetworkManager.Singleton.StartHost();

            return joinCode;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay] Failed to create relay: {e.Message}");
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
            Debug.Log($"[Relay] Joining with code: {joinCode}");

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            // Configure transport
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData
            );

            // Start client
            NetworkManager.Singleton.StartClient();

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay] Failed to join relay: {e.Message}");
            return false;
        }
    }
}