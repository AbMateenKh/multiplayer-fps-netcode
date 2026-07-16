using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    public class HealthPickup : Pickup
    {
        [Header("Parameters")] [Tooltip("Amount of health to heal on pickup")]
        public float HealAmount;

        protected override bool ApplyPickupEffect(PlayerCharacterController player, bool serverAuthoritative)
        {
            Health playerHealth = player.GetComponent<Health>();
            if (playerHealth && playerHealth.CanPickup())
            {
                if (serverAuthoritative)
                {
                    playerHealth.Heal(HealAmount);
                }

                return true;
            }

            return false;
        }
    }
}
