using System;
using Unity.Netcode;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.FPS.Game
{
    public class GameFlowManager : NetworkBehaviour
    {
        [Header("Match Settings")]
        [Tooltip("Match duration in seconds")]
        public float MatchDuration = 180f; // 3 minutes

        [Tooltip("Delay before respawning after death")]
        public float RespawnDelay = 3f;

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
                // Start match timer
                MatchTimer.Value = MatchDuration;
                IsMatchActive.Value = true;
                IsMatchOver.Value = false;
            }

            // ALL CLIENTS: Listen for match end
            IsMatchOver.OnValueChanged += OnMatchOverChanged;

            OnMatchStarted?.Invoke();
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
                MatchTimer.Value -= Time.deltaTime;

                if (MatchTimer.Value <= 0f)
                {
                    MatchTimer.Value = 0f;
                    IsMatchActive.Value = false;
                    IsMatchOver.Value = true;
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
        }

        void EndMatch()
        {
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
        }

       
        // ============================================
        public void RecordKill(ulong victimId, ulong killerId)
        {
            if (!IsServer) return;

            Debug.Log($"[GFM] RecordKill: victim={victimId}, killer={killerId}");
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
            Debug.Log($"[GFM] RequestRespawn for client {clientId}");
            StartCoroutine(RespawnAfterDelay(clientId));
        }

        System.Collections.IEnumerator RespawnAfterDelay(ulong clientId)
        {
            yield return new WaitForSeconds(RespawnDelay);

            if (!IsMatchActive.Value) yield break;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.ClientId == clientId && client.PlayerObject != null)
                {
                    Health health = client.PlayerObject.GetComponent<Health>();
                    if (health != null)
                    {
                        health.Respawn();
                    }

                    Transform spawnPoint = GetRandomSpawnPoint();

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

        Transform GetRandomSpawnPoint()
        {
            // Find all spawn points in scene
            var spawnPoints = FindObjectsOfType<PlayerSpawnPoint>();
            if (spawnPoints.Length > 0)
            {
                return spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)].transform;
            }

            // Fallback: spawn at origin
            return transform;
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
            int highestKills = -1;
            ulong winnerId = 0;

            for (int i = 0; i < PlayerIds.Count; i++)
            {
                if (PlayerKills[i] > highestKills)
                {
                    highestKills = PlayerKills[i];
                    winnerId = PlayerIds[i];
                }
            }

            return winnerId;
        }

        public override void OnNetworkDespawn()
        {
            IsMatchOver.OnValueChanged -= OnMatchOverChanged;
        }



        
    }
}