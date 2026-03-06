using Unity.FPS.Game;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

public class PlayerSpawner : NetworkBehaviour
{
    [Header("Player Prefab")]
    public GameObject PlayerPrefab;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Spawn players for all connected clients
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                SpawnPlayer(client.ClientId);
            }

            // Spawn future players
            NetworkManager.Singleton.OnClientConnectedCallback += SpawnPlayer;
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

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= SpawnPlayer;
        }
    }
}

