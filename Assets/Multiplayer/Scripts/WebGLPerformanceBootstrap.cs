using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class WebGLPerformanceBootstrap
{
    const int TargetFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ConfigureFrameBudget()
    {
        Application.targetFrameRate = TargetFrameRate;
        QualitySettings.vSyncCount = 0;

#if UNITY_WEBGL && !UNITY_EDITOR
        QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, 32f);
        QualitySettings.shadowResolution = UnityEngine.ShadowResolution.Medium;
        QualitySettings.shadowCascades = 1;
        QualitySettings.realtimeReflectionProbes = false;
        QualitySettings.lodBias = Mathf.Min(QualitySettings.lodBias, 1.25f);
        QualitySettings.maximumLODLevel = 0;

        SceneManager.sceneLoaded += DisableExpensiveWebGLEffects;
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    static void DisableExpensiveWebGLEffects(Scene scene, LoadSceneMode mode)
    {
        Volume[] volumes = Object.FindObjectsByType<Volume>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < volumes.Length; i++)
        {
            VolumeProfile profile = volumes[i].profile;
            if (profile == null)
                continue;

            if (profile.TryGet(out MotionBlur motionBlur))
                motionBlur.active = false;

            if (profile.TryGet(out FilmGrain filmGrain))
                filmGrain.active = false;
        }
    }
#endif
}
