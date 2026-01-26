using System.Collections.Generic;
using UnityEngine;

public class TrajectoryPreview : MonoBehaviour
{
    public LineRenderer line;

    [Header("Arc Shape")]
    public int points = 40;
    public float gravity = 18f;
    public float forwardSpeed = 25f;

    public void ShowArc(
        Vector3 origin,
        Vector3 direction,
        float apexHeight
    )
    {
        line.enabled = true;
        line.positionCount = points;

        // Compute vertical launch velocity needed to reach apex
        float verticalVelocity = Mathf.Sqrt(2f * gravity * apexHeight);

        float timeStep = 0.1f;

        for (int i = 0; i < points; i++)
        {
            float t = i * timeStep;

            Vector3 horizontal =
                direction.normalized * forwardSpeed * t;

            float vertical =
                verticalVelocity * t - 0.5f * gravity * t * t;

            Vector3 pos = origin + horizontal;
            pos.y += vertical;

            line.SetPosition(i, pos);
        }
    }

    public void Hide()
    {
        line.enabled = false;
    }
}
