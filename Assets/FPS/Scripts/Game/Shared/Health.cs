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

        [Tooltip("Seconds of damage immunity after spawning or respawning")]
        public float RespawnProtectionDuration = 2f;

        public UnityAction<float, GameObject> OnDamaged;
        public UnityAction<float> OnHealed;
        public UnityAction OnDie;

        public NetworkVariable<float> CurrentHealth = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<float> RespawnProtectionTimer = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public bool Invincible { get; set; }
        public bool IsRespawnProtected => RespawnProtectionTimer.Value > 0f;
        public bool CanTakeDamage => !Invincible && !IsRespawnProtected && CurrentHealth.Value > 0f;
        public bool CanPickup() => CurrentHealth.Value < MaxHealth;
        public float GetRatio() => CurrentHealth.Value / MaxHealth;
        public bool IsCritical() => GetRatio() <= CriticalHealthRatio;

        bool m_IsDead;
        bool m_IsSubscribedToHealthChanges;

        // NEW: Track who last dealt damage (server only)
        GameObject m_LastDamageSource;

        void Awake()
        {
            SubscribeToHealthChanges();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                CurrentHealth.Value = MaxHealth;
                if (CanUseRespawnProtection())
                {
                    StartRespawnProtection();
                }
            }

            SubscribeToHealthChanges();
        }

        void Update()
        {
            if (!IsServer || RespawnProtectionTimer.Value <= 0f)
                return;

            RespawnProtectionTimer.Value = Mathf.Max(0f, RespawnProtectionTimer.Value - Time.deltaTime);
        }

        public override void OnNetworkDespawn()
        {
            UnsubscribeFromHealthChanges();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            UnsubscribeFromHealthChanges();
        }

        void SubscribeToHealthChanges()
        {
            if (m_IsSubscribedToHealthChanges)
                return;

            CurrentHealth.OnValueChanged += OnHealthChanged;
            m_IsSubscribedToHealthChanges = true;
        }

        void UnsubscribeFromHealthChanges()
        {
            if (!m_IsSubscribedToHealthChanges)
                return;

            CurrentHealth.OnValueChanged -= OnHealthChanged;
            m_IsSubscribedToHealthChanges = false;
        }

        void OnHealthChanged(float previousValue, float newValue)
        {
            NotifyHealthChanged(previousValue, newValue, null);
        }

        void NotifyHealthChanged(float previousValue, float newValue, GameObject damageSource)
        {
            if (newValue < previousValue)
            {
                float damageAmount = previousValue - newValue;
                OnDamaged?.Invoke(damageAmount, damageSource);
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
            if (IsServer || !IsSpawned)
            {
                if (!CanTakeDamage) return;

                m_LastDamageSource = damageSource;

                float previousHealth = CurrentHealth.Value;
                float newHealth = Mathf.Clamp(CurrentHealth.Value - damage, 0f, MaxHealth);
                bool shouldNotifyManually = !IsSpawned ||
                    NetworkManager.Singleton == null ||
                    !NetworkManager.Singleton.IsListening;

                // Check death BEFORE setting NetworkVariable
                if (newHealth <= 0f && !m_IsDead)
                {
                    CurrentHealth.Value = newHealth;
                    if (shouldNotifyManually)
                    {
                        NotifyHealthChanged(previousHealth, newHealth, damageSource);
                    }
                    ProcessDeath();
                }
                else
                {
                    CurrentHealth.Value = newHealth;
                    if (shouldNotifyManually)
                    {
                        NotifyHealthChanged(previousHealth, newHealth, damageSource);
                    }
                }
            }
            else
            {
                TakeDamageServerRpc(damage);
            }
        }

        void ProcessDeath()
        {
            if (m_IsDead) return;

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
            Revive(true);
        }

        public void Revive(bool useRespawnProtection)
        {
            if (!IsServer && IsSpawned) return;

            m_IsDead = false;
            Invincible = false;
            m_LastDamageSource = null;
            CurrentHealth.Value = MaxHealth;
            RespawnProtectionTimer.Value = 0f;
            if (useRespawnProtection)
            {
                StartRespawnProtection();
            }

            // Tell clients to reset death state
            if (IsSpawned)
            {
                RespawnClientRpc();
            }
        }

        void StartRespawnProtection()
        {
            RespawnProtectionTimer.Value = Mathf.Max(0f, RespawnProtectionDuration);
        }

        bool CanUseRespawnProtection()
        {
            return GetComponent<IPlayerController>() != null;
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

    }
}
