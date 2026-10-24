using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Reflection;
public static class CompatibilitySmoke {
 public static void Run() {
  try {
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
   var method=typeof(LocalBridge).GetMethod("Execute",BindingFlags.NonPublic|BindingFlags.Static);
   Func<LocalBridge.Request,LocalBridge.Reply> call=r=>(LocalBridge.Reply)method.Invoke(null,new object[]{r,true});
   var created=call(new LocalBridge.Request {action="create",name="Compatibility Cube",primitive="Cube",scale=Vector3.one});
   if(!created.ok || string.IsNullOrEmpty(created.instanceId)) throw new Exception("Create failed: "+created.error);
   var rotated=call(new LocalBridge.Request {action="rotate",instanceId=created.instanceId,rotation=new Vector3(0,45,0)});
   if(!rotated.ok) throw new Exception(rotated.error);
   var inspected=call(new LocalBridge.Request {action="inspect",instanceId=created.instanceId,componentType="UnityEngine.Transform"});
   if(!inspected.ok || inspected.properties.Count==0) throw new Exception("Inspector failed");
   var batchMethod=typeof(LocalBridge).GetMethod("Batch",BindingFlags.NonPublic|BindingFlags.Static);
   var batch=(LocalBridge.Reply)batchMethod.Invoke(null,new object[]{new LocalBridge.Request{action="batch",commands=new LocalBridge.Command[]{new LocalBridge.Command{action="create_empty",name="Batch Compatibility"},new LocalBridge.Command{action="rename",instanceId="$0",name="Batch Renamed"}}}});
   if(!batch.ok) throw new Exception(batch.error);
   call(new LocalBridge.Request{action="undo"});
   var hierarchy=call(new LocalBridge.Request{action="hierarchy"});
   foreach(var go in hierarchy.objects) if(go.name=="Batch Renamed") throw new Exception("Grouped Undo failed");
   File.WriteAllText("compatibility-result.json","{\"ok\":true,\"unityVersion\":\""+Application.unityVersion+"\",\"checks\":5}");
   EditorApplication.Exit(0);
  } catch(Exception ex) {File.WriteAllText("compatibility-result.json",ex.ToString());EditorApplication.Exit(1);}
 }
}
