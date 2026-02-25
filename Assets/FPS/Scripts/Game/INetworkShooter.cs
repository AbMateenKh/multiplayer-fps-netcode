// In fps.Game assembly
using UnityEngine;

namespace Unity.FPS.Game
{
    public interface INetworkShooter
    {
        void RequestShoot(Vector3 origin, Vector3 direction);
    }
}