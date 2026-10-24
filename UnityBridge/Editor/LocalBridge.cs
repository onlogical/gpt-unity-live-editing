using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public static partial class LocalBridge
{
    [Serializable] public class Request { public string id, action, name, primitive, scenePath; public string instanceId, componentId, componentType, propertyPath, value, assetPath, source, parentId, referenceId; public int componentIndex, arraySize, materialIndex; public bool worldPositionStays = true; public Vector3 position, rotation; public Vector3 scale = Vector3.one; }
    [Serializable] public class Item { public string instanceId, parentId; public string name; public Vector3 position, rotation, scale; }
    [Serializable] public class ComponentInfo { public string id, type; public int index; }
    [Serializable] public class Reply { public string id, action, error, timestamp, lastAction, lastCommandId, lastCommandAt; public bool ok, playing, compiling, updating, lastCommandOk; public string instanceId; public List<PropertyInfo> properties = new List<PropertyInfo>(); public List<ComponentInfo> componentDetails = new List<ComponentInfo>(); public List<string> components = new List<string>(); public List<Item> objects = new List<Item>(); public List<string> errors = new List<string>(); }
    [Serializable] public class PropertyInfo { public string path, type, value, referenceId; public bool editable, supported, isArray; public int arraySize; }
    static GameObject FindObject(string id) {
        foreach(var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include,FindObjectsSortMode.None)) if(go.GetEntityId().ToString() == id) return go;
        throw new ArgumentException("Scene object not found: " + id);
    }
    static Type ComponentType(string name) {
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var type = assembly.GetType(name);
            if(type != null && typeof(Component).IsAssignableFrom(type)) return type;
        }
        throw new ArgumentException("Use a fully qualified component type, e.g. UnityEngine.Rigidbody.");
    }
    static string AssetPath(string path) {
        if(string.IsNullOrEmpty(path) || !path.StartsWith("Assets/") || path.Contains("..") || path.Contains("\\")) throw new ArgumentException("Use a project-relative Assets/ path.");
        return path;
    }
    static void Inspector(Request request, Reply reply, bool write) {
        var go = FindObject(request.instanceId);
        var counts = new Dictionary<Type,int>();
        foreach(var comp in go.GetComponents<Component>()) if(comp != null) {
            var type = comp.GetType(); int index = counts.TryGetValue(type,out int count) ? count : 0; counts[type] = index + 1;
            reply.components.Add(type.FullName);
            reply.componentDetails.Add(new ComponentInfo { id = comp.GetEntityId().ToString(), type = type.FullName, index = index });
        }
        UnityEngine.Object target = ResolveTarget(go,request);
        if(target == null) throw new ArgumentException("Component not found on object.");
        var serialized = new SerializedObject(target);
        if(write) {
            var property = serialized.FindProperty(request.propertyPath);
            if(property == null || !property.editable) throw new ArgumentException("Property is missing or read-only.");
            Undo.RecordObject(target,"Bridge inspector edit");
            switch(property.propertyType) {
                case SerializedPropertyType.ArraySize:
                    int size = int.Parse(request.value); if(size < 0 || size > 10000) throw new ArgumentOutOfRangeException("Array size must be 0..10000."); property.intValue = size; break;
                case SerializedPropertyType.Integer: property.longValue = long.Parse(request.value,System.Globalization.CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Float: property.doubleValue = double.Parse(request.value,System.Globalization.CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Boolean: property.boolValue = bool.Parse(request.value); break;
                case SerializedPropertyType.String: property.stringValue = request.value; break;
                case SerializedPropertyType.Enum:
                    int enumIndex = Array.IndexOf(property.enumNames,request.value);
                    if(enumIndex < 0) throw new ArgumentException("Unknown enum name."); property.enumValueIndex = enumIndex; break;
                case SerializedPropertyType.Vector3: property.vector3Value = JsonUtility.FromJson<Vector3>(request.value); break;
                case SerializedPropertyType.Color: property.colorValue = JsonUtility.FromJson<Color>(request.value); break;
                case SerializedPropertyType.ObjectReference:
                    var reference = ResolveReference(request);
                    property.objectReferenceValue = reference;
                    if(reference != null && property.objectReferenceValue != reference) throw new ArgumentException("Reference type does not match this field."); break;
                default: throw new NotSupportedException("Property type not supported for writing: " + property.propertyType);
            }
            serialized.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorSceneManager.MarkSceneDirty(go.scene);
        }
        var iterator = serialized.GetIterator();
        while(iterator.NextVisible(true)) {
            string value = "";
            switch(iterator.propertyType) {
                case SerializedPropertyType.ArraySize: value = iterator.intValue.ToString(); break;
                case SerializedPropertyType.Integer: value = iterator.longValue.ToString(); break;
                case SerializedPropertyType.Float: value = iterator.doubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Boolean: value = iterator.boolValue.ToString(); break;
                case SerializedPropertyType.String: value = iterator.stringValue; break;
                case SerializedPropertyType.Vector3: value = JsonUtility.ToJson(iterator.vector3Value); break;
                case SerializedPropertyType.Color: value = JsonUtility.ToJson(iterator.colorValue); break;
                case SerializedPropertyType.Enum: value = iterator.enumValueIndex >= 0 && iterator.enumValueIndex < iterator.enumNames.Length ? iterator.enumNames[iterator.enumValueIndex] : ""; break;
                case SerializedPropertyType.ObjectReference: value = iterator.objectReferenceValue == null ? "null" : AssetDatabase.GetAssetPath(iterator.objectReferenceValue); break;
            }
            reply.properties.Add(new PropertyInfo { path = iterator.propertyPath, type = iterator.propertyType.ToString(), value = value, editable = iterator.editable, supported = CanWrite(iterator.propertyType), isArray = iterator.isArray && iterator.propertyType != SerializedPropertyType.String, arraySize = iterator.isArray && iterator.propertyType != SerializedPropertyType.String ? iterator.arraySize : 0, referenceId = iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue != null ? iterator.objectReferenceValue.GetEntityId().ToString() : "" });
        }
    }
    static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../.codex-bridge"));
    static List<string> errors = new List<string>();
    static double next;
    static LocalBridge() { EditorApplication.update += Poll; Application.logMessageReceived += Log; }
    static void Log(string message, string stack, LogType type) { if(type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors.Add(message + "\n" + stack); if(errors.Count > 50) errors.RemoveAt(0); } }
    static Reply pendingReply;
    static bool Write(string name, Reply reply) {
        try {
            Directory.CreateDirectory(Root); var path = Path.Combine(Root,name);
            File.WriteAllText(path + ".tmp",JsonUtility.ToJson(reply,true));
            if(File.Exists(path)) File.Replace(path + ".tmp",path,null); else File.Move(path + ".tmp",path);
            return true;
        } catch(IOException) { return false; } // Readers or antivirus may briefly hold a Windows file handle. Retry on the next tick.
    }
    static Reply Status() => new Reply { ok = true, playing = EditorApplication.isPlaying, compiling = EditorApplication.isCompiling, updating = EditorApplication.isUpdating, timestamp = DateTime.UtcNow.ToString("o"), errors = new List<string>(errors), lastAction = SessionState.GetString("LiveBridge.action",""), lastCommandId = SessionState.GetString("LiveBridge.id",""), lastCommandAt = SessionState.GetString("LiveBridge.at",""), lastCommandOk = SessionState.GetBool("LiveBridge.ok",true) };
    [MenuItem("Tools/Local Unity Bridge/Status")]
    static void ShowStatus() { BridgeWindow.Open(); }
    static void Poll() {
        if(EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 0.25;
        if(pendingReply != null) { if(!Write("response.json",pendingReply)) return; pendingReply = null; }
        Write("status.json", Status());
        string path = Path.Combine(Root,"request.json");
        if(!File.Exists(path) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var reply = Status();
        try {
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(path)); File.Delete(path);
            reply.id = request.id; reply.action = request.action;
            if(request.action != "undo") { Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Bridge " + request.action); }
            if(HandleExtended(request,reply)) { Finish(reply); return; }
            switch(request.action) {
                case "create_empty":
                    if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
                    var empty = new GameObject(request.name ?? "New Object"); Undo.RegisterCreatedObjectUndo(empty,"Bridge create empty");
                    empty.transform.position = request.position; reply.instanceId = empty.GetEntityId().ToString(); EditorSceneManager.MarkSceneDirty(empty.scene); break;
                case "add_component":
                    if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
                    var owner = FindObject(request.instanceId); var added = Undo.AddComponent(owner,ComponentType(request.componentType)); if(added == null) throw new InvalidOperationException("Unity refused this component."); reply.instanceId = added.GetEntityId().ToString(); EditorSceneManager.MarkSceneDirty(owner.scene); break;
                case "inspect": Inspector(request,reply,false); break;
                case "set_property":
                    if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
                    Inspector(request,reply,true); break;
                case "write_script":
                    var scriptPath = AssetPath(request.assetPath);
                    if(!scriptPath.EndsWith(".cs")) throw new ArgumentException("Script path must end with .cs.");
                    if(File.Exists(scriptPath)) throw new IOException("Script already exists; edit the source file directly.");
                    Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)); File.WriteAllText(scriptPath,request.source ?? ""); AssetDatabase.Refresh(); break;
                case "save_prefab":
                    if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
                    var prefabPath = AssetPath(request.assetPath);
                    if(!prefabPath.EndsWith(".prefab")) throw new ArgumentException("Prefab path must end with .prefab.");
                    if(File.Exists(prefabPath)) throw new IOException("Prefab already exists.");
                    Directory.CreateDirectory(Path.GetDirectoryName(prefabPath)); PrefabUtility.SaveAsPrefabAsset(FindObject(request.instanceId),prefabPath); break;
                case "undo": if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first."); Undo.PerformUndo(); break;
                case "ping": break;
                case "refresh": AssetDatabase.Refresh(); break;
                case "hierarchy":
                    foreach(var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
                        if(go.scene.IsValid()) reply.objects.Add(new Item { instanceId = go.GetEntityId().ToString(), parentId = go.transform.parent == null ? "" : go.transform.parent.gameObject.GetEntityId().ToString(), name = go.name, position = go.transform.position, rotation = go.transform.eulerAngles, scale = go.transform.localScale });
                    break;
                case "create":
                    if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before persistent scene edits.");
                    var goNew = GameObject.CreatePrimitive((PrimitiveType)Enum.Parse(typeof(PrimitiveType),request.primitive ?? "Cube",true));
                    Undo.RegisterCreatedObjectUndo(goNew,"Bridge create"); goNew.name = request.name ?? "Bridge Object";
                    goNew.transform.position = request.position; goNew.transform.localScale = request.scale;
                    Selection.activeGameObject = goNew; reply.instanceId = goNew.GetEntityId().ToString(); EditorSceneManager.MarkSceneDirty(goNew.scene); SceneView.lastActiveSceneView?.FrameSelected(); break;
                case "transform":
                    if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before persistent scene edits.");
                    GameObject target = null; foreach(var candidate in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if(candidate.GetEntityId().ToString() == request.instanceId) { target = candidate; break; }
                    if(target == null) throw new InvalidOperationException("Object not found.");
                    Undo.RecordObject(target.transform,"Bridge transform"); target.transform.position = request.position; target.transform.localScale = request.scale;
                    reply.instanceId = target.GetEntityId().ToString(); EditorSceneManager.MarkSceneDirty(target.scene); break;
                case "save":
                    if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before saving.");
                    if(!string.IsNullOrEmpty(request.scenePath) && (!request.scenePath.StartsWith("Assets/") || !request.scenePath.EndsWith(".unity") || request.scenePath.Contains(".."))) throw new ArgumentException("Use an Assets/ scene path.");
                    if(!EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),request.scenePath ?? "")) throw new IOException("Scene save failed.");
                    AssetDatabase.SaveAssets(); break;
                case "play": EditorApplication.isPlaying = true; break;
                case "stop": EditorApplication.isPlaying = false; break;
                default: throw new ArgumentException("Unknown action: " + request.action);
            }
        } catch(Exception ex) { reply.ok = false; reply.error = ex.ToString(); }
        Finish(reply);
    }
    static void Finish(Reply reply) {
        Undo.FlushUndoRecordObjects();
        SessionState.SetString("LiveBridge.action",reply.action ?? "invalid request"); SessionState.SetString("LiveBridge.id",reply.id ?? "");
        SessionState.SetString("LiveBridge.at",DateTime.UtcNow.ToString("o")); SessionState.SetBool("LiveBridge.ok",reply.ok);
        if(!reply.ok) { errors.Add(reply.error); if(errors.Count > 50) errors.RemoveAt(0); }
        if(!Write("response.json",reply)) pendingReply = reply;
    }
}


