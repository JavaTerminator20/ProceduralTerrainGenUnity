using UnityEngine;
using UnityEngine.UIElements;

public static class TextureGenerator {
    // here we create texture from the colorMap
    public static Texture2D TextureFromColorMap(Color[] colorMap, int width, int height) {
        Texture2D texture = new Texture2D(width, height);
        texture.filterMode = FilterMode.Point;  // naredimo da so pixeli jasni (sharp edges)
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels(colorMap);
        texture.Apply();

        return texture;
    }

    // here we create a colorMap from heightMap and then call the method above to create texture
    public static Texture2D TextureFromHeightMap(float[,] heightMap, MapGenerator.DrawMode drawMode) {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        // it's faster to create an array for all of the colors (for all pixels) and then set it to the texutre (rather than setting pixel by pixel)
        Color[] colorMap = new Color[width * height];
        for (int y = 0; y < height; y += 1) {
            for (int x = 0; x < width; x += 1) {
                float clrVal = heightMap[x, y];

                if (drawMode == MapGenerator.DrawMode.TemperatureMap) {
                    colorMap[y * width + x] = new Color(clrVal, 0.0f, 0.0f, 1.0f);
                } else if (drawMode == MapGenerator.DrawMode.HumidityMap) {
                    colorMap[y * width + x] = new Color(0.0f, 0.0f, clrVal, 1.0f);
                } else {
                    colorMap[y * width + x] = new Color(clrVal, clrVal, clrVal, 1.0f);
                }

            }
        }

        return TextureFromColorMap(colorMap, width, height);
    }
}
