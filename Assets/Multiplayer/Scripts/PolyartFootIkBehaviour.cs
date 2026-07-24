using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Keeps the lower foot planted on nearby ground while preserving the lifted
    /// foot's stride. This runs only on the base locomotion Animator state.
    /// </summary>
    public sealed class PolyartFootIkBehaviour : StateMachineBehaviour
    {
        public LayerMask GroundLayers = 1;
        [Min(0.05f)] public float RayOriginHeight = 0.35f;
        [Min(0.1f)] public float RayDistance = 0.85f;
        [Min(0f)] public float SoleOffset = 0.025f;
        [Min(0.01f)] public float FootLiftReleaseHeight = 0.16f;
        [Min(0f)] public float MaximumPelvisDrop = 0.16f;

        readonly RaycastHit[] m_Hits = new RaycastHit[8];
        Transform m_PlayerRoot;

        public override void OnStateEnter(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex)
        {
            m_PlayerRoot = animator.transform.parent;
        }

        public override void OnStateIK(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex)
        {
            if (layerIndex != 0 || !animator.isHuman)
                return;

            m_PlayerRoot ??= animator.transform.parent;

            Vector3 leftAnimated = animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 rightAnimated = animator.GetIKPosition(AvatarIKGoal.RightFoot);
            float lowestFootHeight = Mathf.Min(leftAnimated.y, rightAnimated.y);

            bool hasLeft = TryGetGround(
                animator,
                AvatarIKGoal.LeftFoot,
                leftAnimated,
                out Vector3 leftTarget,
                out Quaternion leftRotation);
            bool hasRight = TryGetGround(
                animator,
                AvatarIKGoal.RightFoot,
                rightAnimated,
                out Vector3 rightTarget,
                out Quaternion rightRotation);

            float leftWeight = hasLeft
                ? CalculatePlantWeight(leftAnimated.y - lowestFootHeight)
                : 0f;
            float rightWeight = hasRight
                ? CalculatePlantWeight(rightAnimated.y - lowestFootHeight)
                : 0f;

            ApplyPelvisDrop(
                animator,
                leftAnimated,
                rightAnimated,
                leftTarget,
                rightTarget,
                leftWeight,
                rightWeight);
            ApplyFoot(
                animator,
                AvatarIKGoal.LeftFoot,
                leftTarget,
                leftRotation,
                leftWeight);
            ApplyFoot(
                animator,
                AvatarIKGoal.RightFoot,
                rightTarget,
                rightRotation,
                rightWeight);
        }

        bool TryGetGround(
            Animator animator,
            AvatarIKGoal goal,
            Vector3 animatedPosition,
            out Vector3 targetPosition,
            out Quaternion targetRotation)
        {
            Vector3 up = animator.transform.up;
            Vector3 origin = animatedPosition + up * RayOriginHeight;
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                -up,
                m_Hits,
                RayDistance,
                GroundLayers,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = float.PositiveInfinity;
            RaycastHit nearestHit = default;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = m_Hits[i];
                if (hit.collider == null ||
                    (m_PlayerRoot != null &&
                     hit.collider.transform.IsChildOf(m_PlayerRoot)))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestHit = hit;
                }
            }

            if (float.IsPositiveInfinity(nearestDistance))
            {
                targetPosition = animatedPosition;
                targetRotation = Quaternion.identity;
                return false;
            }

            targetPosition = nearestHit.point + nearestHit.normal * SoleOffset;
            Quaternion animatedRotation = animator.GetIKRotation(goal);
            targetRotation =
                Quaternion.FromToRotation(animatedRotation * Vector3.up, nearestHit.normal) *
                animatedRotation;
            return true;
        }

        float CalculatePlantWeight(float heightAboveLowestFoot)
        {
            return 1f - Mathf.InverseLerp(
                FootLiftReleaseHeight * 0.25f,
                FootLiftReleaseHeight,
                heightAboveLowestFoot);
        }

        void ApplyPelvisDrop(
            Animator animator,
            Vector3 leftAnimated,
            Vector3 rightAnimated,
            Vector3 leftTarget,
            Vector3 rightTarget,
            float leftWeight,
            float rightWeight)
        {
            float pelvisDrop = 0f;
            if (leftWeight > 0f)
            {
                pelvisDrop = Mathf.Min(
                    pelvisDrop,
                    (leftTarget.y - leftAnimated.y) * leftWeight);
            }
            if (rightWeight > 0f)
            {
                pelvisDrop = Mathf.Min(
                    pelvisDrop,
                    (rightTarget.y - rightAnimated.y) * rightWeight);
            }

            pelvisDrop = Mathf.Max(pelvisDrop, -MaximumPelvisDrop);
            animator.bodyPosition += animator.transform.up * pelvisDrop;
        }

        static void ApplyFoot(
            Animator animator,
            AvatarIKGoal goal,
            Vector3 targetPosition,
            Quaternion targetRotation,
            float weight)
        {
            animator.SetIKPositionWeight(goal, weight);
            animator.SetIKRotationWeight(goal, weight);
            if (weight <= 0f)
                return;

            animator.SetIKPosition(goal, targetPosition);
            animator.SetIKRotation(goal, targetRotation);
        }
    }
}
