using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.FPS.Game
{
    public enum MatchEndReason
    {
        None = 0,
        ScoreLimit = 1,
        TimeExpired = 2
    }

    public class GameFlowManager : NetworkBehaviour
    {
        [Header("Match Settings")]
        [Tooltip("Match duration in seconds")]
        public float MatchDuration = 180f; // 3 minutes

        [Tooltip("Kills needed to end the match early. Set to 0 to use timer only.")]
        public int ScoreLimit = 10;

        [Tooltip("Seconds players wait before a round becomes playable.")]
        public float CountdownDuration = 3f;

        [Tooltip("Spawn points closer than this to a living player are avoided when possible.")]
        public float SpawnDangerRadius = 14f;

        [Tooltip("Delay before respawning after death")]
        public static float RespawnDelay = 3f;

        [Header("Solo Demo")]
        [Tooltip("Spawn practice targets automatically when hosting alone.")]
        public bool EnableSoloTargetDummies = true;

        [Tooltip("Number of target dummies to spawn for solo practice.")]
        public int SoloTargetDummyCount = 3;

        [Tooltip("Health assigned to each solo target dummy.")]
        public float SoloTargetDummyHealth = 60f;

        [Tooltip("Seconds before a destroyed solo target dummy reappears.")]
        public float SoloTargetDummyRespawnDelay = 2f;

        [Tooltip("Fallback distance used when the scene has no player spawn points.")]
        public float SoloTargetSpawnRadius = 12f;

        [Header("End Game")]
        [Tooltip("Duration of the fade-to-black at the end of the game")]
        public float EndSceneLoadDelay = 3f;

        [Tooltip("The canvas group of the fade-to-black screen")]
        public CanvasGroup EndGameFadeCanvasGroup;

        [Header("Win")] [Tooltip("This string has to be the name of the scene you want to load when winning")]
        public string WinSceneName = "WinScene";

        [Tooltip("Duration of delay before the fade-to-black, if winning")]
        public float DelayBeforeFadeToBlack = 4f;

        [Tooltip("Win game message")]
        public string WinGameMessage;
        [Tooltip("Duration of delay before the win message")]
        public float DelayBeforeWinMessage = 2f;

        [Tooltip("Sound played on win")] public AudioClip VictorySound;

        [Header("Lose")] [Tooltip("This string has to be the name of the scene you want to load when losing")]
        public string LoseSceneName = "LoseScene";

        // NETWORK STATE — Server authoritative
        // ============================================
        public NetworkVariable<float> MatchTimer = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> IsMatchActive = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> IsMatchOver = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> MatchEndReasonValue = new NetworkVariable<int>(
            (int)MatchEndReason.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<float> CountdownTimer = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> IsCountdownActive = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool IsGameplayActive => IsMatchActive.Value && !IsCountdownActive.Value && !IsMatchOver.Value;
        public MatchEndReason CurrentMatchEndReason => (MatchEndReason)MatchEndReasonValue.Value;


        // ============================================
        // KILL TRACKING — Server authoritative
        // Using parallel arrays since NetworkList doesn't support structs easily
        // Key: OwnerClientId, Value: kill count
        // ============================================
        public NetworkList<ulong> PlayerIds;
        public NetworkList<int> PlayerKills;
        public NetworkList<int> PlayerDeaths;

        // Events for UI to subscribe to
        public static event Action<ulong, ulong> OnPlayerKilled; // victimId, killerId
        public static event Action OnMatchStarted;
        public static event Action OnMatchEnded;

        float m_TimeLoadEndGameScene;
        bool m_GameIsEnding;
        readonly List<TargetPracticeDummy> m_SoloTargetDummies = new List<TargetPracticeDummy>();


        public bool GameIsEnding => m_GameIsEnding || IsMatchOver.Value;

        void Awake()
        {
            PlayerIds = new NetworkList<ulong>();
            PlayerKills = new NetworkList<int>();
            PlayerDeaths = new NetworkList<int>();
        }


        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                PrepareRoundCountdown();
                IsMatchOver.Value = false;
                RefreshSoloTargetDummies();
            }

            // ALL CLIENTS: Listen for match end
            IsMatchOver.OnValueChanged += OnMatchOverChanged;

            if (IsMatchOver.Value)
            {
                EndMatch();
            }
            else if (!IsServer && IsGameplayActive)
            {
                OnMatchStarted?.Invoke();
            }
        }
        void Start()
        {
            AudioUtility.SetMasterVolume(1);
        }

        // UPDATE — Server counts down, all clients handle end game fade
        // ============================================
        void Update()
        {
            // SERVER: Count down match timer
            if (IsServer && IsMatchActive.Value)
            {
                if (IsCountdownActive.Value)
                {
                    CountdownTimer.Value = Mathf.Max(0f, CountdownTimer.Value - Time.deltaTime);
                    if (CountdownTimer.Value <= 0f)
                    {
                        CountdownTimer.Value = 0f;
                        IsCountdownActive.Value = false;
                        OnMatchStarted?.Invoke();
                    }

                    return;
                }

                MatchTimer.Value -= Time.deltaTime;

                if (MatchTimer.Value <= 0f)
                {
                    EndMatchServer(MatchEndReason.TimeExpired);
                }
            }

            // ALL CLIENTS: Handle end game fade
            if (m_GameIsEnding)
            {
                float timeRatio = 1 - (m_TimeLoadEndGameScene - Time.time) / EndSceneLoadDelay;
                EndGameFadeCanvasGroup.alpha = timeRatio;
                AudioUtility.SetMasterVolume(1 - timeRatio);
            }
        }

        // MATCH END — Triggered on all clients via NetworkVariable
        // ============================================
        void OnMatchOverChanged(bool previousValue, bool newValue)
        {
            if (newValue)
            {
                EndMatch();
            }
            else
            {
                ResetEndMatchState();
            }
        }

        void EndMatch()
        {
            if (m_GameIsEnding)
                return;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            m_GameIsEnding = true;
            m_TimeLoadEndGameScene = Time.time + EndSceneLoadDelay;

            if (EndGameFadeCanvasGroup != null)
            {
                EndGameFadeCanvasGroup.gameObject.SetActive(true);
            }

            // Play victory sound
            if (VictorySound)
            {
                var audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = VictorySound;
                audioSource.playOnAwake = false;
                audioSource.outputAudioMixerGroup =
                    AudioUtility.GetAudioGroup(AudioUtility.AudioGroups.HUDVictory);
                audioSource.Play();
            }

            OnMatchEnded?.Invoke();
        }

        void ResetEndMatchState()
        {
            m_GameIsEnding = false;
            AudioUtility.SetMasterVolume(1);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (EndGameFadeCanvasGroup != null)
            {
                EndGameFadeCanvasGroup.alpha = 0f;
                EndGameFadeCanvasGroup.gameObject.SetActive(false);
            }
        }

        // PLAYER REGISTRATION — Server tracks who's playing
        // ============================================
        public void RegisterPlayer(ulong clientId)
        {
            if (!IsServer) return;

            if (!PlayerIds.Contains(clientId))
            {
                PlayerIds.Add(clientId);
                PlayerKills.Add(0);
                PlayerDeaths.Add(0);
            }

            SyncConsumedPickupsToClient(clientId);
            RefreshSoloTargetDummies();
        }

        public void UnregisterPlayer(ulong clientId)
        {
            if (!IsServer) return;

            int index = FindPlayerIndex(clientId);
            if (index >= 0)
            {
                PlayerIds.RemoveAt(index);
                PlayerKills.RemoveAt(index);
                PlayerDeaths.RemoveAt(index);
            }

            RefreshSoloTargetDummies();
        }

       
        // ============================================
        public void RecordKill(ulong victimId, ulong killerId)
        {
            if (!IsServer) return;
            if (!IsGameplayActive) return;

            // Record death
            int victimIndex = FindPlayerIndex(victimId);
            if (victimIndex >= 0)
            {
                PlayerDeaths[victimIndex] = PlayerDeaths[victimIndex] + 1;
            }

            // Record kill (don't count self-kills)
            if (victimId != killerId)
            {
                int killerIndex = FindPlayerIndex(killerId);
                if (killerIndex >= 0)
                {
                    PlayerKills[killerIndex] = PlayerKills[killerIndex] + 1;

                    if (ScoreLimit > 0 && PlayerKills[killerIndex] >= ScoreLimit)
                    {
                        EndMatchServer(MatchEndReason.ScoreLimit);
                    }
                }
            }

            // Notify all clients
            NotifyKillClientRpc(victimId, killerId);
        }

        [ClientRpc]
        void NotifyKillClientRpc(ulong victimId, ulong killerId)
        {
            OnPlayerKilled?.Invoke(victimId, killerId);
        }

        // RESPAWN — Server handles respawn after delay
        // ============================================
        public void RequestRespawn(ulong clientId)
        {
            if (!IsServer) return;
            if (!IsGameplayActive) return;
            StartCoroutine(RespawnAfterDelay(clientId));
        }

        System.Collections.IEnumerator RespawnAfterDelay(ulong clientId)
        {
            yield return new WaitForSeconds(RespawnDelay);

            if (!IsGameplayActive) yield break;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.ClientId == clientId && client.PlayerObject != null)
                {
                    Health health = client.PlayerObject.GetComponent<Health>();
                    if (health != null)
                    {
                        health.Respawn();
                    }

                    Transform spawnPoint = GetBestSpawnPoint(clientId);

                    // Tell the owner to move (since movement is owner-authoritative)
                    RespawnAtPositionClientRpc(spawnPoint.position, spawnPoint.rotation,
                        new ClientRpcParams
                        {
                            Send = new ClientRpcSendParams
                            {
                                TargetClientIds = new ulong[] { clientId }
                            }
                        });

                    break;
                }
            }
        }

        [ClientRpc]
        void RespawnAtPositionClientRpc(Vector3 position, Quaternion rotation,
            ClientRpcParams clientRpcParams = default)
        {
            // Find local player and teleport
            var player = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (player != null)
            {
                CharacterController cc = player.GetComponent<CharacterController>();

                // Must disable CharacterController to teleport
                if (cc != null) cc.enabled = false;

                player.transform.position = position;
                player.transform.rotation = rotation;

                if (cc != null) cc.enabled = true;
            }
        }

        Transform GetBestSpawnPoint(ulong spawningClientId)
        {
            PlayerSpawnPoint[] spawnPoints = FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
            if (spawnPoints.Length == 0)
            {
                return transform;
            }

            List<Transform> safeSpawnPoints = new List<Transform>();
            Transform safestFallback = spawnPoints[0].transform;
            float safestFallbackScore = float.NegativeInfinity;

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                Transform candidate = spawnPoints[i].transform;
                float safetyScore = GetSpawnSafetyScore(candidate.position, spawningClientId);

                if (safetyScore > safestFallbackScore)
                {
                    safestFallbackScore = safetyScore;
                    safestFallback = candidate;
                }

                if (safetyScore >= SpawnDangerRadius)
                {
                    safeSpawnPoints.Add(candidate);
                }
            }

            if (safeSpawnPoints.Count > 0)
            {
                return safeSpawnPoints[UnityEngine.Random.Range(0, safeSpawnPoints.Count)];
            }

            return safestFallback;
        }

        float GetSpawnSafetyScore(Vector3 spawnPosition, ulong spawningClientId)
        {
            if (NetworkManager.Singleton == null)
            {
                return float.PositiveInfinity;
            }

            float closestLivePlayerDistance = float.PositiveInfinity;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.ClientId == spawningClientId || client.PlayerObject == null)
                    continue;

                Health health = client.PlayerObject.GetComponent<Health>();
                if (health == null || health.CurrentHealth.Value <= 0f)
                    continue;

                float distance = Vector3.Distance(spawnPosition, client.PlayerObject.transform.position);
                if (distance < closestLivePlayerDistance)
                {
                    closestLivePlayerDistance = distance;
                }
            }

            return closestLivePlayerDistance;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RestartMatchServerRpc(RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (senderClientId != NetworkManager.ServerClientId)
            {
                Debug.LogWarning($"[GameFlowManager] Ignored restart request from non-host client {senderClientId}.");
                return;
            }

            RestartMatch();
        }

        public void RestartMatch()
        {
            if (!IsServer) return;
            if (!IsMatchOver.Value) return;

            IsMatchActive.Value = false;
            IsMatchOver.Value = false;
            MatchEndReasonValue.Value = (int)MatchEndReason.None;
            IsCountdownActive.Value = false;
            CountdownTimer.Value = 0f;


            for (int i = 0; i < PlayerKills.Count; i++)
            {
                PlayerKills[i] = 0;
            }

            for (int i = 0; i < PlayerDeaths.Count; i++)
            {
                PlayerDeaths[i] = 0;
            }

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                RegisterPlayer(client.ClientId);

                if (client.PlayerObject == null)
                {
                    continue;
                }

                Health health = client.PlayerObject.GetComponent<Health>();
                if (health != null)
                {
                    health.Respawn();
                }

                ResetMatchHandlers(client.PlayerObject.gameObject);

                Transform spawnPoint = GetBestSpawnPoint(client.ClientId);
                RespawnAtPositionClientRpc(spawnPoint.position, spawnPoint.rotation,
                    new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams
                        {
                            TargetClientIds = new ulong[] { client.ClientId }
                        }
                    });
            }

            ResetAllMatchPickups();
            ResetPickupsClientRpc();
            PrepareRoundCountdown();
        }

        void PrepareRoundCountdown()
        {
            MatchTimer.Value = MatchDuration;
            IsMatchActive.Value = true;
            MatchEndReasonValue.Value = (int)MatchEndReason.None;

            float countdown = Mathf.Max(0f, CountdownDuration);
            CountdownTimer.Value = countdown;
            IsCountdownActive.Value = countdown > 0f;

            if (!IsCountdownActive.Value)
            {
                OnMatchStarted?.Invoke();
            }
        }

        [ClientRpc]
        void ResetPickupsClientRpc()
        {
            if (IsServer)
                return;

            ResetAllMatchPickups();
        }

        void SyncConsumedPickupsToClient(ulong clientId)
        {
            Vector3[] consumedPickupPositions = GetConsumedPickupPositions();
            if (consumedPickupPositions.Length == 0)
                return;

            SyncConsumedPickupsClientRpc(consumedPickupPositions,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new ulong[] { clientId }
                    }
                });
        }

        [ClientRpc]
        void SyncConsumedPickupsClientRpc(Vector3[] consumedPickupPositions,
            ClientRpcParams clientRpcParams = default)
        {
            if (IsServer)
                return;

            for (int i = 0; i < consumedPickupPositions.Length; i++)
            {
                ConsumeClosestMatchPickup(consumedPickupPositions[i], 0.5f);
            }
        }

        void ResetMatchHandlers(GameObject target)
        {
            MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IMatchRestartHandler matchRestartHandler)
                {
                    matchRestartHandler.ResetForMatchRestart();
                }
            }
        }

        static void ResetAllMatchPickups()
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IMatchPickup pickup)
                {
                    pickup.ResetPickupForMatch();
                }
            }
        }

        static Vector3[] GetConsumedPickupPositions()
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            System.Collections.Generic.List<Vector3> positions = new System.Collections.Generic.List<Vector3>();

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IMatchPickup pickup && pickup.IsConsumedForMatch)
                {
                    positions.Add(pickup.MatchPosition);
                }
            }

            return positions.ToArray();
        }

        static void ConsumeClosestMatchPickup(Vector3 position, float radius)
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            IMatchPickup closestPickup = null;
            float closestSqrDistance = radius * radius;

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is not IMatchPickup pickup || pickup.IsConsumedForMatch)
                    continue;

                float sqrDistance = (pickup.MatchPosition - position).sqrMagnitude;
                if (sqrDistance <= closestSqrDistance)
                {
                    closestPickup = pickup;
                    closestSqrDistance = sqrDistance;
                }
            }

            closestPickup?.ConsumeForMatchSync();
        }

        void RefreshSoloTargetDummies()
        {
            if (!IsServer)
                return;

            bool shouldUseSoloTargets = EnableSoloTargetDummies &&
                NetworkManager.Singleton != null &&
                NetworkManager.Singleton.ConnectedClientsList.Count <= 1;

            if (!shouldUseSoloTargets)
            {
                ClearSoloTargetDummies();
                return;
            }

            int targetCount = Mathf.Max(0, SoloTargetDummyCount);
            while (m_SoloTargetDummies.Count < targetCount)
            {
                m_SoloTargetDummies.Add(CreateSoloTargetDummy(m_SoloTargetDummies.Count));
            }

            while (m_SoloTargetDummies.Count > targetCount)
            {
                TargetPracticeDummy dummy = m_SoloTargetDummies[m_SoloTargetDummies.Count - 1];
                m_SoloTargetDummies.RemoveAt(m_SoloTargetDummies.Count - 1);
                if (dummy != null)
                {
                    Destroy(dummy.gameObject);
                }
            }
        }

        TargetPracticeDummy CreateSoloTargetDummy(int index)
        {
            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            target.name = $"Solo Target Dummy {index + 1}";
            target.transform.position = GetSoloTargetPosition(index);
            target.transform.rotation = GetSoloTargetRotation(target.transform.position);
            target.transform.localScale = new Vector3(0.9f, 1.8f, 0.9f);

            target.AddComponent<NetworkObject>();

            Health health = target.AddComponent<Health>();
            health.MaxHealth = Mathf.Max(1f, SoloTargetDummyHealth);
            health.RespawnProtectionDuration = 0f;

            Damageable damageable = target.AddComponent<Damageable>();
            damageable.DamageMultiplier = 1f;

            TargetPracticeDummy dummy = target.AddComponent<TargetPracticeDummy>();
            dummy.RespawnDelay = Mathf.Max(0.1f, SoloTargetDummyRespawnDelay);
            return dummy;
        }

        Vector3 GetSoloTargetPosition(int index)
        {
            PlayerSpawnPoint[] spawnPoints = FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
            Vector3 position;

            if (spawnPoints.Length > 0)
            {
                Transform spawnPoint = spawnPoints[index % spawnPoints.Length].transform;
                Vector3 lateralOffset = spawnPoint.right * (((index % 2) * 2) - 1) * 2.5f;
                position = spawnPoint.position + spawnPoint.forward * 5f + lateralOffset;
            }
            else
            {
                float angle = index * Mathf.PI * 2f / Mathf.Max(1, SoloTargetDummyCount);
                position = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SoloTargetSpawnRadius;
            }

            if (Physics.Raycast(position + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 50f, -1,
                QueryTriggerInteraction.Ignore))
            {
                position = hit.point + Vector3.up;
            }

            return position;
        }

        Quaternion GetSoloTargetRotation(Vector3 targetPosition)
        {
            Vector3 lookDirection = transform.position - targetPosition;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude < 0.01f)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }

        void ClearSoloTargetDummies()
        {
            for (int i = 0; i < m_SoloTargetDummies.Count; i++)
            {
                if (m_SoloTargetDummies[i] != null)
                {
                    Destroy(m_SoloTargetDummies[i].gameObject);
                }
            }

            m_SoloTargetDummies.Clear();
        }

        // HELPER — Find player index in parallel arrays
        // ============================================
        int FindPlayerIndex(ulong clientId)
        {
            for (int i = 0; i < PlayerIds.Count; i++)
            {
                if (PlayerIds[i] == clientId)
                    return i;
            }
            return -1;
        }

        void EndMatchServer(MatchEndReason reason)
        {
            if (!IsServer)
                return;

            MatchTimer.Value = Mathf.Max(0f, MatchTimer.Value);
            MatchEndReasonValue.Value = (int)reason;
            IsMatchActive.Value = false;
            IsCountdownActive.Value = false;
            CountdownTimer.Value = 0f;
            IsMatchOver.Value = true;
        }

        // PUBLIC GETTERS — For UI to read scores
        // ============================================
        public int GetKills(ulong clientId)
        {
            int index = FindPlayerIndex(clientId);
            return index >= 0 ? PlayerKills[index] : 0;
        }

        public int GetDeaths(ulong clientId)
        {
            int index = FindPlayerIndex(clientId);
            return index >= 0 ? PlayerDeaths[index] : 0;
        }

        public ulong GetWinnerId()
        {
            ulong winnerId = 0;
            int winnerIndex = -1;

            for (int i = 0; i < PlayerIds.Count; i++)
            {
                if (winnerIndex < 0 || IsBetterPlacement(i, winnerIndex))
                {
                    winnerIndex = i;
                    winnerId = PlayerIds[i];
                }
            }

            return winnerId;
        }

        bool IsBetterPlacement(int candidateIndex, int currentBestIndex)
        {
            int killCompare = PlayerKills[candidateIndex].CompareTo(PlayerKills[currentBestIndex]);
            if (killCompare != 0)
                return killCompare > 0;

            int deathCompare = PlayerDeaths[candidateIndex].CompareTo(PlayerDeaths[currentBestIndex]);
            if (deathCompare != 0)
                return deathCompare < 0;

            return PlayerIds[candidateIndex] < PlayerIds[currentBestIndex];
        }

        public override void OnNetworkDespawn()
        {
            IsMatchOver.OnValueChanged -= OnMatchOverChanged;
        }



    }

    [RequireComponent(typeof(Health), typeof(Damageable))]
    public class TargetPracticeDummy : MonoBehaviour
    {
        public float RespawnDelay = 2f;
        public Color AliveColor = new Color(0.2f, 0.55f, 1f, 1f);
        public Color DamagedColor = new Color(1f, 0.7f, 0.2f, 1f);
        public Color DownColor = new Color(0.1f, 0.1f, 0.12f, 1f);

        Health m_Health;
        Renderer[] m_Renderers;
        Collider[] m_Colliders;
        TextMesh m_Label;
        Coroutine m_RespawnRoutine;

        void Awake()
        {
            m_Health = GetComponent<Health>();
            m_Renderers = GetComponentsInChildren<Renderer>();
            m_Colliders = GetComponentsInChildren<Collider>();
            CreateLabel();
        }

        void OnEnable()
        {
            if (m_Health == null)
                return;

            m_Health.OnDamaged += OnDamaged;
            m_Health.OnDie += OnDie;
        }

        void Start()
        {
            m_Health.Revive(false);
            SetTargetActive(true);
            SetColor(AliveColor);
            SetLabel("TARGET", AliveColor);
        }

        void OnDisable()
        {
            if (m_Health == null)
                return;

            m_Health.OnDamaged -= OnDamaged;
            m_Health.OnDie -= OnDie;
        }

        void OnDamaged(float damage, GameObject damageSource)
        {
            SetColor(DamagedColor);
            SetLabel("HIT", DamagedColor);
            CancelInvoke(nameof(RestoreAliveColor));
            Invoke(nameof(RestoreAliveColor), 0.12f);
        }

        void RestoreAliveColor()
        {
            if (m_Health != null && m_Health.CurrentHealth.Value > 0f)
            {
                SetColor(AliveColor);
                SetLabel("TARGET", AliveColor);
            }
        }

        void OnDie()
        {
            if (m_RespawnRoutine != null)
            {
                StopCoroutine(m_RespawnRoutine);
            }

            m_RespawnRoutine = StartCoroutine(RespawnAfterDelay());
        }

        IEnumerator RespawnAfterDelay()
        {
            SetColor(DownColor);
            SetLabel("DOWN", DownColor);
            SetTargetActive(false);

            yield return new WaitForSeconds(RespawnDelay);

            m_Health.Revive(false);
            SetTargetActive(true);
            SetColor(AliveColor);
            SetLabel("TARGET", AliveColor);
            m_RespawnRoutine = null;
        }

        void LateUpdate()
        {
            if (m_Label == null || Camera.main == null)
                return;

            Vector3 lookDirection = m_Label.transform.position - Camera.main.transform.position;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                m_Label.transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            }
        }

        void SetTargetActive(bool active)
        {
            for (int i = 0; i < m_Colliders.Length; i++)
            {
                m_Colliders[i].enabled = active;
            }
        }

        void SetColor(Color color)
        {
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                m_Renderers[i].material.color = color;
            }
        }

        void CreateLabel()
        {
            GameObject labelObject = new GameObject("TargetLabel");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 1.35f, 0f);

            m_Label = labelObject.AddComponent<TextMesh>();
            m_Label.text = "TARGET";
            m_Label.anchor = TextAnchor.MiddleCenter;
            m_Label.alignment = TextAlignment.Center;
            m_Label.fontSize = 32;
            m_Label.characterSize = 0.08f;
            m_Label.color = AliveColor;
        }

        void SetLabel(string text, Color color)
        {
            if (m_Label == null)
                return;

            m_Label.text = text;
            m_Label.color = color;
        }
    }

}
