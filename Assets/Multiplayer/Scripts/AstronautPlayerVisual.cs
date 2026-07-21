using System.Collections;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.FPS.Gameplay
{
    public sealed class AstronautPlayerVisual : NetworkBehaviour
    {
        const float k_StateSendInterval = 0.08f;

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int DeadHash = Animator.StringToHash("Dead");
        static readonly int ShootHash = Animator.StringToHash("Shoot");
        static readonly int HitHash = Animator.StringToHash("Hit");

        public GameObject[] CharacterModels;
        public RuntimeAnimatorController AnimationController;
        public Material CharacterMaterial;
        public float ModelScale = 0.62f;
        public Vector3 ModelOffset = Vector3.zero;
        public float ModelYaw = 180f;

        readonly NetworkVariable<float> m_MoveSpeed = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<bool> m_Grounded = new(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<ushort> m_ShotSequence = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        PlayerCharacterController m_Character;
        PlayerWeaponsManager m_Weapons;
        WeaponController m_ObservedWeapon;
        Health m_Health;
        Animator m_Animator;
        GameObject m_VisualInstance;
        float m_NextStateSendTime;
        float m_MovementCycle;
        float m_Recoil;
        float m_HitPulse;
        float m_DeathBlend;
        bool m_IsDead;

        public override void OnNetworkSpawn()
        {
            m_Character = GetComponent<PlayerCharacterController>();
            m_Weapons = GetComponent<PlayerWeaponsManager>();
            m_Health = GetComponent<Health>();

            DisablePrototypeRenderers();
            CreateCharacterVisual();

            m_ShotSequence.OnValueChanged += OnShotSequenceChanged;
            if (m_Health != null)
            {
                m_Health.CurrentHealth.OnValueChanged += OnHealthChanged;
                SetDead(m_Health.CurrentHealth.Value <= 0f);
            }

            if (IsOwner)
            {
                if (m_Weapons != null)
                {
                    m_Weapons.OnSwitchedToWeapon += OnWeaponSwitched;
                }

                StartCoroutine(BindInitialWeapon());
            }
        }

        public override void OnNetworkDespawn()
        {
            m_ShotSequence.OnValueChanged -= OnShotSequenceChanged;

            if (m_Health != null)
            {
                m_Health.CurrentHealth.OnValueChanged -= OnHealthChanged;
            }

            if (m_Weapons != null)
            {
                m_Weapons.OnSwitchedToWeapon -= OnWeaponSwitched;
            }

            BindWeapon(null);
        }

        void Update()
        {
            if (IsOwner && Time.unscaledTime >= m_NextStateSendTime)
            {
                float horizontalSpeed = 0f;
                bool grounded = true;
                if (m_Character != null)
                {
                    Vector3 velocity = m_Character.CharacterVelocity;
                    horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
                    grounded = m_Character.IsGrounded;
                }

                m_MoveSpeed.Value = horizontalSpeed;
                m_Grounded.Value = grounded;
                m_NextStateSendTime = Time.unscaledTime + k_StateSendInterval;
            }

            if (m_Animator != null)
            {
                m_Animator.SetFloat(SpeedHash, m_MoveSpeed.Value, 0.1f, Time.deltaTime);
                m_Animator.SetBool(GroundedHash, m_Grounded.Value);
            }
            else
            {
                AnimateProceduralVisual();
            }
        }

        void CreateCharacterVisual()
        {
            if (CharacterModels == null || CharacterModels.Length == 0)
                return;

            int variantIndex = (int)(OwnerClientId % (ulong)CharacterModels.Length);
            GameObject model = CharacterModels[variantIndex];
            if (model == null)
                return;

            m_VisualInstance = Instantiate(model, transform);
            m_VisualInstance.name = $"AstronautVisual_{variantIndex + 1}";
            m_VisualInstance.transform.SetLocalPositionAndRotation(
                ModelOffset,
                Quaternion.Euler(0f, ModelYaw, 0f));
            m_VisualInstance.transform.localScale = Vector3.one * ModelScale;

            m_Animator = m_VisualInstance.GetComponentInChildren<Animator>(true);
            if (m_Animator != null && AnimationController != null)
            {
                m_Animator.runtimeAnimatorController = AnimationController;
                m_Animator.applyRootMotion = false;
                m_Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                m_Animator.Rebind();
                m_Animator.Update(0f);
                m_Animator.SetBool(GroundedHash, true);
            }
            else
            {
                if (m_Animator != null)
                {
                    m_Animator.enabled = false;
                }

                m_Animator = null;
            }

            foreach (Renderer renderer in m_VisualInstance.GetComponentsInChildren<Renderer>(true))
            {
                if (CharacterMaterial != null)
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        materials[i] = CharacterMaterial;
                    }

                    renderer.sharedMaterials = materials;
                }

                if (IsOwner)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }
        }

        void AnimateProceduralVisual()
        {
            if (m_VisualInstance == null)
                return;

            float deltaTime = Time.deltaTime;
            float speed01 = Mathf.Clamp01(m_MoveSpeed.Value / 5.5f);
            m_MovementCycle += deltaTime * Mathf.Lerp(2f, 10f, speed01);
            m_Recoil = Mathf.MoveTowards(m_Recoil, 0f, deltaTime * 7f);
            m_HitPulse = Mathf.MoveTowards(m_HitPulse, 0f, deltaTime * 4f);
            m_DeathBlend = Mathf.MoveTowards(
                m_DeathBlend,
                m_IsDead ? 1f : 0f,
                deltaTime * 2.5f);

            float groundedBob = m_Grounded.Value
                ? Mathf.Sin(m_MovementCycle * 2f) * 0.035f * speed01
                : 0.035f;
            float strideRoll = Mathf.Sin(m_MovementCycle) * 2.4f * speed01;
            float hitRoll = Mathf.Sin(Time.time * 32f) * 7f * m_HitPulse;

            m_VisualInstance.transform.localPosition =
                ModelOffset +
                Vector3.up * (groundedBob - 0.62f * m_DeathBlend) +
                Vector3.back * (0.05f * m_Recoil);
            m_VisualInstance.transform.localRotation = Quaternion.Euler(
                -8f * m_Recoil,
                ModelYaw,
                strideRoll + hitRoll + 88f * m_DeathBlend);
        }

        void DisablePrototypeRenderers()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.name.StartsWith("Capsule"))
                {
                    renderer.enabled = false;
                }
            }
        }

        IEnumerator BindInitialWeapon()
        {
            for (int i = 0; i < 10 && m_Weapons != null; i++)
            {
                WeaponController activeWeapon = m_Weapons.GetActiveWeapon();
                if (activeWeapon != null)
                {
                    BindWeapon(activeWeapon);
                    yield break;
                }

                yield return null;
            }
        }

        void OnWeaponSwitched(WeaponController weapon)
        {
            BindWeapon(weapon);
        }

        void BindWeapon(WeaponController weapon)
        {
            if (m_ObservedWeapon != null)
            {
                m_ObservedWeapon.OnShootProcessed -= OnLocalShot;
            }

            m_ObservedWeapon = weapon;
            if (m_ObservedWeapon != null)
            {
                m_ObservedWeapon.OnShootProcessed += OnLocalShot;
            }
        }

        void OnLocalShot()
        {
            if (!IsOwner || !IsSpawned)
                return;

            m_ShotSequence.Value++;
            TriggerShoot();
        }

        void OnShotSequenceChanged(ushort previous, ushort current)
        {
            if (!IsOwner && previous != current)
            {
                TriggerShoot();
            }
        }

        void TriggerShoot()
        {
            if (m_Animator != null)
            {
                m_Animator.SetTrigger(ShootHash);
            }
            else
            {
                m_Recoil = 1f;
            }
        }

        void OnHealthChanged(float previous, float current)
        {
            SetDead(current <= 0f);
            if (current > 0f && current < previous && m_Animator != null)
            {
                m_Animator.SetTrigger(HitHash);
            }
            else if (current > 0f && current < previous)
            {
                m_HitPulse = 1f;
            }
        }

        void SetDead(bool dead)
        {
            m_IsDead = dead;
            if (m_Animator != null)
            {
                m_Animator.SetBool(DeadHash, dead);
            }
        }
    }
}
