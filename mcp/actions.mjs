import { z } from 'zod';
const vector = z.object({ x: z.number().finite(), y: z.number().finite(), z: z.number().finite() });
const id = z.string().regex(/^[A-Za-z0-9_-]{1,100}$/);
const target = { instanceId: id.describe('GameObject ID from unity_hierarchy') };
const selector = { componentId: id.optional(), componentType: z.string().optional(), componentIndex: z.number().int().nonnegative().optional() };
const assetPath = z.string().startsWith('Assets/');
export const actions = {
  ping: { description: 'Check that Unity processes commands.', shape: {}, readOnly: true },
  hierarchy: { description: 'List loaded scene GameObjects, transforms and parent IDs. Refresh IDs after scene reloads.', shape: {}, readOnly: true },
  inspect: { description: 'Read Inspector fields and component IDs. Select an exact component by ID or type and index.', shape: { ...target, ...selector }, readOnly: true },
  create: { description: 'Create a scene primitive; returns its GameObject ID.', shape: { name: z.string(), primitive: z.enum(['Cube','Sphere','Capsule','Cylinder','Plane','Quad']), position: vector.optional(), scale: vector.optional() } },
  create_empty: { description: 'Create an empty scene GameObject.', shape: { name: z.string(), position: vector.optional() } },
  transform: { description: 'Set world position and local scale. Supply both.', shape: { ...target, position: vector, scale: vector } },
  rotate: { description: 'Set world rotation in Euler degrees.', shape: { ...target, rotation: vector } },
  rename: { description: 'Rename an existing GameObject.', shape: { ...target, name: z.string().min(1) } },
  duplicate: { description: 'Duplicate a GameObject and its children; returns a new ID.', shape: { ...target, name: z.string().optional() } },
  delete: { description: 'Delete a scene GameObject. Reversible using Unity Undo.', shape: target },
  parent: { description: 'Parent a GameObject, or unparent with an empty parentId. Cycles are rejected.', shape: { ...target, parentId: z.string(), worldPositionStays: z.boolean().optional() } },
  add_component: { description: 'Attach a fully qualified Component type. Returns the new component ID.', shape: { ...target, componentType: z.string() } },
  set_property: { description: 'Edit a serialized Inspector path. value is a string, JSON vector/color, asset path, or null string. referenceId sets a scene reference.', shape: { ...target, ...selector, propertyPath: z.string(), value: z.string().optional(), referenceId: id.optional() } },
  resize_array: { description: 'Resize a serialized array/list. Edit elements with set_property and Array.data[index] paths.', shape: { ...target, ...selector, propertyPath: z.string(), arraySize: z.number().int().min(0).max(10000) } },
  create_material: { description: 'Create a new material asset; refuses overwrites.', shape: { assetPath, name: z.string().optional() } },
  assign_material: { description: 'Assign a material to a renderer slot.', shape: { ...target, ...selector, assetPath, materialIndex: z.number().int().nonnegative().optional() } },
  save_prefab: { description: 'Save a scene object as a new prefab asset; refuses overwrites.', shape: { ...target, assetPath } },
  instantiate_prefab: { description: 'Spawn a prefab asset as a connected scene instance.', shape: { assetPath, name: z.string().optional(), position: vector.optional() } },
  write_script: { description: 'Create a new C# script under Assets. Triggers normal compilation; refuses overwrites.', shape: { assetPath, source: z.string() } },
  save: { description: 'Save the active scene. scenePath must be under Assets and end with .unity.', shape: { scenePath: z.string().optional() } },
  refresh: { description: 'Refresh imported assets and trigger compilation when sources changed.', shape: {} },
  play: { description: 'Request Play mode. Confirm settled state with unity_status.', shape: {} },
  stop: { description: 'Request Edit mode. Confirm settled state with unity_status.', shape: {} },
  undo: { description: 'Undo the most recent scene edit or complete batch.', shape: {} },
  open_panel: { description: 'Open the Unity connection/status panel.', shape: {} },
  clear_errors: { description: 'Clear captured bridge errors.', shape: {} },
  demo: { description: 'Create an additive demo scene with a sample spinning sculpture, camera and light. Does not overwrite or close your existing scene.', shape: {} }
};
export const batchSchema = {
  label: z.string().max(120).optional(),
  commands: z.array(z.object({ action: z.string() }).catchall(z.unknown())).min(1).max(100)
};
