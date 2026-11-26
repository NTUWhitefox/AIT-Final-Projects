"""Prepare the selected CC0 traffic meshes for the Kobo streetscape preview."""
import json
import sys
from collections import defaultdict
from math import cos, pi, radians, sin, sqrt
from pathlib import Path

import bpy
import bmesh
from mathutils import Quaternion, Vector


source, people, output, commuter_source = map(Path, sys.argv[sys.argv.index("--") + 1:])
output.mkdir(parents=True, exist_ok=True)


def export(name):
    bpy.ops.export_scene.fbx(
        filepath=str(output / (name + ".fbx")), object_types={"MESH"},
        axis_forward="-Z", axis_up="Y", bake_anim=False, add_leaf_bones=False,
    )


def split_lamps(body, slots, name):
    # Separate the actual lamp surface so it can emit independently in Unity.
    # Keeping just one surface also avoids two overlapping curved meshes.
    bpy.context.tool_settings.mesh_select_mode = (False, False, True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="DESELECT")
    bm = bmesh.from_edit_mesh(body.data)
    faces = [face for face in bm.faces if body.data.materials[face.material_index].name in slots]
    if not faces:
        raise ValueError(f"Could not find {name} faces")
    for face in faces:
        face.select_set(True)
    bmesh.update_edit_mesh(body.data, destructive=False)
    before = set(bpy.context.scene.objects)
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    lamp, = set(bpy.context.scene.objects) - before
    lamp.name = name


def round_tyres(wheel):
    """Replace the angular rubber while retaining the original spokes and hub."""
    mesh = wheel.data
    black = mesh.materials.find("Black")
    if black < 0:
        raise ValueError(f"Missing tyre material for {wheel.name}")
    bm = bmesh.new()
    bm.from_mesh(mesh)
    rubber = [face for face in bm.faces if face.material_index == black]
    for left in (False, True):
        points = [v.co.copy() for face in rubber for v in face.verts if (v.co.x > 0) == left]
        if not points:
            continue
        low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
        high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
        centre = (low + high) / 2
        radius = (high.z - low.z) / 2
        profile = ((.07, .63), (.03, .83), (.15, 1), (.85, 1), (.97, .83), (.93, .63))
        segments = 36
        rings = [[bm.verts.new((low.x + t * (high.x - low.x),
                               centre.y + radius * r * cos(2*pi*i/segments),
                               centre.z + radius * r * sin(2*pi*i/segments)))
                  for i in range(segments)] for t, r in profile]
        created = []
        for j, ring in enumerate(rings):
            following = rings[(j+1) % len(rings)]
            for i in range(segments):
                k = (i+1) % segments
                face = bm.faces.new((ring[i], following[i], following[k], ring[k]))
                face.material_index = black
                face.smooth = True
                created.append(face)
        bmesh.ops.recalc_face_normals(bm, faces=created)
    bmesh.ops.delete(bm, geom=rubber, context="FACES")
    bm.to_mesh(mesh)
    bm.free()


def soften_coachwork(body):
    """Round large panels while holding trim, lamp boundaries and wheel arches."""
    mesh = body.data
    if mesh.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    # The GLB duplicates vertices across triangles and materials. Continuous
    # panels need shared edges before rounding or setting smooth normals.
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.0001)
    bmesh.ops.join_triangles(bm, faces=list(bm.faces), cmp_materials=True,
                            angle_face_threshold=radians(24), angle_shape_threshold=radians(42))
    bm.normal_update()
    crease = bm.edges.layers.float.new("crease_edge")
    panel = mesh.materials.find("Blue")
    if panel < 0:
        panel = mesh.materials.find("White")
    windows = mesh.materials.find("Windows")
    for face in bm.faces:
        face.smooth = True
    for edge in bm.edges:
        # Open edges and small trim keep shape. The roof, hood, and body sides
        # use rounded transitions even where the old surface changed abruptly.
        slots = {face.material_index for face in edge.link_faces}
        if not edge.is_manifold:
            edge[crease] = 1
        elif len(slots) > 1:
            edge[crease] = .92
        elif slots <= {panel, windows}:
            edge[crease] = 0
        else:
            edge[crease] = .84 if edge.calc_face_angle() > radians(35) else 0
    bm.to_mesh(mesh)
    bm.free()
    modifier = body.modifiers.new("Rounded body panels", "SUBSURF")
    modifier.levels = 1
    # Use the same tessellation without displacement as a reference. Blend
    # back towards it near windows, plates and lamps so their outlines stay
    # legible and the bumper does not become inflated.
    modifier.subdivision_type = "SIMPLE"
    bpy.context.view_layer.update()
    reference = body.evaluated_get(bpy.context.evaluated_depsgraph_get()).data
    original = [v.co.copy() for v in reference.vertices]
    topology = [tuple(p.vertices) for p in reference.polygons]
    modifier.subdivision_type = "CATMULL_CLARK"
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    if len(body.data.vertices) != len(original) or [tuple(p.vertices) for p in body.data.polygons] != topology:
        raise ValueError("The two subdivision modes must produce the same indexed topology")
    trim_vertices = {v for p in body.data.polygons if p.material_index != panel for v in p.vertices}
    for vertex in body.data.vertices:
        amount = .28 if vertex.index in trim_vertices else .76
        vertex.co = original[vertex.index].lerp(vertex.co, amount)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    # Take out small ripples left by the rounded mesh where long body faces
    # meet fine triangulation. Window, lamp and trim boundaries stay fixed.
    for _ in range(2):
        relax = {}
        for vertex in bm.verts:
            if len(vertex.link_edges) < 3 or any(f.material_index != panel for f in vertex.link_faces):
                continue
            neighbours = [e.other_vert(vertex).co for e in vertex.link_edges]
            offset = (sum(neighbours, Vector()) / len(neighbours) - vertex.co) * .18
            relax[vertex] = vertex.co + (offset.normalized() * .006 if offset.length > .006 else offset)
        for vertex, location in relax.items():
            vertex.co = location
    bm.normal_update()
    for edge in bm.edges:
        if edge.is_manifold:
            edge.smooth = edge.calc_face_angle() < radians(46)
    bm.to_mesh(body.data)
    bm.free()


def car(file, name, length, mirror_width, roof_height):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source / file))
    objects = sorted(bpy.context.scene.objects, key=lambda obj: obj.name)
    body = None
    for obj in objects:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        if "Wheel" in obj.name:
            round_tyres(obj)
            obj.name = ("FrontLeft" if "FrontLeft" in obj.name else
                        "FrontRight" if "FrontRight" in obj.name else "RearAxle")
            continue
        obj.name = "Coachwork"
        body = obj
        soften_coachwork(obj)

    lo, hi = bounds([v.co for obj in objects for v in obj.data.vertices])
    stretch = length/(hi.y-lo.y)
    widen = mirror_width/(hi.x-lo.x)
    roof_lift = roof_height-(hi.z-lo.z)
    for obj in objects:
        wheel_centre = None
        if obj != body:
            near, far = bounds([v.co for v in obj.data.vertices])
            wheel_centre = (near.y+far.y)/2
        for vertex in obj.data.vertices:
            p = vertex.co
            p.x *= widen
            if wheel_centre is not None:
                # Move the axle with the body without turning a round tyre oval.
                p.y += wheel_centre*(stretch-1)
            else:
                p.y *= stretch
                p.z += roof_lift*max(0, min(1, (p.z-.42)/(hi.z-.42)))

    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    split_lamps(body, {"TailLights"}, "BrakeLight")
    export(name)


def weld_islands(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.00012)
    bm.faces.index_update()
    remaining = set(bm.verts)
    for first in bm.verts:
        if first not in remaining:
            continue
        remaining.remove(first)
        pending = [first]
        verts, faces = [], set()
        while pending:
            vertex = pending.pop()
            verts.append(vertex.co.copy())
            faces.update(vertex.link_faces)
            for edge in vertex.link_edges:
                other = edge.other_vert(vertex)
                if other in remaining:
                    remaining.remove(other)
                    pending.append(other)
        if faces:
            yield verts, sorted(faces, key=lambda face: face.index)
    bm.free()


def bounds(points):
    lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    return lo, hi


def commuter():
    """Split the supplied TDAL scooter by connected parts, in metre units."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(commuter_source), use_anim=False)
    raw = sorted(bpy.context.scene.objects, key=lambda obj: obj.name)
    role_meshes = defaultdict(lambda: ([], []))
    tyres = {}

    def merge(role, faces):
        points, polygons = role_meshes[role]
        ids = {}
        for face in faces:
            indices = []
            for vertex in face.verts:
                if vertex not in ids:
                    ids[vertex] = len(points)
                    points.append(vertex.co.copy())
                indices.append(ids[vertex])
            if len(set(indices)) >= 3:
                polygons.append(tuple(indices))

    for obj in raw:
        if obj.type != "MESH":
            continue
        for points, faces in weld_islands(obj):
            lo, hi = bounds(points)
            mid, size = (hi+lo)/2, hi-lo
            name = obj.name
            if "Board_BodyShell" in name:
                role = "Steer_Paint" if lo.z > .85 else "Body_Paint"
            elif "Board_bodyGray" in name:
                role = "Steer_Trim" if lo.z > .82 else "Body_Trim"
            elif "Board_Mirror" in name:
                role = "GlassLeft" if mid.x < 0 else "GlassRight"
            elif "BoardSP" in name:
                role = "Steer_Dash"
            elif "Board_Tire" in name:
                axle = "FrontWheel" if mid.y < 0 else "RearWheel"
                role = axle + ("_Rubber" if size.z > .40 else "_Rim" if size.x > .06 else "_Hub")
                if role.endswith("_Rubber"):
                    tyres[axle] = (lo, hi)
            elif "Board_Light" in name:
                if mid.y > .6:
                    role = "Body_BrakeLight"
                else:
                    role = "Steer_Headlamp" if lo.z > .90 else "Body_Amber"
            elif "MachineStuff" in name:
                if size.y > .65 and lo.y > 0 and lo.z > .63:
                    role = "Body_Seat"
                elif lo.z > .86:
                    role = "Steer_Trim" if lo.z > 1.04 else "Steer_Metal"
                elif hi.y < -.40 and hi.z < .53:
                    # Keep the caliper/fork stationary relative to the steering.
                    role = "Steer_Metal"
                else:
                    role = "Body_Metal"
            else:
                raise ValueError(f"Unclassified scooter material group: {name}")
            merge(role, faces)
    for obj in raw:
        bpy.data.objects.remove(obj, do_unlink=True)

    ground = min(lo.z for lo, _ in tyres.values())
    before = [p for points, _ in role_meshes.values() for p in points]
    low, high = bounds(before)
    for role, (vertices, faces) in sorted(role_meshes.items()):
        mesh = bpy.data.meshes.new(role)
        mesh.from_pydata([p - Vector((0, 0, ground)) for p in vertices], [], faces)
        mesh.update()
        bm = bmesh.new()
        bm.from_mesh(mesh)
        for face in bm.faces:
            face.smooth = role not in {"Steer_Dash", "GlassLeft", "GlassRight"}
        for edge in bm.edges:
            if edge.is_manifold:
                edge.smooth = edge.calc_face_angle() < radians(44)
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new(role, mesh)
        bpy.context.collection.objects.link(obj)
        mesh.materials.append(material(role))
        ratio = {"Body_Metal": .32, "Steer_Metal": .57, "Body_Trim": .73}.get(role)
        if ratio is not None:
            bpy.ops.object.select_all(action="DESELECT")
            obj.select_set(True)
            bpy.context.view_layer.objects.active = obj
            simplify = obj.modifiers.new("Simplify chassis detail", "DECIMATE")
            simplify.ratio = ratio
            bpy.ops.object.modifier_apply(modifier=simplify.name)
        if role.startswith("Glass"):
            lo, hi = bounds([v.co for v in mesh.vertices])
            uv = mesh.uv_layers.new()
            for loop in mesh.loops:
                p = mesh.vertices[loop.vertex_index].co
                uv.data[loop.index].uv = ((p.x-lo.x)/(hi.x-lo.x), (p.z-lo.z)/(hi.z-lo.z))

    grip_y, grip_z = -.298, 1.019 - ground
    for side in (-1, 1):
        bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=.021, depth=.118,
                                          location=(side*.313, grip_y, grip_z), rotation=(0, pi/2, 0))
        obj = bpy.context.object
        obj.name = "Steer_GripLeft" if side < 0 else "Steer_GripRight"
        obj.data.materials.append(material("Grip"))
        for face in obj.data.polygons:
            face.use_smooth = len(face.vertices) == 4

    def axis(axle):
        lo, hi = tyres[axle]
        p = (lo+hi)/2
        return [0, p.z-ground, -p.y]

    front, rear = axis("FrontWheel"), axis("RearWheel")
    fit = {
        "front": {"x": front[0], "y": front[1], "z": front[2]},
        "rear": {"x": rear[0], "y": rear[1], "z": rear[2]},
        "steering": {"x": 0, "y": .94, "z": .32},
        "grip": {"x": .313, "y": grip_z, "z": -grip_y},
        "nose": -low.y,
        "width": high.x-low.x,
        "length": high.y-low.y,
        "radius": front[1],
        "wheelbase": front[2]-rear[2],
    }
    if not 1.25 < fit["wheelbase"] < 1.4 or not .2 < fit["radius"] < .24:
        raise ValueError(f"Unexpected TDAL wheel geometry: {fit}")
    (output / "ScooterFit.json").write_text(json.dumps(fit, indent=2), encoding="utf-8")
    export("CommuterScooter")
    return fit


def aim(bone, target):
    direction = (bone.tail - bone.head).normalized()
    turn = direction.rotation_difference((target - bone.head).normalized())
    matrix = (turn.to_matrix().to_4x4() @ bone.matrix).copy()
    matrix.translation = bone.head
    bone.matrix = matrix
    bpy.context.view_layer.update()


def limb(upper, lower, target, bend, scale):
    start = upper.head.copy()
    goal = target / scale - start
    d = goal.length
    direction = goal.normalized()
    a, b = upper.length, lower.length
    reach = min(d, a + b - .001)
    if d > reach + .015:
        raise ValueError(f"{upper.name} misses its contact point by {(d-reach)*scale:.3f} m")
    bend = (bend - direction * bend.dot(direction)).normalized()
    along = (a*a - b*b + reach*reach) / (2*reach)
    elbow = start + direction * along + bend * sqrt(max(0, a*a - along*along))
    aim(upper, elbow)
    aim(lower, start + direction * reach)


def material(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)


def rounded_piece(name, centre, size, tilt=0):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, location=centre)
    obj = bpy.context.object
    obj.name = name
    obj.scale = size
    obj.rotation_euler.x = tilt
    obj.data.materials.append(material(name))
    for p in obj.data.polygons:
        p.use_smooth = True


def rider(fit):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(people / "Casual2_Male.fbx"), use_anim=False)
    armature, = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    mesh, = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bones = armature.pose.bones
    # The previous large torso and elbows dominated the 1.89 m scooter.
    # Use a smaller adult and fit all three contacts again at full vehicle scale.
    scale = .585
    hip = Vector((0, .241, .779))
    origin = hip - bones["Hips"].head * scale

    def local(x, y, z):
        return Vector((x, y, z)) - origin

    # Blender -Y is the vehicle's forward direction. Contacts are measured on
    # the existing 1.33 m wheelbase scooter, with Z vertical and ground at zero.
    pelvis = bones["Hips"]
    spine = (Quaternion((1, 0, 0), radians(16)).to_matrix().to_4x4() @ pelvis.matrix).copy()
    spine.translation = pelvis.head
    pelvis.matrix = spine
    bpy.context.view_layer.update()
    for side, sign in (("L", 1), ("R", -1)):
        arm_target = local(sign * .292, -fit["grip"]["z"] + .055, fit["grip"]["y"] + .012)
        limb(bones["UpperArm." + side], bones["LowerArm." + side], arm_target,
             Vector((sign * .29, .08, -.8)), scale)
        fist = bones["Fist." + side]
        aim(fist, fist.head + Vector((0, -1, -.12)))

        foot_target = local(sign * .111, .064, .326)
        limb(bones["UpperLeg." + side], bones["LowerLeg." + side], foot_target,
             Vector((sign * .24, -1, .1)), scale)
        foot = bones["Foot." + side]
        rest = foot.bone.matrix_local.copy()
        rest.translation = foot.head
        foot.matrix = rest
        bpy.context.view_layer.update()

    # Fit a separate helmet around the neck. Remove the original head and hair
    # from the baked body, leaving the collar and arms as actual skinned geometry.
    neck = origin + bones["Neck"].head * scale
    posed_object = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    baked = bpy.data.meshes.new_from_object(posed_object)
    for vertex in baked.vertices:
        vertex.co = origin + (posed_object.matrix_world @ vertex.co) * scale
    obj = bpy.data.objects.new("RiderBody", baked)
    bpy.context.collection.objects.link(obj)
    covered = {group.index for group in mesh.vertex_groups
               if group.name == "Head" or group.name.startswith(("Fist.", "Foot."))}
    sleeves = {group.index for group in mesh.vertex_groups
               if group.name.startswith(("UpperArm.", "LowerArm.", "Fist."))}
    def weighted(groups):
        return {v.index for v in mesh.data.vertices
                if sum(g.weight for g in v.groups if g.group in groups) > .55}
    covered_vertices = weighted(covered)
    sleeve_vertices = weighted(sleeves)
    bm = bmesh.new()
    bm.from_mesh(baked)
    removed = [face for face in bm.faces
               if baked.materials[face.material_index].name in {"Face", "Hair"}
               or all(v.index in covered_vertices for v in face.verts)]
    shirt = baked.materials.find("Shirt")
    for face in bm.faces:
        if baked.materials[face.material_index].name == "Skin":
            if face.calc_center_median().z < .67:
                face.material_index = baked.materials.find("Pants")
            elif any(v.index in sleeve_vertices for v in face.verts):
                face.material_index = shirt
    bmesh.ops.delete(bm, geom=removed, context="FACES")
    # Clean the ankle cuffs at the shoe opening. The standing mesh otherwise
    # leaves thin toe faces behind the sole after bending the leg.
    bmesh.ops.bisect_plane(bm, geom=[*bm.verts, *bm.edges, *bm.faces],
                          dist=.0001, plane_co=(0, 0, .35), plane_no=(0, 0, 1), clear_inner=True)
    bm.to_mesh(baked)
    bm.free()
    bpy.data.objects.remove(mesh, do_unlink=True)
    bpy.data.objects.remove(armature, do_unlink=True)
    centre = neck + Vector((0, -.015, .103))
    rounded_piece("Helmet", centre, (.108, .126, .125), radians(8))
    rounded_piece("Visor", centre + Vector((0, -.080, -.018)), (.100, .056, .050), radians(10))
    for side, sign in (("L", 1), ("R", -1)):
        grip = fit["grip"]
        rounded_piece("Glove" + side, (sign * grip["x"], -grip["z"] + .01, grip["y"] + .007), (.051, .064, .036))
        rounded_piece("Shoe" + side, (sign * .111, -.018, .329), (.046, .113, .042), radians(-3))
    export("HelmetedRider")


car("quaternius-car-a.glb", "Sedan", 4.425, 1.82, 1.475)
car("quaternius-suv.glb", "SUV", 4.310, 1.93, 1.655)
rider(commuter())
