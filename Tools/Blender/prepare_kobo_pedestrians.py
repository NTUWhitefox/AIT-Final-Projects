"""Create two street pedestrians from the CC0 Quaternius Standard archives."""
import json
import sys
import zipfile
from pathlib import Path
from math import pi
from statistics import median

import bmesh
import bpy
from mathutils import Matrix, Vector


source, output = map(Path, sys.argv[sys.argv.index("--") + 1:])
output.mkdir(parents=True, exist_ok=True)
inputs = source / "Selected"
inputs.mkdir(exist_ok=True)


def extract(archive, suffix, target):
    with zipfile.ZipFile(source / archive) as pack:
        name, = [name for name in pack.namelist() if name.endswith("/" + suffix)]
        target.write_bytes(pack.read(name))


for sex in ("Male", "Female"):
    extract("base.zip", f"Base Characters/Unity/Superhero_{sex}_FullBody.fbx", inputs / f"{sex}.fbx")
for hair in ("Hair_SimpleParted", "Hair_Buns"):
    extract("base.zip", f"Hairstyles/Rigged to Head Bone/FBX (Unity)/{hair}.fbx", inputs / (hair + ".fbx"))
extract("motion.zip", "Unity/UAL1_Standard_RM.fbx", inputs / "Motion.fbx")
extract("base.zip", "Base Characters/Textures/T_Eye_Brown.png", output / "PedestrianEye.png")
for i in (1, 2):
    extract("base.zip", f"Base Characters/Godot - UE/T_Hair_{i}_BaseColor_png.png", output / f"PedestrianHair{i}.png")


def import_rig(name, animated=False):
    existing = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(inputs / (name + ".fbx")), use_anim=animated)
    objects = set(bpy.data.objects) - existing
    rig, = [o for o in objects if o.type == "ARMATURE"]
    return rig, [o for o in objects if o.type == "MESH"]


def material(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)


def add_shoes(bm, body, rig, female):
    weights = bm.verts.layers.deform.verify()
    shoes = []
    # Cross sections run from the heel towards the toe. Their base is flat,
    # and the two front sections follow the ball bone through toe-off.
    sections = ((-.056,.034,.068), (-.036,.046,.116), (0,.050,.145),
                (.054,.049,.104), (.119,.051,.074), (.172,.047,.066), (.201,.026,.041))
    profile = ((-.78,0), (.78,0), (1,.21), (.94,.66), (.62,1),
               (-.62,1), (-.94,.66), (-1,.21))
    for side in ("l", "r"):
        foot = "foot_" + side
        ball = "ball_" + side
        pin = rig.matrix_world @ rig.data.bones[foot].head_local
        foot_id = body.vertex_groups[foot].index
        toe_id = body.vertex_groups[ball].index
        rings = []
        for along, width, high in sections:
            factor = .92 if female else 1
            y = pin.y + along*factor
            toe_weight = min(1, max(0, (along - .101) / .074))
            ring = [bm.verts.new((pin.x + x*width*factor, y, z*high)) for x,z in profile]
            for vert in ring:
                vert[weights][foot_id] = 1 - toe_weight
                if toe_weight > 0:
                    vert[weights][toe_id] = toe_weight
            rings.append(ring)
        for a,b in zip(rings, rings[1:]):
            for i in range(len(profile)):
                j = (i + 1) % len(profile)
                face = bm.faces.new((a[i], b[i], b[j], a[j]))
                face.material_index = 4 if i in {0, 1, 7} else 3
                face.smooth = face.material_index == 3
                shoes.append(face)
        for ring in (rings[0], rings[-1]):
            face = bm.faces.new(ring)
            face.material_index = 3
            face.smooth = True
            shoes.append(face)
    bmesh.ops.recalc_face_normals(bm, faces=shoes)


def clothes(body, rig, female):
    # Split the original skinned surface on the garment boundaries. BMesh
    # interpolates the skin weights on the new edges, preserving elbow/knee bends.
    bm = bmesh.new()
    bm.from_mesh(body.data)
    inv = body.matrix_world.inverted()
    bm.transform(body.matrix_world)
    waist = 1.01 if female else 1.06
    top = 1.514 if female else 1.557
    sleeve = .367 if female else .406
    for point, normal in [((0, 0, z), (0, 0, 1)) for z in (.128, waist, top)] + [
        ((side*sleeve, 0, 0), (1, 0, 0)) for side in (-1, 1)
    ]:
        bmesh.ops.bisect_plane(bm, geom=[*bm.verts, *bm.edges, *bm.faces],
                              plane_co=point, plane_no=normal, dist=.00001)
    slots = ("Skin", "Shirt", "Trousers", "Shoes", "Soles")
    for face in bm.faces:
        x, _, z = face.calc_center_median()
        face.material_index = (3 if z < .128 else 2 if z < waist else
                               1 if z < top and abs(x) < sleeve else 0)
        face.smooth = True
    # Reduce the chest relief of the athletic mesh. Give the top and trousers
    # a little room, retaining body weight distribution and the original rig.
    for vertex in bm.verts:
        x, y, z = vertex.co
        garment = {f.material_index for f in vertex.link_faces}
        if 1 in garment and abs(x) < .245 and y > 0:
            vertex.co.y = y * .89 + .009
        if 2 in garment and .18 < z < waist:
            vertex.co.x = x * 1.025
    bmesh.ops.delete(bm, geom=[face for face in bm.faces if face.material_index == 3], context="FACES")
    add_shoes(bm, body, rig, female)
    bm.transform(inv)
    body.data.materials.clear()
    for name in slots:
        body.data.materials.append(material(name))
    bm.to_mesh(body.data)
    bm.free()


def use_action(rig, action):
    rig.animation_data_create()
    rig.animation_data.action = action
    rig.animation_data.action_slot = action.slots[0]


def animate(target, body, motion, clip, label):
    source_action, = [a for a in bpy.data.actions if a.name.endswith("|" + clip)]
    use_action(motion, source_action)
    scene = bpy.context.scene
    start, end = (round(f) for f in source_action.frame_range)
    rig_world = target.matrix_world.copy()
    rig_inverse = rig_world.inverted()
    source_world = motion.matrix_world.copy()
    source_rest = {b.name: (source_world @ b.matrix_local).to_quaternion() for b in motion.data.bones}
    target_rest = {b.name: (rig_world @ b.matrix_local).to_quaternion() for b in target.data.bones}
    bones = list(target.pose.bones)
    expected = {b.name for b in motion.pose.bones}
    if not {b.name for b in bones} <= expected:
        raise ValueError("Base and motion rigs do not share the same bones")
    pose = target.animation_data_create()
    pose.action = None
    new = bpy.data.actions.new(label)
    pose.action = new
    roots = []
    ground_tracks = []
    last_rotation = {}
    for i, frame in enumerate(range(start, end+1)):
        scene.frame_set(frame)
        dependencies = bpy.context.evaluated_depsgraph_get()
        src = motion.evaluated_get(dependencies)
        root = source_world @ src.pose.bones["root"].head
        roots.append(root)
        desired = {}
        for bone in bones:
            name = bone.name
            src_pose = src.pose.bones[name]
            if name == "root":
                desired[name] = bone.bone.matrix_local.copy()
                continue
            parent = bone.parent
            parent_pose = desired[parent.name]
            parent_rest = parent.bone.matrix_local
            rest = bone.bone.matrix_local
            head = (parent_pose @ parent_rest.inverted() @ rest).translation
            if name == "pelvis":
                base_head = source_world @ src_pose.bone.head_local
                delta = (source_world @ src_pose.head) - base_head - root
                head += rig_inverse.to_3x3() @ delta
            orientation = (source_world @ src_pose.matrix).to_quaternion() @ source_rest[name].inverted() @ target_rest[name]
            matrix = rig_inverse @ orientation.to_matrix().to_4x4()
            matrix.translation = head
            desired[name] = matrix

        for bone in bones:
            parent = bone.parent
            constraints = ({"parent_matrix": desired[parent.name], "parent_matrix_local": parent.bone.matrix_local}
                           if parent else {})
            bone.matrix_basis = bone.bone.convert_local_to_pose(desired[bone.name], bone.bone.matrix_local, invert=True, **constraints)
        bpy.context.view_layer.update()
        evaluated = body.evaluated_get(dependencies)
        low = min((evaluated.matrix_world @ v.co).z for v in evaluated.data.vertices)
        # Translate the rig root so the supporting shoe actually reaches the road.
        root_bone = target.pose.bones["root"]
        root_bone.location += (rig_world @ root_bone.bone.matrix_local).inverted().to_3x3() @ Vector((0, 0, -low))
        bpy.context.view_layer.update()
        evaluated = body.evaluated_get(dependencies)
        mesh_positions = [evaluated.matrix_world @ v.co for v in evaluated.data.vertices]
        ground_tracks.append([min((p for p in mesh_positions if (p.x > 0) == side and p.z < .24), key=lambda p: p.z).copy()
                              for side in (False, True)])
        for bone in bones:
            bone.rotation_mode = "QUATERNION"
            previous = last_rotation.get(bone.name)
            if previous and bone.rotation_quaternion.dot(previous) < 0:
                bone.rotation_quaternion.negate()
            last_rotation[bone.name] = bone.rotation_quaternion.copy()
            bone.keyframe_insert(data_path="rotation_quaternion", frame=1+i, group=bone.name)
            if bone.name in {"root", "pelvis"}:
                bone.keyframe_insert(data_path="location", frame=1+i, group=bone.name)

    pose.action = None
    track = pose.nla_tracks.new()
    track.name = label
    strip = track.strips.new(label, 1, new)
    track.mute = True
    duration = (end-start)/scene.render.fps
    if label != "Walk":
        return 0
    # Estimate plant speed from the same low shoe vertices across frames.
    # The original clip's root motion is the fallback for toe/heel transitions.
    speeds = [(a.y-b.y)*scene.render.fps for previous, following in zip(ground_tracks, ground_tracks[1:])
              for a,b in zip(previous, following) if max(a.z,b.z) < .016 and .5 < (a.y-b.y)*scene.render.fps < 2.4]
    return median(speeds) if len(speeds) >= 12 else abs(roots[-1].y-roots[0].y)/duration


def pedestrian(sex, height, hair_name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = 30
    target, meshes = import_rig(sex)
    body, = [o for o in meshes if "superhero" in o.name.lower()]
    body.name = "StreetBody"
    clothes(body, target, sex == "Female")
    hair_rig, hairs = import_rig(hair_name)
    for hair in hairs:
        world = hair.matrix_world.copy()
        hair.parent = target
        hair.matrix_world = world
        for modifier in hair.modifiers:
            if modifier.type == "ARMATURE":
                modifier.object = target
    bpy.data.objects.remove(hair_rig, do_unlink=True)
    motion, dummy = import_rig("Motion", True)
    animate(target, body, motion, "Idle_Loop", "Idle")
    pace = animate(target, body, motion, "Walk_Loop" if sex == "Male" else "Walk_Formal_Loop", "Walk")
    if not .55 < pace < 2.25:
        raise ValueError(f"Unexpected walking pace: {pace}")
    for obj in [motion, *dummy]:
        bpy.data.objects.remove(obj, do_unlink=True)
    turn = Matrix.Rotation(pi, 4, "Z")
    target.matrix_world = turn @ target.matrix_world
    for track in target.animation_data.nla_tracks:
        track.mute = False
    bpy.ops.object.select_all(action="DESELECT")
    for obj in [target, *meshes, *hairs]:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.export_scene.fbx(filepath=str(output / ("Street" + sex + ".fbx")), use_selection=True,
                            object_types={"ARMATURE", "MESH"}, axis_forward="-Z", axis_up="Y",
                            add_leaf_bones=False, bake_anim=True, bake_anim_use_nla_strips=True,
                            bake_anim_use_all_actions=False, bake_anim_simplify_factor=.15)
    return {"height": height, "pace": pace}


fit = {"male": pedestrian("Male", 1.74, "Hair_SimpleParted"), "female": pedestrian("Female", 1.62, "Hair_Buns")}
(output / "PedestrianFit.json").write_text(json.dumps(fit, indent=2), encoding="utf-8")
print(fit, flush=True)
