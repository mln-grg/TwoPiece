using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class HeightmapMeshGenerator : EditorWindow
{
    private Texture2D _heightmap;
    private float _sizeScale      = 10f;
    private float _heightScale    = 2f;
    private float _threshold      = 0.05f;
    private int   _resDivisor     = 4;
    private bool  _placeInScene   = true;

    // Island masking
    private Color _waterColor     = new Color(0.30f, 0.45f, 0.52f);  // default ocean teal
    private float _colorTolerance = 0.25f;

    private string _savePath = "Assets/GeneratedMeshes";
    private string _meshName = "HeightmapMesh";

    private Vector2 _scrollPos;

    [MenuItem("Tools/Heightmap Mesh Generator")]
    public static void Open()
    {
        var window = GetWindow<HeightmapMeshGenerator>("Heightmap Mesh Generator");
        window.minSize = new Vector2(340f, 500f);
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Heightmap Mesh Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        DrawTextureSection();
        EditorGUILayout.Space(8);
        DrawMaskSection();
        EditorGUILayout.Space(8);
        DrawParametersSection();
        EditorGUILayout.Space(8);
        DrawOutputSection();
        EditorGUILayout.Space(12);
        DrawGenerateButton();

        EditorGUILayout.EndScrollView();
    }

    // -------------------------------------------------------------------------
    // UI sections
    // -------------------------------------------------------------------------

    private void DrawTextureSection()
    {
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _heightmap = (Texture2D)EditorGUILayout.ObjectField("Heightmap", _heightmap, typeof(Texture2D), false);

            if (_heightmap != null)
            {
                bool readable = IsTextureReadable(_heightmap);
                int sampledW  = Mathf.Max(2, _heightmap.width  / Mathf.Max(1, _resDivisor));
                int sampledH  = Mathf.Max(2, _heightmap.height / Mathf.Max(1, _resDivisor));

                EditorGUILayout.LabelField(
                    $"Original: {_heightmap.width}×{_heightmap.height}  →  Sampled: {sampledW}×{sampledH}",
                    EditorStyles.miniLabel);

                if (!readable)
                    EditorGUILayout.HelpBox("Texture is not readable. Enable 'Read/Write' in its import settings.", MessageType.Error);
            }
        }
    }

    private void DrawMaskSection()
    {
        EditorGUILayout.LabelField("Island Mask", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _waterColor     = EditorGUILayout.ColorField(
                new GUIContent("Water Color", "Pick the ocean/water colour from your map. Pixels close to this colour will be excluded from the mesh."),
                _waterColor);
            _colorTolerance = EditorGUILayout.Slider(
                new GUIContent("Color Tolerance", "How far a pixel's colour can be from the Water Color and still be treated as water (0–1 in linear RGB distance)."),
                _colorTolerance, 0f, 1f);

            EditorGUILayout.HelpBox("Tip: use the colour picker eyedropper to sample the ocean directly from your texture preview.", MessageType.None);
        }
    }

    private void DrawParametersSection()
    {
        EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _sizeScale   = EditorGUILayout.FloatField(
                new GUIContent("Size Scale",         "World-space width and depth of the mesh."), _sizeScale);
            _heightScale = EditorGUILayout.FloatField(
                new GUIContent("Height Scale",       "Multiplier applied to pixel brightness for vertex Y."), _heightScale);
            _threshold   = EditorGUILayout.Slider(
                new GUIContent("Height Threshold",   "Pixels with brightness below this are clamped to Y=0."), _threshold, 0f, 1f);
            _resDivisor  = EditorGUILayout.IntSlider(
                new GUIContent("Resolution Divisor", "Sample every Nth pixel. 1=full res, 4=quarter res."), _resDivisor, 1, 16);

            _sizeScale   = Mathf.Max(0.01f, _sizeScale);
            _heightScale = Mathf.Max(0f,    _heightScale);
        }
    }

    private void DrawOutputSection()
    {
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _meshName = EditorGUILayout.TextField("Mesh Name", _meshName);
            using (new EditorGUILayout.HorizontalScope())
            {
                _savePath = EditorGUILayout.TextField("Save Path", _savePath);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    string chosen = EditorUtility.OpenFolderPanel("Choose Save Folder", "Assets", "");
                    if (!string.IsNullOrEmpty(chosen))
                    {
                        if (chosen.StartsWith(Application.dataPath))
                            _savePath = "Assets" + chosen.Substring(Application.dataPath.Length);
                        else
                            EditorUtility.DisplayDialog("Invalid Path", "Please choose a folder inside your Assets directory.", "OK");
                    }
                }
            }

            _placeInScene = EditorGUILayout.Toggle(
                new GUIContent("Place in Scene", "Spawn a preview GameObject in the active scene after saving."),
                _placeInScene);

            if (!_savePath.StartsWith("Assets"))
                EditorGUILayout.HelpBox("Save path must be inside the Assets folder.", MessageType.Warning);
        }
    }

    private void DrawGenerateButton()
    {
        bool canGenerate = _heightmap != null
                        && IsTextureReadable(_heightmap)
                        && !string.IsNullOrWhiteSpace(_meshName)
                        && _savePath.StartsWith("Assets");

        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            if (GUILayout.Button("Generate & Save Mesh", GUILayout.Height(36)))
                GenerateAndSave();
        }

        if (_heightmap == null)
            EditorGUILayout.HelpBox("Assign a heightmap texture to get started.", MessageType.Info);
    }

    // -------------------------------------------------------------------------
    // Generation + save
    // -------------------------------------------------------------------------

    private void GenerateAndSave()
    {
        Mesh mesh = BuildMesh(_heightmap, _sizeScale, _heightScale, _threshold, _resDivisor, _waterColor, _colorTolerance);
        if (mesh == null || mesh.vertexCount == 0)
        {
            EditorUtility.DisplayDialog("Error", "No land pixels were found with the current Water Color and Tolerance settings. Try adjusting them.", "OK");
            return;
        }

        if (!AssetDatabase.IsValidFolder(_savePath))
        {
            string parent = Path.GetDirectoryName(_savePath).Replace('\\', '/');
            string folder = Path.GetFileName(_savePath);
            AssetDatabase.CreateFolder(parent, folder);
        }

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{_savePath}/{_meshName}.asset");
        AssetDatabase.CreateAsset(mesh, assetPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[HeightmapMeshGenerator] Saved → {assetPath}  ({mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris)");

        if (_placeInScene)
            SpawnPreviewObject(mesh);

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = mesh;
    }

    private static void SpawnPreviewObject(Mesh mesh)
    {
        var go = new GameObject(mesh.name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var mr     = go.AddComponent<MeshRenderer>();
        var shader = Shader.Find("HDRP/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        mr.sharedMaterial = new Material(shader) { name = "HeightmapPreview" };

        SceneView sv = SceneView.lastActiveSceneView;
        if (sv != null)
            go.transform.position = sv.pivot - new Vector3(mesh.bounds.size.x * 0.5f, 0f, mesh.bounds.size.z * 0.5f);

        Undo.RegisterCreatedObjectUndo(go, "Place Heightmap Mesh");
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    // -------------------------------------------------------------------------
    // Mesh builder
    // -------------------------------------------------------------------------

    private static Mesh BuildMesh(
        Texture2D tex,
        float sizeScale, float heightScale, float heightThreshold,
        int resDivisor,
        Color waterColor, float colorTolerance)
    {
        int srcW = tex.width;
        int srcH = tex.height;
        int step = Mathf.Max(1, resDivisor);

        int w = Mathf.Max(2, srcW / step);
        int h = Mathf.Max(2, srcH / step);

        Color[] pixels = tex.GetPixels(0);

        float stepX = sizeScale / (w - 1);
        float stepZ = sizeScale / (h - 1);

        // --- 1. Classify every grid point as land or water ---
        bool[] isLand = new bool[w * h];
        for (int gz = 0; gz < h; gz++)
        {
            int srcZ = Mathf.Min(gz * step, srcH - 1);
            for (int gx = 0; gx < w; gx++)
            {
                int srcX = Mathf.Min(gx * step, srcW - 1);
                Color c  = pixels[srcZ * srcW + srcX];
                isLand[gz * w + gx] = ColorDistance(c, waterColor) > colorTolerance;
            }
        }

        // --- 2. Collect only triangles where all 3 corners are land ---
        //        A quad is emitted as 2 triangles; each triangle is only included
        //        when every one of its 3 grid-point indices is land.
        var triIndices = new List<int>();

        for (int gz = 0; gz < h - 1; gz++)
        {
            for (int gx = 0; gx < w - 1; gx++)
            {
                int bl = gz * w + gx;
                int br = bl + 1;
                int tl = bl + w;
                int tr = tl + 1;

                // Lower-left triangle
                if (isLand[bl] && isLand[tl] && isLand[tr])
                {
                    triIndices.Add(bl);
                    triIndices.Add(tl);
                    triIndices.Add(tr);
                }
                // Upper-right triangle
                if (isLand[bl] && isLand[tr] && isLand[br])
                {
                    triIndices.Add(bl);
                    triIndices.Add(tr);
                    triIndices.Add(br);
                }
            }
        }

        if (triIndices.Count == 0)
            return null;

        // --- 3. Build a compact vertex list (only land points that appear in tris) ---
        int[] remap = new int[w * h];
        for (int i = 0; i < remap.Length; i++) remap[i] = -1;

        // Mark vertices actually referenced
        foreach (int idx in triIndices)
            remap[idx] = 0;   // placeholder – will get real index below

        int vertCount = 0;
        for (int i = 0; i < remap.Length; i++)
            if (remap[i] == 0) remap[i] = vertCount++;

        var vertices = new Vector3[vertCount];
        var uvs      = new Vector2[vertCount];

        for (int gz = 0; gz < h; gz++)
        {
            int srcZ = Mathf.Min(gz * step, srcH - 1);
            for (int gx = 0; gx < w; gx++)
            {
                int gridIdx = gz * w + gx;
                if (remap[gridIdx] < 0) continue;

                int srcX     = Mathf.Min(gx * step, srcW - 1);
                float bright = pixels[srcZ * srcW + srcX].grayscale;
                float y      = bright >= heightThreshold ? bright * heightScale : 0f;

                int vi = remap[gridIdx];
                vertices[vi] = new Vector3(gx * stepX, y, gz * stepZ);
                uvs[vi]      = new Vector2((float)gx / (w - 1), (float)gz / (h - 1));
            }
        }

        // Remap triangle indices to compact vertex list
        var triangles = new int[triIndices.Count];
        for (int i = 0; i < triIndices.Count; i++)
            triangles[i] = remap[triIndices[i]];

        // --- 4. Assemble mesh ---
        var mesh = new Mesh { name = "HeightmapMesh" };

        if (vertCount > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.Optimize();

        return mesh;
    }

    // Euclidean RGB distance normalised to [0, 1]
    private static float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db) / 1.732f;   // 1.732 = sqrt(3)
    }

    private static bool IsTextureReadable(Texture2D tex)
    {
        try   { tex.GetPixel(0, 0); return true; }
        catch { return false; }
    }
}
