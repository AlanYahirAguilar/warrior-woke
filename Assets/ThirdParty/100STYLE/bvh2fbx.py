# Blender (4.5) batch: 100STYLE Neutral BVH -> FBX for Unity.
# blender -b -P bvh2fbx.py -- <bvh_dir> <out_dir>
# - cm -> m (global_scale 0.01), Y up / -Z forward as the BVH
# - 60 fps -> 30 fps (scene at 30, use_fps_scale) like the Kinematica takes
# - trimmed to the clean range of Frame_Cuts.csv (drops the T-pose and setup at both ends)
import bpy, os, sys

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
os.makedirs(out, exist_ok=True)

# Frame_Cuts.csv, row Neutral (frames of the 60 fps BVH, 0-based)
CUTS = {"BR": (338, 5481), "BW": (380, 8235), "FR": (357, 4227), "FW": (327, 7445),
        "ID": (655, 1555), "SR": (340, 3066), "SW": (354, 5730), "TR1": (304, 6775)}
FPS_SRC, FPS_DST = 60, 30

for key, (a, b) in CUTS.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = FPS_DST
    scene.render.fps_base = 1.0
    path = os.path.join(src, f"Neutral_{key}.bvh")
    bpy.ops.import_anim.bvh(filepath=path, global_scale=0.01, frame_start=0, use_fps_scale=True,
                            update_scene_fps=False, update_scene_duration=True,
                            rotate_mode='NATIVE', axis_forward='-Z', axis_up='Y')
    arm = bpy.context.selected_objects[0]
    arm.name = arm.data.name = "Neutral"
    k = FPS_DST / FPS_SRC
    scene.frame_start = int(round(a * k))
    scene.frame_end = int(round(b * k))
    dst = os.path.join(out, f"Neutral_{key}.fbx")
    bpy.ops.export_scene.fbx(filepath=dst, object_types={'ARMATURE'}, add_leaf_bones=False,
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', use_armature_deform_only=False,
                             bake_anim=True, bake_anim_use_all_bones=True,
                             bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
                             bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                             bake_anim_simplify_factor=0.0)
    print(f"[bvh2fbx] {key}: frames {scene.frame_start}-{scene.frame_end} @ {FPS_DST} fps -> {dst}")
    if key == "ID":
        # Skeleton alone in its rest pose (the BVH offsets: a T-pose) for the Unity Avatar
        os.makedirs(os.path.join(out, "Character"), exist_ok=True)
        skel = os.path.join(out, "Character", "Neutral_Skeleton.fbx")
        arm.data.pose_position = 'REST'
        arm.animation_data_clear()
        bpy.ops.export_scene.fbx(filepath=skel, object_types={'ARMATURE'}, add_leaf_bones=False,
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                                 axis_forward='-Z', axis_up='Y', use_armature_deform_only=False,
                                 bake_anim=False)
        print(f"[bvh2fbx] skeleton -> {skel}")
