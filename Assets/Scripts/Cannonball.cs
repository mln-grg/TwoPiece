using UnityEngine;

public class Cannonball : MonoBehaviour
{
    public float lifetime = 10f;
    public float damage = 50f;
    public GameObject splashVFX;
    public GameObject hitVFX;

    public float gravityScale = 1.3f;
    
    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        //rb.useGravity = false; // IMPORTANT
    }
    void Start()
    {
        Destroy(gameObject, lifetime);
    }
    void FixedUpdate()
    {
        /*rb.AddForce(
            Physics.gravity * gravityScale,
            ForceMode.Acceleration
        );*/
    }
    void OnCollisionEnter(Collision col)
    {
        if (hitVFX)
            Instantiate(hitVFX, transform.position, Quaternion.identity);

        if (splashVFX && col.gameObject.CompareTag("Water"))
            Instantiate(splashVFX, transform.position, Quaternion.identity);

        // Damage hook goes here

        //Destroy(gameObject);
    }
}
