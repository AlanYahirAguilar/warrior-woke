using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Marks a standard parkour obstacle (Parkour Obstacle Standard, docs/arquitectura.md §5.13) and
/// checks that its geometry matches the standard of its type. It has no runtime logic: the player
/// still measures the real geometry with rays (EnvironmentChecker), so this component only tells
/// a designer whether the obstacle is what the parkour expects, and draws its contact points.
///
/// Convention: the pivot is on the floor, at the center of the face the player approaches, and the
/// obstacle extends along the local +Z (the direction of the approach). Width is local X.
/// The prefabs (Assets/Prefabs/Parkour) are generated from ParkourStandard by
/// Tools → Warrior Woke → Generar Prefabs de Obstáculos.
/// </summary>
[DisallowMultipleComponent]
public class ParkourObstacle : MonoBehaviour
{
    [SerializeField] private ParkourObstacleType type;

    public ParkourObstacleType Type => type;

#if UNITY_EDITOR
    /// <summary>Used by the prefab generator.</summary>
    public void EditorSetType(ParkourObstacleType value) => type = value;
#endif

    // ─── Measurement ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Local bounds (relative to the pivot) of the colliders that belong to this obstacle, without
    /// nested obstacles. Works in edit mode and on prefab assets (it reads the box shapes, not the
    /// physics scene).
    /// </summary>
    public int CollectParts(List<Bounds> parts)
    {
        parts.Clear();
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            if (c.GetComponentInParent<ParkourObstacle>(true) != this) continue;
            parts.Add(LocalBounds(c));
        }
        return parts.Count;
    }

    private Bounds LocalBounds(Collider c)
    {
        Vector3 center, half;
        if (c is BoxCollider box) { center = box.center; half = box.size * 0.5f; }
        else
        {
            // Other shapes: their world AABB, good enough for a check
            Bounds w = c.bounds;
            var b = new Bounds(transform.InverseTransformPoint(w.center), Vector3.zero);
            b.Encapsulate(transform.InverseTransformPoint(w.min));
            b.Encapsulate(transform.InverseTransformPoint(w.max));
            return b;
        }

        Transform t = c.transform;
        var bounds = new Bounds(transform.InverseTransformPoint(t.TransformPoint(center)), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
            bounds.Encapsulate(transform.InverseTransformPoint(t.TransformPoint(center + corner)));
        }
        return bounds;
    }

    /// <summary>
    /// Measured size in the terms of the standard: height (clearance for a slide bar, platform
    /// height for a jump gap), depth (bar depth / gap) and width. False if there is no geometry.
    /// </summary>
    public bool TryMeasure(out float height, out float depth, out float width)
    {
        height = depth = width = 0f;
        var parts = new List<Bounds>();
        if (CollectParts(parts) == 0) return false;

        Bounds all = parts[0];
        foreach (Bounds b in parts) all.Encapsulate(b);

        switch (type)
        {
            case ParkourObstacleType.SlideBar:
            {
                // The bar is the part that does not touch the floor; the posts stand outside the passage
                float clearance = float.MaxValue;
                foreach (Bounds b in parts)
                {
                    if (b.min.y <= ParkourStandard.Tolerance) continue;
                    if (b.min.y < clearance) { clearance = b.min.y; depth = b.size.z; width = b.size.x; }
                }
                if (clearance == float.MaxValue) return false;
                height = clearance;
                return true;
            }
            case ParkourObstacleType.JumpGap:
            {
                // Two platforms: the gap is the free floor between them along +Z
                if (parts.Count < 2) return false;
                parts.Sort((a, b) => a.min.z.CompareTo(b.min.z));
                height = parts[0].max.y;
                depth  = parts[1].min.z - parts[0].max.z;
                width  = Mathf.Min(parts[0].size.x, parts[1].size.x);
                return true;
            }
            default:
                height = all.max.y;
                depth  = all.size.z;
                width  = all.size.x;
                return true;
        }
    }

    // ─── Validation ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks the obstacle against the standard of its type. Each problem is added to
    /// <paramref name="issues"/> as a readable sentence. Returns true if it complies.
    /// </summary>
    public bool Validate(List<string> issues)
    {
        int before = issues.Count;
        string name = $"'{gameObject.name}' ({ParkourStandard.Label(type)})";

        if (Vector3.Angle(transform.up, Vector3.up) > 1f)
            issues.Add($"{name}: está inclinado; solo puede girar sobre el eje vertical.");

        if (type == ParkourObstacleType.Combined)
        {
            ValidateCombined(name, issues);
            return issues.Count == before;
        }

        var parts = new List<Bounds>();
        if (CollectParts(parts) == 0)
        {
            issues.Add($"{name}: no tiene colliders.");
            return false;
        }

        ParkourObstacleSpec spec = ParkourStandard.Spec(type);
        float tol = ParkourStandard.Tolerance;

        // Pivot on the floor, at the approach face
        Bounds all = parts[0];
        foreach (Bounds b in parts) all.Encapsulate(b);
        if (Mathf.Abs(all.min.y) > tol)
            issues.Add($"{name}: la base está a {all.min.y:F2} m del pivote; el pivote va en el suelo.");
        if (Mathf.Abs(all.min.z) > tol)
            issues.Add($"{name}: la cara de aproximación está a {all.min.z:F2} m del pivote; el pivote va en esa cara (Z local = 0).");

        if (!TryMeasure(out float height, out float depth, out float width))
        {
            issues.Add($"{name}: no se pudo medir su geometría.");
            return false;
        }

        string what = type == ParkourObstacleType.SlideBar ? "Paso bajo la barra" : type == ParkourObstacleType.JumpGap ? "Altura de las plataformas" : "Altura";
        string deep = type == ParkourObstacleType.JumpGap ? "Hueco" : "Fondo";
        if (height < spec.MinHeight - tol || height > spec.MaxHeight + tol)
            issues.Add($"{name}: {what} {height:F2} m fuera del rango {spec.MinHeight:F2}–{spec.MaxHeight:F2} m (estándar {spec.Height:F2}).");
        if (depth < spec.MinDepth - tol || depth > spec.MaxDepth + tol)
            issues.Add($"{name}: {deep} {depth:F2} m fuera del rango {spec.MinDepth:F2}–{Max(spec.MaxDepth)} (estándar {spec.Depth:F2}).");
        if (width < spec.MinWidth - tol)
            issues.Add($"{name}: ancho {width:F2} m menor que el mínimo {spec.MinWidth:F2} m.");

        int layer = LayerMask.NameToLayer(spec.Layer);
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            if (c.GetComponentInParent<ParkourObstacle>(true) != this || c.gameObject.layer == layer) continue;
            issues.Add($"{name}: '{c.name}' está en la layer '{LayerMask.LayerToName(c.gameObject.layer)}'; debe estar en '{spec.Layer}'.");
        }
        return issues.Count == before;
    }

    private static string Max(float value) => value >= 100f ? "sin límite" : $"{value:F2} m";

    /// <summary>A course of standard obstacles: each one valid, and enough floor between two actions.</summary>
    private void ValidateCombined(string name, List<string> issues)
    {
        var children = new List<ParkourObstacle>();
        foreach (ParkourObstacle o in GetComponentsInChildren<ParkourObstacle>(true))
            if (o != this && o.transform.parent == transform) children.Add(o);
        if (children.Count < 2)
        {
            issues.Add($"{name}: un recorrido combinado necesita al menos dos obstáculos estándar como hijos.");
            return;
        }

        children.Sort((a, b) => a.transform.localPosition.z.CompareTo(b.transform.localPosition.z));
        var parts = new List<Bounds>();
        float previousBack = float.MinValue;
        string previousName = null;
        foreach (ParkourObstacle o in children)
        {
            o.Validate(issues);
            float front = o.transform.localPosition.z;
            float back  = front;
            if (o.CollectParts(parts) > 0)
            {
                foreach (Bounds b in parts)
                    back = Mathf.Max(back, transform.InverseTransformPoint(o.transform.TransformPoint(b.max)).z,
                                           transform.InverseTransformPoint(o.transform.TransformPoint(b.min)).z);
            }
            bool action = o.Type != ParkourObstacleType.Step;
            if (action && previousName != null && front - previousBack < ParkourStandard.ChainSpacing - ParkourStandard.Tolerance)
                issues.Add($"{name}: entre '{previousName}' y '{o.name}' hay {front - previousBack:F2} m; el mínimo entre acciones es {ParkourStandard.ChainSpacing:F1} m.");
            if (action) { previousBack = back; previousName = o.name; }
        }
    }

    // ─── Contact points (derived from the standard) ──────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (type == ParkourObstacleType.Combined) return; // each child draws its own
        if (!TryMeasure(out float height, out float depth, out float width)) return;

        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 labelAt = new Vector3(0f, height + 0.4f, 0f);

        if (ParkourStandard.IsVault(type))
        {
            // Detection: knee ray within reach, and the valid height band on the face
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(new Vector3(0f, ParkourStandard.VaultKneeRay, -ParkourStandard.VaultReach), new Vector3(0f, ParkourStandard.VaultKneeRay, 0f));
            DrawBand(ParkourStandard.VaultMinHeight, ParkourStandard.VaultMaxHeight, width, 0f);
            // Start: where the run-up reaches the hand point at the clip's pace
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(new Vector3(0f, 0.05f, -(ParkourTimings.VaultClipHandReach - ParkourTimings.VaultHandInset)), 0.12f);
            // Hand: left of the body line, just past the edge
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(new Vector3(-ParkourTimings.VaultHandLateral, height, ParkourTimings.VaultHandInset), 0.06f);
            // Landing: where the clip lands, and the closest landing accepted
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(new Vector3(0f, 0.05f, depth + ParkourTimings.VaultClipLandDistance), 0.15f);
            Gizmos.DrawLine(new Vector3(-0.3f, 0.02f, depth + ParkourStandard.VaultMinLanding), new Vector3(0.3f, 0.02f, depth + ParkourStandard.VaultMinLanding));
        }
        else if (ParkourStandard.IsLedge(type))
        {
            // Detection: chest and head rays, and the band grabbed from the ground
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(new Vector3(0f, ParkourStandard.LedgeChestRay, -ParkourStandard.LedgeReachGround), new Vector3(0f, ParkourStandard.LedgeChestRay, 0f));
            Gizmos.DrawLine(new Vector3(0f, ParkourStandard.LedgeHeadRay, -ParkourStandard.LedgeReachGround), new Vector3(0f, ParkourStandard.LedgeHeadRay, 0f));
            DrawBand(ParkourStandard.LedgeGroundMinRise, ParkourStandard.LedgeGroundMaxRise, width, 0f);
            // Start: within reach of the wall
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(new Vector3(0f, 0.05f, -ParkourStandard.LedgeReachGround * 0.75f), 0.12f);
            // Grab: both wrists on the edge
            Gizmos.color = Color.yellow;
            foreach (float side in new[] { -1f, 1f })
                Gizmos.DrawSphere(new Vector3(side * ParkourTimings.HandLateral, height + ParkourTimings.WristAboveSurface, -ParkourTimings.WristOutOfFace), 0.06f);
            // Stand: where the feet end after the climb
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(new Vector3(0f, height + 0.05f, ParkourStandard.LedgeStandInset), 0.15f);
        }
        else if (type == ParkourObstacleType.Mantle)
        {
            // Detection: low and high face rays, and the band climbed onto
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(new Vector3(0f, ParkourStandard.MantleLowRay, -ParkourStandard.MantleReach), new Vector3(0f, ParkourStandard.MantleLowRay, 0f));
            Gizmos.DrawLine(new Vector3(0f, ParkourStandard.MantleHighRay, -ParkourStandard.MantleReach), new Vector3(0f, ParkourStandard.MantleHighRay, 0f));
            DrawBand(ParkourStandard.MantleMinRise, ParkourStandard.MantleMaxRise, width, 0f);
            // Start, supporting hand on the top, and where the feet end
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(new Vector3(0f, 0.05f, -ParkourStandard.MantleReach * 0.6f), 0.12f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(new Vector3(-ParkourTimings.HandLateral, height + ParkourTimings.WristAboveSurface, ParkourTimings.VaultHandInset), 0.06f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(new Vector3(0f, height + 0.05f, ParkourStandard.MantleStandInset), 0.15f);
        }
        else if (type == ParkourObstacleType.SlideBar)
        {
            // Press C before the entry point; the sliding body passes under the bar
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(new Vector3(0f, 0.05f, -ParkourStandard.SlideEntryDistance), 0.12f);
            Gizmos.color = Color.cyan;
            DrawBand(ParkourStandard.Spec(type).MinHeight, height, width, 0f);
            labelAt.y = height + ParkourStandard.SlideBarThickness + 0.4f;
        }
        else if (type == ParkourObstacleType.JumpGap)
        {
            // Take-off edge and landing zone
            float takeoff = ParkourStandard.JumpPlatformDepth;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(new Vector3(-width * 0.5f, height + 0.02f, takeoff), new Vector3(width * 0.5f, height + 0.02f, takeoff));
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(new Vector3(0f, height + 0.05f, takeoff + depth + 1f), 0.15f);
        }

        Gizmos.matrix = previous;
        Handles.color = Color.white;
        var issues = new List<string>();
        bool ok = Validate(issues);
        Handles.Label(transform.TransformPoint(labelAt), $"{ParkourStandard.Label(type)} · {height:F2} m · {(ok ? "cumple el estándar" : "NO cumple: ver Inspector")}");
    }

    /// <summary>Two lines on the face plane at the limits of a valid height band.</summary>
    private static void DrawBand(float min, float max, float width, float z)
    {
        float w = width * 0.5f;
        Gizmos.DrawLine(new Vector3(-w, min, z), new Vector3(w, min, z));
        Gizmos.DrawLine(new Vector3(-w, max, z), new Vector3(w, max, z));
    }
#endif
}
