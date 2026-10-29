"""3D 모델(뼈대 + 애니메이션) → 사용자 그림 시트(PNG) — 편집기 「사용자 그림 만들기 > 3D 모델에서…」가 Blender 로 부른다(사용자 요청 ed-7).

    blender -b --factory-startup -P tools/custom-sprite/render_3d.py -- <모델.glb|.fbx|.blend> <내보낼 폴더> [설정.json]

모든 캐릭터에 같은 동작 한 벌을 입혀 같은 장 수 · 같은 원점으로 뽑으려는 것이다. 동작마다 시트 한 장(줄 = 뒷모습 · 옆모습(왼쪽을 봄) · 앞모습,
칸 = 장)을 쓰고, 편집기가 그대로 읽는 시트 설정(parts.json — CustomSpriteWindow.Part 꼴)을 함께 쓴다. 바탕은 쨍한 핑크(ff00ff)이고
반투명 가장자리는 알파 반에서 끊어 핑크가 번지지 않게 한다(게임 그림은 255색 팔레트 + 투명 하나라 반투명이 없다).

카메라는 고정 — 직교, 내려다보는 각(elevation, 기본 30도), 모델 발밑(원점)을 칸의 같은 자리에 둔다. 그래서 모든 장 · 동작 · 캐릭터가
같은 원점과 크기다. 방향은 모델을 Z 축으로 돌려 찍는다(glTF 모델은 들여오면 −Y 를 본다 = 앞모습).

설정.json(없으면 기본값):
  {"elevation": 30, "cell": 256, "margin": 1.35, "hide": ["2H_Sword", "Spike_Shield"],
   "actions": [{"action": 0, "clip": "idle", "frames": 4, "ticks": 8}, …]}
  clip 은 애니메이션 이름에 들어 있는 글(대소문자 무시) — 여러 개면 「|」 로(앞의 것부터 찾는다). 「=이름」 이면 그 이름 그대로만. 못 찾은 동작은 건너뛴다.
  animations 는 동작을 따로 담은 파일(모델 폴더 기준) — Mixamo 는 동작마다 FBX 를 하나씩 준다(첫 것만 「With Skin」, 나머지는 「Without Skin」).
  들여와 그 동작만 남기고 이름을 파일 이름으로 바꾼다(Mixamo 동작은 모두 「mixamo.com」 이라 그대로는 못 가른다) — clip 에 「=파일 이름」 으로 쓴다.
  toon(기본 켬)은 셀 음영 · 외곽선 — 색을 단계(toon_levels)로 끊고 바깥 테두리를 outline 픽셀만큼 어둡게 두른다(원본 도트 그림처럼).
  hide 는 렌더하지 않을 물체 이름(무기 · 방패를 여러 벌 싣는 모델 — 하나만 남긴다). 「Icosphere」(glTF 내보내기가 남기는 빈 공)는 늘 숨긴다.
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector

DEFAULT = {
    "elevation": 30,
    "toon": True,
    "toon_levels": 6,
    "outline": 3,
    "cell": 256,
    "margin": 1.35,
    "actions": [
        {"action": 0, "clip": "idle", "frames": 4, "ticks": 8},
        {"action": 1, "clip": "walk", "frames": 8, "ticks": 3},
        {"action": 2, "clip": "hit|hurt|no|death", "frames": 5, "ticks": 4},
        {"action": 8, "clip": "punch|attack|slash|sword", "frames": 8, "ticks": 4, "hit": "5"},
        {"action": 13, "clip": "punch|attack|slash|sword", "frames": 8, "ticks": 4, "hit": "3+6"},
    ],
}
DIRS = [("back", 180.0), ("side", -90.0), ("front", 0.0)]   # 줄 차례 · 모델을 돌리는 각(도) — 옆모습은 화면 왼쪽을 본다
KEY = (1.0, 0.0, 1.0)


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(argv) < 2:
        raise SystemExit("쓰기: blender -b --factory-startup -P render_3d.py -- <모델> <내보낼 폴더> [설정.json]")
    cfg = dict(DEFAULT)
    if len(argv) > 2:
        with open(argv[2], encoding="utf-8") as f:
            cfg.update(json.load(f))
    return os.path.abspath(argv[0]), os.path.abspath(argv[1]), cfg


def load_model(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ext = os.path.splitext(path)[1].lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext == ".blend":
        bpy.ops.wm.open_mainfile(filepath=path)
    else:
        raise SystemExit(f"모르는 모델 꼴: {ext} (.glb · .gltf · .fbx · .blend)")
    arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    if arm is None:
        raise SystemExit("뼈대(armature)가 없는 모델입니다")
    # 모델 전체를 빈 물체 하나에 달아 돌린다 — 뼈대 · 메시가 따로 놓여 있어도 같이 돈다.
    pivot = bpy.data.objects.new("pivot", None)
    bpy.context.scene.collection.objects.link(pivot)
    for o in bpy.context.scene.objects:
        if o.parent is None and o is not pivot:
            o.parent = pivot
    return arm, pivot


def import_animations(model, files):
    """동작 파일들을 들여와 그 동작만 남긴다 — 이름은 파일 이름(확장자 뺌)."""
    folder = os.path.dirname(model)
    for rel in files:
        path = rel if os.path.isabs(rel) else os.path.join(folder, rel)
        before_objs, before_acts = set(bpy.data.objects), set(bpy.data.actions)
        ext = os.path.splitext(path)[1].lower()
        if ext == ".fbx":
            bpy.ops.import_scene.fbx(filepath=path)
        else:
            bpy.ops.import_scene.gltf(filepath=path)
        for act in set(bpy.data.actions) - before_acts:
            act.name = os.path.splitext(os.path.basename(path))[0]
            act.use_fake_user = True
        for o in set(bpy.data.objects) - before_objs:
            bpy.data.objects.remove(o, do_unlink=True)


def toon(rgba, levels, outline):
    """셀 음영 · 외곽선 — 색을 levels 단계로 끊고, 불투명한 곳 가운데 투명한 곳에서 outline 픽셀 안쪽을 0.35 배로 어둡게."""
    rgb, solid = rgba[..., :3], rgba[..., 3] >= 0.5
    rgb = np.round(np.clip(rgb, 0, 1) * levels) / levels
    edge = np.zeros_like(solid)
    for dy in range(-outline, outline + 1):
        for dx in range(-outline, outline + 1):
            if dx * dx + dy * dy > outline * outline:
                continue
            edge |= ~np.roll(np.roll(solid, dy, axis=0), dx, axis=1)
    edge &= solid
    rgb = np.where(edge[..., None], rgb * 0.35, rgb)
    return np.concatenate([rgb, rgba[..., 3:]], axis=2)


def find_action(arm, spec):
    # 뼈를 움직이는 동작만 — glTF 는 얼굴 표정(모양 키) 동작을 같은 이름으로 따로 싣는다(RobotExpressive 의 「Idle_Head」 따위).
    names = [a.name for a in bpy.data.actions if any(fc.data_path.startswith("pose.bones") for fc in a.fcurves)]
    for want in spec.split("|"):
        for n in names:
            if (n == want[1:] if want.startswith("=") else want.lower() in n.lower()):
                return bpy.data.actions[n]
    return None


def bounds(objects):
    lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
    deps = bpy.context.evaluated_depsgraph_get()
    for o in objects:
        if o.type != "MESH":
            continue
        ev = o.evaluated_get(deps)
        for v in ev.bound_box:
            w = ev.matrix_world @ Vector(v)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


def setup_scene(cfg, height):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = cfg["cell"]
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("w")
    world.color = (0.55, 0.55, 0.6)
    scene.world = world
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = height * cfg["margin"]
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    el = math.radians(cfg["elevation"])
    dist = height * 10
    # 발밑(원점)이 칸 아래쪽 1/6 자리에 오게 — 몸 가운데(키 0.45)를 본다.
    target = Vector((0, 0, height * 0.45))
    cam.location = target + Vector((0, -math.cos(el) * dist, math.sin(el) * dist))
    cam.rotation_euler = (math.pi / 2 - el, 0, 0)
    scene.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)


def render_cell(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1]   # 위가 0 인 줄 차례로
    bpy.data.images.remove(img)
    return px


def main():
    model, out, cfg = args()
    os.makedirs(out, exist_ok=True)
    arm, pivot = load_model(model)
    import_animations(model, cfg.get("animations", []))
    for o in bpy.context.scene.objects:
        if o.name in set(cfg.get("hide", [])) | {"Icosphere"}:
            o.hide_render = True
    if arm.animation_data is None:
        arm.animation_data_create()
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH" and not o.hide_render]
    # 키 — 첫 동작(서기) 첫 장의 메시 높이. 모든 동작 · 방향이 이 카메라를 같이 쓴다.
    first = next((a for a in (find_action(arm, x["clip"]) for x in cfg["actions"]) if a), None)
    if first:
        arm.animation_data.action = first
        bpy.context.scene.frame_set(int(first.frame_range[0]))
    lo, hi = bounds(meshes)
    height = max(0.01, hi.z - lo.z)
    pivot.location.z = -lo.z                                 # 발밑을 z = 0 에
    setup_scene(cfg, height)
    cell = cfg["cell"]
    # 칸 안 원점(발밑)과 키(픽셀) — 카메라가 고정이라 모든 칸 · 동작이 같다. 그림 만들기 도구가 재지 않고 이것을 쓴다(CustomSpriteBuilder.Part.OriginX · PixelHeight).
    scene, cam = bpy.context.scene, bpy.context.scene.camera
    bpy.context.view_layer.update()                          # 새로 놓은 카메라의 행렬을 먼저 셈한다
    foot = world_to_camera_view(scene, cam, Vector((0, 0, 0)))
    head = world_to_camera_view(scene, cam, Vector((0, 0, height)))
    origin = (round(foot.x * cell), round((1 - foot.y) * cell))
    pixel_height = round((head.y - foot.y) * cell, 1)
    tmp = os.path.join(out, "_cell.png")
    parts, report = [], []
    for spec in cfg["actions"]:
        act = find_action(arm, spec["clip"])
        if act is None:
            report.append(f"동작 {spec['action']}: 「{spec['clip']}」 애니메이션 없음 — 건너뜀")
            continue
        arm.animation_data.action = act
        start, end = act.frame_range
        n = spec["frames"]
        frames = [start + (end - start) * i / n for i in range(n)]
        sheet = np.zeros((cell * 3, cell * n, 4), dtype=np.float32)
        for row, (_, angle) in enumerate(DIRS):
            pivot.rotation_euler = (0, 0, math.radians(angle))
            for col, f in enumerate(frames):
                bpy.context.scene.frame_set(int(f), subframe=f - int(f))
                px = render_cell(tmp)
                if cfg.get("toon"):
                    px = toon(px, cfg["toon_levels"], cfg["outline"])
                sheet[row * cell:(row + 1) * cell, col * cell:(col + 1) * cell] = px
        # 알파 반에서 끊고 바탕을 핑크로 — 게임 그림에는 반투명이 없다.
        solid = sheet[..., 3] >= 0.5
        rgb = np.where(solid[..., None], sheet[..., :3], np.array(KEY, dtype=np.float32))
        name = f"a{spec['action']:02d}_{act.name}.png".replace(" ", "_")
        save_png(os.path.join(out, name), rgb)
        rows = ",".join(f"{d}:{i * cell}:{(i + 1) * cell}" for i, (d, _) in enumerate(DIRS))
        parts.append({"Path": name, "Action": spec["action"], "Rows": rows, "ColX": 0, "ColW": cell, "Cols": n,
                      "Background": "key=ff00ff", "Anchor": "foot" if spec["action"] not in (0, 1) else "body",
                      "Hit": spec.get("hit", ""), "Sound": spec.get("sound", ""), "Ticks": spec.get("ticks", 0),
                      "OriginX": origin[0], "OriginY": origin[1], "PixelHeight": pixel_height})
        report.append(f"동작 {spec['action']}: {act.name} {n}장 ({start:.0f}~{end:.0f}틀)")
    if os.path.exists(tmp):
        os.remove(tmp)
    with open(os.path.join(out, "parts.json"), "w", encoding="utf-8") as f:
        json.dump({"Parts": parts, "Clips": [a.name for a in bpy.data.actions], "Report": report}, f, ensure_ascii=False, indent=2)
    print("RENDER3D " + " / ".join(report))


def save_png(path, rgb):
    h, w, _ = rgb.shape
    img = bpy.data.images.new("sheet", w, h, alpha=True)
    rgba = np.concatenate([rgb, np.ones((h, w, 1), dtype=np.float32)], axis=2)[::-1]
    img.pixels = rgba.ravel().tolist()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


main()
