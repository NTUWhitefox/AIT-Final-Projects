"""Offline Blender asset inspection/conversion. Never runs in the user's UI session."""
import argparse
import importlib.util
import importlib
import json
import math
import shutil
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


def bounds(objects):
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box if obj.type == 'MESH']
    if not points:
        return None
    low = [min(p[a] for p in points) for a in range(3)]
    high = [max(p[a] for p in points) for a in range(3)]
    return {'min': low, 'max': high, 'size': [high[a]-low[a] for a in range(3)]}


def inspect(objects):
    result = []
    for obj in objects:
        if obj.type != 'MESH':
            continue
        obj.data.calc_loop_triangles()
        result.append({'name': obj.name, 'bounds': bounds([obj]), 'vertices': len(obj.data.vertices),
                       'triangles': len(obj.data.loop_triangles),
                       'materials': [m.name if m else None for m in obj.data.materials],
                       'matrix': [list(row) for row in obj.matrix_world]})
    return result


def clear():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)


def load_pasha(source, addon):
    spec = importlib.util.spec_from_file_location('ait_3ds_import', addon / '__init__.py', submodule_search_locations=[str(addon)])
    package = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = package
    spec.loader.exec_module(package)
    importer = importlib.import_module(spec.name + '.import_3ds')
    importer.load_3ds(str(source), bpy.context, CONSTRAIN=0, UNITS=False, IMAGE_SEARCH=True,
                      FILTER={'MESH'}, KEYFRAME=False, APPLY_MATRIX=True, CONVERSE=Matrix.Identity(4))
    bpy.context.view_layer.update()


def export_pasha(destination):
    """Keep the eight connected native building groups, including nearby detached details.

    This pack is NOT 27 clean individual building objects. Do not invent that structure.
    Remove six aerial reference planes and empty objects; preserve UVs and textures.
    """
    destination.mkdir(parents=True, exist_ok=True)
    (destination/'Models').mkdir(exist_ok=True); (destination/'Textures').mkdir(exist_ok=True)
    vertices, faces, lookup = [], [], {}
    for obj in list(bpy.context.scene.objects):
        if obj.type != 'MESH' or obj.name.startswith('Google_E') or not obj.data.polygons:
            continue
        ids = []
        for vert in obj.data.vertices:
            point = (obj.matrix_world @ vert.co) * .0254
            key = tuple(round(v, 5) for v in point)
            if key not in lookup:
                lookup[key] = len(vertices); vertices.append(point)
            ids.append(lookup[key])
        uvs = obj.data.uv_layers.active
        for face in obj.data.polygons:
            faces.append({'ids': [ids[i] for i in face.vertices],
                          'uv': [list(uvs.data[i].uv) if uvs else [0,0] for i in face.loop_indices],
                          'material': obj.data.materials[face.material_index] if obj.data.materials else None})
    parent = list(range(len(vertices)))
    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]; i = parent[i]
        return i
    for face in faces:
        for index in face['ids'][1:]: parent[find(index)] = find(face['ids'][0])
    components = {}
    for face in faces: components.setdefault(find(face['ids'][0]), []).append(face)
    def extent(group):
        points = [vertices[i] for face in group for i in face['ids']]
        low = Vector([min(p[a] for p in points) for a in range(3)])
        high = Vector([max(p[a] for p in points) for a in range(3)])
        return low, high
    big = []
    for group in components.values():
        low, high = extent(group); size = high-low
        if min(size.x, size.y) > 3 and size.z > 4: big.append({'faces':list(group), 'low':low, 'high':high, 'centre':(low+high)/2})
    big.sort(key=lambda g:(g['centre'].y,g['centre'].x))
    if len(big) != 8: raise ValueError('Unexpected Pasha connected building groups: '+str(len(big)))
    main_faces = {id(face) for group in big for face in group['faces']}
    for group in components.values():
        if id(group[0]) in main_faces: continue
        low,high=extent(group); centre=(low+high)/2
        def score(target):
            distance=sum(max(target['low'][a]-centre[a],0,centre[a]-target['high'][a])**2 for a in range(3))
            return distance+(centre-target['centre']).length_squared*.001
        min(big,key=score)['faces'].extend(group)
    used_materials = sorted({f['material'] for f in faces if f['material']},key=lambda m:m.name)
    material_data=[]
    for material in used_materials:
        image=next((n.image for n in material.node_tree.nodes if n.type=='TEX_IMAGE' and n.image),None) if material.use_nodes else None
        texture=None
        if image:
            original=Path(bpy.path.abspath(image.filepath)); texture=original.name
            if not original.exists(): raise FileNotFoundError(original)
            shutil.copy2(original,destination/'Textures'/texture)
        material_data.append({'name':material.name,'colour':list(material.diffuse_color),'texture':texture or ''})
    output=[]
    for index,group in enumerate(big):
        directions={}; counts={}
        for face in group['faces']:
            a,b,c=[vertices[i] for i in face['ids'][:3]]; normal=(b-a).cross(c-a)
            if normal.length < .001 or abs(normal.normalized().z)>.2: continue
            mat=face['material']
            if not mat or mat.name in ('FrontCol','Foregrou') or not mat.use_nodes: continue
            if not any(n.type=='TEX_IMAGE' and n.image for n in mat.node_tree.nodes):continue
            angle=round(math.degrees(math.atan2(normal.y,normal.x))/5)*5
            directions[angle]=directions.get(angle,0)+normal.length*.5
            counts[angle]=counts.get(angle,0)+1
        direction=max(directions,key=directions.get)
        # Largest textured faces in this pack are often side walls. These small
        # per-group corrections were chosen from actual side renders, not inferred
        # from that surface area. Keep them explicit for this exact source archive.
        correction={1:0,2:90,3:90,4:90,5:-90,6:-90,7:90,8:90}[index+1]
        rotation=Matrix.Rotation(math.radians(90-direction+correction),4,'Z')
        points=[rotation@vertices[i] for face in group['faces'] for i in face['ids']]
        low=Vector([min(p[a] for p in points) for a in range(3)]);high=Vector([max(p[a] for p in points) for a in range(3)])
        origin=Vector(((low.x+high.x)/2,low.y,low.z))
        mesh_vertices=[]; polygons=[]; uv_values=[]; mats=[]; face_materials=[]
        for face in group['faces']:
            first=len(mesh_vertices); mesh_vertices.extend([rotation@vertices[i]-origin for i in face['ids']])
            polygons.append(tuple(range(first,len(mesh_vertices))));uv_values.extend(face['uv'])
            if face['material'] not in mats:mats.append(face['material'])
            face_materials.append(mats.index(face['material']))
        clear(); name=f'Pasha_Block_{index+1:02}'
        mesh=bpy.data.meshes.new(name);mesh.from_pydata(mesh_vertices,[],polygons);mesh.update()
        for mat in mats:mesh.materials.append(mat)
        for face,mat in zip(mesh.polygons,face_materials):face.material_index=mat
        layer=mesh.uv_layers.new(name='UVMap')
        for loop,uv in zip(layer.data,uv_values):loop.uv=uv
        obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);obj.select_set(True);bpy.context.view_layer.objects.active=obj
        bpy.ops.export_scene.fbx(filepath=str(destination/'Models'/(name+'.fbx')),use_selection=True,object_types={'MESH'},
                                 add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='STRIP')
        mesh.calc_loop_triangles()
        output.append({'name':name,'triangles':len(mesh.loop_triangles),'size':list(high-low),'front_angle':direction,
                       'materials':[m.name for m in mats], 'source_faces':len(group['faces']), 'front_face_votes':counts, 'verified_front_correction':correction})
    (destination/'manifest.json').write_text(json.dumps({'source_scale':.0254,'scale_basis':'Inferred inches; not author-certified. Check street-level proportions.',
        'removed':'Six Google_E aerial reference planes and zero-face objects. Eight connected groups retained; detached details assigned by proximity.',
        'materials':material_data,'models':output},ensure_ascii=False,indent=2),encoding='utf-8')
    return output


def main():
    args = argparse.ArgumentParser()
    args.add_argument('--pasha', type=Path, required=True)
    args.add_argument('--kobo', type=Path, required=True)
    args.add_argument('--addon', type=Path, required=True)
    args.add_argument('--out', type=Path, required=True)
    args.add_argument('--export', type=Path)
    opts = args.parse_args(sys.argv[sys.argv.index('--')+1:])
    opts.out.mkdir(parents=True, exist_ok=True)
    clear()
    load_pasha(opts.pasha, opts.addon)
    report = {'blender': bpy.app.version_string, 'pasha': {'bounds': bounds(bpy.context.scene.objects), 'objects': inspect(bpy.context.scene.objects),
              'images': [{'name': im.name, 'path': im.filepath, 'size': list(im.size), 'has_data': im.has_data} for im in bpy.data.images if im.source == 'FILE']}}
    bpy.ops.wm.save_as_mainfile(filepath=str(opts.out/'Pasha-imported.blend'))
    if opts.export:
        report['pasha']['exported']=export_pasha(opts.export/'Pasha')
    report['kobo'] = []
    for file in sorted((opts.kobo/'models').glob('*.fbx')):
        if file.stem.endswith(('_b', '_c')):
            continue
        clear()
        bpy.ops.import_scene.fbx(filepath=str(file))
        bpy.context.view_layer.update()
        objects = [o for o in bpy.context.scene.objects if not o.name.startswith('UCX_')]
        report['kobo'].append({'file': file.name, 'bounds': bounds(objects), 'objects': inspect(objects)})
    (opts.out/'inspection.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('INSPECTION', opts.out/'inspection.json')


if __name__ == '__main__':
    main()
