namespace Unity.FPS.Gameplay
{
    public class JetpackPickup : Pickup
    {
        protected override bool ApplyPickupEffect(PlayerCharacterController byPlayer, bool serverAuthoritative)
        {
            var jetpack = byPlayer.GetComponent<Jetpack>();
            if (!jetpack)
                return false;

            return jetpack.TryUnlock();
        }
    }
}
