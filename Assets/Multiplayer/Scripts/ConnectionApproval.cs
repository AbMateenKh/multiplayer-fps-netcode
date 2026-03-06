using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;

public class ConnectionApproval : MonoBehaviour
{
    void Start()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;
    }

    void ApproveConnection(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = true;
        response.CreatePlayerObject = true;

        // Find a spawn point in the current scene
        var spawnPoints = FindObjectsOfType<PlayerSpawnPoint>();
        if (spawnPoints.Length > 0)
        {
            Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)].transform;
            response.Position = point.position;
            response.Rotation = point.rotation;
        }
    }
}