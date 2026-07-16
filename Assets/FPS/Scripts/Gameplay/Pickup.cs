using Unity.FPS.Game;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class Pickup : MonoBehaviour, IMatchPickup
    {
        public static event System.Action<Pickup, PlayerCharacterController> OnLocalPickupConfirmed;

        [Tooltip("Frequency at which the item will move up and down")]
        public float VerticalBobFrequency = 1f;

        [Tooltip("Distance the item will move up and down")]
        public float BobbingAmount = 1f;

        [Tooltip("Rotation angle per second")] public float RotatingSpeed = 360f;

        [Tooltip("Sound played on pickup")] public AudioClip PickupSfx;
        [Tooltip("VFX spawned on pickup")] public GameObject PickupVfxPrefab;

        public Rigidbody PickupRigidbody { get; private set; }
        public bool IsConsumed { get; private set; }
        public bool IsConsumedForMatch => IsConsumed;
        public Vector3 MatchPosition => transform.position;

        static readonly List<Pickup> s_AllPickups = new List<Pickup>();

        Collider m_Collider;
        Vector3 m_StartPosition;
        Quaternion m_StartRotation;
        bool m_HasPlayedFeedback;

        void Awake()
        {
            if (!s_AllPickups.Contains(this))
            {
                s_AllPickups.Add(this);
            }
        }

        void OnDestroy()
        {
            s_AllPickups.Remove(this);
        }

        protected virtual void Start()
        {
            PickupRigidbody = GetComponent<Rigidbody>();
            DebugUtility.HandleErrorIfNullGetComponent<Rigidbody, Pickup>(PickupRigidbody, this, gameObject);
            m_Collider = GetComponent<Collider>();
            DebugUtility.HandleErrorIfNullGetComponent<Collider, Pickup>(m_Collider, this, gameObject);

            PickupRigidbody.isKinematic = true;
            m_Collider.isTrigger = true;

            m_StartPosition = transform.position;
            m_StartRotation = transform.rotation;
        }

        void Update()
        {
            float bobbingAnimationPhase = ((Mathf.Sin(Time.time * VerticalBobFrequency) * 0.5f) + 0.5f) * BobbingAmount;
            transform.position = m_StartPosition + Vector3.up * bobbingAnimationPhase;
            transform.Rotate(Vector3.up, RotatingSpeed * Time.deltaTime, Space.Self);
        }

        void OnTriggerEnter(Collider other)
        {
            if (IsConsumed)
                return;

            PlayerCharacterController pickingPlayer = other.GetComponentInParent<PlayerCharacterController>();
            if (pickingPlayer == null)
                return;

            if (IsNetworkGameActive())
            {
                pickingPlayer.RequestPickup(this);
            }
            else if (ApplyPickupEffect(pickingPlayer, true))
            {
                ConsumeLocally(true, true);
            }
        }

        public bool TryApplyServerPickup(PlayerCharacterController playerController)
        {
            if (IsConsumed)
                return false;

            if (IsNetworkGameActive() && !NetworkManager.Singleton.IsServer)
                return false;

            if (!ApplyPickupEffect(playerController, true))
                return false;

            IsConsumed = true;
            BroadcastPickupEvent();
            PlayPickupFeedback();
            NotifyLocalPickupConfirmed(playerController);

            if (IsNetworkGameActive())
            {
                playerController.ResolvePickupOnClients(transform.position);
            }

            gameObject.SetActive(false);
            return true;
        }

        public bool TryApplyClientConfirmedPickup(PlayerCharacterController playerController)
        {
            if (IsConsumed)
                return false;

            bool applied = ApplyPickupEffect(playerController, false);
            ConsumeLocally(applied, false);
            if (applied)
            {
                NotifyLocalPickupConfirmed(playerController);
            }
            return applied;
        }

        public void ConsumeLocally(bool playFeedback, bool broadcastPickupEvent)
        {
            if (IsConsumed)
                return;

            IsConsumed = true;

            if (broadcastPickupEvent)
            {
                BroadcastPickupEvent();
            }

            if (playFeedback)
            {
                PlayPickupFeedback();
            }

            gameObject.SetActive(false);
        }

        public void ResetPickup()
        {
            IsConsumed = false;
            m_HasPlayedFeedback = false;
            transform.position = m_StartPosition;
            transform.rotation = m_StartRotation;
            gameObject.SetActive(true);
        }

        public void ResetPickupForMatch()
        {
            ResetPickup();
        }

        public void ConsumeForMatchSync()
        {
            ConsumeLocally(false, false);
        }

        protected virtual bool ApplyPickupEffect(PlayerCharacterController playerController, bool serverAuthoritative)
        {
            return true;
        }

        public void PlayPickupFeedback()
        {
            if (m_HasPlayedFeedback)
                return;

            if (PickupSfx)
            {
                AudioUtility.CreateSFX(PickupSfx, transform.position, AudioUtility.AudioGroups.Pickup, 0f);
            }

            if (PickupVfxPrefab)
            {
                Instantiate(PickupVfxPrefab, transform.position, Quaternion.identity);
            }

            m_HasPlayedFeedback = true;
        }

        void BroadcastPickupEvent()
        {
            PickupEvent evt = Events.PickupEvent;
            evt.Pickup = gameObject;
            EventManager.Broadcast(evt);
        }

        void NotifyLocalPickupConfirmed(PlayerCharacterController playerController)
        {
            if (playerController != null && playerController.IsOwner)
            {
                OnLocalPickupConfirmed?.Invoke(this, playerController);
            }
        }

        static bool IsNetworkGameActive()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        }

        public static Pickup FindClosestAvailable(Vector3 position, float radius)
        {
            Collider[] colliders = Physics.OverlapSphere(position, radius, -1, QueryTriggerInteraction.Collide);
            Pickup closestPickup = null;
            float closestSqrDistance = float.PositiveInfinity;

            foreach (Collider candidateCollider in colliders)
            {
                Pickup pickup = candidateCollider.GetComponentInParent<Pickup>();
                if (pickup == null || pickup.IsConsumed)
                    continue;

                float sqrDistance = (pickup.transform.position - position).sqrMagnitude;
                if (sqrDistance < closestSqrDistance)
                {
                    closestPickup = pickup;
                    closestSqrDistance = sqrDistance;
                }
            }

            return closestPickup;
        }

        public static void ResetAllPickups()
        {
            for (int i = s_AllPickups.Count - 1; i >= 0; i--)
            {
                if (s_AllPickups[i] != null)
                {
                    s_AllPickups[i].ResetPickup();
                }
            }
        }

        public static Vector3[] GetConsumedPickupPositions()
        {
            List<Vector3> positions = new List<Vector3>();

            for (int i = 0; i < s_AllPickups.Count; i++)
            {
                Pickup pickup = s_AllPickups[i];
                if (pickup != null && pickup.IsConsumed)
                {
                    positions.Add(pickup.transform.position);
                }
            }

            return positions.ToArray();
        }

        public static void ConsumeClosestLocal(Vector3 position, float radius)
        {
            Pickup closestPickup = null;
            float closestSqrDistance = radius * radius;

            for (int i = 0; i < s_AllPickups.Count; i++)
            {
                Pickup pickup = s_AllPickups[i];
                if (pickup == null || pickup.IsConsumed)
                    continue;

                float sqrDistance = (pickup.transform.position - position).sqrMagnitude;
                if (sqrDistance <= closestSqrDistance)
                {
                    closestPickup = pickup;
                    closestSqrDistance = sqrDistance;
                }
            }

            if (closestPickup != null)
            {
                closestPickup.ConsumeLocally(false, false);
            }
        }
    }
}
