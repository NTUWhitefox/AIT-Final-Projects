"""Inspect native Pasha layout and connected pieces before adapting it to the road."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'VR-Motor-Street-Riding/Personal/BuildingComparison/Inspection'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Pasha-imported.blend'))
for obj in list(bpy.context.scene.objects):
    if obj.name.startswith('Google_E') or obj.type != 'MESH' or not obj.data.polygons:
        bpy.data.objects.remove(obj, do_unlink=True)
for obj in bpy.context.scene.objects:
    obj.matrix_world.translation *= .0254
    obj.scale *= .0254
bpy.context.view_layer.update()

vertices, faces, materials, lookup = [], [], [], {}
for obj in bpy.context.scene.objects:
    ids = []
    for vert in obj.data.vertices:
        p = obj.matrix_world @ vert.co
        key = tuple(round(v, 5) for v in p)
        if key not in lookup:
            lookup[key] = len(vertices); vertices.append(p)
        ids.append(lookup[key])
    for face in obj.data.polygons:
        faces.append([ids[i] for i in face.vertices])
        materials.append(obj.data.materials[face.material_index].name if obj.data.materials else '')
parent = list(range(len(vertices)))
def find(i):
    while parent[i] != i:
        parent[i] = parent[parent[i]]; i = parent[i]
    return i
for face in faces:
    for i in face[1:]: parent[find(i)] = find(face[0])
components = {}
for face, mat in zip(faces, materials):
    group = components.setdefault(find(face[0]), {'ids':set(), 'faces':0, 'materials':set()})
    group['ids'].update(face); group['faces'] += 1; group['materials'].add(mat)
report = []
for component in components.values():
    points = [vertices[i] for i in component['ids']]
    low = Vector([min(p[a] for p in points) for a in range(3)])
    high = Vector([max(p[a] for p in points) for a in range(3)])
    report.append({'vertices':len(points),'faces':component['faces'],'min':list(low),'max':list(high),'size':list(high-low),'materials':sorted(component['materials'])})
report.sort(key=lambda x:x['faces'],reverse=True)
(OUT/'pasha-components.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('COMPONENTS',len(report))
for item in report[:35]:print(item)

scene = bpy.context.scene
scene.render.engine = 'CYCLES'; scene.cycles.samples = 16
scene.render.resolution_x = 1400; scene.render.resolution_y = 1000; scene.render.resolution_percentage = 100
scene.world.color = (.45,.45,.45)
scene.view_settings.view_transform = 'Standard'
sun = bpy.data.objects.new('Inspection sun', bpy.data.lights.new('Sun','SUN')); scene.collection.objects.link(sun)
sun.rotation_euler = (.6,-.3,-.6); sun.data.energy = 2
points=[obj.matrix_world @ Vector(c) for obj in scene.objects if obj.type=='MESH' for c in obj.bound_box]
low=Vector([min(p[a] for p in points) for a in range(3)]);high=Vector([max(p[a] for p in points) for a in range(3)])
centre=(low+high)/2
camera=bpy.data.objects.new('Inspection camera',bpy.data.cameras.new('Inspection camera'));scene.collection.objects.link(camera);scene.camera=camera
for name,offset in [('pasha-layout',Vector((130,-160,150))),('pasha-front',Vector((60,-100,55)))]:
    camera.location=centre+offset;camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO';camera.data.ortho_scale=110 if name.endswith('front') else 170
    scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
