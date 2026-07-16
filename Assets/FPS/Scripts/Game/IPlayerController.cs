// In fps.Game assembly
using UnityEngine;

namespace Unity.FPS.Game
{
    public interface IPlayerController
    {
        ulong OwnerClientId { get; }
        void OnRespawn();
    }

    public interface IMatchRestartHandler
    {
        void ResetForMatchRestart();
    }

    public interface IMatchPickup
    {
        bool IsConsumedForMatch { get; }
        Vector3 MatchPosition { get; }
        void ResetPickupForMatch();
        void ConsumeForMatchSync();
    }
}
