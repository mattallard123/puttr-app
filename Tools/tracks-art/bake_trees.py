"""Blender: renders one scanned tree or bush to two cut-outs, from the side and from above.

  blender -b -P Tools/tracks-art/bake_trees.py -- <model.gltf> <out folder> <name> [plant]

Some models hold several plants side by side, one object each: plant (0, 1, ... by object name) renders that one alone.

Lit by an even white sky only (Cycles), so the picture is the leaves' own colour darkened where the canopy shades
itself: the game adds the sun. Writes <name>_side.png, <name>_top.png (RGBA, 1024 px) and <name>.txt with
"height width depth" in metres. The side view is a square as tall as the taller of height and width, the trunk's foot
at the middle of its bottom edge; the top view is a square over the canopy's middle.
"""
import bpy, sys, os, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
model, out, name = argv[0], argv[1], argv[2]
os.makedirs(out, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=model)
scene = bpy.context.scene
meshes = sorted([o for o in scene.objects if o.type == "MESH"], key=lambda o: o.name)
if len(argv) > 3:
    keep = meshes[int(argv[3])]
    for o in meshes: o.hide_render = o is not keep
    meshes = [keep]
lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
for o in meshes:
    for c in o.bound_box:
        p = o.matrix_world @ Vector(c)
        lo = Vector(map(min, lo, p)); hi = Vector(map(max, hi, p))
size = hi - lo
mid = (lo + hi) / 2

scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 48
scene.cycles.use_denoising = True
scene.cycles.transparent_max_bounces = 64
scene.cycles.max_bounces = 3
scene.render.film_transparent = True
scene.render.resolution_x = scene.render.resolution_y = 1024
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"

world = bpy.data.worlds.new("Even sky")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (1, 1, 1, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
scene.world = world

cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
far = max(size) * 4 + 10
cam_data.clip_start = 0.01; cam_data.clip_end = far * 2

# From the side (looking along +Y): the foot at the bottom middle.
side = max(size.z, size.x) * 1.04
cam_data.ortho_scale = side
cam.location = (mid.x, lo.y - far, lo.z + side / 2 - side * 0.01)
cam.rotation_euler = (math.radians(90), 0, 0)
scene.render.filepath = os.path.join(out, name + "_side.png")
bpy.ops.render.render(write_still=True)

# From above.
top = max(size.x, size.y) * 1.04
cam_data.ortho_scale = top
cam.location = (mid.x, mid.y, hi.z + far)
cam.rotation_euler = (0, 0, 0)
scene.render.filepath = os.path.join(out, name + "_top.png")
bpy.ops.render.render(write_still=True)

with open(os.path.join(out, name + ".txt"), "w") as f:
    f.write("%.3f %.3f %.3f %.3f %.3f\n" % (size.z, size.x, size.y, side, top))
print("BAKED", name, tuple(round(v, 2) for v in size))
