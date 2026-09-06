# /// script
# dependencies = []
# ///
"""Blender headless: build conforming, head-weighted face shells and verification renders.

/opt/homebrew/bin/blender --background --factory-startup --python tools/build_face_decals.py
"""
import argparse
import json
import math
from pathlib import Path
import sys
import tempfile
import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
FBX = ROOT / 'Assets/Characters/BabyBunny.fbx'
BASE = ROOT / 'Assets/Meshy_AI_Bunny_Baby_Joy_0905100123_texture_fbx/Meshy_AI_Bunny_Baby_Joy_0905100123_texture.png'
PATCHES = [('FaceEyes', 0.17, 1.185, 1.29, 256, 128), ('FaceMouth', 0.09, 1.08, 1.185, 80, 64)]


def activate(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def material(name, color=None, texture=False):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    out = nodes.new('ShaderNodeOutputMaterial')
    emit = nodes.new('ShaderNodeEmission')
    mat.node_tree.links.new(emit.outputs[0], out.inputs['Surface'])
    if texture:
        image = nodes.new('ShaderNodeTexImage')
        image.image = bpy.data.images.load(str(BASE), check_existing=True)
        mat.node_tree.links.new(image.outputs['Color'], emit.inputs['Color'])
    else:
        emit.inputs['Color'].default_value = (*color, 1)
    return mat


def load_model(path):
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(path))
    body = bpy.data.objects.get('Bunny')
    rigs = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    if body is None or len(rigs) != 1 or 'mixamorig:Head' not in rigs[0].data.bones:
        raise RuntimeError('Expected Bunny and one Mixamo armature with Head')
    return body, rigs[0]


def create_shell(body, rig, name, half_width, low, high, cols, rows, shrink):
    # The mutation shrinks each horizontal side by 1 cm; input FBX units are metres.
    half_width -= shrink
    verts = [(half_width * (2 * x / cols - 1), -0.6, low + (high-low) * z / rows)
             for z in range(rows+1) for x in range(cols+1)]
    faces = []
    for z in range(rows):
        for x in range(cols):
            i = z*(cols+1)+x
            faces.append((i, i+1, i+cols+2, i+cols+1)) # outward normal -Y
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces); mesh.update()
    ob = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(ob)
    uv = mesh.uv_layers.new(name='UVMap')
    for poly in mesh.polygons:
        poly.use_smooth = True
        for loop in poly.loop_indices:
            index = mesh.loops[loop].vertex_index
            uv.data[loop].uv = ((index % (cols+1))/cols, (index//(cols+1))/rows)
    activate(ob)
    wrap = ob.modifiers.new('ProjectFace', 'SHRINKWRAP')
    wrap.target = body; wrap.wrap_method = 'PROJECT'
    wrap.use_project_x = False; wrap.use_project_y = True; wrap.use_project_z = False
    wrap.use_positive_direction = True; wrap.use_negative_direction = False
    wrap.wrap_mode = 'ON_SURFACE'; wrap.offset = 0
    bpy.ops.object.modifier_apply(modifier=wrap.name)
    bm = bmesh.new(); bm.from_mesh(mesh)
    missed = [v for v in bm.verts if abs(v.co.y + 0.6) < 1e-6]
    removed = len(missed)
    bmesh.ops.delete(bm, geom=missed, context='VERTS')
    bm.normal_update()
    for vertex in bm.verts:
        vertex.co += vertex.normal * 0.0025
    bm.to_mesh(mesh); bm.free(); mesh.update()
    if not mesh.polygons: raise RuntimeError(f'{name}: projection missed every face')
    transfer = ob.modifiers.new('BodyNormals', 'DATA_TRANSFER')
    transfer.object = body; transfer.use_loop_data = True
    transfer.data_types_loops = {'CUSTOM_NORMAL'}
    transfer.loop_mapping = 'POLYINTERP_NEAREST'
    bpy.ops.object.modifier_apply(modifier=transfer.name)
    group = ob.vertex_groups.new(name='mixamorig:Head')
    group.add(list(range(len(mesh.vertices))), 1.0, 'REPLACE')
    arm = ob.modifiers.new('Armature', 'ARMATURE'); arm.object = rig
    ob.parent = rig; ob.matrix_parent_inverse = rig.matrix_world.inverted()
    ob.data.materials.append(bpy.data.materials.get(name) or bpy.data.materials.new(name))
    print(f'DECAL {name} vertices={len(mesh.vertices)} faces={len(mesh.polygons)} missed={removed}', flush=True)
    return ob


def setup_render(output):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 8
    scene.cycles.use_denoising = False
    scene.render.resolution_x = 1024; scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'; scene.render.image_settings.color_mode = 'RGBA'
    scene.view_settings.view_transform = 'Standard'; scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0; scene.view_settings.gamma = 1
    scene.world.color = (0,0,0)
    camera_data = bpy.data.cameras.new('VerificationCamera'); camera_data.type = 'ORTHO'; camera_data.ortho_scale = 0.70
    camera = bpy.data.objects.new('VerificationCamera',camera_data); scene.collection.objects.link(camera)
    scene.camera = camera
    return scene, camera


def eye_region_material():
    mat = material('EyeRegion', (1,1,1)); nodes=mat.node_tree.nodes; links=mat.node_tree.links
    geometry=nodes.new('ShaderNodeNewGeometry'); xyz=nodes.new('ShaderNodeSeparateXYZ')
    links.new(geometry.outputs['Position'],xyz.inputs[0])
    tests=[]
    for axis, op, value in [('X','GREATER_THAN',-0.18),('X','LESS_THAN',0.18),('Z','GREATER_THAN',1.17),('Z','LESS_THAN',1.29),('Y','LESS_THAN',0)]:
        node=nodes.new('ShaderNodeMath');node.operation=op;node.inputs[1].default_value=value
        links.new(xyz.outputs[axis],node.inputs[0]);tests.append(node.outputs[0])
    result=tests[0]
    for test in tests[1:]:
        node=nodes.new('ShaderNodeMath');node.operation='MULTIPLY';links.new(result,node.inputs[0]);links.new(test,node.inputs[1]);result=node.outputs[0]
    links.new(result,nodes.get('Emission').inputs['Color'])
    return mat


def render_verification(body, shells, output):
    scene, camera=setup_render(output)
    base=material('SourceColor',texture=True); magenta=material('Coverage',(1,0,1)); white=material('Silhouette',(1,1,1)); roi=eye_region_material()
    output.mkdir(parents=True,exist_ok=True)
    for degrees in [0,-30,30,-60,60]:
        theta=math.radians(degrees)
        camera.location=(3*math.sin(theta),-3*math.cos(theta),1.30)
        camera.rotation_euler=(Vector((0,0,1.30))-camera.location).to_track_quat('-Z','Y').to_euler()
        for mode in ['baseline','eye-region','covered','silhouette']:
            body.hide_render = mode=='silhouette'
            body.data.materials[0]=roi if mode=='eye-region' else base
            for shell in shells:
                shell.hide_render = mode in {'baseline','eye-region'}
                shell.data.materials[0]=white if mode=='silhouette' else magenta
            scene.render.filepath=str(output/f'{degrees}_{mode}.png')
            bpy.ops.render.render(write_still=True)
        print(f'RENDER angle={degrees} done',flush=True)
    body.hide_render=False
    for shell in shells: shell.hide_render=False


def bake_frames(body, shells, output):
    scene, _ = setup_render(output)
    scene.cycles.samples=1
    body.data.materials[0]=material('BakeSource',texture=True)
    scene.render.bake.use_selected_to_active=True
    scene.render.bake.use_cage=False;scene.render.bake.cage_extrusion=0.02;scene.render.bake.max_ray_distance=0.05
    scene.render.bake.margin=0
    output.mkdir(parents=True,exist_ok=True)
    for shell in shells:
        image=bpy.data.images.new(shell.name+'_frame0',width=128,height=128,alpha=True)
        mat=material(shell.name+'_BakeTarget',(1,1,1))
        node=mat.node_tree.nodes.new('ShaderNodeTexImage'); node.image=image
        mat.node_tree.nodes.active=node
        shell.data.materials[0]=mat
        cage=bpy.data.objects.new(shell.name+'_BakeCage',shell.data.copy())
        bpy.context.collection.objects.link(cage);cage.matrix_world=shell.matrix_world.copy()
        for vertex in cage.data.vertices:
            world=cage.matrix_world@vertex.co;world.y=-0.6
            vertex.co=cage.matrix_world.inverted()@world
        cage.hide_render=True
        scene.render.bake.use_cage=True;scene.render.bake.cage_object=cage
        scene.render.bake.max_ray_distance=1.0
        activate(shell);body.select_set(True)
        bpy.ops.object.bake(type='EMIT')
        bpy.data.objects.remove(cage,do_unlink=True)
        image.filepath_raw=str(output/(shell.name+'-frame0.png'));image.file_format='PNG';image.save()
        print(f'BAKE {shell.name} 128x128 done',flush=True)


def append_shells_fbx(source_path, exported_path, output_path):
    """Keep original body/rig records exact; append only Blender-exported shell records.

    FBX re-export rounds the existing skeleton and changes Unity's Humanoid rest pose.
    The Blender FBX codec retains original numeric properties while adding the new meshes.
    """
    from io_scene_fbx import parse_fbx, encode_bin
    from collections import Counter
    source, version = parse_fbx.parse(str(source_path))
    exported, export_version = parse_fbx.parse(str(exported_path))
    if version != export_version: raise RuntimeError('FBX versions differ')
    child = lambda node, name: next(x for x in node.elems if x.id == name)
    name = lambda node: node.props[1].split(b'\x00\x01')[0]
    shell_names = {b'FaceEyes', b'FaceMouth'}
    source_objects = child(source, b'Objects')
    source_connections = child(source, b'Connections')
    export_objects = child(exported, b'Objects')
    export_connections = child(exported, b'Connections')

    def shell_graph(objects, connections):
        by_id = {o.props[0]: o for o in objects.elems}
        selected = {i for i,o in by_id.items() if o.id == b'Model' and name(o) in shell_names}
        changed = True
        while changed:
            changed = False
            for c in connections.elems:
                src, dst = c.props[1:3]
                if dst in selected and src in by_id and by_id[src].id != b'Model' and src not in selected:
                    selected.add(src); changed = True
        return selected

    removed = shell_graph(source_objects, source_connections)
    source_objects.elems[:] = [o for o in source_objects.elems if o.props[0] not in removed]
    source_connections.elems[:] = [c for c in source_connections.elems if not (set(c.props[1:3]) & removed)]
    added = shell_graph(export_objects, export_connections)
    if sum(o.id == b'Model' and o.props[0] in added for o in export_objects.elems) != 2:
        raise RuntimeError('Export must contain exactly two face models')
    originals = {name(o): o.props[0] for o in source_objects.elems if o.id == b'Model'}
    mapping = {o.props[0]: originals[name(o)] for o in export_objects.elems if o.id == b'Model' and name(o) in originals}
    next_id = max(o.props[0] for o in source_objects.elems) + 1
    for i, old in enumerate(sorted(added)): mapping[old] = next_id + i

    # Parsed records are immutable tuples but their props/children lists are editable.
    for o in export_objects.elems:
        if o.props[0] in added:
            o.props[0] = mapping[o.props[0]]
            source_objects.elems.append(o)
    for c in export_connections.elems:
        src, dst = c.props[1:3]
        if src not in added and dst not in added: continue
        if src not in mapping or (dst != 0 and dst not in mapping):
            raise RuntimeError(f'Unresolved shell connection {c.props}')
        c.props[1] = mapping[src]; c.props[2] = mapping.get(dst, 0)
        source_connections.elems.append(c)

    pose = next(o for o in source_objects.elems if o.id == b'Pose')
    pose.elems[:] = [p for p in pose.elems if p.id != b'PoseNode' or child(p,b'Node').props[0] not in removed]
    for exported_pose in (o for o in export_objects.elems if o.id == b'Pose'):
        for p in exported_pose.elems:
            if p.id != b'PoseNode': continue
            node = child(p, b'Node')
            if node.props[0] in added:
                node.props[0] = mapping[node.props[0]]; pose.elems.append(p)
    child(pose,b'NbPoseNodes').props[0] = sum(p.id == b'PoseNode' for p in pose.elems)
    definitions = child(source, b'Definitions')
    counts = Counter(o.id for o in source_objects.elems)
    child(definitions,b'Count').props[0] = len(source_objects.elems)
    for definition in definitions.elems:
        if definition.id == b'ObjectType': child(definition,b'Count').props[0] = counts[definition.props[0]]

    methods = dict(zip(b'BCZYILFDRSilfdbc', ['add_bool','add_char','add_int8','add_int16','add_int32','add_int64','add_float32','add_float64','add_bytes','add_string','add_int32_array','add_int64_array','add_float32_array','add_float64_array','add_bool_array','add_byte_array']))
    def encode(node):
        result = encode_bin.FBXElem(node.id)
        for kind, prop in zip(node.props_type, node.props): getattr(result, methods[kind])(prop)
        result.elems = [encode(c) for c in node.elems]
        return result
    encode_bin.write(str(output_path.resolve()), encode(source), version)
    print(f'PRESERVED_FBX original rig/body; added_objects={len(added)}', flush=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--input',type=Path,default=FBX)
    parser.add_argument('--output',type=Path,default=FBX)
    parser.add_argument('--render-dir',type=Path)
    parser.add_argument('--bake-dir',type=Path)
    parser.add_argument('--shrink',type=float,default=0)
    parser.add_argument('--verify-existing',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    body,rig=load_model(args.input)
    rig.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    if args.verify_existing:
        shells=[bpy.data.objects.get(p[0]) for p in PATCHES]
        if any(s is None for s in shells): raise RuntimeError('Missing face shells')
    else:
        for name,*_ in PATCHES:
            old=bpy.data.objects.get(name)
            if old:
                old_mesh=old.data
                bpy.data.objects.remove(old,do_unlink=True)
                if old_mesh.users==0: bpy.data.meshes.remove(old_mesh)
        shells=[create_shell(body,rig,*p,args.shrink) for p in PATCHES]
        bpy.ops.object.select_all(action='DESELECT')
        for ob in [body,rig,*shells]: ob.select_set(True)
        with tempfile.TemporaryDirectory() as temporary:
            exported = Path(temporary) / 'shells.fbx'
            bpy.ops.export_scene.fbx(filepath=str(exported),use_selection=True,add_leaf_bones=False,bake_anim=False,
                                     apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y')
            append_shells_fbx(args.input, exported, args.output)
        print(f'EXPORT {args.output} done',flush=True)
    if args.render_dir: render_verification(body,shells,args.render_dir)
    if args.bake_dir: bake_frames(body,shells,args.bake_dir)
    print('FACE_DECALS_DONE',flush=True)

if __name__=='__main__': main()
