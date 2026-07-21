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

        [Header("Authored Player Prefab References")]
        public GameObject CharacterRoot;
        public Animator CharacterAnimator;
        public Transform AimTorso;

        readonly NetworkVariable<float> m_MoveSpeed = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<bool> m_Grounded = new(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<float> m_AimPitch = new(
            0f,
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
        Transform m_AimTorso;
        float m_NextStateSendTime;
        float m_VisualAimPitch;

        public override void OnNetworkSpawn()
        {
            m_Character = GetComponent<PlayerCharacterController>();
            m_Weapons = GetComponent<PlayerWeaponsManager>();
            m_Health = GetComponent<Health>();

            DisablePrototypeRenderers();
            BindAuthoredCharacterVisual();

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

                    if (m_Character.PlayerCamera != null)
                    {
                        m_AimPitch.Value = Mathf.Clamp(
                            Mathf.DeltaAngle(
                                0f,
                                m_Character.PlayerCamera.transform.localEulerAngles.x),
                            -75f,
                            75f);
                    }
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
        }

        void LateUpdate()
        {
            if (m_Animator == null || m_AimTorso == null)
                return;

            float aimSharpness = 1f - Mathf.Exp(-14f * Time.deltaTime);
            m_VisualAimPitch = Mathf.LerpAngle(
                m_VisualAimPitch,
                m_AimPitch.Value,
                aimSharpness);

            // The Animator writes the locomotion pose in Update. Layer a restrained
            // upper-body pitch afterwards so remote aim remains readable.
            m_AimTorso.localRotation *= Quaternion.AngleAxis(
                m_VisualAimPitch * 0.55f,
                Vector3.right);
        }

        void BindAuthoredCharacterVisual()
        {
            m_VisualInstance = CharacterRoot;
            m_Animator = CharacterAnimator;
            m_AimTorso = AimTorso;

            if (m_VisualInstance == null || m_Animator == null)
            {
                Debug.LogError(
                    "[Astronaut Visual] Player.prefab is missing its authored character references.",
                    this);
                m_Animator = null;
                return;
            }

            m_Animator.enabled = true;
            m_Animator.applyRootMotion = false;
            m_Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            m_Animator.Rebind();
            m_Animator.SetFloat(SpeedHash, 0f);
            m_Animator.SetBool(GroundedHash, true);
            m_Animator.Update(0f);

            foreach (Renderer renderer in m_VisualInstance.GetComponentsInChildren<Renderer>(true))
            {
                if (IsOwner)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }
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
        }

        void OnHealthChanged(float previous, float current)
        {
            SetDead(current <= 0f);
            if (current > 0f && current < previous && m_Animator != null)
            {
                m_Animator.SetTrigger(HitHash);
            }
        }

        void SetDead(bool dead)
        {
            if (m_Animator != null)
            {
                m_Animator.SetBool(DeadHash, dead);
            }
        }
    }
}
