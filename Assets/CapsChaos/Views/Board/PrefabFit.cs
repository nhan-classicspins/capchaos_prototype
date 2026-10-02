using System.Collections.Generic;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// How to drop an authored model prefab (any scale, any pivot) into the board's units: measured once per prefab from
    /// its meshes' bounds in the prefab's own space — no instance is drawn to measure it.
    /// </summary>
    public static class PrefabFit
    {
        private static readonly Dictionary<GameObject, Bounds> Cache = new Dictionary<GameObject, Bounds>();

        /// <summary>The bounds of every mesh under <paramref name="prefab"/>, in the prefab root's space (empty if none).</summary>
        public static Bounds BoundsOf(GameObject prefab)
        {
            if (Cache.TryGetValue(prefab, out var b)) return b;
            var toRoot = prefab.transform.worldToLocalMatrix;
            bool any = false;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = toRoot * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            Cache[prefab] = b;
            return b;
        }

        /// <summary>The uniform scale and the offset that make <paramref name="prefab"/> <paramref name="footprint"/> wide (its
        /// widest of x / z), centred over the origin and standing on y = 0.</summary>
        public static (float scale, Vector3 offset) Footprint(GameObject prefab, float footprint)
        {
            var b = BoundsOf(prefab);
            float wide = Mathf.Max(b.size.x, b.size.z);
            if (wide < 1e-5f) return (1f, Vector3.zero);
            float scale = footprint / wide;
            return (scale, new Vector3(-b.center.x, -b.min.y, -b.center.z) * scale);
        }
    }
}
