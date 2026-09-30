using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UIElements;

// mora biti ExecuteAlways, ker drugace:
// In Unity, if a script does not have [ExecuteAlways], you cannot safely instantiate objects, alter their hierarchy, 
// or manipulate their active state in certain ways during Edit Mode
[ExecuteAlways]
public class EndlessTerrain : MonoBehaviour {

    const float viewMoveThresholdForChunkUpdate = 25f;  // to je razdalja, ki jo mora igralec prehoditi, da se bo update-ala vidnost chunkov (da ne bomo vsak frame preverjali, kateri chunk-i so v maxViewDst, ampak samo, ce igralec prehodi dovolj veliko razdaljo)
    const float sqrViewMoveThresholdForChunkUpdate = viewMoveThresholdForChunkUpdate * viewMoveThresholdForChunkUpdate;  // ker bomo primerjali kvadrat razdalje, da ne bomo delali korenjenja, moramo tudi to vrednost kvadrirati

    public LODInfo[] detailLevels;
    public static float maxViewDst;

    public Transform viewer;                        // to je igralec, ki se premika po svetu
    public Material mapMaterial;

    public static Vector2 viewerPosition;           // to je pozicija igralca, ki se premika po svetu, ampak v 2D (x,z), ker nas ne zanima y koordinata
    Vector2 viewerPositionOld;                      // to je pozicija igralca v prejšnjem update-u
    public static MapGenerator mapGenerator;
    int chunkSize;                                  // velikost chunka (v enotah), ki ga bomo prikazovali
    int chunksVisibleInViewDst;                     // koliko chunkov lahko vidimo glede na maxViewDst

    public static Dictionary<Vector2, TerrainChunk> terrainChunkDictionary = new Dictionary<Vector2, TerrainChunk>();  // slovar, kjer je kljuc koordinata chunka, vrednost pa sam chunk (zato da se vsak chunk ustvari samo enkrat, ko ga prvič vidimo, nato pa ga samo prikazujemo in skrivamo glede na to, ali je v maxViewDst)
    public static List<TerrainChunk> terrainChunksVisibleLastUpdate = new List<TerrainChunk>();  // seznam chunkov, ki so bili vidni v zadnjem update-u

    static List<Vector2> chunksToUnload = new List<Vector2>();  // zacasni seznam koordinat chunkov za sprostitev (ponovno uporabljen vsak update, da ne alociramo novega seznama vsakic)
    int chunksLoadedInMemoryDst;                                // koliko chunkov dalec od opazovalca se chunk se obdrzi v pomnilniku (v enotah chunkov)

    float chunkUnloadDstMultiplier;
    int maxChunkUnloadsPerUpdate;


    // separate Init() method to get some vars initiated in editor as well, not just in Start() (start doesn't get called when in Editor)
    void Init() {
        mapGenerator = GetComponent<MapGenerator>();                            // if on same GameObject

        maxViewDst = detailLevels[detailLevels.Length - 1].visibleDstThreshold; // maxViewDst je enak vidni razdalji zadnjega LOD nivoja
        chunkSize = MapGenerator.mapChunkSize - 1;                              // ker je stevilo vozlisc v chunku mapChunkSize, je dejanska velikost chunka (mapChunkSize-1)
        chunksVisibleInViewDst = Mathf.RoundToInt(maxViewDst / chunkSize);      // koliko chunkov lahko vidimo glede na maxViewDst


        chunkUnloadDstMultiplier = mapGenerator.chunkUnloadDstMultiplier;
        maxChunkUnloadsPerUpdate = mapGenerator.maxChunkUnloadsPerUpdate;

        // prag za sprostitev iz pomnilnika, izrazen v enotah chunkov (da lahko primerjamo cela stevila namesto razdalj)
        // Mathf.Max poskrbi, da je prag vedno vsaj za en chunk vecji od vidnega polja, sicer bi sproscali se vidne chunke
        chunksLoadedInMemoryDst = Mathf.Max(
            chunksVisibleInViewDst + 1,
            Mathf.CeilToInt(chunksVisibleInViewDst * chunkUnloadDstMultiplier)
        );

        // pravilno sprostimo vse chunke iz prejsnjega zagona (mreze, teksture, materiale), preden pocistimo slovar
        foreach (TerrainChunk chunk in terrainChunkDictionary.Values) {
            chunk.Dispose();
        }

        // clear stale editor/play mode chunk references before destroying old child objects
        terrainChunkDictionary.Clear();
        // without this, Start() can try to hide GameObjects that were already destroyed in Init()
        terrainChunksVisibleLastUpdate.Clear();
        chunksToUnload.Clear();

        // delete all existing chunk GameObjects
        while (transform.childCount > 0) {
            DestroyImmediate(transform.GetChild(0).gameObject);
        }
    }

    public void Start() {
        Init();
        UpdateVisibleChunks();
    }

    void OnDestroy() {
        // Dispose() sprosti vse vire chunka (mreze vseh LOD nivojev, teksturo, material, podatke karte in rastlinstvo)
        foreach (TerrainChunk chunk in terrainChunkDictionary.Values) {
            chunk.Dispose();
        }

        terrainChunkDictionary.Clear();
        terrainChunksVisibleLastUpdate.Clear();
        chunksToUnload.Clear();
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }

    // Unity v igri objektov ne sme unicevati takoj (DestroyImmediate), v urejevalniku pa Destroy() ne deluje.
    // Ker je razred [ExecuteAlways], tece v obeh nacinih, zato pravo metodo izberemo tukaj na enem mestu.
    public static void SafeDestroy(UnityEngine.Object obj) {
        if (obj == null) { return; }

        if (Application.isPlaying) {
            Destroy(obj);
        } else {
            DestroyImmediate(obj);
        }
    }


    public void EditorUpdate() {
        Update(); // just call your existing dequeue logic
    }
    // #endif

    void Update() {
        viewerPosition = new Vector2(viewer.position.x, viewer.position.z);  // pretvorimo pozicijo igralca v 2D (x,z), ker nas ne zanima y koordinata

        if ((viewerPositionOld - viewerPosition).sqrMagnitude > sqrViewMoveThresholdForChunkUpdate) {  // ce je igralec prehodil dovolj veliko razdaljo, da se bo update-ala vidnost chunkov
            viewerPositionOld = viewerPosition;  // posodobimo staro pozicijo igralca
            UpdateVisibleChunks();
        }


        // risanje vegetacije za vse chunke, ki so bili vidni v zadnjem update-u
        foreach (TerrainChunk chunk in terrainChunksVisibleLastUpdate) {
            if (chunk.meshObject == null) { continue; }  // chunk je bil medtem sproscen iz pomnilnika, zato ga preskocimo

            if (chunk.vegetationRenderer.HasVegetation) {
                chunk.vegetationRenderer.Draw();  // draw the vegetation instances for this chunk
            }

        }
    }

    // tukaj bomo najprej skril vse chunke, ki so bili vidni v zadnjem update-u, nato pa bomo preverili, kateri chunk-i so v maxViewDst in jih bomo prikazovali, ostale pa skrivali
    public void UpdateVisibleChunks() {
        // skrijemo vse chunke, ki so bili vidni v zadnjem update-u
        for (int i = 0; i < terrainChunksVisibleLastUpdate.Count; i++) {
            terrainChunksVisibleLastUpdate[i].SetVisible(false);  // skrijemo vse chunke, ki so bili vidni v zadnjem update-u
        }
        terrainChunksVisibleLastUpdate.Clear();  // pocistimo seznam chunkov, ki so bili vidni v zadnjem update-u, da bomo lahko v naslednjem update-u dodali nove vidne chunke


        // tukaj bomo preverjali, kateri chunk-i so v maxViewDst in jih bomo prikazovali, ostale pa skrivali
        int currentChunkCoordX = Mathf.RoundToInt(viewerPosition.x / chunkSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewerPosition.y / chunkSize);

        // zanka cez vse chunke, ki se nahajajo v kvadratu okoli igralca, kjer je stranica kvadrata enaka 2*chunksVisibleInViewDst+1 (preverimo ali je chunk v maxViewDst)
        for (int yOffset = -chunksVisibleInViewDst; yOffset <= chunksVisibleInViewDst; yOffset++) {
            for (int xOffset = -chunksVisibleInViewDst; xOffset <= chunksVisibleInViewDst; xOffset++) {

                Vector2 viewedChunkCoord = new Vector2(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset);

                if (terrainChunkDictionary.ContainsKey(viewedChunkCoord)) {
                    terrainChunkDictionary[viewedChunkCoord].UpdateTerrainChunk();

                } else {
                    terrainChunkDictionary.Add(viewedChunkCoord, new TerrainChunk(viewedChunkCoord, chunkSize, detailLevels, transform, mapMaterial));
                }
            }
        }

        UnloadDistantChunks(currentChunkCoordX, currentChunkCoordY);
    }

    /* Sprosti chunke, ki so od opazovalca oddaljeni vec kot chunksLoadedInMemoryDst.
    // Brez tega bi slovar samo rasel, saj se vanj chunki le dodajajo in nikoli ne odstranjujejo,
    // kar bi pri dolgem raziskovanju sveta vodilo v napako zaradi zmanjkanja pomnilnika. */
    void UnloadDistantChunks(int currentChunkCoordX, int currentChunkCoordY) {
        chunksToUnload.Clear();

        // Razdaljo merimo kar v koordinatah chunkov (Chebysevljeva razdalja - vecja od razlike po x in po y),
        // zato zadostuje primerjava celih stevil; ni nam treba racunati korena niti razdalje do roba chunka.
        foreach (Vector2 coord in terrainChunkDictionary.Keys) {
            int dstX = Mathf.Abs(Mathf.RoundToInt(coord.x) - currentChunkCoordX);
            int dstY = Mathf.Abs(Mathf.RoundToInt(coord.y) - currentChunkCoordY);

            if (Mathf.Max(dstX, dstY) > chunksLoadedInMemoryDst) {
                chunksToUnload.Add(coord);

                // amortizacija: v eni posodobitvi sprostimo najvec toliko chunkov, ostali pridejo na vrsto naslednjic
                // (ce se opazovalec nenadoma prestavi zelo dalec, bi sicer naenkrat sprostili na stotine chunkov in povzrocili zastoj)
                if (chunksToUnload.Count >= maxChunkUnloadsPerUpdate) { break; }
            }
        }

        // slovarja ne smemo spreminjati med sprehodom po njem, zato brisemo sele zdaj
        foreach (Vector2 coord in chunksToUnload) {
            terrainChunkDictionary[coord].Dispose();
            terrainChunkDictionary.Remove(coord);
        }
    }

    // this generates all the chunks that are inside view distance of a player, but in editor without threading (because we can't use threading in editor - we don't have Update() to check when the thread finishes)
    public void UpdateVisibleChunksInEditor() {
        Init();

        // here we actually generate each chunk that's inside view distance (without threading)
        for (int yOffset = -chunksVisibleInViewDst; yOffset <= chunksVisibleInViewDst; yOffset++) {
            for (int xOffset = -chunksVisibleInViewDst; xOffset <= chunksVisibleInViewDst; xOffset++) {
                Vector2 viewedChunkCoord = new Vector2(xOffset, yOffset);                                                    // coordinate of the chunk that is being generated, relative to the viewer
                TerrainChunk chunk = new TerrainChunk(viewedChunkCoord, chunkSize, detailLevels, transform, mapMaterial);           // create a new chunk at the given coordinate
                chunk.GenerateInEditor();                                                                                           // generate the chunk synchronously (without threading) and make it visible immediately
                terrainChunkDictionary[viewedChunkCoord] = chunk;                                                                   // chunk zabelezimo v slovar, da ga bo naslednji Init() lahko pravilno sprostil (sicer bi njegove mreze in teksture ostale v pomnilniku)
            }
        }
    }

    public class TerrainChunk {
        Vector2 position;
        public GameObject meshObject;
        Bounds bounds;  // to je kocka, ki vsebuje nas chunk, in nam bo pomagala pri preverjanju, ali je chunk v maxViewDst

        public MeshRenderer meshRenderer;
        public MeshFilter meshFilter;
        MeshCollider meshCollider;

        LODInfo[] detailLevels;
        LODMesh[] lodMeshes;

        BiomeMapData biomeMapData;
        bool mapDataReceived;
        bool isDisposed;                // ko je true, je chunk sproscen iz pomnilnika in ga ne smemo vec uporabljati
        int previouseLODIndex = -1;

        public ChunkVegetationRenderer vegetationRenderer = new ChunkVegetationRenderer();  // this will hold all the vegetation instances for this chunk

        public TerrainChunk(Vector2 coord, int size, LODInfo[] detailLevels, Transform parent, Material material) {
            this.detailLevels = detailLevels;

            position = coord * size;                                        // pozicija chunka je koordinata chunka (ki je v enotah chunkov) pomnozena z velikostjo chunka, da dobimo pozicijo v enotah sveta
            bounds = new Bounds(position, Vector2.one * size);              // bounds ima center in velikost, tukaj je center pozicija chunka, velikost pa je velikost chunka
            Vector3 positionV3 = new Vector3(position.x, 0, position.y);    // ker je pozicija v 2D, jo moramo pretvoriti v 3D, kjer je y koordinata 0 (ker nas ne zanima visina chunka)

            meshObject = new GameObject("Terrain Chunk");                   // ustvarimo nov GameObject, ki bo predstavljal nas chunk
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            //meshRenderer.material = material;  //--old code
            // create a chunk-specific material instance without touching renderer.material in edit mode
            meshRenderer.sharedMaterial = new Material(material);

            meshFilter = meshObject.AddComponent<MeshFilter>();
            meshCollider = meshObject.AddComponent<MeshCollider>();

            meshObject.transform.position = positionV3;
            meshObject.transform.parent = parent;                           // samo zato da so vsi chunki v hierarhiji pod MapGenerator objektom, da je bolj pregledno
            SetVisible(false);                                              // chunk je sprva skrit, ker ga bomo prikazali samo, ce bo v maxViewDst

            lodMeshes = new LODMesh[detailLevels.Length];
            for (int i = 0; i < detailLevels.Length; i++) {
                lodMeshes[i] = new LODMesh(detailLevels[i].lod, UpdateTerrainChunk, position, meshObject);
            }

            mapGenerator.RequestMapData(position, OnMapDataReceived);
        }


        // method that generates the chunk synchronously (without threading) and makes it visible immediately, for in editor rendering
        public void GenerateInEditor() {
            BiomeMapData mapData = mapGenerator.GenerateBiomeMapDataSync(position);

            int colorMapSize = mapData.heightMap.GetLength(0);
            Texture2D texture = TextureGenerator.TextureFromColorMap(mapData.colorMap, colorMapSize, colorMapSize);

            SafeDestroy(meshRenderer.sharedMaterial);  // kopijo materiala, ustvarjeno v konstruktorju, tukaj zamenjamo, zato jo moramo prej sprostiti
            meshRenderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            meshRenderer.sharedMaterial.mainTexture = texture;
            // we could use just "meshRenderer.material.mainTexture = texture;", but this would create some warning (ram leakage) - instead we create a new material instance for this chunk

            MeshData meshData = MeshGenerator.GenerateBiomeMesh(mapData.heightMap, mapGenerator.biomes, mapData.biomeWeightsMap, mapGenerator.editorPreviewLOD, mapGenerator.useBorder); // LOD 0 for editor preview - here we actually generate the mesh for the chunk
            meshFilter.sharedMesh = meshData.CreateMesh();

            SetVisible(true);
        }

        // tukaj bomo prejeli map data iz MapGeneratorja, ko bo koncal z generiranjem map data v ločenem threadu, in bomo na podlagi tega map data-ja ustvarili mesh za nas chunk
        void OnMapDataReceived(BiomeMapData biomeMapData) {
            // Ce je bil chunk medtem sproscen iz pomnilnika, je ta povratni klic zakasnel rezultat ozadnje niti. Brez te preverbe bi tukaj ustvarili teksturo, ki je nihce vec ne bi sprostil, in dostopali do unicenega predmeta.
            if (isDisposed) { return; }

            this.biomeMapData = biomeMapData;
            mapDataReceived = true;

            // zato da bo nas chunk imel barvo
            int colorMapSize = biomeMapData.heightMap.GetLength(0);
            Texture2D texture = TextureGenerator.TextureFromColorMap(biomeMapData.colorMap, colorMapSize, colorMapSize);

            // meshRenderer.material.mainTexture = texture; --old code. .material looks at the shared master material and makes a brand-new hidden copy of that material in RAM (in editor this causes RAM leakage)
            // assign the texture through sharedMaterial so Unity does not auto-instantiate a leaking material. sharedMaterial is pointer to the original asset sitting in project folders.
            meshRenderer.sharedMaterial.mainTexture = texture;

            SpawnVegetation();

            UpdateTerrainChunk();
        }

        void SpawnVegetation() {
            AnimationCurve[] biomesMeshHeightCurve = MeshGenerator.GetBiomeHeightCurvesArray(BiomeGenerator.biomes); // get biome height curves array for vegetation spawning

            foreach (Vector2 treePos in biomeMapData.vegetationPositions.Keys) {
                Vegetation vegType = biomeMapData.vegetationPositions[treePos];

                for (int i = 0; i < vegType.count; i++) {
                    float mul = vegType.count * vegType.groupSpacing;                       // multiply by groupSpacing to get the distance between each instance in the group
                    float posX = treePos.x + Random.Range(-0.5f * mul, 0.5f * mul);         // randomize position of each instance in the group
                    float posZ = treePos.y + Random.Range(-0.5f * mul, 0.5f * mul);         // randomize position of each instance in the group

                    // get the height of the mesh at the position of the treeS
                    float height = MeshGenerator.InterpolateHeightAtPosition(posX, posZ, biomeMapData.heightMap, biomeMapData.biomeWeightsMap, BiomeGenerator.biomes, biomesMeshHeightCurve); // get the height of the mesh at the position of the tree, using the biome height curves

                    // Use (Size - 1) / 2f zato ker je dolzina chunka (mapChunkSize - 1). mapChunkSize je stevilo vozlisc v eni dimenziji chunka.
                    float halfChunkSize = (MapGenerator.mapChunkSize - 1) / 2f;

                    float finalX = this.position.x + (posX - halfChunkSize);
                    float finalZ = this.position.y + (halfChunkSize - posZ);  // moramo odsteti y koordinato od halfChunkSize, ker v Unityju 'z' os narasca v smeri "gor", v nasem array-u tock chunka pa z narasca v smeri "dol" (prva vrstica je cisto na vrhu chunka)


                    Vector3 position = new Vector3(finalX, height, finalZ);
                    Quaternion rotation = Quaternion.Euler(-90, Random.Range(0, 360f), 0);
                    Vector3 scale = Vector3.one * Random.Range(0.8f, 2f) * vegType.scale;

                    //vegetationRenderer.AddInstance(vegType.prefab, position, rotation, scale);    // add the vegetation instance to the renderer for this chunk
                    if (vegType.gpuInstancing && !mapGenerator.globalGPUInstancingDisable) {        // if the vegetation type supports GPU instancing and global GPU instancing is not disabled, add the instance to the renderer
                        vegetationRenderer.AddInstance(vegType.prefab, position, rotation, scale);  // add the vegetation instance to the renderer for this chunk
                    } else {
                        GameObject tree = Instantiate(vegType.prefab, new Vector3(finalX, height, finalZ), Quaternion.Euler(-90, Random.Range(0, 360f), 0), meshObject.transform); // instantiate tree prefab at the position with random rotation around y-axis
                        float randomScale = Random.Range(0.8f, 2f) * vegType.scale;
                        tree.transform.localScale *= randomScale;
                    }

                }

            }

        }



        // tukaj bomo preverjali, ali je chunk v maxViewDst in ga bomo prikazovali ali skrivali glede na to (nastavimo mesh in prikazemo/skrijemo chunk)
        public void UpdateTerrainChunk() {
            if (isDisposed) { return; }                                                         // sproscen chunk ni vec del sveta, zato ga ne posodabljamo (in ga ne smemo dodati med vidne)
            if (!mapDataReceived) { return; }                                                   // ce se map data se ni prejel, ne moremo ustvariti mesha, zato samo vrnemo

            float viewerDistFromNearestEdge = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));   // SqrDistance nam da kvadrat razdalje, zato vzamemo koren, da dobimo pravo razdaljo
            bool visible = viewerDistFromNearestEdge <= maxViewDst;                             // chunk je viden, ce je razdalja od igralca do najblizjega roba chunka manjsa ali enaka maxViewDst

            // tukaj bomo preverjali, kateri LOD nivo naj uporabimo glede na razdaljo od igralca do chunka, in ce se je LOD nivo spremenil, bomo zahtevali nov mesh za nas chunk
            if (visible) {
                int lodIndex = 0;

                for (int i = 0; i < detailLevels.Length - 1; i++) {                             // tukaj preverjamo, kateri LOD nivo naj uporabimo glede na razdaljo od igralca do chunka
                    if (viewerDistFromNearestEdge > detailLevels[i].visibleDstThreshold) {
                        lodIndex = i + 1;
                    } else {
                        break;
                    }
                }

                if (lodIndex != previouseLODIndex) {            // ce se je LOD nivo spremenil, bomo zahtevali nov mesh za nas chunk
                    LODMesh lodMesh = lodMeshes[lodIndex];
                    if (lodMesh.hasMesh) {
                        previouseLODIndex = lodIndex;
                        meshFilter.mesh = lodMesh.mesh;         // ce mesh ze obstaja, ga samo nastavimo na mesh filterju/rendererju, da se bo prikazal (prikazemo chunk)
                        meshCollider.sharedMesh = lodMesh.mesh;   // nastavimo tudi mesh collider, da se bo igralec lahko sprehajal po chuncku


                    } else if (!lodMesh.hasRequestedMesh) {
                        lodMesh.RequestMesh(biomeMapData);           // ce mesh ne obstaja in ga se nismo zahtevali, ga zahtevamo (zahtevamo nov mesh za nas chunk)
                    }

                }
                terrainChunksVisibleLastUpdate.Add(this);       // dodamo nas chunk v seznam chunkov, ki so vidni v zadnjem update-u, da ga bomo lahko v naslednjem update-u skril, ce ne bo vec v maxViewDst

            }

            SetVisible(visible);
        }

        /* Sprosti vse vire, ki jih drzi ta chunk.
        // Unity poligonskih mrez, tekstur in materialov ne pobira samodejno, ceprav nanje nihce vec ne kaze,
        // zato samo unicenje predmeta meshObject ne bi zadostovalo - pomnilnik bi ostal zaseden. */
        public void Dispose() {
            if (isDisposed) { return; }
            isDisposed = true;

            // 1. mreze vseh nivojev podrobnosti (vsak nivo ima svojo mrezo)
            if (lodMeshes != null) {
                for (int i = 0; i < lodMeshes.Length; i++) {
                    lodMeshes[i].Dispose();
                }
            }

            // 2. mreza, ki je trenutno nastavljena (v urejevalniku je lahko druga od zgornjih)
            if (meshFilter != null) { SafeDestroy(meshFilter.sharedMesh); }
            if (meshCollider != null) { meshCollider.sharedMesh = null; }

            // 3. tekstura in kopija materiala, ki ju ima vsak chunk svojo
            if (meshRenderer != null && meshRenderer.sharedMaterial != null) {
                SafeDestroy(meshRenderer.sharedMaterial.mainTexture);
                SafeDestroy(meshRenderer.sharedMaterial);
            }

            // 4. rastlinstvo: matrike primerkov so navadna polja, zato jih pobere smetar, ko izgubijo kazalec
            if (vegetationRenderer != null) { vegetationRenderer.Clear(); }

            // 5. podatki karte (visinska karta, barvna karta, uteži biomov) - najvecji del porabe pomnilnika na chunk
            biomeMapData = default;
            mapDataReceived = false;

            // 6. sele na koncu predmet scene, skupaj z njim pa tudi vsi primerki rastlinstva, ki so njegovi otroci
            SafeDestroy(meshObject);
            meshObject = null;

            // ce je chunk se v seznamu vidnih, ga odstranimo, da Update() ne bi izrisoval sproscenega rastlinstva
            terrainChunksVisibleLastUpdate.Remove(this);
        }

        public void SetVisible(bool visible) {
            if (meshObject == null) { return; }
            meshObject.SetActive(visible);
        }

        public bool IsVisible() {
            return meshObject != null && meshObject.activeSelf;
        }
    }

    // ta razred bo vseboval mesh za določen LOD nivo, in bo imel metodo za zahtevanje mesha iz MapGeneratorja, ko bo prejel map data
    class LODMesh {
        public Mesh mesh;
        public bool hasRequestedMesh;
        public bool hasMesh;

        // this vars are needed for vegetation spawning
        public MeshData meshData;
        public Vector2 chunkPosition;
        public GameObject meshObject;

        int lod;
        bool isDisposed;                    // ko je true, je pripadajoci chunk sproscen in mrez ne ustvarjamo vec
        BiomeMapData biomeMapData;
        System.Action updateCallback;       // this callback is UpdateTerrainChunk() method of TerrainChunk class

        public LODMesh(int lod, System.Action updateCallback, Vector2 chunkPosition, GameObject meshObject) {
            this.lod = lod;
            this.updateCallback = updateCallback;
            this.chunkPosition = chunkPosition;
            this.meshObject = meshObject;
        }

        void OnMeshDataReceived(MeshData meshData) {
            // Zahteva je bila oddana ozadnji niti, chunk pa je bil medtem lahko ze sproscen iz pomnilnika.
            // Ce mrezo kljub temu ustvarimo, ostane v pomnilniku, ne da bi jo kdorkoli izrisal ali sprostil.
            if (isDisposed) { return; }

            this.meshData = meshData;
            mesh = meshData.CreateMesh();
            hasMesh = true;
            //SpawnVegetation(meshData);  // ko prejmemo mesh data, poklicemo metodo za spawnanje vegetacije na chuncku
            updateCallback();           // ko prejmemo mesh data, poklicemo update callback, da se bo chunk lahko posodobil z novim meshom
        }

        public void RequestMesh(BiomeMapData biomeMapData) {
            hasRequestedMesh = true;
            this.biomeMapData = biomeMapData;
            mapGenerator.RequestMeshData(biomeMapData, lod, OnMeshDataReceived);
        }

        // sprosti poligonsko mrezo tega nivoja podrobnosti in prekine morebitne se neobdelane zahteve
        public void Dispose() {
            isDisposed = true;

            SafeDestroy(mesh);
            mesh = null;
            meshData = null;
            biomeMapData = default;
            meshObject = null;
            hasMesh = false;
            hasRequestedMesh = false;
        }

    }

    [System.Serializable]  // zato da se LODInfo prikaže v inspectorju
    public struct LODInfo {
        public int lod;
        public float visibleDstThreshold;

        public LODInfo(int lod, float visibleDstThreshold) {
            this.lod = lod;
            this.visibleDstThreshold = visibleDstThreshold;
        }
    }
}