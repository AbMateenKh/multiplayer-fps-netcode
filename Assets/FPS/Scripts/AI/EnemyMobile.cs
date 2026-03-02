using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.FPS.AI
{
    [RequireComponent(typeof(EnemyController))]
    public class EnemyMobile : NetworkBehaviour
    {
        public enum AIState
        {
            Patrol,
            Follow,
            Attack,
        }

        public Animator Animator;

        [Tooltip("Fraction of the enemy's attack range at which it will stop moving towards target while attacking")]
        [Range(0f, 1f)]
        public float AttackStopDistanceRatio = 0.5f;

        [Tooltip("The random hit damage effects")]
        public ParticleSystem[] RandomHitSparks;

        public ParticleSystem[] OnDetectVfx;
        public AudioClip OnDetectSfx;

        [Header("Sound")] public AudioClip MovementSound;
        public MinMaxFloat PitchDistortionMovementSpeed;

        public AIState AiState { get; private set; }
        EnemyController m_EnemyController;
        AudioSource m_AudioSource;

        const string k_AnimMoveSpeedParameter = "MoveSpeed";
        const string k_AnimAttackParameter = "Attack";
        const string k_AnimAlertedParameter = "Alerted";
        const string k_AnimOnDamagedParameter = "OnDamaged";
        NavMeshAgent m_NavMeshAgent;

        // NETWORK SPAWN — replaces Start()
        public override void OnNetworkSpawn()
        {
            m_EnemyController = GetComponent<EnemyController>();
            m_AudioSource = GetComponent<AudioSource>();
            m_NavMeshAgent = GetComponent<NavMeshAgent>();
            // ALL CLIENTS: play movement sound
            if (m_AudioSource && MovementSound)
            {
                m_AudioSource.clip = MovementSound;
                m_AudioSource.Play();
            }

            // SERVER ONLY: subscribe to AI events
            if (IsServer)
            {
                m_EnemyController.onAttack += OnAttack;
                m_EnemyController.onDetectedTarget += OnDetectedTarget;
                m_EnemyController.onLostTarget += OnLostTarget;
                m_EnemyController.onDamaged += OnDamaged;
                m_EnemyController.SetPathDestinationToClosestNode();
            }

            AiState = AIState.Patrol;
        }

        // UPDATE — Server runs AI, all clients update animation
        void Update()
        {
            // ALL CLIENTS: Update animation speed and audio pitch
            // NavMeshAgent syncs via NetworkTransform, so velocity is available
            if(m_NavMeshAgent != null && m_NavMeshAgent.enabled)
            {
                float moveSpeed = m_EnemyController.NavMeshAgent.velocity.magnitude;
                Animator.SetFloat(k_AnimMoveSpeedParameter, moveSpeed);

                if (m_AudioSource)
                {
                    m_AudioSource.pitch = Mathf.Lerp(
                        PitchDistortionMovementSpeed.Min,
                        PitchDistortionMovementSpeed.Max,
                        moveSpeed / m_EnemyController.NavMeshAgent.speed);
                }
            }

            // SERVER ONLY: AI logic
            if (!IsServer) return;

            UpdateAiStateTransitions();
            UpdateCurrentAiState();
        }

        void UpdateAiStateTransitions()
        {
            // Handle transitions 
            switch (AiState)
            {
                case AIState.Follow:
                    // Transition to attack when there is a line of sight to the target
                    if (m_EnemyController.IsSeeingTarget && m_EnemyController.IsTargetInAttackRange)
                    {
                        AiState = AIState.Attack;
                        m_EnemyController.SetNavDestination(transform.position);
                    }

                    break;
                case AIState.Attack:
                    // Transition to follow when no longer a target in attack range
                    if (!m_EnemyController.IsTargetInAttackRange)
                    {
                        AiState = AIState.Follow;
                    }

                    break;
            }
        }

        void UpdateCurrentAiState()
        {
            // Handle logic 
            switch (AiState)
            {
                case AIState.Patrol:
                    m_EnemyController.UpdatePathDestination();
                    m_EnemyController.SetNavDestination(m_EnemyController.GetDestinationOnPath());
                    break;
                case AIState.Follow:
                    m_EnemyController.SetNavDestination(m_EnemyController.KnownDetectedTarget.transform.position);
                    m_EnemyController.OrientTowards(m_EnemyController.KnownDetectedTarget.transform.position);
                    m_EnemyController.OrientWeaponsTowards(m_EnemyController.KnownDetectedTarget.transform.position);
                    break;
                case AIState.Attack:
                    if (Vector3.Distance(m_EnemyController.KnownDetectedTarget.transform.position,
                            m_EnemyController.DetectionModule.DetectionSourcePoint.position)
                        >= (AttackStopDistanceRatio * m_EnemyController.DetectionModule.AttackRange))
                    {
                        m_EnemyController.SetNavDestination(m_EnemyController.KnownDetectedTarget.transform.position);
                    }
                    else
                    {
                        m_EnemyController.SetNavDestination(transform.position);
                    }

                    m_EnemyController.OrientTowards(m_EnemyController.KnownDetectedTarget.transform.position);
                    m_EnemyController.TryAtack(m_EnemyController.KnownDetectedTarget.transform.position);
                    break;
            }
        }

        void OnAttack()
        {
            OnAttackClientRpc();
        }

        [ClientRpc]
        void OnAttackClientRpc()
        {
            Animator.SetTrigger(k_AnimAttackParameter);
        }

        void OnDetectedTarget()
        {
            if (AiState == AIState.Patrol)
            {
                AiState = AIState.Follow;
            }

            OnDetectedClientRpc();
        }

        [ClientRpc]
        void OnDetectedClientRpc()
        {
            for (int i = 0; i < OnDetectVfx.Length; i++)
            {
                OnDetectVfx[i].Play();
            }

            if (OnDetectSfx)
            {
                AudioUtility.CreateSFX(OnDetectSfx, transform.position,
                    AudioUtility.AudioGroups.EnemyDetection, 1f);
            }

            Animator.SetBool(k_AnimAlertedParameter, true);
        }

        void OnLostTarget()
        {
            if (AiState == AIState.Follow || AiState == AIState.Attack)
            {
                AiState = AIState.Patrol;
            }

            OnLostTargetClientRpc();
        }

        [ClientRpc]
        void OnLostTargetClientRpc()
        {
            for (int i = 0; i < OnDetectVfx.Length; i++)
            {
                OnDetectVfx[i].Stop();
            }

            Animator.SetBool(k_AnimAlertedParameter, false);
        }

        void OnDamaged()
        {
            OnDamagedClientRpc();
        }

        [ClientRpc]
        void OnDamagedClientRpc()
        {
            if (RandomHitSparks.Length > 0)
            {
                int n = Random.Range(0, RandomHitSparks.Length - 1);
                RandomHitSparks[n].Play();
            }

            Animator.SetTrigger(k_AnimOnDamagedParameter);
        }
    }
}