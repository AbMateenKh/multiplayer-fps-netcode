using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Game
{
    public class Health : NetworkBehaviour
    {
        [Tooltip("Maximum amount of health")]
        public float MaxHealth = 10f;

        [Tooltip("Health ratio at which the critical health vignette starts appearing")]
        public float CriticalHealthRatio = 0.3f;

        public UnityAction<float, GameObject> OnDamaged;
        public UnityAction<float> OnHealed;
        public UnityAction OnDie;

        public NetworkVariable<float> CurrentHealth = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public bool Invincible { get; set; }
        public bool CanPickup() => CurrentHealth.Value < MaxHealth;
        public float GetRatio() => CurrentHealth.Value / MaxHealth;
        public bool IsCritical() => GetRatio() <= CriticalHealthRatio;

        bool m_IsDead;

        // NEW: Track who last dealt damage (server only)
        GameObject m_LastDamageSource;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                CurrentHealth.Value = MaxHealth;
            }

            CurrentHealth.OnValueChanged += OnHealthChanged;
        }

        public override void OnNetworkDespawn()
        {
            CurrentHealth.OnValueChanged -= OnHealthChanged;
        }

        void OnHealthChanged(float previousValue, float newValue)
        {
            if (newValue < previousValue)
            {
                float damageAmount = previousValue - newValue;
                OnDamaged?.Invoke(damageAmount, null);
            }
            else if (newValue > previousValue)
            {
                float healAmount = newValue - previousValue;
                OnHealed?.Invoke(healAmount);
            }

            // Client-side death effects ONLY — no m_IsDead here
            if (newValue <= 0f && previousValue > 0f)
            {
                OnDie?.Invoke();
            }
        }

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
            if (IsServer)
            {
                if (Invincible) return;

                m_LastDamageSource = damageSource;

                float newHealth = Mathf.Clamp(CurrentHealth.Value - damage, 0f, MaxHealth);

                // Check death BEFORE setting NetworkVariable
                if (newHealth <= 0f && !m_IsDead)
                {
                    CurrentHealth.Value = newHealth;
                    ProcessDeath();
                }
                else
                {
                    CurrentHealth.Value = newHealth;
                }
            }
            else
            {
                TakeDamageServerRpc(damage);
            }
        }

        void ProcessDeath()
        {

            Debug.Log($"[Health] ProcessDeath ENTRY. m_IsDead: {m_IsDead}");
            if (m_IsDead) return;
            Debug.Log($"[Health] ProcessDeath PASSED m_IsDead check");
            

            m_IsDead = true;
            Invincible = true;

            IPlayerController playerController = GetComponent<IPlayerController>();
            if (playerController != null)
            {
                GameFlowManager gfm = FindFirstObjectByType<GameFlowManager>();
                if (gfm != null)
                {
                    ulong victimId = GetComponent<NetworkObject>().OwnerClientId;

                    ulong killerId = victimId;
                    if (m_LastDamageSource != null)
                    {
                        NetworkObject killerNetObj =
                            m_LastDamageSource.GetComponent<NetworkObject>();
                        if (killerNetObj != null)
                        {
                            killerId = killerNetObj.OwnerClientId;
                        }
                    }

                    Debug.Log($"[Health] Recording kill. Victim: {victimId}, Killer: {killerId}");
                    gfm.RecordKill(victimId, killerId);
                    gfm.RequestRespawn(victimId);
                }
            }
        }

            [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void TakeDamageServerRpc(float damage)
        {
            TakeDamage(damage, null);
        }

        public void Kill()
        {
            if (IsServer)
            {
                if (m_IsDead) return;
                CurrentHealth.Value = 0f;
                ProcessDeath();
            }
            else
            {
                KillServerRpc();
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void KillServerRpc()
        {
            Kill();
        }

        // ============================================
        // RESPAWN — Called by GameFlowManager after delay
        // Resets health and death state
        // ============================================
        public void Respawn()
        {
            if (!IsServer) return;

            m_IsDead = false;
            m_LastDamageSource = null;
            CurrentHealth.Value = MaxHealth;

            // Tell clients to reset death state
            RespawnClientRpc();
        }

        [ClientRpc]
        void RespawnClientRpc()
        {
            m_IsDead = false;

            // Notify player controller to re-enable
            IPlayerController playerController = GetComponent<IPlayerController>();
            if (playerController != null)
            {
                playerController.OnRespawn();
            }
        }

        // ============================================
        // DEATH — Server handles kill tracking + respawn
        // ============================================
        void HandleDeath()
        {
            //if (m_IsDead) return;

            //if (CurrentHealth.Value <= 0f)
            //{
            //    m_IsDead = true;
            //    Invincible = true;

            //    // DON'T call OnDie here — OnHealthChanged already fires it on all clients
            //    // OnDie?.Invoke();  ← REMOVE THIS

            //    // Server-only: record kill and respawn
            //    IPlayerController playerController = GetComponent<IPlayerController>();
            //    if (playerController != null)
            //    {
            //        GameFlowManager gfm = FindObjectOfType<GameFlowManager>();
            //        if (gfm != null)
            //        {
            //            ulong victimId = GetComponent<NetworkObject>().OwnerClientId;

            //            ulong killerId = victimId;
            //            if (m_LastDamageSource != null)
            //            {
            //                NetworkObject killerNetObj =
            //                    m_LastDamageSource.GetComponent<NetworkObject>();
            //                if (killerNetObj != null)
            //                {
            //                    killerId = killerNetObj.OwnerClientId;
            //                }
            //            }

            //            gfm.RecordKill(victimId, killerId);
            //            gfm.RequestRespawn(victimId);
            //        }
            //    }
            //}
        }
    }
}
