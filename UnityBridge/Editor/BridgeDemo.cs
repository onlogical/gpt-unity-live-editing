using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static partial class LocalBridge
{
    [MenuItem("Tools/Local Unity Bridge/Create Demo Scene")]
    static void DemoMenu() { CreateDemo(); }
    static string CreateDemo() {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play mode before creating the demo.");
        const string folder = "Assets/UnityLiveBridgeDemo";
        Directory.CreateDirectory(folder); AssetDatabase.Refresh();
        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        var shader = Shader.Find(pipeline == null ? "Standard" : pipeline.GetType().Name.Contains("HDRender") ? "HDRP/Lit" : "Universal Render Pipeline/Lit");
        if(shader == null) throw new System.InvalidOperationException("Demo needs a Lit or Standard shader.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
        var root = new GameObject("UNITY LIVE BRIDGE · DEMO"); Undo.RegisterCreatedObjectUndo(root,"Create bridge demo");
        Material Mat(string name, Color color) {
            var path = folder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if(existing != null) return existing;
            var mat = new Material(shader) { name = name, color = color }; AssetDatabase.CreateAsset(mat,path); return mat;
        }
        var dark = Mat("Midnight",new Color(0.055f,0.08f,0.14f)); var cyan = Mat("Electric Cyan",new Color(0.1f,0.8f,1));
        var amber = Mat("Warm Amber",new Color(1,0.48f,0.12f)); var pale = Mat("Porcelain",new Color(0.75f,0.82f,0.92f));
        GameObject Shape(string name, PrimitiveType primitive, Vector3 position, Vector3 scale, Material material, Transform parent) {
            var go = GameObject.CreatePrimitive(primitive); go.name = name; go.transform.SetParent(parent); go.transform.position = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }
        Shape("Stage",PrimitiveType.Cube,new Vector3(0,-0.25f,0),new Vector3(12,0.5f,8),dark,root.transform);
        for(int i = -1; i <= 1; i++) {
            Shape("Pedestal " + (i + 2),PrimitiveType.Cylinder,new Vector3(i * 3,0.4f,0),new Vector3(2,0.4f,2),pale,root.transform);
            var pivot = new GameObject("Spinner " + (i + 2)); pivot.transform.SetParent(root.transform); pivot.transform.position = new Vector3(i * 3,1.8f,0);
            var spinner = pivot.AddComponent<UnityLiveBridge.BridgeDemoSpinner>(); spinner.degreesPerSecond = 25 + (i + 1) * 20; spinner.focus = root.transform;
            var shape = Shape("Editable Sculpture",i == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube,new Vector3(i * 3,1.8f,0),Vector3.one * 1.3f,i == 0 ? amber : cyan,pivot.transform);
            shape.transform.rotation = Quaternion.Euler(25,35,25);
            Shape("Accent",PrimitiveType.Sphere,new Vector3(i * 3 + 0.8f,2.6f,0),Vector3.one * 0.32f,amber,pivot.transform);
        }
        var cameraGo = new GameObject("Demo Camera"); cameraGo.transform.SetParent(root.transform); var camera = cameraGo.AddComponent<Camera>();
        cameraGo.transform.position = new Vector3(9,8,-12); cameraGo.transform.LookAt(new Vector3(0,1,0)); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.025f,0.04f,0.08f);
        var lightGo = new GameObject("Demo Key Light"); lightGo.transform.SetParent(root.transform); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.6f; lightGo.transform.rotation = Quaternion.Euler(45,-30,0);
        // Keep ordinary layer-0 objects from existing additive scenes out of the demo camera.
        foreach(var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
        camera.cullingMask = 1 << 30; camera.depth = 100; light.cullingMask = 1 << 30;
        var pathScene = AssetDatabase.GenerateUniqueAssetPath(folder + "/LiveEditingDemo.unity");
        if(!EditorSceneManager.SaveScene(scene,pathScene)) throw new IOException("Could not save demo scene.");
        AssetDatabase.SaveAssets(); Selection.activeGameObject = root; SceneView.lastActiveSceneView?.FrameSelected(); return pathScene;
    }
}
