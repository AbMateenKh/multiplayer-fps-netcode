using System.Collections.Generic;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerSpawner : MonoBehaviour
{
    public GameObject PlayerPrefab;
    public string GameSceneName = "MainScene";

    bool m_GameSceneLoaded = false;

    void Start()
    {
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
    }

    void OnServerStarted()
    {
        Debug.Log("[Spawner] Server started");
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoadComplete;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    void OnSceneLoadComplete(string sceneName,
        UnityEngine.SceneManagement.LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut)
    {
        Debug.Log($"[Spawner] Scene '{sceneName}' load complete");

        if (sceneName == GameSceneName)
        {
            m_GameSceneLoaded = true;

            // Spawn for all connected clients
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                {
                    SpawnPlayer(client.ClientId);
                }
            }
        }
    }

    void OnClientConnected(ulong clientId)
    {
        // Only spawn if game scene is loaded
        if (!m_GameSceneLoaded)
        {
            Debug.Log($"[Spawner] Client {clientId} connected but game scene not loaded yet");
            return;
        }

        StartCoroutine(DelayedSpawn(clientId));
    }

    System.Collections.IEnumerator DelayedSpawn(ulong clientId)
    {
        yield return new WaitForSeconds(0.5f);

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
        var spawnPoints = FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);

        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;

        if (spawnPoints.Length > 0)
        {
            Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)].transform;
            position = point.position;
            rotation = point.rotation;
        }

        Debug.Log($"[Spawner] Spawning player {clientId} at {position}");

        GameObject player = Instantiate(PlayerPrefab, position, rotation);
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoadComplete;
            }
        }
    }
}