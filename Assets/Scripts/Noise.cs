using System;
using TreeEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class Noise {

    public static float sampleInterval = 4f;

    public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, float scale, int octaves, float persistance, float lacunarity, int seed, Vector2 offset) {
        float[,] noiseMap = new float[mapWidth, mapHeight];     //float[,] naredi 2d array, kjer so vsi podarray-i enako dolgi, pri float[][] pa je lahko vsak sub-array drugacne dolzine (jagged 2d array)

        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];
        for (int i = 0; i < octaves; i++) {
            float offsetX = prng.Next(-10000, 10000) + offset.x;        // offset se uporablja samo zato, da lahko scrollamo cez noise map 
            float offsetY = prng.Next(-10000, 10000) - offset.y;
            octaveOffsets[i] = new Vector2(offsetX, offsetY);
        }

        if (scale <= 0) { scale = 0.0001f; }    // preprecimo deljenje z 0

        // float maxNoiseHeight = float.MinValue;
        // float minNoiseHeight = float.MaxValue;

        // we use global max and min for normalization (don't calculate it for each chunk separately - that makes seams between chunks)
        float maxNoiseHeight = 0f;
        float amp = 0.8f;
        for (int i = 0; i < octaves; i++) {
            maxNoiseHeight += amp;
            amp *= persistance;
        }
        float minNoiseHeight = -maxNoiseHeight;

        float halfHeight = mapHeight * sampleInterval / 2f;  // se uporablja za korekcijo pri spremembi noiseScale (da ne zoomiramo zgoraj desno ampak na sredino)
        float halfWidth = mapWidth * sampleInterval / 2f;

        // in here we actually fill in the noiseMap 2d array with height values
        for (int y = 0; y < mapHeight; y++) {
            for (int x = 0; x < mapWidth; x++) {
                float amplitude = 1;
                float frequency = 1;
                float noiseHeight = 0;

                for (int i = 0; i < octaves; i++) {
                    float sampleX = (x * sampleInterval - halfWidth + octaveOffsets[i].x) / scale * frequency;       // the higher the freq. the further apart our sample points will be - height value will change more rapidly
                    float sampleY = (y * sampleInterval - halfHeight + octaveOffsets[i].y) / scale * frequency;      // (y-halfHeight) se uporablja zato, da ko spreminjamo noiseScale, se premikamo proti sredini in ne proti zgornjem desnem kotu

                    float perlineValue = Mathf.PerlinNoise(sampleX, sampleY) * 2 - 1;  // perlinValue is in the rage of [-1, 1]     
                    noiseHeight += perlineValue * amplitude;

                    amplitude *= persistance;   // persistance <= 1 && >= 0  -> it decreases each octave
                    frequency *= lacunarity;    // lacunarity >= 1 -> it increases each octave
                }

                //if (noiseHeight > maxNoiseHeight) { maxNoiseHeight = noiseHeight; } // this is later used for normalization
                //if (noiseHeight < minNoiseHeight) { minNoiseHeight = noiseHeight; }

                noiseMap[x, y] = noiseHeight;
            }
        }

        // since noiseMap values aren't [0, 1] now, we need to normalize them
        for (int y = 0; y < mapHeight; y++) {
            for (int x = 0; x < mapWidth; x++) {
                noiseMap[x, y] = Mathf.InverseLerp(minNoiseHeight, maxNoiseHeight, noiseMap[x, y]);  // noisemap se spremeni v interpolirano vrednost na [0, 1], kjer je 0 minNoiseHeight, 1 pa maxNoiseHeight
            }
        }
        return noiseMap;

    }
}
