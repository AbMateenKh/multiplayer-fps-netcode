using UnityEngine;

namespace Unity.FPS.Game
{
    public class PlayerSpawnPoint : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            Gizmos.DrawLine(transform.position,
                transform.position + transform.forward * 1.5f);
        }
    }
}