using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Game
{
    public class Health : NetworkBehaviour
    {
        [Tooltip("Maximum amount of health")] public float MaxHealth = 10f;

        [Tooltip("Health ratio at which the critical health vignette starts appearing")]
        public float CriticalHealthRatio = 0.3f;

        public UnityAction<float, GameObject> OnDamaged;
        public UnityAction<float> OnHealed;
        public UnityAction OnDie;


        // NETWORKED STATE
        // Server writes, all clients read
        public NetworkVariable<float> CurrentHealth = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,    // All clients can read
            NetworkVariableWritePermission.Server       // Only server can write
        );
        public bool Invincible { get; set; }
        public bool CanPickup() => CurrentHealth.Value < MaxHealth;

        public float GetRatio() => CurrentHealth.Value / MaxHealth;
        public bool IsCritical() => GetRatio() <= CriticalHealthRatio;

        bool m_IsDead;


        public override void OnNetworkSpawn()
        {
            if(IsServer)
            {
                CurrentHealth.Value = MaxHealth;
            }

            CurrentHealth.OnValueChanged += OnHealthChanged;
        }

        public override void OnNetworkDespawn()
        {
            CurrentHealth.OnValueChanged -= OnHealthChanged;
        }

        private void OnHealthChanged(float previousValue, float newValue)
        {
            // Someone took damage
            if (newValue < previousValue)
            {
                float damageAmount = previousValue - newValue;
                OnDamaged?.Invoke(damageAmount, null);
            }
            // Someone got healed
            else if (newValue > previousValue)
            {
                float healAmount = newValue - previousValue;
                OnHealed?.Invoke(healAmount);
            }

            // Check for death on all clients
            // so each client can react (animations, UI, etc.)
            if (newValue <= 0f && !m_IsDead)
            {
                m_IsDead = true;
                OnDie?.Invoke();
            }
        }
        /// <summary>
        /// Only Server will heal
        /// </summary>
        /// <param name="healAmount"></param>
        public void Heal(float healAmount)
        {
            if (IsServer)
            {
                CurrentHealth.Value += healAmount;
                CurrentHealth.Value = Mathf.Clamp(CurrentHealth.Value, 0f, MaxHealth);
            }
            else
            {
                HealServerRpc(healAmount);
            }
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void HealServerRpc(float healAmount)
        {
            Heal(healAmount);
        }

        public void TakeDamage(float damage, GameObject damageSource)
        {
            // If we're the server, just do it
            if (IsServer)
            {
                if (Invincible) return;

                CurrentHealth.Value -= damage;
                CurrentHealth.Value = Mathf.Clamp(CurrentHealth.Value, 0f, MaxHealth);
                HandleDeath();
            }
            // If we're a client, ask the server to do it
            else
            {
                TakeDamageServerRpc(damage);
            }
        }


        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void TakeDamageServerRpc(float damage)
        {
            TakeDamage(damage, null);
        }

        // KILL — Server only
        public void Kill()
        {
            if (IsServer)
            {
                CurrentHealth.Value = 0f;
                HandleDeath();
            }
            else
            {
                KillServerRpc();
            }
        }

        [Rpc(SendTo.Server,InvokePermission = RpcInvokePermission.Everyone)]
        public void KillServerRpc()
        {
            Kill();
        }

        


        // DEATH — Server checks, NetworkVariable
        // change triggers reaction on all clients
        void HandleDeath()
        {
            if (m_IsDead) return;

            if (CurrentHealth.Value <= 0f)
            {
                m_IsDead = true;
                OnDie?.Invoke();
            }
        }
    }
}