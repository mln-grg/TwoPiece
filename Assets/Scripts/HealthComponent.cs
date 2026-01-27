using System;
using UnityEngine;

public interface IDamageable
{
    void TakeDamage(DamageInfo damage);
}

public struct DamageInfo
{
    public float amount;
    public Vector3 hitPoint;
    public GameObject source;
}
[RequireComponent(typeof(BoxCollider))]
public class HealthComponent : MonoBehaviour,IDamageable
{
    public float maxHealth = 100f;
    public float currentHealth;

    public event Action<float> OnDamaged;
    public event Action OnDestroyed;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(DamageInfo damage)
    {
        currentHealth -= damage.amount;
        currentHealth = Mathf.Max(0f, currentHealth);

        OnDamaged?.Invoke(damage.amount);

        if (currentHealth <= 0f)
            OnDestroyed?.Invoke();
    }
}
