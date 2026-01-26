using System.Collections.Generic;
using UnityEngine;

public class TrajectoryPreview : MonoBehaviour
{
    [Header("Visual")]
    public LineRenderer linePrefab;
    public int pointsPerLine = 30;
    public float timeStep = 0.1f;

    [Header("Arc")]
    public float gravityScale = 1.3f;
    public int sampleCount = 5;          // how wide the fan is
    public float horizontalSpread = 6f;  // degrees

    List<LineRenderer> lines = new();

    void Awake()
    {
        for (int i = 0; i < sampleCount; i++)
        {
            var lr = Instantiate(linePrefab, transform);
            lr.enabled = false;
            lines.Add(lr);
        }
    }

    public void Show(
        Vector3 origin,
        Vector3 baseDirection,
        float speed,
        float verticalAngle
    )
    {
        for (int i = 0; i < lines.Count; i++)
        {
            float t = lines.Count == 1
                ? 0
                : (float)i / (lines.Count - 1);

            float yaw =
                Mathf.Lerp(-horizontalSpread, horizontalSpread, t);

            Quaternion spread =
                Quaternion.Euler(verticalAngle, yaw, 0f);

            Vector3 dir = spread * baseDirection;

            DrawLine(lines[i], origin, dir * speed);
        }
    }

    public void Hide()
    {
        foreach (var lr in lines)
            lr.enabled = false;
    }

    void DrawLine(LineRenderer lr, Vector3 startPos, Vector3 velocity)
    {
        lr.enabled = true;
        lr.positionCount = pointsPerLine;

        Vector3 pos = startPos;
        Vector3 vel = velocity;

        for (int i = 0; i < pointsPerLine; i++)
        {
            lr.SetPosition(i, pos);

            vel += Physics.gravity * gravityScale * timeStep;
            pos += vel * timeStep;
        }
    }
}
