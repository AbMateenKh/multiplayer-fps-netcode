using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class StanceHUD : MonoBehaviour
    {
        [Tooltip("Image component for the stance sprites")]
        public Image StanceImage;

        [Tooltip("Sprite to display when standing")]
        public Sprite StandingSprite;

        [Tooltip("Sprite to display when crouching")]
        public Sprite CrouchingSprite;

        PlayerCharacterController m_PlayerCharacterController;

        void Awake()
        {
            PlayerCharacterController.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
        }

        void OnDestroy()
        {
            PlayerCharacterController.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;

            if (m_PlayerCharacterController != null)
            {
                m_PlayerCharacterController.OnStanceChanged -= OnStanceChanged;
            }
        }

        void OnLocalPlayerSpawned(PlayerCharacterController player)
        {
            if (m_PlayerCharacterController != null)
            {
                m_PlayerCharacterController.OnStanceChanged -= OnStanceChanged;
            }

            m_PlayerCharacterController = player;
            m_PlayerCharacterController.OnStanceChanged += OnStanceChanged;

            OnStanceChanged(m_PlayerCharacterController.IsCrouching);
        }

        void OnStanceChanged(bool crouched)
        {
            StanceImage.sprite = crouched ? CrouchingSprite : StandingSprite;
        }
    }
}
