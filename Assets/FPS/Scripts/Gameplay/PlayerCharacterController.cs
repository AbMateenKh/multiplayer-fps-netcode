using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Gameplay
{
    [RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(AudioSource))]
    public class PlayerCharacterController : NetworkBehaviour, INetworkShooter, IPlayerController, IMatchRestartHandler
    {
        [Header("References")] [Tooltip("Reference to the main camera used for the player")]
        public Camera PlayerCamera;

        [Tooltip("Audio source for footsteps, jump, etc...")]
        public AudioSource AudioSource;

        [Header("General")] [Tooltip("Force applied downward when in the air")]
        public float GravityDownForce = 20f;

        [Tooltip("Physic layers checked to consider the player grounded")]
        public LayerMask GroundCheckLayers = -1;

        [Tooltip("distance from the bottom of the character controller capsule to test for grounded")]
        public float GroundCheckDistance = 0.05f;

        [Header("Movement")] [Tooltip("Max movement speed when grounded (when not sprinting)")]
        public float MaxSpeedOnGround = 10f;

        [Tooltip(
            "Sharpness for the movement when grounded, a low value will make the player accelerate and decelerate slowly, a high value will do the opposite")]
        public float MovementSharpnessOnGround = 15;

        [Tooltip("Max movement speed when crouching")] [Range(0, 1)]
        public float MaxSpeedCrouchedRatio = 0.5f;

        [Tooltip("Max movement speed when not grounded")]
        public float MaxSpeedInAir = 10f;

        [Tooltip("Acceleration speed when in the air")]
        public float AccelerationSpeedInAir = 25f;

        [Tooltip("Multiplicator for the sprint speed (based on grounded speed)")]
        public float SprintSpeedModifier = 2f;

        [Tooltip("Height at which the player dies instantly when falling off the map")]
        public float KillHeight = -50f;

        [Header("Rotation")] [Tooltip("Rotation speed for moving the camera")]
        public float RotationSpeed = 200f;

        [Range(0.1f, 1f)] [Tooltip("Rotation speed multiplier when aiming")]
        public float AimingRotationMultiplier = 0.4f;

        [Header("Jump")] [Tooltip("Force applied upward when jumping")]
        public float JumpForce = 9f;

        [Header("Stance")] [Tooltip("Ratio (0-1) of the character height where the camera will be at")]
        public float CameraHeightRatio = 0.9f;

        [Tooltip("Height of character when standing")]
        public float CapsuleHeightStanding = 1.8f;

        [Tooltip("Height of character when crouching")]
        public float CapsuleHeightCrouching = 0.9f;

        [Tooltip("Speed of crouching transitions")]
        public float CrouchingSharpness = 10f;

        [Header("Audio")] [Tooltip("Amount of footstep sounds played when moving one meter")]
        public float FootstepSfxFrequency = 1f;

        [Tooltip("Amount of footstep sounds played when moving one meter while sprinting")]
        public float FootstepSfxFrequencyWhileSprinting = 1f;

        [Tooltip("Sound played for footsteps")]
        public AudioClip FootstepSfx;

        [Tooltip("Sound played when jumping")] public AudioClip JumpSfx;
        [Tooltip("Sound played when landing")] public AudioClip LandSfx;

        [Tooltip("Sound played when taking damage froma fall")]
        public AudioClip FallDamageSfx;

        [Header("Fall Damage")]
        [Tooltip("Whether the player will recieve damage when hitting the ground at high speed")]
        public bool RecievesFallDamage;

        [Tooltip("Minimun fall speed for recieving fall damage")]
        public float MinSpeedForFallDamage = 10f;

        [Tooltip("Fall speed for recieving th emaximum amount of fall damage")]
        public float MaxSpeedForFallDamage = 30f;

        [Tooltip("Damage recieved when falling at the mimimum speed")]
        public float FallDamageAtMinSpeed = 10f;

        [Tooltip("Damage recieved when falling at the maximum speed")]
        public float FallDamageAtMaxSpeed = 50f;

        public UnityAction<bool> OnStanceChanged;

        public Vector3 CharacterVelocity { get; set; }
        public bool IsGrounded { get; private set; }
        public bool HasJumpedThisFrame { get; private set; }
        public bool IsDead => IsDeadOrUnableToAct();
        public bool IsCrouching { get; private set; }

        bool m_IsDead;
        public float RotationMultiplier
        {
            get
            {
                if (m_WeaponsManager.IsAiming)
                {
                    return AimingRotationMultiplier;
                }

                return 1f;
            }
        }

        Health m_Health;
        PlayerInputHandler m_InputHandler;
        CharacterController m_Controller;
        PlayerWeaponsManager m_WeaponsManager;
        Actor m_Actor;
        Vector3 m_GroundNormal;
        Vector3 m_CharacterVelocity;
        Vector3 m_LatestImpactSpeed;
        float m_LastTimeJumped = 0f;
        float m_CameraVerticalAngle = 0f;
        float m_FootstepDistanceCounter;
        float m_TargetCharacterHeight;

        const float k_JumpGroundingPreventionTime = 0.2f;
        const float k_GroundCheckDistanceInAir = 0.07f;
        const float k_MaxValidatedShotDistance = 1000f;
        const float k_MaxShotOriginDistanceFromPlayer = 4f;
        const float k_MaxShotDirectionErrorDegrees = 45f;
        const float k_RemoteTracerDuration = 0.08f;
        const float k_RemoteImpactDuration = 0.45f;
        const float k_MaxPickupDistanceFromPlayer = 4f;
        const float k_PickupSearchRadius = 2f;


        // Static event — any script can subscribe to this
        public static event System.Action<PlayerCharacterController> OnLocalPlayerSpawned;
        public static event System.Action OnLocalShotConfirmed;
        public static event System.Action OnLocalShotBlocked;
        static Material s_RemoteHitMaterial;
        static Material s_RemoteMissMaterial;
        static Material s_RemoteBlockedMaterial;


        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                StartCoroutine(InitializeLocalPlayer());
            }

            // Register this player as an actor on ALL clients
            // Must happen here because player spawns after scene enemies
            Actor actor = GetComponent<Actor>();
            ActorsManager actorsManager = FindFirstObjectByType<ActorsManager>();
            if (actorsManager != null && actor != null
                && !actorsManager.Actors.Contains(actor))
            {
                actor.Affiliation = 0; // Player team
                actorsManager.Actors.Add(actor);
            }

            // SERVER: Register player for kill tracking
            if (IsServer)
            {
                GameFlowManager gfm = FindFirstObjectByType<GameFlowManager>();
                if (gfm != null)
                {
                    gfm.RegisterPlayer(OwnerClientId);
                }
            }


        }


        System.Collections.IEnumerator InitializeLocalPlayer()
        {
            yield return null;

            OnLocalPlayerSpawned?.Invoke(this);
        }

        void Start()
        {
            // fetch components on the same gameObject
            m_Controller = GetComponent<CharacterController>();
            m_InputHandler = GetComponent<PlayerInputHandler>();
            m_WeaponsManager = GetComponent<PlayerWeaponsManager>();
            m_Health = GetComponent<Health>();
            m_Actor = GetComponent<Actor>();
            m_Controller.enableOverlapRecovery = true;
            m_Health.OnDie += OnDie;

            // force the crouch state to false when starting
            SetCrouchingState(false, true);
            UpdateCharacterHeight(true);


            if (!IsOwner && PlayerCamera != null)
            {
                // Disable ONLY the camera component, not the whole GameObject
                PlayerCamera.enabled = false;

                // Also disable the AudioListener if there is one
                AudioListener listener = PlayerCamera.GetComponent<AudioListener>();
                if (listener != null)
                    listener.enabled = false;
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            if (m_Health != null)
            {
                m_Health.OnDie -= OnDie;
            }
        }

        void Update()
        {

            if (!IsOwner) return;

            if (IsDeadOrUnableToAct())
            {
                CharacterVelocity = Vector3.zero;
                HasJumpedThisFrame = false;
                return;
            }

            if (!IsLocalGameplayActive())
            {
                CharacterVelocity = Vector3.zero;
                HasJumpedThisFrame = false;
                GroundCheck();
                UpdateCharacterHeight(false);
                return;
            }

            // ... rest of your existing Update code


            if (IsOwner)
            {
                if (!IsDead && transform.position.y < KillHeight)
                {
                    m_Health.Kill();
                }

                HasJumpedThisFrame = false;

                bool wasGrounded = IsGrounded;
                GroundCheck();

                if (IsGrounded && !wasGrounded)
                {
                    float fallSpeed = -Mathf.Min(CharacterVelocity.y, m_LatestImpactSpeed.y);
                    float fallSpeedRatio = (fallSpeed - MinSpeedForFallDamage) /
                                           (MaxSpeedForFallDamage - MinSpeedForFallDamage);
                    if (RecievesFallDamage && fallSpeedRatio > 0f)
                    {
                        float dmgFromFall = Mathf.Lerp(FallDamageAtMinSpeed,
                                                        FallDamageAtMaxSpeed, fallSpeedRatio);
                        m_Health.TakeDamage(dmgFromFall, null);
                        AudioSource.PlayOneShot(FallDamageSfx);
                    }
                    else
                    {
                        AudioSource.PlayOneShot(LandSfx);
                    }
                }

                if (m_InputHandler.GetCrouchInputDown())
                {
                    SetCrouchingState(!IsCrouching, false);
                }

                UpdateCharacterHeight(false);
                HandleCharacterMovement();
            }
        }

        //
        // CAMERA — Owner only, purely local
        // 
        void HandleCameraRotation()
        {
            m_CameraVerticalAngle += m_InputHandler.GetLookInputsVertical()
                                     * RotationSpeed * RotationMultiplier;
            m_CameraVerticalAngle = Mathf.Clamp(m_CameraVerticalAngle, -89f, 89f);
            PlayerCamera.transform.localEulerAngles = new Vector3(m_CameraVerticalAngle, 0, 0);
        }



        void OnDie()
        {
            m_IsDead = true;
            CharacterVelocity = Vector3.zero;
            HasJumpedThisFrame = false;

            if (!IsOwner) return;

            // Disable weapon visuals
            PlayerWeaponsManager weaponsManager = GetComponent<PlayerWeaponsManager>();
            if (weaponsManager != null)
            {
                weaponsManager.enabled = false;
            }
        }

        public void OnRespawn()
        {
            m_IsDead = false;

            if (!IsOwner) return;

            // Re-enable weapons
            PlayerWeaponsManager weaponsManager = GetComponent<PlayerWeaponsManager>();
            if (weaponsManager != null)
            {
                weaponsManager.enabled = true;
            }
        }

        public void ResetForMatchRestart()
        {
            if (!IsServer)
                return;

            ResetRoundLocalState();
            ResetForMatchRestartClientRpc();
        }

        [ClientRpc]
        void ResetForMatchRestartClientRpc()
        {
            if (IsServer)
                return;

            ResetRoundLocalState();
        }

        void ResetRoundLocalState()
        {
            m_IsDead = false;

            if (m_WeaponsManager != null)
            {
                m_WeaponsManager.enabled = true;
                m_WeaponsManager.ResetLoadout();
            }

            Jetpack jetpack = GetComponent<Jetpack>();
            if (jetpack != null)
            {
                jetpack.ResetJetpack();
            }
        }

   

        void GroundCheck()
        {
            // Make sure that the ground check distance while already in air is very small, to prevent suddenly snapping to ground
            float chosenGroundCheckDistance =
                IsGrounded ? (m_Controller.skinWidth + GroundCheckDistance) : k_GroundCheckDistanceInAir;

            // reset values before the ground check
            IsGrounded = false;
            m_GroundNormal = Vector3.up;

            // only try to detect ground if it's been a short amount of time since last jump; otherwise we may snap to the ground instantly after we try jumping
            if (Time.time >= m_LastTimeJumped + k_JumpGroundingPreventionTime)
            {
                // if we're grounded, collect info about the ground normal with a downward capsule cast representing our character capsule
                if (Physics.CapsuleCast(GetCapsuleBottomHemisphere(), GetCapsuleTopHemisphere(m_Controller.height),
                    m_Controller.radius, Vector3.down, out RaycastHit hit, chosenGroundCheckDistance, GroundCheckLayers,
                    QueryTriggerInteraction.Ignore))
                {
                    // storing the upward direction for the surface found
                    m_GroundNormal = hit.normal;

                    // Only consider this a valid ground hit if the ground normal goes in the same direction as the character up
                    // and if the slope angle is lower than the character controller's limit
                    if (Vector3.Dot(hit.normal, transform.up) > 0f &&
                        IsNormalUnderSlopeLimit(m_GroundNormal))
                    {
                        IsGrounded = true;

                        // handle snapping to the ground
                        if (hit.distance > m_Controller.skinWidth)
                        {
                            m_Controller.Move(Vector3.down * hit.distance);
                        }
                    }
                }
            }
        }



        void HandleCharacterMovement()
        {
            // Horizontal rotation — owner rotates, NetworkTransform syncs to all
            transform.Rotate(
                new Vector3(0f, m_InputHandler.GetLookInputsHorizontal()
                            * RotationSpeed * RotationMultiplier, 0f), Space.Self);

            // Vertical camera — stays purely local
            m_CameraVerticalAngle += m_InputHandler.GetLookInputsVertical()
                                     * RotationSpeed * RotationMultiplier;
            m_CameraVerticalAngle = Mathf.Clamp(m_CameraVerticalAngle, -89f, 89f);
            PlayerCamera.transform.localEulerAngles = new Vector3(m_CameraVerticalAngle, 0, 0);

            // Movement — owner moves, NetworkTransform syncs to all
            bool isSprinting = m_InputHandler.GetSprintInputHeld();
            if (isSprinting)
            {
                isSprinting = SetCrouchingState(false, false);
            }

            float speedModifier = isSprinting ? SprintSpeedModifier : 1f;
            Vector3 worldspaceMoveInput = transform.TransformVector(m_InputHandler.GetMoveInput());

            if (IsGrounded)
            {
                Vector3 targetVelocity = worldspaceMoveInput * MaxSpeedOnGround * speedModifier;
                if (IsCrouching)
                    targetVelocity *= MaxSpeedCrouchedRatio;
                targetVelocity = GetDirectionReorientedOnSlope(
                    targetVelocity.normalized, m_GroundNormal) * targetVelocity.magnitude;

                CharacterVelocity = Vector3.Lerp(CharacterVelocity, targetVelocity,
                    MovementSharpnessOnGround * Time.deltaTime);

                if (IsGrounded && m_InputHandler.GetJumpInputDown())
                {
                    if (SetCrouchingState(false, false))
                    {
                        CharacterVelocity = new Vector3(CharacterVelocity.x, 0f, CharacterVelocity.z);
                        CharacterVelocity += Vector3.up * JumpForce;

                        AudioSource.PlayOneShot(JumpSfx);

                        m_LastTimeJumped = Time.time;
                        HasJumpedThisFrame = true;
                        IsGrounded = false;
                        m_GroundNormal = Vector3.up;
                    }
                }

                float chosenFootstepSfxFrequency =
                    (isSprinting ? FootstepSfxFrequencyWhileSprinting : FootstepSfxFrequency);
                if (m_FootstepDistanceCounter >= 1f / chosenFootstepSfxFrequency)
                {
                    m_FootstepDistanceCounter = 0f;
                    AudioSource.PlayOneShot(FootstepSfx);
                }
                m_FootstepDistanceCounter += CharacterVelocity.magnitude * Time.deltaTime;
            }
            else
            {
                CharacterVelocity += worldspaceMoveInput * AccelerationSpeedInAir * Time.deltaTime;

                float verticalVelocity = CharacterVelocity.y;
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(CharacterVelocity, Vector3.up);
                horizontalVelocity = Vector3.ClampMagnitude(
                    horizontalVelocity, MaxSpeedInAir * speedModifier);
                CharacterVelocity = horizontalVelocity + (Vector3.up * verticalVelocity);

                CharacterVelocity += Vector3.down * GravityDownForce * Time.deltaTime;
            }

            Vector3 capsuleBottomBeforeMove = GetCapsuleBottomHemisphere();
            Vector3 capsuleTopBeforeMove = GetCapsuleTopHemisphere(m_Controller.height);
            m_Controller.Move(CharacterVelocity * Time.deltaTime);

            m_LatestImpactSpeed = Vector3.zero;
            if (Physics.CapsuleCast(capsuleBottomBeforeMove, capsuleTopBeforeMove,
                m_Controller.radius, CharacterVelocity.normalized, out RaycastHit hit,
                CharacterVelocity.magnitude * Time.deltaTime, -1,
                QueryTriggerInteraction.Ignore))
            {
                m_LatestImpactSpeed = CharacterVelocity;
                CharacterVelocity = Vector3.ProjectOnPlane(CharacterVelocity, hit.normal);
            }
        }

        // Returns true if the slope angle represented by the given normal is under the slope angle limit of the character controller
        bool IsNormalUnderSlopeLimit(Vector3 normal)
        {
            return Vector3.Angle(transform.up, normal) <= m_Controller.slopeLimit;
        }

        // Gets the center point of the bottom hemisphere of the character controller capsule    
        Vector3 GetCapsuleBottomHemisphere()
        {
            return transform.position + (transform.up * m_Controller.radius);
        }

        // Gets the center point of the top hemisphere of the character controller capsule    
        Vector3 GetCapsuleTopHemisphere(float atHeight)
        {
            return transform.position + (transform.up * (atHeight - m_Controller.radius));
        }

        // Gets a reoriented direction that is tangent to a given slope
        public Vector3 GetDirectionReorientedOnSlope(Vector3 direction, Vector3 slopeNormal)
        {
            Vector3 directionRight = Vector3.Cross(direction, transform.up);
            return Vector3.Cross(slopeNormal, directionRight).normalized;
        }

        void UpdateCharacterHeight(bool force)
        {
            // Update height instantly
            if (force)
            {
                m_Controller.height = m_TargetCharacterHeight;
                m_Controller.center = Vector3.up * m_Controller.height * 0.5f;
                PlayerCamera.transform.localPosition = Vector3.up * m_TargetCharacterHeight * CameraHeightRatio;
                m_Actor.AimPoint.transform.localPosition = m_Controller.center;
            }
            // Update smooth height
            else if (m_Controller.height != m_TargetCharacterHeight)
            {
                // resize the capsule and adjust camera position
                m_Controller.height = Mathf.Lerp(m_Controller.height, m_TargetCharacterHeight,
                    CrouchingSharpness * Time.deltaTime);
                m_Controller.center = Vector3.up * m_Controller.height * 0.5f;
                PlayerCamera.transform.localPosition = Vector3.Lerp(PlayerCamera.transform.localPosition,
                    Vector3.up * m_TargetCharacterHeight * CameraHeightRatio, CrouchingSharpness * Time.deltaTime);
                m_Actor.AimPoint.transform.localPosition = m_Controller.center;
            }
        }

        // returns false if there was an obstruction
        bool SetCrouchingState(bool crouched, bool ignoreObstructions)
        {
            // set appropriate heights
            if (crouched)
            {
                m_TargetCharacterHeight = CapsuleHeightCrouching;
            }
            else
            {
                // Detect obstructions
                if (!ignoreObstructions)
                {
                    Collider[] standingOverlaps = Physics.OverlapCapsule(
                        GetCapsuleBottomHemisphere(),
                        GetCapsuleTopHemisphere(CapsuleHeightStanding),
                        m_Controller.radius,
                        -1,
                        QueryTriggerInteraction.Ignore);
                    foreach (Collider c in standingOverlaps)
                    {
                        if (c != m_Controller)
                        {
                            return false;
                        }
                    }
                }

                m_TargetCharacterHeight = CapsuleHeightStanding;
            }

            if (OnStanceChanged != null)
            {
                OnStanceChanged.Invoke(crouched);
            }

            IsCrouching = crouched;
            return true;
        }

        public void RequestShoot(Vector3 origin, Vector3 direction, int shotIndex)
        {
            if (IsDeadOrUnableToAct() || !IsLocalGameplayActive())
                return;

            RequestShootServerRpc(origin, direction.normalized, shotIndex);
        }

        public void RequestChargeStart()
        {
            if (IsDeadOrUnableToAct() || !IsLocalGameplayActive())
                return;

            if (IsServer && IsOwner)
                return;

            RequestChargeStartServerRpc();
        }

        public void RequestPickup(Pickup pickup)
        {
            if (pickup == null || IsDeadOrUnableToAct() || !IsLocalGameplayActive())
                return;

            if (IsServer)
            {
                TryApplyPickupOnServer(pickup);
            }
            else if (IsOwner)
            {
                RequestPickupServerRpc(pickup.transform.position);
            }
        }

        [ServerRpc]
        void RequestPickupServerRpc(Vector3 pickupPosition)
        {
            if (IsDeadOrUnableToAct() || !IsServerGameplayActive())
                return;

            if (Vector3.Distance(transform.position, pickupPosition) >
                k_MaxPickupDistanceFromPlayer + k_PickupSearchRadius)
            {
                return;
            }

            Pickup pickup = Pickup.FindClosestAvailable(pickupPosition, k_PickupSearchRadius);
            TryApplyPickupOnServer(pickup);
        }

        void TryApplyPickupOnServer(Pickup pickup)
        {
            if (!IsServer || pickup == null)
                return;

            if (IsDeadOrUnableToAct() || !IsServerGameplayActive())
                return;

            if (Vector3.Distance(transform.position, pickup.transform.position) > k_MaxPickupDistanceFromPlayer)
                return;

            pickup.TryApplyServerPickup(this);
        }

        public void ResolvePickupOnClients(Vector3 pickupPosition)
        {
            if (!IsServer)
                return;

            ResolvePickupClientRpc(pickupPosition);
        }

        [ClientRpc]
        void ResolvePickupClientRpc(Vector3 pickupPosition)
        {
            Pickup pickup = Pickup.FindClosestAvailable(pickupPosition, k_PickupSearchRadius);
            if (pickup == null)
                return;

            if (IsOwner && !IsServer)
            {
                pickup.TryApplyClientConfirmedPickup(this);
            }
            else
            {
                pickup.ConsumeLocally(true, false);
            }
        }

        [ServerRpc]
        void RequestChargeStartServerRpc()
        {
            if (!IsServer || IsDeadOrUnableToAct() || !IsServerGameplayActive())
                return;

            m_WeaponsManager?.TryAuthorizeServerChargeStart();
        }

        [ServerRpc]
        void RequestShootServerRpc(Vector3 origin, Vector3 direction, int shotIndex)
        {
            if (!IsServer || IsDeadOrUnableToAct() || !IsServerGameplayActive())
            {
                return;
            }

            bool consumeShotAmmo = !(IsServer && IsOwner);
            if (m_WeaponsManager == null ||
                !m_WeaponsManager.TryAuthorizeServerShot(shotIndex, consumeShotAmmo, out float validatedDamage))
            {
                return;
            }

            if (Vector3.Distance(origin, transform.position) > k_MaxShotOriginDistanceFromPlayer)
            {
                return;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 flatDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (flatDirection.sqrMagnitude > 0.001f &&
                Vector3.Angle(flatForward, flatDirection) > k_MaxShotDirectionErrorDegrees)
            {
                return;
            }

            Vector3 shotEnd = origin + direction * k_MaxValidatedShotDistance;
            Vector3 shotNormal = -direction;
            bool didHit = false;
            bool hitDamageable = false;
            bool hitBlockedDamageable = false;

            if (Physics.Raycast(origin, direction, out RaycastHit hit, k_MaxValidatedShotDistance, -1,
                QueryTriggerInteraction.Ignore))
            {
                shotEnd = hit.point;
                shotNormal = hit.normal;
                didHit = true;

                Damageable damageable = hit.collider.GetComponentInParent<Damageable>();
                if (damageable != null && damageable.Health != null && damageable.Health != m_Health)
                {
                    if (damageable.Health.CanTakeDamage)
                    {
                        damageable.InflictDamage(validatedDamage, false, gameObject);
                        hitDamageable = true;
                        ConfirmShotClientRpc(new ClientRpcParams
                        {
                            Send = new ClientRpcSendParams
                            {
                                TargetClientIds = new ulong[] { OwnerClientId }
                            }
                        });
                    }
                    else
                    {
                        hitBlockedDamageable = true;
                        BlockShotClientRpc(new ClientRpcParams
                        {
                            Send = new ClientRpcSendParams
                            {
                                TargetClientIds = new ulong[] { OwnerClientId }
                            }
                        });
                    }
                }
            }

            ShootVisualClientRpc(origin, shotEnd, shotNormal, didHit, hitDamageable, hitBlockedDamageable);
        }

        bool IsServerGameplayActive()
        {
            GameFlowManager gameFlowManager = FindFirstObjectByType<GameFlowManager>();
            return gameFlowManager == null ||
                   gameFlowManager.IsGameplayActive;
        }

        bool IsDeadOrUnableToAct()
        {
            return m_IsDead || (m_Health != null && m_Health.CurrentHealth.Value <= 0f);
        }

        bool IsLocalGameplayActive()
        {
            GameFlowManager gameFlowManager = FindFirstObjectByType<GameFlowManager>();
            return gameFlowManager == null ||
                   gameFlowManager.IsGameplayActive;
        }

        [ClientRpc]
        void ConfirmShotClientRpc(ClientRpcParams clientRpcParams = default)
        {
            if (IsOwner)
            {
                OnLocalShotConfirmed?.Invoke();
            }
        }

        [ClientRpc]
        void BlockShotClientRpc(ClientRpcParams clientRpcParams = default)
        {
            if (IsOwner)
            {
                OnLocalShotBlocked?.Invoke();
            }
        }

        [ClientRpc]
        void ShootVisualClientRpc(Vector3 origin, Vector3 endPoint, Vector3 hitNormal, bool didHit,
            bool hitDamageable, bool hitBlockedDamageable)
        {
            if (IsOwner) return;

            CreateRemoteTracer(origin, endPoint, hitDamageable, hitBlockedDamageable);

            if (didHit)
            {
                CreateRemoteImpact(endPoint, hitNormal, hitDamageable, hitBlockedDamageable);
            }
        }

        void CreateRemoteTracer(Vector3 origin, Vector3 endPoint, bool hitDamageable, bool hitBlockedDamageable)
        {
            GameObject tracer = new GameObject("RemoteShotTracer");
            LineRenderer line = tracer.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, origin);
            line.SetPosition(1, endPoint);
            line.useWorldSpace = true;
            line.widthMultiplier = hitDamageable ? 0.035f : hitBlockedDamageable ? 0.03f : 0.02f;
            line.material = GetRemoteShotMaterial(hitDamageable, hitBlockedDamageable);
            line.numCapVertices = 2;
            Destroy(tracer, k_RemoteTracerDuration);
        }

        void CreateRemoteImpact(Vector3 point, Vector3 normal, bool hitDamageable, bool hitBlockedDamageable)
        {
            GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impact.name = hitDamageable ? "RemoteHitImpact" : hitBlockedDamageable ? "RemoteBlockedImpact" : "RemoteImpact";
            impact.transform.position = point + normal.normalized * 0.025f;
            impact.transform.localScale = Vector3.one * (hitDamageable ? 0.16f : hitBlockedDamageable ? 0.13f : 0.09f);

            Collider impactCollider = impact.GetComponent<Collider>();
            if (impactCollider != null)
            {
                Destroy(impactCollider);
            }

            Renderer renderer = impact.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = GetRemoteShotMaterial(hitDamageable, hitBlockedDamageable);
            }

            Destroy(impact, k_RemoteImpactDuration);
        }

        static Material GetRemoteShotMaterial(bool hitDamageable, bool hitBlockedDamageable)
        {
            if (hitDamageable)
            {
                if (s_RemoteHitMaterial == null)
                {
                    s_RemoteHitMaterial = new Material(Shader.Find("Sprites/Default"));
                    s_RemoteHitMaterial.color = new Color(1f, 0.25f, 0.18f, 0.95f);
                }

                return s_RemoteHitMaterial;
            }

            if (hitBlockedDamageable)
            {
                if (s_RemoteBlockedMaterial == null)
                {
                    s_RemoteBlockedMaterial = new Material(Shader.Find("Sprites/Default"));
                    s_RemoteBlockedMaterial.color = new Color(0.2f, 0.55f, 1f, 0.9f);
                }

                return s_RemoteBlockedMaterial;
            }

            if (s_RemoteMissMaterial == null)
            {
                s_RemoteMissMaterial = new Material(Shader.Find("Sprites/Default"));
                s_RemoteMissMaterial.color = new Color(0.35f, 0.8f, 1f, 0.75f);
            }

            return s_RemoteMissMaterial;
        }
    }
}
