using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    public sealed class AstronautWeaponViewModel : MonoBehaviour
    {
        WeaponController m_Weapon;
        Vector3 m_RestPosition;
        Quaternion m_RestRotation;
        float m_Recoil;

        void Awake()
        {
            m_RestPosition = transform.localPosition;
            m_RestRotation = transform.localRotation;
        }

        void Start()
        {
            m_Weapon = GetComponentInParent<WeaponController>();
            if (m_Weapon != null)
            {
                m_Weapon.OnShootProcessed += OnShoot;
            }
        }

        void OnDestroy()
        {
            if (m_Weapon != null)
            {
                m_Weapon.OnShootProcessed -= OnShoot;
            }
        }

        void OnShoot()
        {
            m_Recoil = 1f;
        }

        void LateUpdate()
        {
            m_Recoil = Mathf.MoveTowards(m_Recoil, 0f, Time.deltaTime * 9f);
            float easedRecoil = m_Recoil * m_Recoil;
            transform.localPosition =
                m_RestPosition + new Vector3(0.012f, -0.012f, -0.075f) * easedRecoil;
            transform.localRotation =
                m_RestRotation * Quaternion.Euler(-7f * easedRecoil, 0f, 2f * easedRecoil);
        }
    }
}
