using UnityEngine;

public class Cannon : MonoBehaviour
{
    [Header("References")]
    public Transform muzzle;
    public GameObject cannonballPrefab;
    public ParticleSystem muzzleFlash;
    public ParticleSystem smoke;

    [Header("Recoil Animation")]
    public bool enableRecoil = true;
    public float recoilDistance = 0.3f;
    public float recoilSpeed = 8f;
    public float returnSpeed = 2f;

    Vector3 initialLocalPos;
    float recoilProgress; // 0 = rest, 1 = full recoil

    void Awake()
    {
        if (muzzle)
            initialLocalPos = muzzle.localPosition;
        else
            initialLocalPos = transform.localPosition;
    }

    void Update()
    {
        if (!enableRecoil) return;

        // Animate recoil recovery
        if (recoilProgress > 0f)
        {
            recoilProgress -= returnSpeed * Time.deltaTime;
            recoilProgress = Mathf.Max(0f, recoilProgress);

            ApplyRecoil();
        }
    }

    public void Fire()
    {
        // Trigger effects
        if (muzzleFlash)
            muzzleFlash.Play();

        if (smoke)
            smoke.Play();

        // Start recoil
        if (enableRecoil)
        {
            recoilProgress = 1f;
        }
    }

    void ApplyRecoil()
    {
        if (!muzzle) return;

        // Ease-out curve for smoother recoil
        float eased = 1f - Mathf.Pow(1f - recoilProgress, 2f);
        
        // Move cannon backward along local Z axis
        Vector3 recoilOffset = Vector3.back * (recoilDistance * eased);
        muzzle.localPosition = initialLocalPos + recoilOffset;
    }
}
