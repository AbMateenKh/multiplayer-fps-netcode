using System.Collections;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.FPS.Gameplay
{
    public sealed class AstronautPlayerVisual : NetworkBehaviour
    {
        // Transform replication runs at the NetworkManager tick rate. Keep the visual
        // state cadence high enough that a remote locomotion blend never looks delayed.
        const float k_StateSendInterval = 0.05f;
        const float k_RemoteVisualSmoothTime = 0.065f;
        const float k_RemoteVisualRotationSharpness = 22f;
        const float k_RemoteVisualTeleportDistance = 3.5f;

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int DeadHash = Animator.StringToHash("Dead");
        static readonly int ShootHash = Animator.StringToHash("Shoot");
        static readonly int HitHash = Animator.StringToHash("Hit");

        [Header("Authored Player Prefab References")]
        public GameObject CharacterRoot;
        public Animator CharacterAnimator;
        public Transform AimTorso;
        [Tooltip("Package pistol animated with the astronaut's armed clips. This is a visual-only child.")]
        public Transform CharacterWeapon;

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
        Vector3 m_RemoteVisualVelocity;
        Vector3 m_RemoteVisualWorldPosition;
        Quaternion m_RemoteVisualWorldRotation;
        bool m_HasRemoteVisualState;

        public override void OnNetworkSpawn()
        {
            m_Character = GetComponent<PlayerCharacterController>();
            m_Weapons = GetComponent<PlayerWeaponsManager>();
            m_Health = GetComponent<Health>();

            DisablePrototypeRenderers();
            BindAuthoredCharacterVisual();
            ConfigureWeaponPresentation();

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
                m_Animator.SetFloat(SpeedHash, m_MoveSpeed.Value, 0.06f, Time.deltaTime);
                m_Animator.SetBool(GroundedHash, m_Grounded.Value);
            }
        }

        void LateUpdate()
        {
            if (m_Animator == null || m_AimTorso == null)
                return;

            UpdateRemoteVisualSmoothing();

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

        void UpdateRemoteVisualSmoothing()
        {
            if (IsOwner || m_VisualInstance == null)
                return;

            Transform visualTransform = m_VisualInstance.transform;
            Vector3 networkPosition = transform.position;
            Quaternion networkRotation = transform.rotation;

            if (!m_HasRemoteVisualState ||
                Vector3.Distance(m_RemoteVisualWorldPosition, networkPosition) >
                k_RemoteVisualTeleportDistance)
            {
                m_RemoteVisualWorldPosition = networkPosition;
                m_RemoteVisualWorldRotation = networkRotation;
                m_RemoteVisualVelocity = Vector3.zero;
                m_HasRemoteVisualState = true;
            }
            else
            {
                m_RemoteVisualWorldPosition = Vector3.SmoothDamp(
                    m_RemoteVisualWorldPosition,
                    networkPosition,
                    ref m_RemoteVisualVelocity,
                    k_RemoteVisualSmoothTime,
                    Mathf.Infinity,
                    Time.deltaTime);

                float rotationT = 1f - Mathf.Exp(
                    -k_RemoteVisualRotationSharpness * Time.deltaTime);
                m_RemoteVisualWorldRotation = Quaternion.Slerp(
                    m_RemoteVisualWorldRotation,
                    networkRotation,
                    rotationT);
            }

            // This offsets only the third-person presentation under the replicated
            // Player root. Collisions, hit validation, and the NetworkTransform remain
            // authoritative and untouched.
            visualTransform.SetPositionAndRotation(
                m_RemoteVisualWorldPosition,
                m_RemoteVisualWorldRotation);
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

        void ConfigureWeaponPresentation()
        {
            // PlayerWeaponsManager creates the original first-person weapon for every
            // player instance. It belongs only to its owner; leaving it active remotely
            // produces the detached gun seen beside third-person astronauts.
            if (!IsOwner)
            {
                StartCoroutine(HideRemoteViewModelAfterInitialization());
            }

            if (CharacterWeapon == null)
            {
                Debug.LogWarning(
                    "[Astronaut Visual] Player.prefab is missing the package pistol reference.",
                    this);
                return;
            }

            CharacterWeapon.gameObject.SetActive(true);
        }

        IEnumerator HideRemoteViewModelAfterInitialization()
        {
            // PlayerWeaponsManager.Start builds its gameplay weapon state in Start.
            // Keep the hierarchy active for that work, then hide only the remote
            // first-person renderers after Start has completed.
            yield return null;

            if (m_Weapons == null || m_Weapons.WeaponParentSocket == null)
                yield break;

            foreach (Renderer renderer in
                     m_Weapons.WeaponParentSocket.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
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
