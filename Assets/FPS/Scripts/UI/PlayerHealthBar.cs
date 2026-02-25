using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class PlayerHealthBar : MonoBehaviour
    {
        [Tooltip("Image component displaying current health")]
        public Image HealthFillImage;

        Health m_PlayerHealth;

        void Start()
        {
            PlayerCharacterController.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
        }

        void OnDestroy()
        {
            PlayerCharacterController.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;

            if (m_PlayerHealth != null)
            {
                m_PlayerHealth.CurrentHealth.OnValueChanged -= OnHealthChanged;
            }
        }

        void OnLocalPlayerSpawned(PlayerCharacterController player)
        {
            m_PlayerHealth = player.GetComponent<Health>();

            // Subscribe to NetworkVariable changes instead of polling in Update
            m_PlayerHealth.CurrentHealth.OnValueChanged += OnHealthChanged;

            // Set initial value
            UpdateHealthBar();
        }

        void OnHealthChanged(float previousValue, float newValue)
        {
            UpdateHealthBar();
        }

        void UpdateHealthBar()
        {
            if (m_PlayerHealth != null)
            {
                HealthFillImage.fillAmount = m_PlayerHealth.GetRatio();
            }
        }

    }
}