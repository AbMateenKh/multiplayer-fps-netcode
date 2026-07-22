using System.Collections;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Drives the authored third-person player visual from compact replicated state.
    /// The character hierarchy and Animator are stored in Player.prefab; this component
    /// never creates presentation objects at runtime.
    /// </summary>
    public sealed class NetworkPlayerPresentation : NetworkBehaviour
    {
        const float StateSendInterval = 0.05f;
        const float RemoteVisualSmoothTime = 0.065f;
        const float RemoteVisualRotationSharpness = 22f;
        const float RemoteVisualTeleportDistance = 3.5f;

        static readonly int MoveXHash = Animator.StringToHash("MoveX");
        static readonly int MoveYHash = Animator.StringToHash("MoveY");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int DeadHash = Animator.StringToHash("Dead");
        static readonly int ShootHash = Animator.StringToHash("Shoot");
        static readonly int ReloadHash = Animator.StringToHash("Reload");

        [Header("Authored Player Prefab References")]
        public GameObject CharacterRoot;
        public Animator CharacterAnimator;
        public Transform AimTorso;
        [Tooltip("Third-person rifle authored into the character prefab.")]
        public Transform CharacterWeapon;

        readonly NetworkVariable<Vector2> m_LocalMove = new(
            Vector2.zero,
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

        readonly NetworkVariable<ushort> m_ReloadSequence = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        PlayerCharacterController m_Character;
        PlayerWeaponsManager m_Weapons;
        WeaponController m_ObservedWeapon;
        Health m_Health;
        float m_NextStateSendTime;
        float m_VisualAimPitch;
        float m_HitFlinch;
        bool m_WasReloading;

        Vector3 m_AuthoredLocalPosition;
        Quaternion m_AuthoredLocalRotation;
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
            BindAuthoredPresentation();

            m_ShotSequence.OnValueChanged += OnShotSequenceChanged;
            m_ReloadSequence.OnValueChanged += OnReloadSequenceChanged;

            if (m_Health != null)
            {
                m_Health.CurrentHealth.OnValueChanged += OnHealthChanged;
                SetDead(m_Health.CurrentHealth.Value <= 0f);
            }

            if (IsOwner && m_Weapons != null)
            {
                m_Weapons.OnSwitchedToWeapon += OnWeaponSwitched;
                StartCoroutine(BindInitialWeapon());
            }

            if (!IsOwner)
            {
                StartCoroutine(HideRemoteFirstPersonWeapons());
            }
        }

        public override void OnNetworkDespawn()
        {
            m_ShotSequence.OnValueChanged -= OnShotSequenceChanged;
            m_ReloadSequence.OnValueChanged -= OnReloadSequenceChanged;

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
                PublishOwnerState();
                m_NextStateSendTime = Time.unscaledTime + StateSendInterval;
            }

            if (CharacterAnimator != null)
            {
                CharacterAnimator.SetFloat(MoveXHash, m_LocalMove.Value.x, 0.08f, Time.deltaTime);
                CharacterAnimator.SetFloat(MoveYHash, m_LocalMove.Value.y, 0.08f, Time.deltaTime);
                CharacterAnimator.SetBool(GroundedHash, m_Grounded.Value);
            }

            UpdateReloadState();
            m_HitFlinch = Mathf.MoveTowards(m_HitFlinch, 0f, Time.deltaTime * 5f);
        }

        void LateUpdate()
        {
            if (CharacterAnimator == null || AimTorso == null)
                return;

            UpdateRemoteVisualSmoothing();

            float aimT = 1f - Mathf.Exp(-14f * Time.deltaTime);
            m_VisualAimPitch = Mathf.LerpAngle(m_VisualAimPitch, m_AimPitch.Value, aimT);

            // Applied after the Animator so aim and damage response layer over every clip.
            AimTorso.localRotation *= Quaternion.Euler(
                m_VisualAimPitch * 0.55f,
                0f,
                m_HitFlinch * 8f);
        }

        void PublishOwnerState()
        {
            if (m_Character == null)
                return;

            Vector3 localVelocity = transform.InverseTransformDirection(m_Character.CharacterVelocity);
            float topSpeed = Mathf.Max(
                0.01f,
                m_Character.MaxSpeedOnGround * m_Character.SprintSpeedModifier);
            m_LocalMove.Value = Vector2.ClampMagnitude(
                new Vector2(localVelocity.x, localVelocity.z) / topSpeed,
                1f);
            m_Grounded.Value = m_Character.IsGrounded;

            if (m_Character.PlayerCamera != null)
            {
                m_AimPitch.Value = Mathf.Clamp(
                    Mathf.DeltaAngle(0f, m_Character.PlayerCamera.transform.localEulerAngles.x),
                    -75f,
                    75f);
            }
        }

        void BindAuthoredPresentation()
        {
            if (CharacterRoot == null || CharacterAnimator == null)
            {
                Debug.LogError(
                    "[Player Presentation] Player.prefab is missing authored Polyart references.",
                    this);
                return;
            }

            Transform visualTransform = CharacterRoot.transform;
            m_AuthoredLocalPosition = visualTransform.localPosition;
            m_AuthoredLocalRotation = visualTransform.localRotation;

            CharacterAnimator.enabled = true;
            CharacterAnimator.applyRootMotion = false;
            CharacterAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            CharacterAnimator.Rebind();
            CharacterAnimator.SetFloat(MoveXHash, 0f);
            CharacterAnimator.SetFloat(MoveYHash, 0f);
            CharacterAnimator.SetBool(GroundedHash, true);
            CharacterAnimator.Update(0f);

            if (CharacterWeapon != null)
            {
                CharacterWeapon.gameObject.SetActive(true);
            }

            if (IsOwner)
            {
                foreach (Renderer renderer in CharacterRoot.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }
        }

        void UpdateRemoteVisualSmoothing()
        {
            if (IsOwner || CharacterRoot == null)
                return;

            Vector3 targetPosition = transform.TransformPoint(m_AuthoredLocalPosition);
            Quaternion targetRotation = transform.rotation * m_AuthoredLocalRotation;

            if (!m_HasRemoteVisualState ||
                Vector3.Distance(m_RemoteVisualWorldPosition, targetPosition) >
                RemoteVisualTeleportDistance)
            {
                m_RemoteVisualWorldPosition = targetPosition;
                m_RemoteVisualWorldRotation = targetRotation;
                m_RemoteVisualVelocity = Vector3.zero;
                m_HasRemoteVisualState = true;
            }
            else
            {
                m_RemoteVisualWorldPosition = Vector3.SmoothDamp(
                    m_RemoteVisualWorldPosition,
                    targetPosition,
                    ref m_RemoteVisualVelocity,
                    RemoteVisualSmoothTime,
                    Mathf.Infinity,
                    Time.deltaTime);

                float rotationT = 1f - Mathf.Exp(-RemoteVisualRotationSharpness * Time.deltaTime);
                m_RemoteVisualWorldRotation = Quaternion.Slerp(
                    m_RemoteVisualWorldRotation,
                    targetRotation,
                    rotationT);
            }

            CharacterRoot.transform.SetPositionAndRotation(
                m_RemoteVisualWorldPosition,
                m_RemoteVisualWorldRotation);
        }

        IEnumerator HideRemoteFirstPersonWeapons()
        {
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
            m_WasReloading = weapon != null && weapon.IsReloading;

            if (m_ObservedWeapon != null)
            {
                m_ObservedWeapon.OnShootProcessed += OnLocalShot;
            }
        }

        void UpdateReloadState()
        {
            if (!IsOwner || m_ObservedWeapon == null)
                return;

            bool isReloading = m_ObservedWeapon.IsReloading;
            if (isReloading && !m_WasReloading)
            {
                m_ReloadSequence.Value++;
                TriggerReload();
            }

            m_WasReloading = isReloading;
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

        void OnReloadSequenceChanged(ushort previous, ushort current)
        {
            if (!IsOwner && previous != current)
            {
                TriggerReload();
            }
        }

        void TriggerShoot()
        {
            if (CharacterAnimator != null)
            {
                CharacterAnimator.SetTrigger(ShootHash);
            }
        }

        void TriggerReload()
        {
            if (CharacterAnimator != null)
            {
                CharacterAnimator.SetTrigger(ReloadHash);
            }
        }

        void OnHealthChanged(float previous, float current)
        {
            SetDead(current <= 0f);
            if (current > 0f && current < previous)
            {
                m_HitFlinch = 1f;
            }
        }

        void SetDead(bool dead)
        {
            if (CharacterAnimator != null)
            {
                CharacterAnimator.SetBool(DeadHash, dead);
            }
        }
    }
}
