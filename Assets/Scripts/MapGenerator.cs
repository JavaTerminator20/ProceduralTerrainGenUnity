using UnityEngine;
using System;
using System.Threading;
using System.Collections.Generic;
using Unity.VisualScripting;

//[ExecuteAlways]
public class MapGenerator : MonoBehaviour {
    public enum DrawMode { HeightMap, ContinentMap, CombinedHeight, ColorMap, TemperatureMap, HumidityMap, BiomeMap, BiomeMesh, View };   // s tem ustvarimo nekaksen drop-down meni, ker ima lahko spremeljivka drawMode omejeno stavilo vrednosti
    public DrawMode drawMode;               // s tem ustvarimo nekaksen drop-down meni, ker ima lahko spremeljivka drawMode omejeno stavilo vrednosti
    public int mapSize = 3000;              // to je velikost dela mape (2d plosce), ki ga bomo generirali v editorju, da lahko vidimo, kako bo izgledala
    public float sampleInterval = 3.0f;

    public static int mapChunkSize = 241;    // s tem smo nadomestili mapHeight in mapWidth. Vrednost je 241, ker taksno stevilo vozlisc nam da dejansko sirino: (241-1), ki je zelo lepo deljiva z: 1, 2, 3, 4, 5, 6
    int buffer = 0;
    [Range(0, 6)]
    public int editorPreviewLOD;

    [Header("TerrainNoise")]
    public float noiseScale;
    public int octaves;

    [Range(0, 1)]                           // s tem ustvarimo slider za vrednost spremenljivke persistance
    public float persistance;
    public float lacunarity;

    public int seed;
    public Vector2 offset;

    [Header("ContinentNoise")]              //seed and offset are the same as for terrain noise
    public float continentScale;
    public int continentOctaves;

    [Range(0, 1)]
    public float continentPersistance;
    public float continentLacunarity;

    public TerrainType[] regions;

    public float meshHeightMultiplier;      // brez tega parametra zgleda terren cisto raven - rabimo povdariti visino
    public AnimationCurve meshHeightCurve;  //ta krivulja bo povedala, koliksen vpliv ima heightMultiplier pri doloceni visini - tam kjer je morje ne zelimo skoraj nobenega vpliva (flat), v gorah pa to zelimo

    public bool autoUpdate = false;

    //v nekem frame-u se lahko najvec 2 chunka procesirata na enkrat - prepreci da bi se 100 novih chunkov generiralo v enem frame-u - microstutter
    [Header("Chunk Processing")]
    public int maxChunkCallbacksPerFrame = 2;
    //TEMPERATURE MAP VARS
    [Header("BiomeNoise")]
    public float humidityFactor;
    public float biomeNoiseScale;
    public int biomeOctaves;
    [Range(0, 1)]
    public float biomePersistance;
    public float biomeLacunarity;
    public int biomeSeed;
    public Vector2 biomeOffset;

    [Header("Vegetation")]
    public bool globalGPUInstancingDisable = false;

    [Header("Coastline")]
    [Range(0f, 1f)]
    public float oceanThreshold = 0.344f;   // continentalness below which an area is ocean/coast rather than land

    [Header("Biomes")]
    public Biome[] biomes = BiomeGenerator.biomes;
    [Range(0.1f, 3f)]
    public float biomeHeightTransitionWidth = 1f; // <1 = sharper bands, >1 = smoother transitions

    //public GameObject vegetation;

    [Header("Border Settings")]
    public bool useBorder = false;           // if true, the mesh will have a border of vertices around it that are not part of the mesh but are used to calculate normals for the edge vertices

    Queue<MapThreadInfo<BiomeMapData>> biomeMapDataThreadInfoQueue = new Queue<MapThreadInfo<BiomeMapData>>();     // to je queue, kamor bomo shranjevali callback-e, ki jih bomo klicali v Update() metodi, ko bo map data thread koncal z delom
    Queue<MapThreadInfo<MeshData>> meshDataThreadInfoQueue = new Queue<MapThreadInfo<MeshData>>();                 // tukaj bomo shranjevali callback-e, ki jih bomo klicali v Update() metodi, ko bo mesh data thread koncal z delom

    BiomeGenerator biomeGenerator;

    // method that retrieves BiomeGenerator instance (needed when building terrain in editor, otherwise would be in Start())
    void Init() {
        biomeGenerator = new BiomeGenerator(this);
        if (useBorder) {
            mapChunkSize = 239; // 239 + 2 border vertices = 241
            buffer = 2;
        } else {
            mapChunkSize = 241;
            buffer = 0;
        }
    }

    void Start() {
        Init();
        Noise.sampleInterval = 1.0f;                // ko recimo hocemo pokazati BiomeMap je ta vrednost spremenjena - jo rabimo nastaviti nazaj na 1.0f da se svet pravilno generira
    }


    // this method draws the map in the editor - no need to run the game - just select the mode
    public void DrawMapInEditor() {
        Init();

        BiomeMapData biomeMapData = biomeGenerator.GenerateBiomeMapData(Vector2.zero + offset, mapSize);

        MapDisplay display = FindAnyObjectByType<MapDisplay>();
        if (drawMode == DrawMode.HeightMap) {
            display.DrawTexture(TextureGenerator.TextureFromHeightMap(GetChunkHeightMap(Vector2.zero, mapSize), DrawMode.HeightMap), sampleInterval);
        }// 
        else if (drawMode == DrawMode.ContinentMap) {
            display.DrawTexture(TextureGenerator.TextureFromHeightMap(GetChunkContinentalnessMap(Vector2.zero, mapSize), DrawMode.ContinentMap), sampleInterval);
        }//
        else if (drawMode == DrawMode.CombinedHeight) {
            display.DrawTexture(TextureGenerator.TextureFromHeightMap(GetChunkCombinedHeightMap(Vector2.zero, mapSize), DrawMode.CombinedHeight), sampleInterval);
        }//
        else if (drawMode == DrawMode.TemperatureMap) {
            display.DrawTexture(TextureGenerator.TextureFromHeightMap(GetChunkTemperatureMap(Vector2.zero, mapSize), DrawMode.TemperatureMap), sampleInterval);
        }//
        else if (drawMode == DrawMode.HumidityMap) {
            display.DrawTexture(TextureGenerator.TextureFromHeightMap(GetChunkHumidityMap(Vector2.zero, mapSize), DrawMode.HumidityMap), sampleInterval);
        }

        // show each biome in its own color
        else if (drawMode == DrawMode.BiomeMap) {
            Color[] biomeMapColors = new Color[mapSize * mapSize];

            for (int x = 0; x < mapSize; x++) {
                for (int y = 0; y < mapSize; y++) {
                    biomeMapColors[y * mapSize + x] = biomeMapData.biomeMap[x, y].color;
                }
            }

            display.DrawTexture(TextureGenerator.TextureFromColorMap(biomeMapColors, mapSize, mapSize), sampleInterval);
        }//
        // else if (drawMode == DrawMode.TemperatureMap) {
        //     display.DrawTexture(TextureGenerator.TextureFromHeightMap(biomeMapData.heightMap, DrawMode.TemperatureMap));
        // } //

        // show all colors
        else if (drawMode == DrawMode.ColorMap) {
            display.DrawTexture(TextureGenerator.TextureFromColorMap(biomeMapData.colorMap, mapSize, mapSize), sampleInterval);
        }
        // render actual mesh in the max view distance set in EndlessTerrain script
        else if (drawMode == DrawMode.View) {
            display.DrawView();
        }
    }

    /*legacy multithreading method - before thread pool optimization
    public void RequestMapData(Vector2 center, Action<BiomeMapData> callback) {         // CENTER is used for scrolling the noise map when player moves
        ThreadStart threadStart = delegate {
            MapDataThread(center, callback);                                            // this method will run on a separate thread.
        };

        new Thread(threadStart).Start();
    }*/


    // ------------------------------------------- MULTITHREADING METHODS (PLAY MODE) -----------------------------------------------

    // Action is used to pass a method as a parameter (callback) that will be called when the map data is generated in the thread
    // we create a new thread (actually now we use threadpool - we don't create new threads because that is very resource heavy) 
    // and call the MapDataThread method in it, which will generate the map data and then call the callback method with the generated map data as a parameter
    public void RequestMapData(Vector2 center, Action<BiomeMapData> callback) {      //CENTER is used for scrolling the noise map when player moves
        // Create a delegate work item for the thread pool.  -- delegate variable hold ref. to a method instad of standard data value
        WaitCallback work = delegate {
            MapDataThread(center, callback);                                        // this method will run on a separate thread.
        };

        // queue the work - a recycled background worker thread will pick this up automatically - much faster than creating new Thread (very resource heavy)
        // we are handing a job assignment to an existing pool of bg workers. once a thread finishes one chunk it instantly grabs the next chunk without any thread initialization overhead.
        ThreadPool.QueueUserWorkItem(work);
    }

    // Action must be void function. Since MapDataThread runs on a separate thread, the GenerateMapData method will also run there.
    void MapDataThread(Vector2 center, Action<BiomeMapData> callback) {
        BiomeMapData biomeMapData = biomeGenerator.GenerateBiomeMapData(center, mapChunkSize + buffer);        // this method will generate the noiseMap and colorMap (add +2 for bordered generation - if mapChunkSize=239)

        // we lock biomeMapDataThreadInfoQueue because we are accessing it from a different thread and we want to prevent race conditions.
        lock (biomeMapDataThreadInfoQueue) {
            biomeMapDataThreadInfoQueue.Enqueue(new MapThreadInfo<BiomeMapData>(callback, biomeMapData));
        }
    }

    // this method is called by the TerrainChunk class when it receives the map data from the MapGenerator, and it will request the mesh data to be generated in a separate thread
    public void RequestMeshData(BiomeMapData biomeMapData, int lod, Action<MeshData> callback) {
        WaitCallback work = delegate {
            MeshDataThread(biomeMapData, lod, callback);
        };

        ThreadPool.QueueUserWorkItem(work);
    }

    void MeshDataThread(BiomeMapData biomeMapData, int lod, Action<MeshData> callback) {
        MeshData meshData = MeshGenerator.GenerateBiomeMesh(biomeMapData.heightMap, biomes, biomeMapData.biomeWeightsMap, lod, useBorder);

        lock (meshDataThreadInfoQueue) {                                                        // locking here is critical to prevent race condition (in Update() the Queue is beeing constantly checked)
            meshDataThreadInfoQueue.Enqueue(new MapThreadInfo<MeshData>(callback, meshData));
        }
    }

    // --------------------------------------- END OF MULTITHREADING METHODS -------------------------------------------------

    // ----------------------------------------- MULTITHREADING IN EDITOR -----------------------------------------------------

    // poimenovano NotOnEnable, ker je OnEnable metoda ki se poklice sama od sebe (built in method)
    void NotOnEnable() {
        Debug.Log("Starting threaded generation in editor mode");
        if (!Application.isPlaying) {
            globalGPUInstancingDisable = true;                      // disable GPU instancing in editor mode, because it causes issues with the editor - can't move around
            Start();

            EndlessTerrain endlessTerrain = GetComponent<EndlessTerrain>();
            //EndlessTerrain.terrainChunkDictionary.Clear();            // to dvoje se zgodi v endlessTerrain.Start()
            //EndlessTerrain.terrainChunksVisibleLastUpdate.Clear();
            endlessTerrain.Start();

            UnityEditor.EditorApplication.update += EditorUpdate;

            // to je useless ker ima EndlessTerrain atribut [ExecuteAlways] in class declaration, 
            // kar pomeni da se bo Update metoda klicala sama od sebe (ko bo mouse premaknjen nad editor window)
            // zgornja vrstica pa je nujna, saj MapGenerator nima tega atributa
            // UnityEditor.EditorApplication.update += endlessTerrain.EditorUpdate;
        }
    }

    public void StartThreadedGeneration() {
        NotOnEnable();
    }

    public void EndThreadedGeneration() {
        globalGPUInstancingDisable = false;                     // re-enable GPU instancing in editor mode, because it causes issues with the editor - can't move around
        NotOnDisable();
    }

    void NotOnDisable() {
        Debug.Log("Stopping threaded generation in editor mode");
        EndlessTerrain endlessTerrain = GetComponent<EndlessTerrain>();
        UnityEditor.EditorApplication.update -= EditorUpdate;
        UnityEditor.EditorApplication.update -= endlessTerrain.EditorUpdate;
    }

    void EditorUpdate() {
        Update(); // just call your existing dequeue logic
    }

    //-------------------------------------- END OF MULTITHREADING IN EDITOR ---------------------------------------------------

    /*legacy update method (before threadpool optimization):
    while (biomeMapDataThreadInfoQueue.Count > 0) {
            MapThreadInfo<BiomeMapData> threadInfo = biomeMapDataThreadInfoQueue.Dequeue();
            threadInfo.callback(threadInfo.parameter);   // this will call the callback method with the generated map data as a parameter
        }

        while (meshDataThreadInfoQueue.Count > 0) {
            MapThreadInfo<MeshData> threadInfo = meshDataThreadInfoQueue.Dequeue();
            threadInfo.callback(threadInfo.parameter);
        }
    */

    // in update execute what requested execution from threads
    void Update() {
        // we check if there is any map data that has been generated in a separate thread and is waiting in the queue to be processed (callback called)
        // we never do more than 2 heavy operations per frame (process map or mesh Data)

        int callbacksProcessed = 0;
        // we process biomeMapData first (if there is any) if there was some biomeMapData to process, then callbacksProcessed increments (goes to 1 or 2)
        // if we processed 2 biomeMapData's then we don't do anything this frame... we repeat this until no biomeMapData's are to process left
        // then the code starts processing at max 2 (maxChunkCallbacksPerFrame) meshData's

        callbacksProcessed += ProcessThreadInfoQueue(biomeMapDataThreadInfoQueue, maxChunkCallbacksPerFrame - callbacksProcessed);

        // ce pri prejsnjem klicu nismo dosegli limita - to pomeni da smo koncali s procesiranjem mapData 
        // in lahko zacnemo procesirati meshData - ustvarjati dejanske meshe
        if (callbacksProcessed < maxChunkCallbacksPerFrame) {
            ProcessThreadInfoQueue(meshDataThreadInfoQueue, maxChunkCallbacksPerFrame - callbacksProcessed);
        }

    }

    /// <summary>
    /// A generic, thread-safe runner that unloads data from a background thread queue 
    /// and executes its callback on Unity's main thread without exceeding a specific limit.
    /// </summary>
    /// <typeparam name="T">Accepts either BiomeMapData or MeshData structs.</typeparam>
    /// <param name="threadInfoQueue">The thread queue we want to process.</param>
    /// <param name="maxCallbacksToProcess">The maximum allowed operations before we stop to save frame rate.</param>
    /// <returns>The actual number of callbacks successfully processed in this cycle.</returns>

    // the first parameter is either biomeMapDataThreadInfoQueue or meshData...Queue
    // the second param dictates the max number of "stuff" that can get processed (biomeMapData's or meshData's)
    int ProcessThreadInfoQueue<T>(Queue<MapThreadInfo<T>> threadInfoQueue, int maxCallbacksToProcess) {
        int callbacksProcessed = 0;

        while (callbacksProcessed < maxCallbacksToProcess) {
            MapThreadInfo<T> threadInfo;

            lock (threadInfoQueue) {                    // we need lock because the queue can be at the moment used (.Enqeue()) by one of bg threads.
                if (threadInfoQueue.Count == 0) {       // if the queue is empty, then skip
                    break;
                }

                threadInfo = threadInfoQueue.Dequeue(); // dequeue an element from queue
            }

            threadInfo.callback(threadInfo.parameter);  // call the callback function with the parameter
            callbacksProcessed++;                       // increment the number of stuff processed in this call
        }

        return callbacksProcessed;
    }



    //just a public method for GenerateBiomeMapData, so that we can call it from the editor without starting the game
    public BiomeMapData GenerateBiomeMapDataSync(Vector2 center) {
        center = center + offset;
        BiomeMapData biomeMapData = biomeGenerator.GenerateBiomeMapData(center, mapChunkSize);                                                               //this is our color map based on biomes

        return new BiomeMapData(biomeMapData.colorMap, biomeMapData.biomeWeightsMap, biomeMapData.heightMap, biomeMapData.biomeMap, biomeMapData.vegetationPositions);
    }


    // this method generates the noiseMap and colorMap, and then returns them in a MapData struct
    MapData GenerateMapData(Vector2 center) {
        float[,] noiseMap = Noise.GenerateNoiseMap(mapChunkSize, mapChunkSize, noiseScale, octaves, persistance, lacunarity, seed, center + offset);
        Color[] colorMap = new Color[mapChunkSize * mapChunkSize];
        for (int y = 0; y < mapChunkSize; y++) {
            for (int x = 0; x < mapChunkSize; x++) {
                float currentHeight = noiseMap[x, y];
                for (int i = 0; i < regions.Length; i++) {
                    if (currentHeight <= regions[i].height) {
                        colorMap[y * mapChunkSize + x] = regions[i].color;
                        break;
                    }
                }

            }
        }

        return new MapData(noiseMap, colorMap);
    }

    // getter methods used in BioimeGenerator - so we don't have to pass all those variables and so we can call them drawing 2d map of biomes
    public float[,] GetChunkHeightMap(Vector2 center, int chunkSize) {
        return Noise.GenerateNoiseMap(chunkSize, chunkSize, noiseScale, octaves, persistance, lacunarity, seed, center + offset);
    }

    public float[,] GetChunkContinentalnessMap(Vector2 center, int chunkSize) {
        return Noise.GenerateNoiseMap(chunkSize, chunkSize, continentScale, continentOctaves, continentPersistance, continentLacunarity, seed, center + offset);
    }

    public float[,] GetChunkCombinedHeightMap(Vector2 center, int chunkSize) {
        float[,] heightMap = GetChunkHeightMap(center, chunkSize);
        float[,] continentalnessMap = GetChunkContinentalnessMap(center, chunkSize);

        float[,] combinedHeightMap = new float[chunkSize, chunkSize];
        for (int y = 0; y < chunkSize; y++) {
            for (int x = 0; x < chunkSize; x++) {
                combinedHeightMap[x, y] = heightMap[x, y] * 0.4f + continentalnessMap[x, y] * 0.6f;
            }
        }

        return combinedHeightMap;
    }

    public float[,] GetChunkTemperatureMap(Vector2 center, int chunkSize) {
        return Noise.GenerateNoiseMap(chunkSize, chunkSize, biomeNoiseScale, biomeOctaves, biomePersistance, biomeLacunarity, biomeSeed, center + biomeOffset);
    }

    // this is the same as temperature map, just different seed and different noiseScale - humidity changes more often than temperature
    public float[,] GetChunkHumidityMap(Vector2 center, int chunkSize) {
        return Noise.GenerateNoiseMap(chunkSize, chunkSize, biomeNoiseScale * humidityFactor, biomeOctaves, biomePersistance, biomeLacunarity, biomeSeed * 747796405 + 289133645, center + biomeOffset);
    }

    // this method is called whenever the public variable values are changed in the inspector
    public void OnValidate() {
        // we clamp certain variable values (limit the bottom val)
        //if (mapChunkSize < 1) { mapChunkSize = 1; }
        //if (mapChunkSize < 1) { mapChunkSize = 1; }
        if (lacunarity < 1) { lacunarity = 1; }
        if (octaves < 0) { octaves = 0; }

        // propagate inspector change to static Noise.sampleInterval so editor drawing uses the new value immediately
        Noise.sampleInterval = sampleInterval;

    }

    // making it generic so that we can use it for both MapData and MeshData
    struct MapThreadInfo<T> {
        public readonly Action<T> callback;
        public readonly T parameter;

        public MapThreadInfo(Action<T> callback, T parameter) {
            this.callback = callback;
            this.parameter = parameter;
        }
    }

}

[System.Serializable]   // brez tega nas struct ne bo viden v inspectorju, kljub temu da ga nastavimo na "public"
public struct TerrainType {
    public string name;
    public float height;
    public Color color;
}

[System.Serializable]
public struct HeightColor {
    public float height;
    public Color color;
}


// struct for heightMap and colorMap
public struct MapData {
    public float[,] heightMap;
    public Color[] colorMap;

    public MapData(float[,] heightMap, Color[] colorMap) {
        this.heightMap = heightMap;
        this.colorMap = colorMap;
    }
}

/// <summary>
/// This struct is used to pass all the data generated in BiomeGenerator to the TerrainChunk
/// it has the: <br/> heightMap, <br/> colorMap, <br/> biomeMap, <br/> biomeWeightsMap, <br/>vegetationPositions
/// </summary>
public struct BiomeMapData {
    public float[,] heightMap;
    public Color[] colorMap;
    public Biome[,] biomeMap;
    public float[,,] biomeWeightsMap;
    public Dictionary<Vector2, Vegetation> vegetationPositions;


    public BiomeMapData(Color[] colorMap, float[,,] biomeWeightsMap, float[,] heightMap, Biome[,] biomeMap, Dictionary<Vector2, Vegetation> vegetationPositions) {
        this.colorMap = colorMap;
        this.biomeMap = biomeMap;
        this.heightMap = heightMap;
        this.biomeWeightsMap = biomeWeightsMap;
        this.vegetationPositions = vegetationPositions;
    }
}
