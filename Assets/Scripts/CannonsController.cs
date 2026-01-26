using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class CannonsController : MonoBehaviour
{
    public List<Cannon> leftCannons;
    public List<Cannon> rightCannons;

    public float reloadTime = 5f;

    bool canFireLeft = true;
    bool canFireRight = true;

    private void Start()
    {
        SortCannonsByHullDirection(leftCannons);
        SortCannonsByHullDirection(rightCannons);
        
    }

    void SortCannonsByHullDirection(List<Cannon> cannons)
    {
        cannons.Sort((a, b) =>
            a.transform.position.z.CompareTo(b.transform.position.z)
        );
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q) && canFireLeft)
            StartCoroutine(FireBroadside(leftCannons, true));

        if (Input.GetKeyDown(KeyCode.E) && canFireRight)
            StartCoroutine(FireBroadside(rightCannons, false));
    }

    System.Collections.IEnumerator FireBroadside(List<Cannon> cannons, bool left)
    {
        if (left) canFireLeft = false;
        else canFireRight = false;

        // Fire all (or stagger them slightly)
        var shuffled = cannons.OrderBy(_ => UnityEngine.Random.value).ToList();

        int batchSize = 3; // fire 3 cannons at once
        for (int i = 0; i < shuffled.Count; i += batchSize)
        {
            for (int j = i; j < i + batchSize && j < shuffled.Count; j++)
                shuffled[j].Fire();

            yield return new WaitForSeconds(Random.Range(0.05f, 0.12f));
        }

        yield return new WaitForSeconds(reloadTime);

        if (left) canFireLeft = true;
        else canFireRight = true;
    }
}
