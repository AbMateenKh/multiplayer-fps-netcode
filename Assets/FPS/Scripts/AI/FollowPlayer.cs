using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.AI
{
    public class FollowPlayer : MonoBehaviour
    {
        Transform m_PlayerTransform;
        Vector3 m_OriginalOffset;

        void Start()
        {
            //TODO MULTIPLAYER CONVERSION: This will need to be changed to get the local player character controller instead of just finding one in the scene

            //ActorsManager actorsManager = FindObjectOfType<ActorsManager>();
            //if (actorsManager != null)
            //    m_PlayerTransform = actorsManager.Player.transform;
            //else
            //{
            //    enabled = false;
            //    return;
            //}

            //m_OriginalOffset = transform.position - m_PlayerTransform.position;
        }

        void LateUpdate()
        {
            //transform.position = m_PlayerTransform.position + m_OriginalOffset;
        }
    }
}