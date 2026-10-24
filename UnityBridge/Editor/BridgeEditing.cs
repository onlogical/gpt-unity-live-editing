using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static partial class LocalBridge
{
    static UnityEngine.Object ResolveTarget(GameObject go, Request request) {
        if(!string.IsNullOrEmpty(request.componentId)) {
            foreach(var component in go.GetComponents<Component>())
                if(component != null && ObjectId(component) == request.componentId) return component;
            throw new ArgumentException("Component ID does not belong to this object.");
        }
        if(string.IsNullOrEmpty(request.componentType)) return go;
        var matches = go.GetComponents(ComponentType(request.componentType));
        if(request.componentIndex < 0 || request.componentIndex >= matches.Length) throw new ArgumentException("Component index is out of range.");
        return matches[request.componentIndex];
    }

    static UnityEngine.Object ResolveReference(Request request) {
        if(!string.IsNullOrEmpty(request.referenceId)) {
            foreach(var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
                if(ObjectId(go) == request.referenceId) return go;
                foreach(var component in go.GetComponents<Component>())
                    if(component != null && ObjectId(component) == request.referenceId) return component;
            }
            throw new ArgumentException("Scene reference not found.");
        }
        if(string.IsNullOrEmpty(request.value) || request.value == "null") return null;
        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetPath(request.value));
        if(asset == null) throw new ArgumentException("Asset not found.");
        return asset;
    }

    static bool CanWrite(SerializedPropertyType type) {
        switch(type) {
            case SerializedPropertyType.Integer: case SerializedPropertyType.Float: case SerializedPropertyType.Boolean:
            case SerializedPropertyType.String: case SerializedPropertyType.Enum: case SerializedPropertyType.Vector3:
            case SerializedPropertyType.Color: case SerializedPropertyType.ObjectReference: case SerializedPropertyType.ArraySize: case SerializedPropertyType.LayerMask: return true;
            default: return false;
        }
    }

    static bool HandleExtended(Request request, Reply reply) {
        switch(request.action) {
            case "open_panel": BridgeWindow.Open(); return true;
            case "clear_errors": errors.Clear(); return true;
            case "demo": reply.scenePath = CreateDemo(); return true;
            case "rotate": case "rename": case "duplicate": case "delete": case "parent":
            case "assign_material": case "resize_array": case "instantiate_prefab": case "create_material": break;
            default: return false;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before editing.");
        if(request.action == "create_material") {
            var path = AssetPath(request.assetPath);
            if(!path.EndsWith(".mat") || System.IO.File.Exists(path)) throw new ArgumentException("Choose a new .mat asset path.");
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if(shader == null) throw new InvalidOperationException("No supported material shader found.");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var material = new Material(shader); material.name = request.name ?? "Bridge Material";
            AssetDatabase.CreateAsset(material,path); AssetDatabase.SaveAssets(); return true;
        }
        if(request.action == "instantiate_prefab") {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath(request.assetPath));
            if(asset == null || !PrefabUtility.IsPartOfPrefabAsset(asset)) throw new ArgumentException("Prefab asset not found.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            Undo.RegisterCreatedObjectUndo(instance,"Bridge instantiate prefab"); instance.transform.position = request.position;
            if(!string.IsNullOrEmpty(request.name)) instance.name = request.name;
            reply.instanceId = ObjectId(instance); EditorSceneManager.MarkSceneDirty(instance.scene); return true;
        }
        var go = FindObject(request.instanceId); var scene = go.scene;
        reply.instanceId = ObjectId(go);
        switch(request.action) {
            case "rotate": Undo.RecordObject(go.transform,"Bridge rotate"); go.transform.eulerAngles = request.rotation; PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform); break;
            case "rename":
                if(string.IsNullOrWhiteSpace(request.name)) throw new ArgumentException("Name cannot be empty.");
                Undo.RecordObject(go,"Bridge rename"); go.name = request.name; PrefabUtility.RecordPrefabInstancePropertyModifications(go); break;
            case "duplicate":
                var clone = UnityEngine.Object.Instantiate(go,go.transform.parent); Undo.RegisterCreatedObjectUndo(clone,"Bridge duplicate");
                clone.name = string.IsNullOrEmpty(request.name) ? go.name + " Copy" : request.name;
                reply.instanceId = ObjectId(clone); break;
            case "delete": Undo.DestroyObjectImmediate(go); break;
            case "parent":
                var parent = string.IsNullOrEmpty(request.parentId) ? null : FindObject(request.parentId).transform;
                if(parent == go.transform || (parent != null && parent.IsChildOf(go.transform))) throw new ArgumentException("Parent would create a hierarchy cycle.");
                Undo.SetTransformParent(go.transform,parent,request.worldPositionStays,"Bridge parent"); break;
            case "assign_material":
                var renderer = ResolveTarget(go,request) as Renderer;
                if(renderer == null && string.IsNullOrEmpty(request.componentId) && string.IsNullOrEmpty(request.componentType)) renderer = go.GetComponent<Renderer>();
                if(renderer == null) throw new ArgumentException("Renderer not found.");
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetPath(request.assetPath));
                if(material == null) throw new ArgumentException("Material not found.");
                var materials = renderer.sharedMaterials;
                if(request.materialIndex < 0 || request.materialIndex >= materials.Length) throw new ArgumentOutOfRangeException("Material slot is out of range.");
                Undo.RecordObject(renderer,"Bridge assign material"); materials[request.materialIndex] = material; renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); break;
            case "resize_array":
                var target = ResolveTarget(go,request); var serialized = new SerializedObject(target); var array = serialized.FindProperty(request.propertyPath);
                if(array == null || !array.isArray || array.propertyType == SerializedPropertyType.String || !array.editable) throw new ArgumentException("Editable array or list property required.");
                if(request.arraySize < 0 || request.arraySize > 10000) throw new ArgumentOutOfRangeException("Array size must be 0..10000.");
                Undo.RecordObject(target,"Bridge resize array"); array.arraySize = request.arraySize; serialized.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(target); break;
        }
        EditorSceneManager.MarkSceneDirty(scene); return true;
    }
}
