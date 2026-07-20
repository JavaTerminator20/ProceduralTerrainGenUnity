using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.UIElements;

public static class MeshGenerator {

    // obsolete old method for generating terrain mesh without biomes
    public static MeshData GenerateTerrainMesh(float[,] heightMap, float heightMultiplier, AnimationCurve _heightCurve, int levelOfDetail) {
        AnimationCurve heightCurve = new AnimationCurve(_heightCurve.keys); // to je potrebno, da lahko uporabljamo heightCurve.Evaluate() v vseh threadih na enkrat, ker AnimationCurve ni thread safe, zato moramo ustvariti kopijo (drugace imamo race condition)
        int height = heightMap.GetLength(1);
        int width = heightMap.GetLength(0);

        float topLeftX = (width - 1) / -2f;     // these values are used to make the mesh perfectly centered on the screen (otherwise the center (0, y, 0) would be the bottom left corner of the terrain)
        float topLeftZ = (height - 1) / 2f;

        int meshSimplificationIncrement = (levelOfDetail == 0) ? 1 : levelOfDetail * 2; // ce je LoD = 0, potem bo increment = 1 (v for loopu gremo cez vse pixle), ce pa je karkoli drugega, bo inkrement v for-loopu vecji - manj pogosto ustvarimo oglisca
        // (width-1) predstavlja dejansko sirino (izvzamemo koncno vozlisce), kjer je width stevilo vseh oglisc... ce to delimo z nekim faktorjem (chunkSize / 4) -> dobimo novo sirino, vendar moramo dodati +1 (koncno vozlisce)
        int verticesPerLine = (width - 1) / meshSimplificationIncrement + 1;

        MeshData meshData = new MeshData(verticesPerLine);
        int vertexIndex = 0;

        // zanka cez vsa oglisca nase mreze, najprej dodamo trenutno oglisce v seznam oglisc (meshData.vertices), nato pa
        // preverimo, da nismo na desnem ali ali zgornjem robu, in dodamo indekse obeh trikotnikov tega kvadrata
        for (int y = 0; y < height; y += meshSimplificationIncrement) {
            for (int x = 0; x < width; x += meshSimplificationIncrement) {

                meshData.vertices[vertexIndex] = new Vector3(topLeftX + x, heightCurve.Evaluate(heightMap[x, y]) * heightMultiplier, topLeftZ - y);

                if (x < width - 1 && y < height - 1) {        // with this we are ignoring the top and the right edge vertices
                    meshData.AddTriangle(vertexIndex, vertexIndex + verticesPerLine + 1, vertexIndex + verticesPerLine);    //dodamo prvi trikotnik
                    meshData.AddTriangle(vertexIndex + verticesPerLine + 1, vertexIndex, vertexIndex + 1);        //dodamo drugi trikotnik (skupaj s prvim tvori en kvadrat)
                }

                meshData.uvs[vertexIndex] = new Vector2(x / (float)width, y / (float)height);   // teksturne koordinate v posamezni tocki so samo razmerje med to tocko in celo dimenzijo
                vertexIndex++;
            }
        }

        return meshData;
    }


    public static MeshData GenerateBiomeMesh(float[,] heightMap, Biome[] biomes, float[,,] biomeWeightsMap, int levelOfDetail, bool useBorder) {

        int meshSimplificationIncrement = (levelOfDetail == 0) ? 1 : levelOfDetail * 2; // ce je LoD = 0, potem bo increment = 1 (v for loopu gremo cez vse pixle), ce pa je karkoli drugega, bo inkrement v for-loopu vecji - manj pogosto ustvarimo oglisca

        int borderedSize = heightMap.GetLength(0);      // to je stevilo vertexov chunka, vkljucno z borderjem (1 extra vertex layer okoli nasega chunka ki se uporablja za izracun normal - da ni seam-ov) |  rabimo samo 1 dim. saj je kvadrat
        int meshSize = borderedSize - 2 * meshSimplificationIncrement;                // to je velikost mesha v enotah sveta
        int meshSizeUnsimplified = borderedSize - 2;    // to je dejansko stevilo vertexov chunka (brez borderja) ki ima LOD = 0 - full resolution

        float topLeftX = (borderedSize - 1) / -2f;     // these values are used to make the mesh perfectly centered on the screen (otherwise the center (0, y, 0) would be the bottom left corner of the terrain)
        float topLeftZ = (borderedSize - 1) / 2f;

        // (width-1) predstavlja dejansko sirino (izvzamemo koncno vozlisce), kjer je width stevilo vseh oglisc... ce to delimo z nekim faktorjem (chunkSize / 4) -> dobimo novo sirino, vendar moramo dodati +1 (koncno vozlisce)
        int verticesPerLine = useBorder
            ? (meshSize - 1) / meshSimplificationIncrement + 1
            : (borderedSize - 1) / meshSimplificationIncrement + 1;

        MeshData meshData = new MeshData(verticesPerLine);
        //int vertexIndex = 0;

        // to je potrebno, da lahko uporabljamo heightCurve.Evaluate() v vseh threadih na enkrat, ker AnimationCurve ni thread safe, zato moramo ustvariti kopijo (drugace imamo race condition)
        AnimationCurve[] biomesMeshHeightCurve = GetBiomeHeightCurvesArray(biomes);

        int[,] vertexIndicesMap = new int[borderedSize, borderedSize];  // ustvarimo nov 2d array kjer shanjujemo indekse oglisc (zunanji rob tega 2d array-a so negativno stevila - border, znotraj pa pozitivna)
        int meshVertexIndex = 0;                                        // indeksi oglisc znotraj dejanskega mesha so pozitivno, zacnejo se z 0
        int borderVertexIndex = -1;                                     // indeksi oglisc v borderju so negativni, zacnejo se z -1

        for (int y = 0; y < borderedSize; y += meshSimplificationIncrement) {
            for (int x = 0; x < borderedSize; x += meshSimplificationIncrement) {
                bool isBorderVertex = (y == 0 || y == borderedSize - 1 || x == 0 || x == borderedSize - 1) && useBorder;

                if (isBorderVertex) {
                    vertexIndicesMap[x, y] = borderVertexIndex;
                    borderVertexIndex--;                            // indeksi oglisc izven na meji so negativni in se dekrementirajo (zgoraj levo = -1, desno od njega -2... itd)
                } else {
                    vertexIndicesMap[x, y] = meshVertexIndex;
                    meshVertexIndex++;                              // indeksi oglisc znotraj dejanskega chunka (ne na meji) so pozitivni in se inkrementirajo (zgoraj levo = 0, denso = 1, 2, ...)
                }
            }
        }



        // zanka cez vsa oglisca nase mreze, najprej dodamo trenutno oglisce v seznam oglisc (meshData.vertices), nato pa
        // preverimo, da nismo na desnem ali ali zgornjem robu, in dodamo indekse obeh trikotnikov tega kvadrata
        for (int y = 0; y < borderedSize; y += meshSimplificationIncrement) {
            for (int x = 0; x < borderedSize; x += meshSimplificationIncrement) {
                /* --------------NON-BLENDED BIOMES VALUE  -- legacy code, not used anymore----------------
                // Biome curBiome = biomeMap[x, y];
                // AnimationCurve heightCurve = curBiome.biomeMeshHeightCurve;
                // float heightMultiplier = curBiome.biomeMeshHeightMultiplier;
                // meshData.vertices[vertexIndex] = new Vector3(topLeftX + x, heightCurve.Evaluate(heightMap[x, y]) * heightMultiplier, topLeftZ - y);

                // --------------------BLENDED BIOMES VALUE-----------------------
                // float heightCurveEval = 0;
                // float heightMultiplier = 0;
                // for (int i = 0; i < biomes.Length; i++) {
                //     heightCurveEval += biomeWeightsMap[x, y, i] * biomesMeshHeightCurve[i].Evaluate(heightMap[x, y]);
                //     heightMultiplier += biomeWeightsMap[x, y, i] * biomes[i].biomeMeshHeightMultiplier;
                // }

                // float finalHeight = heightCurveEval * heightMultiplier; */

                int vertexIndex = vertexIndicesMap[x, y];

                // For height sampling: snap edge vertices to the true boundary
                int sampleX = x;
                int sampleY = y;

                bool isFirstRealX = (x == meshSimplificationIncrement);                     // the first real vertex is the first vertex after the border, so it is at index = meshSimplificationIncrement
                bool isLastRealX = (x == borderedSize - 1 - meshSimplificationIncrement);   // the last real vertex is the last vertex before the border, so it is at index = borderedSize - 1 - meshSimplificationIncrement
                bool isFirstRealY = (y == meshSimplificationIncrement);                     // the first real vertex is the first vertex after the border, so it is at index = meshSimplificationIncrement
                bool isLastRealY = (y == borderedSize - 1 - meshSimplificationIncrement);   // the last real vertex is the last vertex before the border, so it is at index = borderedSize - 1 - meshSimplificationIncrement

                if (isFirstRealX) sampleX = 1;
                if (isLastRealX) sampleX = borderedSize - 2;
                if (isFirstRealY) sampleY = 1;
                if (isLastRealY) sampleY = borderedSize - 2;

                float finalHeight;
                if (useBorder) {
                    finalHeight = GetHeightAtChunkPosition(sampleX, sampleY, heightMap, biomeWeightsMap, biomes, biomesMeshHeightCurve);
                } else {
                    finalHeight = GetHeightAtChunkPosition(x, y, heightMap, biomeWeightsMap, biomes, biomesMeshHeightCurve);
                }

                //meshData.finalHeightMap[x, y] = finalHeight;

                /*teksturne koordinate v posamezni tocki so samo razmerje med to tocko in celo dimenzijo
                (x-mSI) je zaradi uporabe borderja pri meshu za izracun smooth normal. ko je x = 0 in y = 0 je ta vertex del borderja, naslednji v iteraciji pa je 
                y = 0, x += mSI, in ta bo kot prva teksturna koordinata, zato mora biti x / meshSize = 0, zato moramo odsteti to mSI -> (x-mSI) / meshSzie = 0*/
                //Vector2 percent = new Vector2((sampleX - meshSimplificationIncrement) / (float)meshSize, (sampleY - meshSimplificationIncrement) / (float)meshSize);

                float realStart = meshSimplificationIncrement;
                float realEnd = borderedSize - 1 - meshSimplificationIncrement;
                float realSpan = realEnd - realStart;  // = borderedSize - 1 - 2*meshSimplificationIncrement

                Vector2 percent = new Vector2(
                    (x - realStart) / realSpan,
                    (y - realStart) / realSpan
                );

                Vector3 uvs;
                Vector3 vertexPosition;

                if (useBorder) {
                    uvs = new Vector2((x - meshSimplificationIncrement) / (float)meshSizeUnsimplified, (y - meshSimplificationIncrement) / (float)meshSizeUnsimplified);
                    vertexPosition = new Vector3(topLeftX + percent.x * (meshSizeUnsimplified - 1), finalHeight, topLeftZ - percent.y * (meshSizeUnsimplified - 1));
                } else {
                    uvs = new Vector2(x / (float)borderedSize, y / (float)borderedSize);
                    vertexPosition = new Vector3(topLeftX + x, finalHeight, topLeftZ - y);
                }


                meshData.AddVertex(vertexPosition, uvs, vertexIndex);       // za vsak vertex dodamo 2 trikotnika (1 kvadrat) na chunk mesh

                // with this we are ignoring the top and the right edge vertices - because in the last row (column) there is no triangles to make (ni nobene vrstice oglisc pod / bolj desno od zadnje)
                if (x < borderedSize - 1 && y < borderedSize - 1) {
                    // we are creating 2 triangles of the mesh (square unit)
                    int a = vertexIndicesMap[x, y];                                                                 // zgoraj levo
                    int b = vertexIndicesMap[x + meshSimplificationIncrement, y];                                   // zgoraj desno
                    int c = vertexIndicesMap[x, y + meshSimplificationIncrement];                                   // spodaj levo
                    int d = vertexIndicesMap[x + meshSimplificationIncrement, y + meshSimplificationIncrement];     // spodaj desno
                    meshData.AddTriangle(a, d, c);                                                                  //dodamo prvi trikotnik (oglisca clock-wise)
                    meshData.AddTriangle(d, a, b);                                                                  //dodamo drugi trikotnik (skupaj s prvim tvori en kvadrat)
                }


                //vertexIndex++;
            }
        }

        return meshData;
    }

    // this method is used to get the height of the mesh at a given local position (x, z) in the mesh
    // to potrebujemo zato da lahko neodvisno od LOD-a dobimo tocke na mesh-u (za vegetacijo)
    // (ker pri velikem LOD se nekatere tocke za visino ne izracunajo)
    // heightMap je 241x241 dimenzije
    public static float GetHeightAtChunkPosition(int localX, int localZ, float[,] heightMap, float[,,] biomeWeightsMap, Biome[] biomes, AnimationCurve[] biomesMeshHeightCurve) {
        float heightCurveEval = 0;
        float heightMultiplier = 0;
        for (int i = 0; i < biomes.Length; i++) {
            heightCurveEval += biomeWeightsMap[localX, localZ, i] * biomesMeshHeightCurve[i].Evaluate(heightMap[localX, localZ]);
            heightMultiplier += biomeWeightsMap[localX, localZ, i] * biomes[i].biomeMeshHeightMultiplier;
        }

        float finalHeight = heightCurveEval * heightMultiplier;
        return finalHeight;
    }

    // metodo potrebujemo da ustvari kopije AnimationCurve-ov zato da jih lahko uporabimo na multi-threaded environmentu (ker AnimationCurve.Evaluate() ni thread safe)
    public static AnimationCurve[] GetBiomeHeightCurvesArray(Biome[] biomes) {
        AnimationCurve[] biomesMeshHeightCurve = new AnimationCurve[biomes.Length];
        for (int i = 0; i < biomes.Length; i++) {
            biomesMeshHeightCurve[i] = new AnimationCurve(biomes[i].biomeMeshHeightCurve.keys); // to je potrebno, da lahko uporabljamo heightCurve.Evaluate() v vseh threadih na enkrat, ker AnimationCurve ni thread safe, zato moramo ustvariti kopijo (drugace imamo race condition)
        }
        return biomesMeshHeightCurve;
    }

    // this method is used to get the interpolated height of the mesh at a given local position (x, z) in the mesh (using bilinear interpolation)
    public static float InterpolateHeightAtPosition(float localX, float localZ, float[,] heightMap, float[,,] biomeWeightsMap, Biome[] biomes, AnimationCurve[] biomesMeshHeightCurve) {
        // Clamp so we don't go out of bounds
        float gridX = Mathf.Clamp(localX, 0, MapGenerator.mapChunkSize - 1.0001f);
        float gridZ = Mathf.Clamp(localZ, 0, MapGenerator.mapChunkSize - 1.0001f);

        // Get the integer coordinates of the 4 surrounding points
        int x0 = Mathf.FloorToInt(gridX);
        int z0 = Mathf.FloorToInt(gridZ);
        int x1 = x0 + 1;
        int z1 = z0 + 1;

        float tx = gridX - x0;
        float tz = gridZ - z0;

        // Get the 4 surrounding points directly from the raw heightmap array
        float h00 = GetHeightAtChunkPosition(x0, z0, heightMap, biomeWeightsMap, biomes, biomesMeshHeightCurve);
        float h10 = GetHeightAtChunkPosition(x1, z0, heightMap, biomeWeightsMap, biomes, biomesMeshHeightCurve);
        float h01 = GetHeightAtChunkPosition(x0, z1, heightMap, biomeWeightsMap, biomes, biomesMeshHeightCurve);
        float h11 = GetHeightAtChunkPosition(x1, z1, heightMap, biomeWeightsMap, biomes, biomesMeshHeightCurve);

        // Bilinear interpolation
        float heightTop = Mathf.Lerp(h00, h10, tx);
        float heightBottom = Mathf.Lerp(h01, h11, tx);

        return Mathf.Lerp(heightTop, heightBottom, tz);
    }
}

/// <summary>
/// This class is used to store the mesh data (vertices, triangles, uvs) for a terrain chunk. It also has a method to create a Unity Mesh from the data. <br/>
/// public Vector3[ ] vertices; <br/>
/// public int[ ] triangles; <br/>
/// public Vector2[ ] uvs; <br/>
/// public float[,] finalHeightMap; <br/>
/// </summary>

// razred katerega objekt bo vseboval oglisca in trikotnike, ki bodo sestavljali teren
public class MeshData {
    public Vector3[] vertices;
    public int[] triangles;
    public Vector2[] uvs;

    Vector3[] borderVertices;
    int[] borderTriangles;

    int triangleIndex;
    int borderTriangleIndex;

    //public float[,] finalHeightMap;         // this is used to store the final heightmap of the mesh, which is used for vegetation placement
    public int verticesPerDim;             // stevilo oglisc v neki dimenziji (width and height the same)

    // konstruktor razreda
    public MeshData(int verticesPerDim) {
        this.verticesPerDim = verticesPerDim;

        vertices = new Vector3[verticesPerDim * verticesPerDim];                 // stevilo oglisc je zmnozek dimenzij (ploscina efektivno)   |  pri LOD = 0 je to 241 * 241       
        borderVertices = new Vector3[verticesPerDim * 4 + 4];                    // dodamo eno vrstico / stolpec na vsako smer (4 smeri) + eno oglisce na vsak vogal

        triangles = new int[(verticesPerDim - 1) * (verticesPerDim - 1) * 6];    // to je niz indeksov, vsaka zaporedna trojka opisuje en trikotnik... stevilo kvadratov v mrezi je (width-1)*(height-2), vsak kvadrat je iz dveh trikotnikov (*2), vsak trikotnik pa iz 3 oglisc (*3)
        borderTriangles = new int[24 * verticesPerDim];                          // poglej si E12 za razlago

        uvs = new Vector2[verticesPerDim * verticesPerDim];      // teksturne koordinate

        //finalHeightMap = new float[borderedSize, borderedSize];
    }

    public void AddVertex(Vector3 vertexPosition, Vector2 uv, int vertexIndex) {
        if (vertexIndex < 0) {
            borderVertices[-vertexIndex - 1] = vertexPosition;      // prvi border vertex ima "virtualni" indeks = -1 -> -(-1) - 1 = 0, naslednji ima -2 -> -(-2) - 1 = 1 itd...
        } else {
            vertices[vertexIndex] = vertexPosition;
            uvs[vertexIndex] = uv;
        }
    }

    // v niz indeksov dodamo 3 int-e ki predstavljajo 3 oglisca (indekse le-teh)
    public void AddTriangle(int a, int b, int c) {
        // ce je katerikoli vertex trikotnika del borderja (border vertex), potem je trikotnik tudi del obrobe
        if (a < 0 || b < 0 || c < 0) {
            borderTriangles[borderTriangleIndex] = a;
            borderTriangles[borderTriangleIndex + 1] = b;
            borderTriangles[borderTriangleIndex + 2] = c;
            borderTriangleIndex += 3;
        } else {
            triangles[triangleIndex] = a;
            triangles[triangleIndex + 1] = b;
            triangles[triangleIndex + 2] = c;
            triangleIndex += 3;
        }
    }

    Vector3[] CalculateNormals() {
        Vector3[] vertexNormals = new Vector3[vertices.Length];
        int triangleCount = triangles.Length / 3;
        for (int i = 0; i < triangleCount; i++) {
            int normalTriangleIndex = i * 3;

            // dobimo 3 oglisca ki tvorijo 1 trikotnik
            int vertexIndexA = triangles[normalTriangleIndex];
            int vertexIndexB = triangles[normalTriangleIndex + 1];
            int vertexIndexC = triangles[normalTriangleIndex + 2];

            Vector3 triangleNormal = SurfaceNormalFromIndices(vertexIndexA, vertexIndexB, vertexIndexC);
            vertexNormals[vertexIndexA] += triangleNormal;  // dodamo prispevek trikotnika ogliscu kateremu pripada (normala nekega oglisca je normalizirana vsota normal vseh trikotnikov kateri si ga delijo)
            vertexNormals[vertexIndexB] += triangleNormal;
            vertexNormals[vertexIndexC] += triangleNormal;
        }

        for (int i = 0; i < vertexNormals.Length; i++) {
            vertexNormals[i].Normalize();
        }

        int borderTriangleCount = borderTriangles.Length / 3;
        for (int i = 0; i < borderTriangleCount; i++) {
            int normalTriangleIndex = i * 3;

            // dobimo 3 oglisca ki tvorijo 1 trikotnik
            int vertexIndexA = borderTriangles[normalTriangleIndex];
            int vertexIndexB = borderTriangles[normalTriangleIndex + 1];
            int vertexIndexC = borderTriangles[normalTriangleIndex + 2];

            Vector3 triangleNormal = SurfaceNormalFromIndices(vertexIndexA, vertexIndexB, vertexIndexC);
            if (vertexIndexA >= 0) { vertexNormals[vertexIndexA] += triangleNormal; }  // dodamo prispevek trikotnika ogliscu kateremu pripada (normala nekega oglisca je normalizirana vsota normal vseh trikotnikov kateri si ga delijo)
            if (vertexIndexB >= 0) { vertexNormals[vertexIndexB] += triangleNormal; }
            if (vertexIndexC >= 0) { vertexNormals[vertexIndexC] += triangleNormal; }
        }

        for (int i = 0; i < vertexNormals.Length; i++) {
            vertexNormals[i].Normalize();
        }

        return vertexNormals;
    }

    // iz 3 oglisc nekega trikotnika izracunamo normalo tega trikotnika (vektorski produkt dveh vektorjev z isto izhodiscno tocko (A))
    Vector3 SurfaceNormalFromIndices(int indexA, int indexB, int indexC) {
        Vector3 pointA = (indexA < 0) ? borderVertices[-indexA - 1] : vertices[indexA];
        Vector3 pointB = (indexB < 0) ? borderVertices[-indexB - 1] : vertices[indexB];
        Vector3 pointC = (indexC < 0) ? borderVertices[-indexC - 1] : vertices[indexC];

        Vector3 sideAB = pointB - pointA;
        Vector3 sideAC = pointC - pointA;

        // to deluje (normala je pravilno orientirana), ker so oglisca trikotnika VEDNO (tako smo jih shranili) orientirana clock-wise. Unity uporablja levo-sucni sistem
        return Vector3.Cross(sideAB, sideAC).normalized;

    }

    // ustvarimo dejansko Mesh komponento
    public Mesh CreateMesh() {
        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.normals = CalculateNormals();
        //mesh.RecalculateNormals(); // zato da nam ni treba na roke racunati normal (sicer mi jih morali)
        return mesh;
    }

}
