"""Render native model sides to choose explicit facade orientation, not a bounding-box guess."""
import bpy
import sys
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'VR-Motor-Street-Riding/Personal/BuildingComparison'
numbers=[int(v) for v in sys.argv[sys.argv.index('--')+1:]] if '--' in sys.argv else list(range(1,9))
for number in numbers:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(BASE/f'Prepared/Pasha/Models/Pasha_Block_{number:02}.fbx'))
    for material in bpy.data.materials:
        if not material.use_nodes:continue
        for node in material.node_tree.nodes:
            if node.type=='TEX_IMAGE' and node.image:
                path=BASE/'Prepared/Pasha/Textures'/Path(node.image.filepath).name
                if path.exists():node.image.filepath=str(path);node.image.reload()
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=8
    scene.render.resolution_x=900;scene.render.resolution_y=600;scene.render.resolution_percentage=100
    scene.view_settings.view_transform='Standard';scene.world=bpy.data.worlds.new('World');scene.world.color=(.5,.5,.5)
    points=[obj.matrix_world@Vector(c) for obj in scene.objects if obj.type=='MESH' for c in obj.bound_box]
    low=Vector([min(p[a] for p in points) for a in range(3)]);high=Vector([max(p[a] for p in points) for a in range(3)]);centre=(low+high)/2
    camera=bpy.data.objects.new('Camera',bpy.data.cameras.new('Camera'));scene.collection.objects.link(camera);scene.camera=camera
    camera.data.type='ORTHO';camera.data.ortho_scale=max(high-low)*1.2
    for side in [-1,1]:
        sun=bpy.data.objects.new('Sun',bpy.data.lights.new('Sun','SUN'));scene.collection.objects.link(sun);sun.data.energy=1.8
        sun.rotation_euler=Vector((-side,-.3,-.6)).to_track_quat('-Z','Y').to_euler()
        camera.location=centre+Vector((side*60,-2,3));camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(BASE/f'Inspection/Block{number:02}_{"positive" if side>0 else "negative"}X.png');bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(sun,do_unlink=True)
