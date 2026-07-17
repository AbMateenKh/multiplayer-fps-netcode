using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;

public class PlayerSpawner : NetworkBehaviour
{
    [Header("Player Prefab")]
    public GameObject PlayerPrefab;

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[Spawner] OnNetworkSpawn. IsServer: {IsServer}");

        if (IsServer)
        {
            Debug.Log($"[Spawner] Connected clients: {NetworkManager.Singleton.ConnectedClientsList.Count}");

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                Debug.Log($"[Spawner] Client {client.ClientId}, " +
                          $"HasPlayer: {client.PlayerObject != null}");

                if (client.PlayerObject == null)
                {
                    SpawnPlayer(client.ClientId);
                }
            }

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    void OnSceneLoadComplete(string sceneName,
        UnityEngine.SceneManagement.LoadSceneMode loadSceneMode,
        System.Collections.Generic.List<ulong> clientsCompleted,
        System.Collections.Generic.List<ulong> clientsTimedOut)
    {
        Debug.Log($"[Spawner] Scene '{sceneName}' load complete. " +
                  $"Clients: {clientsCompleted.Count}");

        // Spawn for all connected clients who don't have a player yet
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
            {
                SpawnPlayer(client.ClientId);
            }
        }
    }

    void OnClientConnected(ulong clientId)
    {
        // For clients who join after scene is already loaded
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject == null)
            {
                SpawnPlayer(clientId);
            }
        }
    }

    void SpawnPlayer(ulong clientId)
    {
        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;
        GameFlowManager gameFlowManager = FindFirstObjectByType<GameFlowManager>();

        if (gameFlowManager != null)
        {
            Transform point = gameFlowManager.GetBestSpawnPoint(clientId);
            position = point.position;
            rotation = point.rotation;
        }
        else
        {
            var spawnPoints = FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
            if (spawnPoints.Length > 0)
            {
                Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)].transform;
                position = point.position;
                rotation = point.rotation;
            }
        }

        Debug.Log($"[Spawner] Spawning player {clientId} at {position}");

        GameObject player = Instantiate(PlayerPrefab, position, rotation);
        NetworkObject networkObject = player.GetComponent<NetworkObject>();
        networkObject.SpawnAsPlayerObject(clientId, true);
        gameFlowManager?.InitializePlayerForCurrentMatch(clientId, networkObject);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoadComplete;
            }
        }
    }
}
