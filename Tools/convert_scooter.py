"""Convert the documented Innerscene CC0 scooter GLB to Unity's native OBJ format.

Usage: python Tools/convert_scooter.py path/to/urban-motor-scooter.glb
No geometry is authored here. Node names, normals and transformed texture UVs are
preserved. This deliberately rejects unsupported GLB features instead of silently
producing a broken conversion. The original source remains alongside the OBJ.
"""
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import sys

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "VR-Motor-Street-Riding/Assets/MotorSimulatorAssets/Models/UrbanScooter"


def convert(source):
    data = Path(source).read_bytes()
    magic, version, length = struct.unpack_from("<III", data)
    assert magic == 0x46546C67 and version == 2 and length == len(data)
    size, kind = struct.unpack_from("<II", data, 12)
    assert kind == 0x4E4F534A
    doc = json.loads(data[20:20 + size])
    size2, kind2 = struct.unpack_from("<II", data, 20 + size)
    assert kind2 == 0x004E4942
    binary = data[28 + size:28 + size + size2]
    assert all(not any(k in n for k in ("matrix", "translation", "rotation", "scale"))
               for n in doc["nodes"]), "Bake node transforms before conversion"
    assert set(doc.get("extensionsRequired", [])) <= {"KHR_texture_transform"}
    DEST.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, DEST / "urban-motor-scooter.glb.bytes")

    def accessor(index):
        a = doc["accessors"][index]
        assert not a.get("sparse") and not a.get("normalized")
        view = doc["bufferViews"][a["bufferView"]]
        dtype = {5126: "<f4", 5125: "<u4", 5123: "<u2", 5121: "u1"}[a["componentType"]]
        count = {"VEC3": 3, "VEC2": 2, "SCALAR": 1}[a["type"]]
        item = np.dtype(dtype).itemsize
        return np.ndarray((a["count"], count), dtype=dtype, buffer=binary,
                          offset=view.get("byteOffset", 0) + a.get("byteOffset", 0),
                          strides=(view.get("byteStride", item * count), item)).copy()

    materials = []
    for i, material in enumerate(doc["materials"]):
        pbr = material.get("pbrMetallicRoughness", {})
        texture = pbr.get("baseColorTexture")
        name = f"Scooter_{i:02d}"
        filename = ""
        if texture:
            img = doc["images"][doc["textures"][texture["index"]]["source"]]
            assert img["mimeType"] == "image/png"
            view = doc["bufferViews"][img["bufferView"]]
            filename = name + ".png"
            (DEST / filename).write_bytes(binary[view.get("byteOffset", 0):
                view.get("byteOffset", 0) + view["byteLength"]])
        materials.append(dict(name=name, label=material["name"], texture=filename,
                              metallic=pbr.get("metallicFactor", 1),
                              smoothness=1-pbr.get("roughnessFactor", 1),
                              color=pbr.get("baseColorFactor", [1, 1, 1, 1])))

    total_vertices = total_triangles = 0
    with (DEST / "UrbanScooter.obj").open("w", encoding="utf-8", newline="\n") as f:
        f.write("# Innerscene Urban motor scooter, CC0. See LICENSE.md.\nmtllib UrbanScooter.mtl\n")
        offset = 1
        for node in doc["nodes"]:
            if "mesh" not in node:
                continue
            mesh = doc["meshes"][node["mesh"]]
            for p in mesh["primitives"]:
                assert p.get("mode", 4) == 4
                attr = p["attributes"]
                positions = accessor(attr["POSITION"])
                normals = accessor(attr["NORMAL"])
                uv = accessor(attr["TEXCOORD_0"])
                mat = doc["materials"][p["material"]]
                tx = mat.get("pbrMetallicRoughness", {}).get("baseColorTexture", {})
                transform = tx.get("extensions", {}).get("KHR_texture_transform", {})
                assert transform.get("rotation", 0) == 0
                uv = uv * transform.get("scale", [1, 1]) + transform.get("offset", [0, 0])
                uv[:, 1] = 1 - uv[:, 1]
                # Source: +X forward, Y up; OBJ: +Z forward. Unity's OBJ importer
                # converts handedness. Reversing X/Z also reverses winding here.
                positions = positions[:, [2, 1, 0]]
                normals = normals[:, [2, 1, 0]]
                node_name = re.sub(r"[^A-Za-z0-9_-]", "_", node["name"])
                # Unity's native OBJ importer splits on groups, not object records.
                f.write(f"o {node_name}\ng {node_name}\n")
                f.write(f"usemtl Scooter_{p['material']:02d}\n")
                for v in positions:
                    f.write("v %.7g %.7g %.7g\n" % tuple(v))
                for v in uv:
                    f.write("vt %.7g %.7g\n" % tuple(v))
                for v in normals:
                    f.write("vn %.7g %.7g %.7g\n" % tuple(v))
                faces = accessor(p["indices"]).astype(np.int64).reshape(-1, 3)[:, [0, 2, 1]] + offset
                for face in faces:
                    f.write("f " + " ".join(f"{v}/{v}/{v}" for v in face) + "\n")
                offset += len(positions)
                total_vertices += len(positions)
                total_triangles += len(faces)
    with (DEST / "UrbanScooter.mtl").open("w", encoding="utf-8", newline="\n") as f:
        for m in materials:
            f.write(f"newmtl {m['name']}\nKd 1 1 1\n")
            if m["texture"]:
                f.write(f"map_Kd {m['texture']}\n")
    (DEST / "materials.json").write_text(json.dumps(dict(materials=materials), indent=2))
    print(json.dumps(dict(vertices=total_vertices, triangles=total_triangles,
                          materials=len(materials), source_sha256=hashlib.sha256(data).hexdigest()), indent=2))


if __name__ == "__main__":
    convert(sys.argv[1])
