using UnityEngine;

public class Cannon : MonoBehaviour
{
    public Transform muzzle;
    public GameObject cannonballPrefab;
    public ParticleSystem muzzleFlash;
    public ParticleSystem smoke;
    Vector3 initialLocalPos;
}
