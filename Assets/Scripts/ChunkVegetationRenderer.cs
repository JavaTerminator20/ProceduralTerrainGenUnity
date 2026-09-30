using UnityEngine;
using System.Collections.Generic;

public class ChunkVegetationRenderer {

    private struct Part {
        public Mesh mesh;
        public int submeshIndex;
        public Material material;
        public Matrix4x4 localMatrix;
    }

    // kljuc je prefab, vrednost pa je lista vseh delov tega prefaba (mesh, submesh, material, lokalna matrika) - pove kako je sestavljen prefab
    private Dictionary<GameObject, List<Part>> prefabPartsCache = new Dictionary<GameObject, List<Part>>();
    // vse matrike za isto mesh-submesh-material kombinacijo shranimo v seznam, value je seznam vseh transformacijihskih matrik za vse instance tega dela prefaba
    private Dictionary<(Mesh, int, Material), List<Matrix4x4>> instanceLists = new Dictionary<(Mesh, int, Material), List<Matrix4x4>>();
    // iste matrike kot zgoraj, ampak so razdeljene v batch-e po 1023 (seznam seznamov matrik po najvec 1023 elementov)
    private Dictionary<(Mesh, int, Material), List<Matrix4x4[]>> bakedBatches = new Dictionary<(Mesh, int, Material), List<Matrix4x4[]>>();

    private bool isDirty = false;   // postavi jo AddInstance, da vemo, da je treba ponovno izracunati bakedBatches (ki jo pocisti) - "od zadnjega bake-a je bilo dodanih novih instanc"


    // klice se enkrat za vsako rastlino (v SapwnVegetation() v EndlessTerrain.cs)
    public void AddInstance(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale) {
        // TryGetValue v enem koraku pove, ali key obstaja in vrne vrednost - ce najde key, vrne true in shrani vrednost v out parametru
        // BuildParts se klice samo prvic za vsak prefab in se info shrani v prefabPartCache
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
            Matrix4x4 finalMatrix = baseMatrix * part.localMatrix;      //part.localMatrix postavi del na pravo mesto znotraj drevesa (listje nad deblo), baseMatrix pa celotno drevo postavti v svet na pravo mesto
            var key = (part.mesh, part.submeshIndex, part.material);

            if (!instanceLists.TryGetValue(key, out var list)) {
                list = new List<Matrix4x4>();
                instanceLists[key] = list;
            }
            list.Add(finalMatrix);
        }

        isDirty = true;
    }

    // iz prefab-a, ki je hierarhija objektov, dobiti raven seznam objektov Part
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

        // vsakic ko se klice AddInstance, pomeni da se je nalozil nek nov chunk z novim rastlinstom, zato je treba se enkrat izracunati 
        // bakedBatches, ki jih potem Draw() uporablja za risanje vseh instanc. Ko se igralec ne premika, se ne klice BakedBatches() - pohitritev
        bakedBatches.Clear();

        //iteriramo po vseh instancah - vsaka kombinacija mesh-submeshIndex-material
        foreach (var kvp in instanceLists) {
            var key = kvp.Key;
            List<Matrix4x4> matrices = kvp.Value;
            List<Matrix4x4[]> chunkedArrays = new List<Matrix4x4[]>();  // seznam, kjer se bodo nabirala razrezana polja matri po najvec 1023 elementov iz matrices

            for (int i = 0; i < matrices.Count; i += 1023) {
                int count = Mathf.Min(1023, matrices.Count - i);        // stevilo elementov v zadnjem batch-u bo lahko manj kot 1023, zato vzamemo minimum med 1023 in preostalim stevilom elementov

                Matrix4x4[] batch = new Matrix4x4[count];               // CopyTo skopira cel blok matrik naenkrat
                matrices.CopyTo(i, batch, 0, count);                    // 1. parameter od kod v seznamu beremo, 2. kam pisemo, 3. od kod v ciljnem polju pisemo, 4. koliko elementov kopiramo
                chunkedArrays.Add(batch);                               // gotovo polje dodamo v seznam razrezanih polj, ki jih bomo potem shranili v bakedBatches
            }

            bakedBatches[key] = chunkedArrays;                          // shranimo seznam razrezanih polj v bakedBatches
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