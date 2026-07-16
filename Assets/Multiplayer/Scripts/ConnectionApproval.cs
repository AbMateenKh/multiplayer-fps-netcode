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
        response.CreatePlayerObject = false;
    }
}
