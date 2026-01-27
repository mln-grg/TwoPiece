using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class Cannonball : MonoBehaviour
{
    public float lifetime = 10f;
    public float damage = 50f;

    public GameObject splashVFX;
    public GameObject hitVFX;

    Rigidbody rb;
    bool hasCollided; // prevents double-processing

    public GameObject Owner;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    void OnCollisionEnter(Collision col)
    {
        // Prevent double execution (important for projectile-projectile hits)
        
        if (col.gameObject.transform.IsChildOf(Owner.transform))
            return;
        
        if (hasCollided)
            return;

        hasCollided = true;

        // ---------------- PROJECTILE vs PROJECTILE ----------------
        Cannonball otherBall = col.gameObject.GetComponent<Cannonball>();
        if (otherBall != null)
        {
            // Let the other ball also know it collided
            otherBall.DestroySelf();

            SpawnHitVFX(col.contacts[0].point);
            DestroySelf();
            return;
        }

        // ---------------- VISUALS ----------------
        SpawnHitVFX(col.contacts[0].point);

        if (splashVFX && col.gameObject.CompareTag("Water"))
            Instantiate(splashVFX, col.contacts[0].point, Quaternion.identity);

        // ---------------- DAMAGE ----------------
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

        DestroySelf();
    }

    void SpawnHitVFX(Vector3 point)
    {
        if (hitVFX)
            Instantiate(hitVFX, point, Quaternion.identity);
    }

    public void DestroySelf()
    {
        if (hasCollided)
            Destroy(gameObject);
    }
}