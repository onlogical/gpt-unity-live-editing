# Live Editing Demo

Import this sample in Package Manager and open LiveEditingDemo.unity. The prebuilt scene uses the Universal Render Pipeline. Alternatively choose **Tools > Local Unity Bridge > Create Demo Scene** to generate a fresh scene with materials for the project's current pipeline. Built-in and HDRP material generation have not been visually tested.

Press Play: three sculptures rotate at different speeds. Stop Play before persistent bridge edits.

Try these requests through the MCP tools or PowerShell client:

1. List the hierarchy and find Spinner 1.
2. Inspect its UnityLiveBridge.BridgeDemoSpinner component.
3. Set degreesPerSecond to "90".
4. Resize notes to four items, then edit notes.Array.data[3].
5. Assign focus to another object's Transform using its component ID.
6. Use a batch to duplicate and rename a sculpture, then Undo the whole batch.

Create Demo Scene opens an additional scene and saves to a unique path under Assets/UnityLiveBridgeDemo. It does not close your existing scenes. Play mode runs all loaded scenes, so isolate the sample when comparing its lighting or cameras.

The demo camera renders layer 30 to avoid overlap with ordinary layer-0 objects from other loaded scenes. If your project already uses that layer, open the sample by itself.
