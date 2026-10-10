# Blender (4.5) batch: CMU mocap (Daz-friendly BVH release by B. Hahne) -> FBX for Unity.
# blender -b --factory-startup -P bvh2fbx.py -- <bvh> <out_dir> <name> <start_s> <end_s>
# - cm -> m (global_scale 0.01), Y up / -Z forward as the BVH
# - 120 fps -> 30 fps (scene at 30, use_fps_scale) like the Kinematica and 100STYLE takes
# - trimmed to [start_s, end_s] of the take (the first frame is a T-pose)
# - lButtock / rButtock folded into the thighs: Humanoid maps the thigh as the upper leg and drops the
#   animation of the in-between buttock bone, which turns with the leg (the thigh keeps its orientation
#   in armature space; its head moves under 1 cm)
# - also exports the skeleton alone in its rest pose (Character/<name>_Skeleton.fbx: the T-pose of the
#   BVH offsets) for the Unity Avatar
import bpy, os, sys

argv = sys.argv[sys.argv.index("--") + 1:]
src, out, name, start_s, end_s = argv[0], argv[1], argv[2], float(argv[3]), float(argv[4])
os.makedirs(out, exist_ok=True)
FPS_DST = 30


def fold_into_child(arm, scene, name, child_name):
    """Moves the rotation of the in-between bone <name> into its child, keeping the child's pose in
    armature space, and leaves <name> at rest."""
    bone, child = arm.pose.bones[name], arm.pose.bones[child_name]
    poses = {}
    for frame in range(scene.frame_start, scene.frame_end + 1):
        scene.frame_set(frame)
        poses[frame] = child.matrix.copy()
    for frame in range(scene.frame_start, scene.frame_end + 1):
        scene.frame_set(frame)
        bone.rotation_mode = 'QUATERNION'
        bone.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
        bone.keyframe_insert("rotation_quaternion", frame=frame)
        bpy.context.view_layer.update()
        child.matrix = poses[frame]
        bpy.context.view_layer.update()
        child.keyframe_insert("rotation_" + ("quaternion" if child.rotation_mode == 'QUATERNION' else "euler"), frame=frame)
        child.keyframe_insert("location", frame=frame)
    # The buttock's old euler keys would still drive it: drop them
    action = arm.animation_data.action
    for fc in [fc for fc in action.fcurves if fc.data_path == f'pose.bones["{name}"].rotation_euler']:
        action.fcurves.remove(fc)


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS_DST
scene.render.fps_base = 1.0
bpy.ops.import_anim.bvh(filepath=src, global_scale=0.01, frame_start=0, use_fps_scale=True,
                        update_scene_fps=False, update_scene_duration=True,
                        rotate_mode='NATIVE', axis_forward='-Z', axis_up='Y')
arm = bpy.context.selected_objects[0]
arm.name = arm.data.name = name
scene.frame_start = int(round(start_s * FPS_DST))
scene.frame_end = int(round(end_s * FPS_DST))
fold_into_child(arm, scene, "lButtock", "lThigh")
fold_into_child(arm, scene, "rButtock", "rThigh")

dst = os.path.join(out, "Animations", f"{name}.fbx")
os.makedirs(os.path.dirname(dst), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=dst, object_types={'ARMATURE'}, add_leaf_bones=False,
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', use_armature_deform_only=False,
                         bake_anim=True, bake_anim_use_all_bones=True,
                         bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
                         bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                         bake_anim_simplify_factor=0.0)
print(f"[bvh2fbx] {name}: frames {scene.frame_start}-{scene.frame_end} @ {FPS_DST} fps -> {dst}")

skel = os.path.join(out, "Character", f"{name}_Skeleton.fbx")
os.makedirs(os.path.dirname(skel), exist_ok=True)
arm.data.pose_position = 'REST'
arm.animation_data_clear()
bpy.ops.export_scene.fbx(filepath=skel, object_types={'ARMATURE'}, add_leaf_bones=False,
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', use_armature_deform_only=False,
                         bake_anim=False)
print(f"[bvh2fbx] skeleton -> {skel}")
