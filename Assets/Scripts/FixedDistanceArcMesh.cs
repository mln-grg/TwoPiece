using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class FixedDistanceArcMesh : MonoBehaviour
{
    public float meshWidth = 1f;
    public float distance = 30f;
    public float height = 8f;
    public int resolution = 30;

    Mesh mesh;

    void Awake()
    {
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
    }

    public void BuildArc(Vector3 origin, Vector3 forward)
    {
        transform.position = origin;
        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        mesh.Clear();

        Vector3[] arcPoints = CalculateArcLocal();

        Vector3[] vertices = new Vector3[(resolution + 1) * 2];
        int[] triangles = new int[resolution * 12];

        for (int i = 0; i <= resolution; i++)
        {
            Vector3 p = arcPoints[i];

            // Tangent for correct ribbon orientation
            Vector3 tangent;
            if (i < resolution)
                tangent = (arcPoints[i + 1] - p).normalized;
            else
                tangent = (p - arcPoints[i - 1]).normalized;

            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;

            vertices[i * 2]     = p + right * (meshWidth * 0.5f);
            vertices[i * 2 + 1] = p - right * (meshWidth * 0.5f);

            if (i < resolution)
            {
                int t = i * 12;
                int v = i * 2;

                triangles[t + 0] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;

                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;

                triangles[t + 6]  = v;
                triangles[t + 7]  = v + 2;
                triangles[t + 8]  = v + 1;

                triangles[t + 9]  = v + 2;
                triangles[t + 10] = v + 3;
                triangles[t + 11] = v + 1;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
    }

    Vector3[] CalculateArcLocal()
    {
        Vector3[] points = new Vector3[resolution + 1];

        for (int i = 0; i <= resolution; i++)
        {
            float t = (float)i / resolution;

            float z = distance * t;
            float y = 4f * height * t * (1f - t); // parabola

            points[i] = new Vector3(0f, y, z);
        }

        return points;
    }
    
    public Vector3[] GetArcWorldPoints()
    {
        Vector3[] points = new Vector3[resolution + 1];

        for (int i = 0; i <= resolution; i++)
        {
            float t = (float)i / resolution;

            float z = distance * t;
            float y = 4f * height * t * (1f - t);

            Vector3 local = new Vector3(0f, y, z);
            points[i] = transform.TransformPoint(local);
        }

        return points;
    }
    
    public void GetArcSample(
        int index,
        out Vector3 position,
        out Vector3 right
    )
    {
        float t = (float)index / resolution;

        float z = distance * t;
        float y = 4f * height * t * (1f - t);

        Vector3 localPos = new Vector3(0f, y, z);
        position = transform.TransformPoint(localPos);

        // Tangent approximation
        float t2 = Mathf.Min(t + (1f / resolution), 1f);
        float z2 = distance * t2;
        float y2 = 4f * height * t2 * (1f - t2);

        Vector3 localNext = new Vector3(0f, y2, z2);
        Vector3 worldNext = transform.TransformPoint(localNext);

        Vector3 tangent = (worldNext - position).normalized;
        right = Vector3.Cross(Vector3.up, tangent).normalized;
    }
    
    
    public List<Vector3[]> SampleLanes(int laneCount)
    {
        List<Vector3[]> lanes = new();

        float halfWidth = meshWidth * 0.5f;

        for (int lane = 0; lane < laneCount; lane++)
        {
            float laneT = laneCount == 1
                ? 0.5f
                : (float)lane / (laneCount - 1);

            float lateralOffset = Mathf.Lerp(-halfWidth, halfWidth, laneT);

            Vector3[] points = new Vector3[resolution + 1];

            for (int i = 0; i <= resolution; i++)
            {
                float t = (float)i / resolution;

                float z = distance * t;
                float y = 4f * height * t * (1f - t);

                Vector3 local =
                    new Vector3(lateralOffset, y, z);

                points[i] = transform.TransformPoint(local);
            }

            lanes.Add(points);
        }

        return lanes;
    }
}