using System;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

public class ServicesInitializer : MonoBehaviour
{
    public static bool IsInitialized { get; private set; }

    async void Start()
    {
        try
        {
            await UnityServices.InitializeAsync();

            // Sign in anonymously (no account needed)
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            Debug.Log($"[Services] Signed in. Player ID: {AuthenticationService.Instance.PlayerId}");
            IsInitialized = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Services] Failed to initialize: {e.Message}");
        }
    }
}