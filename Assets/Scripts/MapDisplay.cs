using UnityEditor.ShaderGraph;
using UnityEngine;

public class MapDisplay : MonoBehaviour {
    public Renderer textureRender;  //dobimo dostop do Mesh Renderer komponente od tal
    public MeshFilter meshFilter;
    public MeshRenderer meshRenderer;

    // here we create a texture has the same pixel count as our plane and we fill it with color according to noise map
    public void DrawTexture(Texture2D texture, float sampleInterval) {
        // we can't use textureRenderer.material, because this is isntatiated only at runtime, meanwhile sharedMaterial works even without pressing "play"
        textureRender.sharedMaterial.mainTexture = texture;
        textureRender.transform.localScale = new Vector3(-texture.width * sampleInterval / 10, 1, texture.height * sampleInterval / 10); //nastavimo velikost plane-a
    }


    //this draws single chunk
    public void DrawMesh(MeshData meshData, Texture2D texture) {
        meshFilter.sharedMesh = meshData.CreateMesh();
        meshRenderer.sharedMaterial.mainTexture = texture;
    }

    // this generates all the chunks that are inside view distance of a player
    public void DrawView() {
        EndlessTerrain endlessTerrain = FindAnyObjectByType<EndlessTerrain>();
        endlessTerrain.UpdateVisibleChunksInEditor();
    }

}
