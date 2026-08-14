// using UnityEngine;
// using System.Collections.Generic;

// public class ChunkVegetationRenderer {

//     // a "part" = one submesh + material combo, at a fixed local offset relative to prefab root
//     private struct Part {
//         public Mesh mesh;
//         public int submeshIndex;
//         public Material material;
//         public Matrix4x4 localMatrix; // this part's transform relative to prefab root
//     }

//     private Dictionary<GameObject, List<Part>> prefabPartsCache = new Dictionary<GameObject, List<Part>>();

//     // final draw batches, keyed by mesh+submesh+material so identical parts across instances batch together
//     private Dictionary<(Mesh, int, Material), List<Matrix4x4>> batches = new Dictionary<(Mesh, int, Material), List<Matrix4x4>>();

//     public void AddInstance(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) {
//         if (!prefabPartsCache.TryGetValue(prefab, out var parts)) {
//             parts = BuildParts(prefab);
//             prefabPartsCache[prefab] = parts;
//         }

//         Matrix4x4 baseMatrix = Matrix4x4.TRS(position, rotation, scale);

//         foreach (var part in parts) {
//             Matrix4x4 finalMatrix = baseMatrix * part.localMatrix;
//             var key = (part.mesh, part.submeshIndex, part.material);

//             if (!batches.TryGetValue(key, out var list)) {
//                 list = new List<Matrix4x4>();
//                 batches[key] = list;
//             }
//             list.Add(finalMatrix);
//         }
//     }

//     private List<Part> BuildParts(GameObject prefab) {
//         var parts = new List<Part>();
//         MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(); // ALL of them, not just first

//         foreach (MeshFilter mf in meshFilters) {
//             if (mf.sharedMesh == null) continue;
//             MeshRenderer mr = mf.GetComponent<MeshRenderer>();
//             if (mr == null) continue;

//             // transform of this child relative to the prefab root, so trunk/leaves keep their correct offset
//             Matrix4x4 localMatrix = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;

//             Material[] mats = mr.sharedMaterials; // handles multi-material renderers
//             for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++) {
//                 Material mat = sub < mats.Length ? mats[sub] : mats[mats.Length - 1];
//                 if (mat == null) continue;

//                 parts.Add(new Part {
//                     mesh = mf.sharedMesh,
//                     submeshIndex = sub,
//                     material = mat,
//                     localMatrix = localMatrix
//                 });
//             }
//         }

//         return parts;
//     }

//     public void Draw() {
//         foreach (var kvp in batches) {
//             var (mesh, submesh, mat) = kvp.Key;
//             List<Matrix4x4> matrices = kvp.Value;

//             for (int i = 0; i < matrices.Count; i += 1023) {
//                 int count = Mathf.Min(1023, matrices.Count - i);
//                 Matrix4x4[] batch = matrices.GetRange(i, count).ToArray();
//                 Graphics.DrawMeshInstanced(mesh, submesh, mat, batch);
//             }
//         }
//     }

//     public bool HasVegetation => batches.Count > 0;
// }




using UnityEngine;
using System.Collections.Generic;

public class ChunkVegetationRenderer {

    private struct Part {
        public Mesh mesh;
        public int submeshIndex;
        public Material material;
        public Matrix4x4 localMatrix;
    }

    private Dictionary<GameObject, List<Part>> prefabPartsCache = new Dictionary<GameObject, List<Part>>();
    private Dictionary<(Mesh, int, Material), List<Matrix4x4>> instanceLists = new Dictionary<(Mesh, int, Material), List<Matrix4x4>>();
    private Dictionary<(Mesh, int, Material), List<Matrix4x4[]>> bakedBatches = new Dictionary<(Mesh, int, Material), List<Matrix4x4[]>>();
    private bool isDirty = false;

    public void AddInstance(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) {
        if (!prefabPartsCache.TryGetValue(prefab, out var parts)) {
            parts = BuildParts(prefab);
            prefabPartsCache[prefab] = parts;

            if (parts.Count == 0) {
                Debug.LogWarning($"[ChunkVegetationRenderer] Prefab '{prefab.name}' generated 0 parts.");
            }
        }

        Matrix4x4 baseMatrix = Matrix4x4.TRS(position, rotation, scale);

        foreach (var part in parts) {
            // Using the native local matrix calculation completely resolves the distortion/scale bugs
            Matrix4x4 finalMatrix = baseMatrix * part.localMatrix;
            var key = (part.mesh, part.submeshIndex, part.material);

            if (!instanceLists.TryGetValue(key, out var list)) {
                list = new List<Matrix4x4>();
                instanceLists[key] = list;
            }
            list.Add(finalMatrix);
        }

        isDirty = true;
    }

    private List<Part> BuildParts(GameObject prefab) {
        var parts = new List<Part>();

        // WORKAROUND FOR SKETCHFAB HIERARCHIES:
        // To get perfectly accurate relative matrices without distortion, we temporarily 
        // snapshot the prefab root's transform, zero it out natively, grab the data, and restore it.
        Vector3 savedPos = prefab.transform.localPosition;
        Quaternion savedRot = prefab.transform.localRotation;
        Vector3 savedScale = prefab.transform.localScale;

        prefab.transform.localPosition = Vector3.zero;
        prefab.transform.localRotation = Quaternion.identity;
        //prefab.transform.localScale = Vector3.one;

        MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);

        foreach (MeshFilter mf in meshFilters) {
            if (mf.sharedMesh == null) continue;

            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null) continue;

            // This natively gets the perfect scale, rotation, and offset relative to the root 
            // without breaking on non-uniform scaling chains.
            Matrix4x4 localMatrix = mf.transform.localToWorldMatrix;

            for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++) {
                Material[] mats = mr.sharedMaterials;
                Material mat = sub < mats.Length ? mats[sub] : (mats.Length > 0 ? mats[mats.Length - 1] : null);

                if (mat == null || !mat.enableInstancing) {
                    string matName = mat != null ? mat.name : "NULL";
                    Debug.LogWarning($"[ChunkVegetationRenderer] Skipping submesh {sub} on '{mf.name}'. Material '{matName}' MUST have 'Enable GPU Instancing' checked!");
                    continue;
                }

                parts.Add(new Part {
                    mesh = mf.sharedMesh,
                    submeshIndex = sub,
                    material = mat,
                    localMatrix = localMatrix
                });
            }
        }

        // Restore the original prefab asset state safely
        prefab.transform.localPosition = savedPos;
        prefab.transform.localRotation = savedRot;
        //prefab.transform.localScale = savedScale;

        return parts;
    }

    public void BakeBatches() {
        if (!isDirty) return;

        bakedBatches.Clear();

        foreach (var kvp in instanceLists) {
            var key = kvp.Key;
            List<Matrix4x4> matrices = kvp.Value;
            List<Matrix4x4[]> chunkedArrays = new List<Matrix4x4[]>();

            for (int i = 0; i < matrices.Count; i += 1023) {
                int count = Mathf.Min(1023, matrices.Count - i);

                Matrix4x4[] batch = new Matrix4x4[count];
                matrices.CopyTo(i, batch, 0, count);
                chunkedArrays.Add(batch);
            }

            bakedBatches[key] = chunkedArrays;
        }

        isDirty = false;
    }

    public void Draw() {
        if (isDirty) BakeBatches();

        foreach (var kvp in bakedBatches) {
            var (mesh, submesh, mat) = kvp.Key;
            List<Matrix4x4[]> batches = kvp.Value;

            foreach (Matrix4x4[] batch in batches) {
                Graphics.DrawMeshInstanced(mesh, submesh, mat, batch);
            }
        }
    }

    // Sprosti vse zbrane primerke rastlinstva tega kosa.
    // Matrike so navadna polja v upravljanem pomnilniku, zato jih smetar pobere sam,
    // ko izgubijo zadnji kazalec nanje - dovolj je, da izpraznimo slovarje.
    public void Clear() {
        instanceLists.Clear();
        bakedBatches.Clear();
        prefabPartsCache.Clear();
        isDirty = false;
    }

    public bool HasVegetation => instanceLists.Count > 0;
}