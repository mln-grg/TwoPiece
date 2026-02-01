using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class Cannonball : MonoBehaviour
{
    public float lifetime = 10f;
    public float damage = 50f;

    public GameObject splashVFX;
    public GameObject hitVFX;

    Rigidbody rb;
    bool hasCollided;

    public GameObject Owner;

    // ===== Analytic flight =====
    BallisticSolution solution;
    float flightTime;
    bool analyticFlight;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    // =====================================================
    // INITIALIZATION (called by CannonsController)
    // =====================================================

    public void LaunchAnalytic(BallisticSolution sol)
    {
        solution = sol;
        flightTime = 0f;
        analyticFlight = true;

        rb.isKinematic = true;
        rb.useGravity = false;
    }

    void FixedUpdate()
    {
        if (!analyticFlight)
            return;

        flightTime += Time.fixedDeltaTime;

        Vector3 nextPos = solution.Evaluate(flightTime);

        // Move via physics system so collisions still register
        rb.MovePosition(nextPos);
    }

    // =====================================================
    // COLLISION
    // =====================================================

    void OnCollisionEnter(Collision col)
    {
        if (hasCollided)
            return;

        if (Owner && col.transform.IsChildOf(Owner.transform))
            return;

        hasCollided = true;

        SpawnHitVFX(col.contacts[0].point);

        if (splashVFX && col.gameObject.CompareTag("Water"))
            Instantiate(splashVFX, col.contacts[0].point, Quaternion.identity);

        IDamageable damageable =
            col.gameObject.GetComponentInParent<IDamageable>();

        if (damageable != null)
        {
            damageable.TakeDamage(new DamageInfo
            {
                amount = damage,
                hitPoint = col.contacts[0].point,
                source = gameObject
            });
        }

        Destroy(gameObject);
    }

    void SpawnHitVFX(Vector3 point)
    {
        if (hitVFX)
            Instantiate(hitVFX, point, Quaternion.identity);
    }
}
