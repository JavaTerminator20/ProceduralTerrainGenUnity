using UnityEngine;
using UnityEditor;
using Codice.Client.BaseCommands;
//

// this is an attribute, that tells Unity, whenever the user click on a GameObject that has a "MapGenerator"
// script attached to it, don't use default Inspector, but use this class to draw instead
[CustomEditor(typeof(MapGenerator))]

public class MapGeneratorEditor : Editor {  //inheriting from Editor, gives us access to special variable called "target" 

    //unity calls this every time your mouse moves over the Inspector window or a value changes
    public override void OnInspectorGUI() {

        //we do a cast so that we can access specific variables inside MapGenerator class
        MapGenerator mapGen = (MapGenerator)target;     // target represents the specific object you are currently looking at in the inspector

        //draws the default stuff (without this, it would be completely empty)
        if (DrawDefaultInspector()) {
            //sem pridemo, ce se katera vrednost spremeni
            if (mapGen.autoUpdate) {
                mapGen.DrawMapInEditor();
            }
        }

        if (GUILayout.Button("Generate")) {
            mapGen.DrawMapInEditor();
        }

        // if (GUILayout.Button("Start Threaded Generation")) {
        //     mapGen.StartThreadedGeneration();
        // }

        // if (GUILayout.Button("End Threaded Generation")) {
        //     mapGen.EndThreadedGeneration();
        // }
    }
}
