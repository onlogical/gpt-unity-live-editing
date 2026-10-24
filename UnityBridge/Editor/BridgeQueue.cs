using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

public static partial class LocalBridge
{
    static string ObjectId(UnityEngine.Object obj) {
#if UNITY_6000_5_OR_NEWER
        return obj.GetEntityId().ToString();
#else
        return obj.GetInstanceID().ToString();
#endif
    }
    static bool ValidId(string id) => !string.IsNullOrEmpty(id) && Regex.IsMatch(id,"^[A-Za-z0-9_-]{1,100}$");
    static int QueueDepth() { try { return Directory.Exists(Path.Combine(Root,"queue")) ? Directory.GetFiles(Path.Combine(Root,"queue"),"*.json").Length : 0; } catch(IOException) { return 0; } }
    static bool Cancelled(string id) => ValidId(id) && File.Exists(Path.Combine(Root,"cancel",id + ".json"));
    static readonly HashSet<string> BatchActions = new HashSet<string> {
        "ping","hierarchy","inspect","create","create_empty","transform","rotate","rename","duplicate","delete","parent",
        "assign_material","resize_array","instantiate_prefab","add_component","set_property"
    };
    static string BatchId(string value, List<StepReply> results) {
        if(string.IsNullOrEmpty(value) || !value.StartsWith("$")) return value;
        int index = value == "$last" ? results.Count - 1 : int.Parse(value.Substring(1));
        if(index < 0 || index >= results.Count || string.IsNullOrEmpty(results[index].instanceId)) throw new ArgumentException("Batch reference must name a previous command that returned an object/component ID.");
        return results[index].instanceId;
    }
    static Reply Batch(Request request) {
        var reply = Status(); reply.id = request.id; reply.action = request.action;
        if(EditorApplication.isPlayingOrWillChangePlaymode) { reply.ok = false; reply.error = "Stop Play mode before batching edits."; return reply; }
        if(request.commands == null || request.commands.Length == 0 || request.commands.Length > 100) { reply.ok = false; reply.error = "Batch requires 1..100 scene commands."; return reply; }
        foreach(var command in request.commands) if(command == null || !BatchActions.Contains(command.action ?? "")) {
            reply.ok = false; reply.error = "Batch contains an unsupported action. Saving, asset/script writes, Play mode and nested batches are excluded."; return reply;
        }
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(string.IsNullOrEmpty(request.label) ? "Bridge batch" : request.label);
        try {
            foreach(var original in request.commands) {
                if(Cancelled(request.id)) throw new OperationCanceledException("Batch cancelled; its scene edits were rolled back.");
                var command = JsonUtility.FromJson<Request>(JsonUtility.ToJson(original));
                command.instanceId = BatchId(command.instanceId,reply.results); command.componentId = BatchId(command.componentId,reply.results);
                command.parentId = BatchId(command.parentId,reply.results); command.referenceId = BatchId(command.referenceId,reply.results);
                var result = Execute(command,false); reply.results.Add(JsonUtility.FromJson<StepReply>(JsonUtility.ToJson(result)));
                if(!result.ok) throw new InvalidOperationException(result.error);
            }
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup(); reply.outcome = "completed";
        } catch(Exception ex) {
            Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); Undo.IncrementCurrentGroup();
            reply.ok = false; reply.error = ex.Message; reply.outcome = ex is OperationCanceledException ? "cancelled" : "rolled_back";
        }
        return reply;
    }

    static bool recovered;
    static Reply transportReply;
    static string transportClaim;
    static void Record(Reply reply) {
        SessionState.SetString("LiveBridge.action",reply.action ?? "invalid request"); SessionState.SetString("LiveBridge.id",reply.id ?? "");
        SessionState.SetString("LiveBridge.at",DateTime.UtcNow.ToString("o")); SessionState.SetBool("LiveBridge.ok",reply.ok);
        if(!reply.ok) { errors.Add(reply.error); if(errors.Count > 50) errors.RemoveAt(0); }
    }
    static bool FlushTransport() {
        if(transportReply == null) return true;
        if(!Write("responses/" + transportReply.id + ".json",transportReply)) return false;
        // The claim is only removed after a durable, per-request response exists.
        try { if(File.Exists(transportClaim)) File.Delete(transportClaim); } catch(IOException) { return false; }
        SessionState.EraseString("LiveBridge.pending"); SessionState.EraseString("LiveBridge.claim");
        transportReply = null; transportClaim = null; return true;
    }
    static void PollTransport() {
        Directory.CreateDirectory(Path.Combine(Root,"queue")); Directory.CreateDirectory(Path.Combine(Root,"processing"));
        Directory.CreateDirectory(Path.Combine(Root,"responses")); Directory.CreateDirectory(Path.Combine(Root,"cancel"));
        if(!recovered) {
            recovered = true;
            string saved = SessionState.GetString("LiveBridge.pending","");
            if(!string.IsNullOrEmpty(saved)) { transportReply = JsonUtility.FromJson<Reply>(saved); transportClaim = SessionState.GetString("LiveBridge.claim",""); }
        }
        if(!FlushTransport()) return;
        if(EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        // A surviving claim without a stored reply has an uncertain outcome: never replay its mutation.
        var abandoned = Directory.GetFiles(Path.Combine(Root,"processing"),"*.json");
        if(abandoned.Length > 0) {
            transportClaim = abandoned[0]; var id = Path.GetFileNameWithoutExtension(transportClaim);
            if(!ValidId(id)) { File.Move(transportClaim,transportClaim + ".invalid"); return; }
            if(File.Exists(Path.Combine(Root,"responses",id + ".json"))) { File.Delete(transportClaim); return; }
            transportReply = Status(); transportReply.id = id; transportReply.ok = false; transportReply.outcome = "interrupted";
            transportReply.error = "Editor interrupted this request. Some edits may have applied. Inspect the scene before retrying; it was not replayed.";
            SavePending(); FlushTransport(); return;
        }
        var queued = Directory.GetFiles(Path.Combine(Root,"queue"),"*.json"); Array.Sort(queued,StringComparer.Ordinal);
        string legacy = Path.Combine(Root,"request.json");
        if(queued.Length == 0) { if(File.Exists(legacy)) ProcessLegacy(legacy); return; }
        string incoming = queued[0]; Request request;
        try { request = JsonUtility.FromJson<Request>(File.ReadAllText(incoming)); }
        catch(Exception ex) when(ex is IOException || ex is ArgumentException) { File.Move(incoming,incoming + ".invalid"); return; }
        if(request == null || !ValidId(request.id)) { File.Move(incoming,incoming + ".invalid"); return; }
        string response = Path.Combine(Root,"responses",request.id + ".json");
        if(File.Exists(response)) { File.Delete(incoming); return; } // Resubmitted IDs use their existing result, never execute again.
        transportClaim = Path.Combine(Root,"processing",request.id + ".json");
        try { File.Move(incoming,transportClaim); } catch(IOException) { return; }
        if(Cancelled(request.id)) {
            transportReply = Status(); transportReply.id = request.id; transportReply.action = request.action;
            transportReply.ok = false; transportReply.outcome = "cancelled"; transportReply.error = "Cancelled before execution.";
        } else transportReply = request.action == "batch" ? Batch(request) : Execute(request);
        if(string.IsNullOrEmpty(transportReply.outcome)) transportReply.outcome = transportReply.ok ? "completed" : "failed";
        Record(transportReply); SavePending(); FlushTransport();
    }
    static void SavePending() { SessionState.SetString("LiveBridge.pending",JsonUtility.ToJson(transportReply)); SessionState.SetString("LiveBridge.claim",transportClaim ?? ""); }
    static void ProcessLegacy(string path) {
        Reply reply;
        try {
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(path)); File.Delete(path);
            if(request == null || !ValidId(request.id)) throw new ArgumentException("Request requires an ID containing letters, numbers, hyphens or underscores.");
            reply = request.action == "batch" ? Batch(request) : Execute(request);
        } catch(Exception ex) { reply = Status(); reply.ok = false; reply.error = ex.ToString(); }
        Finish(reply);
    }
}
