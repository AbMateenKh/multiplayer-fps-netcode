using UnityEngine;

namespace Unity.FPS.Game
{
    public class Destructable : MonoBehaviour
    {
        Health m_Health;

        void Start()
        {
            //TODO MULTIPLAYER CONVERSION: This will need to be changed to get the local player character controller instead of just finding one in the scene

            //m_Health = GetComponent<Health>();
            //DebugUtility.HandleErrorIfNullGetComponent<Health, Destructable>(m_Health, this, gameObject);

            //// Subscribe to damage & death actions
            //m_Health.OnDie += OnDie;
            //m_Health.OnDamaged += OnDamaged;
        }

        void OnDamaged(float damage, GameObject damageSource)
        {
            // TODO: damage reaction
        }

        void OnDie()
        {
            // this will call the OnDestroy function
            Destroy(gameObject);
        }
    }
}