// In fps.Game assembly
namespace Unity.FPS.Game
{
    public interface IPlayerController
    {
        ulong OwnerClientId { get; }
        void OnRespawn();
    }
}