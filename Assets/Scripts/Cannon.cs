using UnityEngine;

public class Cannon : MonoBehaviour
{
    public Transform muzzle;
    public GameObject cannonballPrefab;
    public ParticleSystem muzzleFlash;
    public ParticleSystem smoke;

    public float muzzleVelocity = 40f;
    public float recoilDistance = 0.3f;
    
    [SerializeField] float baseElevationDegrees = 6f;

    Vector3 initialLocalPos;

    void Awake()
    {
        initialLocalPos = transform.localPosition;
    }

    public void Fire()
    {
        // Spawn cannonball
        GameObject ball = Instantiate(
            cannonballPrefab,
            muzzle.position,
            muzzle.rotation
        );
        Rigidbody rb = ball.GetComponent<Rigidbody>();

        /*float yawSpread = Random.Range(-2.5f, 2.5f);   // left/right
        float pitchSpread = Random.Range(-0.5f, 1.5f); // mostly up

        Quaternion spread =
            Quaternion.Euler(pitchSpread, yawSpread, 0f);

        float arcBoost = Random.Range(0.05f, 0.12f);
        Vector3 dir = muzzle.forward + Vector3.up * arcBoost;

        rb.linearVelocity = dir.normalized * muzzleVelocity;*/
        
        Quaternion elevation =
            Quaternion.AngleAxis(baseElevationDegrees, transform.right);

        Vector3 fireDir = elevation * muzzle.forward;

        // small randomness on top
        fireDir = Quaternion.Euler(
            Random.Range(-0.5f, 1.5f),
            Random.Range(-2.5f, 2.5f),
            0f
        ) * fireDir;

        rb.linearVelocity = fireDir.normalized * muzzleVelocity;
        
       
        // VFX
        if (muzzleFlash) muzzleFlash.Play();
        if (smoke) smoke.Play();

        // Recoil
        StopAllCoroutines();
        StartCoroutine(Recoil());
    }

    System.Collections.IEnumerator Recoil()
    {
        float t = 0f;
        Vector3 back = initialLocalPos - Vector3.forward * recoilDistance;

        while (t < 1f)
        {
            t += Time.deltaTime * 10f;
            transform.localPosition = Vector3.Lerp(back, initialLocalPos, t);
            yield return null;
        }
    }
}
