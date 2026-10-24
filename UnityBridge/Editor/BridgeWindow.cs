using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;

public static partial class LocalBridge
{
    public static string StatusJson => JsonUtility.ToJson(Status(),true);
    static LocalBridgeCompilationHooks hooks = new LocalBridgeCompilationHooks();
    class LocalBridgeCompilationHooks {
        public LocalBridgeCompilationHooks() {
            CompilationPipeline.compilationStarted += _ => { errors.Clear(); Write("status.json",Status()); };
            CompilationPipeline.assemblyCompilationFinished += (path,messages) => {
                foreach(var message in messages) if(message.type == CompilerMessageType.Error) {
                    errors.Add(message.file + ":" + message.line + ": " + message.message);
                    if(errors.Count > 50) errors.RemoveAt(0);
                }
                Write("status.json",Status());
            };
        }
    }
}

public class BridgeWindow : EditorWindow
{
    Vector2 scroll;
    [MenuItem("Tools/Local Unity Bridge/Connection")]
    public static void Open() { GetWindow<BridgeWindow>("Unity Live Bridge"); }
    void OnEnable() { EditorApplication.update += Tick; minSize = new Vector2(340,300); }
    void OnDisable() { EditorApplication.update -= Tick; }
    double next;
    void Tick() { if(EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 0.5; Repaint(); }
    void OnGUI() {
        var status = JsonUtility.FromJson<LocalBridge.Reply>(LocalBridge.StatusJson);
        EditorGUILayout.Space(); EditorGUILayout.LabelField("GPT Unity Live Editing",EditorStyles.boldLabel);
        string state = status.compiling ? "Compiling scripts…" : status.updating ? "Importing assets…" : status.playing ? "Ready · Play mode" : "Ready · Edit mode";
        EditorGUILayout.HelpBox(state,MessageType.Info);
        bool active = DateTime.TryParse(status.lastCommandAt,out var time) && (DateTime.UtcNow - time.ToUniversalTime()).TotalSeconds < 30;
        EditorGUILayout.LabelField("Client",active ? "Connected · recent command" : "Waiting for commands");
        EditorGUILayout.LabelField("Transport","Local files · 250 ms");
        EditorGUILayout.LabelField("Last command",string.IsNullOrEmpty(status.lastAction) ? "None yet" : status.lastAction);
        EditorGUILayout.LabelField("Result",string.IsNullOrEmpty(status.lastAction) ? "—" : status.lastCommandOk ? "Succeeded" : "Failed");
        EditorGUILayout.SelectableLabel(status.lastCommandId ?? "",GUILayout.Height(18));
        EditorGUILayout.LabelField("Completed (UTC)",DateTime.TryParse(status.lastCommandAt,out var completed) ? completed.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—");
        EditorGUILayout.Space(); EditorGUILayout.LabelField("Captured errors (" + status.errors.Count + ")",EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach(var error in status.errors) EditorGUILayout.HelpBox(error,MessageType.Error);
        if(status.errors.Count == 0) EditorGUILayout.LabelField("No captured errors.");
        EditorGUILayout.EndScrollView();
        EditorGUILayout.HelpBox("Connected means a client sent a command in the last 30 seconds. Script edits require normal Unity compilation.",MessageType.None);
    }
}
