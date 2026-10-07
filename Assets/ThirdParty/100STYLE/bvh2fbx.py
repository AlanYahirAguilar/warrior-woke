# Blender (4.5) batch: 100STYLE BVH -> FBX for Unity.
# blender -b --factory-startup -P bvh2fbx.py -- <100STYLE dir> <out_dir> <Style> [<Style> ...]
#   <100STYLE dir> holds Frame_Cuts.csv and one folder per style (<Style>/<Style>_<take>.bvh)
# - cm -> m (global_scale 0.01), Y up / -Z forward as the BVH
# - 60 fps -> 30 fps (scene at 30, use_fps_scale) like the Kinematica takes
# - trimmed to the clean range of Frame_Cuts.csv (drops the T-pose and setup at both ends)
# - Chest3's rotation folded into Chest2 (Humanoid has three spine bones, 100STYLE four)
# - Neutral also exports the skeleton alone in its rest pose (Character/Neutral_Skeleton.fbx) for the Avatar
import bpy, csv, os, sys
from mathutils import Quaternion

argv = sys.argv[sys.argv.index("--") + 1:]
src, out, styles = argv[0], argv[1], argv[2:]
os.makedirs(out, exist_ok=True)
FPS_SRC, FPS_DST = 60, 30
TAKES = ("BR", "BW", "FR", "FW", "ID", "SR", "SW", "TR1", "TR2", "TR3")


def fold_into_parent(arm, scene, name):
    """Humanoid maps Chest, Chest2 and Chest4 and discards the animation of the in-between Chest3, which
    turns as much as Chest2 (3-7 deg). Its rest pose matches its parent's, so parent * child rotations
    keep the chest's orientation; Chest3's origin moves under 1 cm."""
    child = arm.pose.bones[name]
    parent = child.parent
    for frame in range(scene.frame_start, scene.frame_end + 1):
        scene.frame_set(frame)
        q = parent.rotation_euler.to_quaternion() @ child.rotation_euler.to_quaternion()
        parent.rotation_euler = q.to_euler(parent.rotation_mode, parent.rotation_euler)
        child.rotation_euler = Quaternion().to_euler(child.rotation_mode)
        parent.keyframe_insert("rotation_euler", frame=frame)
        child.keyframe_insert("rotation_euler", frame=frame)


with open(os.path.join(src, "Frame_Cuts.csv"), newline="") as f:
    cuts = {row["STYLE_NAME"]: row for row in csv.DictReader(f)}

for style in styles:
    for key in TAKES:
        start, stop = cuts[style].get(f"{key}_START", "N/A"), cuts[style].get(f"{key}_STOP", "N/A")
        path = os.path.join(src, style, f"{style}_{key}.bvh")
        if start == "N/A" or not os.path.exists(path):
            continue
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        scene.render.fps = FPS_DST
        scene.render.fps_base = 1.0
        bpy.ops.import_anim.bvh(filepath=path, global_scale=0.01, frame_start=0, use_fps_scale=True,
                                update_scene_fps=False, update_scene_duration=True,
                                rotate_mode='NATIVE', axis_forward='-Z', axis_up='Y')
        arm = bpy.context.selected_objects[0]
        arm.name = arm.data.name = style
        k = FPS_DST / FPS_SRC
        scene.frame_start = int(round(int(start) * k))
        scene.frame_end = int(round(int(stop) * k))
        fold_into_parent(arm, scene, "Chest3")
        dst = os.path.join(out, f"{style}_{key}.fbx")
        bpy.ops.export_scene.fbx(filepath=dst, object_types={'ARMATURE'}, add_leaf_bones=False,
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                                 axis_forward='-Z', axis_up='Y', use_armature_deform_only=False,
                                 bake_anim=True, bake_anim_use_all_bones=True,
                                 bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
                                 bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                                 bake_anim_simplify_factor=0.0)
        print(f"[bvh2fbx] {style}_{key}: frames {scene.frame_start}-{scene.frame_end} @ {FPS_DST} fps -> {dst}")
        if style == "Neutral" and key == "ID":
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
