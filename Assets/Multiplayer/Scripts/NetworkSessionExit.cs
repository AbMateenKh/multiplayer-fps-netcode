using System.Collections;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class NetworkSessionExit : MonoBehaviour
{
    const float k_ShutdownTimeoutSeconds = 3f;

    static NetworkSessionExit s_Instance;
    bool m_IsReturning;

    public static void ReturnToMenu(string menuSceneName = "IntroMenu")
    {
        if (s_Instance == null)
        {
            GameObject coordinator = new GameObject(nameof(NetworkSessionExit));
            s_Instance = coordinator.AddComponent<NetworkSessionExit>();
            DontDestroyOnLoad(coordinator);
        }

        if (!s_Instance.m_IsReturning)
        {
            s_Instance.StartCoroutine(s_Instance.ReturnToMenuRoutine(menuSceneName));
        }
    }

    IEnumerator ReturnToMenuRoutine(string menuSceneName)
    {
        m_IsReturning = true;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        LobbyManager.Instance?.LeaveLobby();

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && (networkManager.IsListening || networkManager.ShutdownInProgress))
        {
            if (!networkManager.ShutdownInProgress)
            {
                networkManager.Shutdown();
            }

            float timeoutAt = Time.realtimeSinceStartup + k_ShutdownTimeoutSeconds;
            while (networkManager != null &&
                   (networkManager.IsListening || networkManager.ShutdownInProgress) &&
                   Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }
        }

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(menuSceneName, LoadSceneMode.Single);
        while (loadOperation != null && !loadOperation.isDone)
        {
            yield return null;
        }

        PlayerInputHandler.SetMenuInputBlocked(false);
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (s_Instance == this)
        {
            s_Instance = null;
        }
    }
}
