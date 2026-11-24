#!/usr/bin/env python3
"""Fetches the CC0 source art for Tracks into Tools/tracks-art/source (not shipped; bake.sh makes what ships).

  python3 Tools/tracks-art/fetch.py            everything
  python3 Tools/tracks-art/fetch.py sky ground only those groups (sky, ground, trees)

Poly Haven (polyhaven.com) and ambientCG (ambientcg.com): both CC0, no attribution needed.
"""
import json, os, sys, urllib.request, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "source")
UA = {"User-Agent": "asset-fetch/1.0"}

SKY = "kloofendal_48d_partly_cloudy_puresky"
PH_TEXTURES = ["sand_01", "coast_sand_01", "forest_leaves_02", "leafy_grass", "sparse_grass", "grass_path_2"]
ACG_TEXTURES = ["Grass001", "Grass002", "Grass003", "Grass004", "Grass005", "Grass006", "Grass007", "Grass008"]
TREES = ["shrub_02", "shrub_04", "searsia_lucida", "island_tree_02", "island_tree_01", "tree_small_02", "jacaranda_tree", "fir_tree_01", "pine_tree_01"]


def get(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req) as r, open(path + ".part", "wb") as f:
        while True:
            chunk = r.read(1 << 20)
            if not chunk:
                break
            f.write(chunk)
    os.rename(path + ".part", path)
    print("got", os.path.relpath(path, SRC), round(os.path.getsize(path) / 1e6, 1), "MB", flush=True)


def files(asset):
    req = urllib.request.Request("https://api.polyhaven.com/files/" + asset, headers=UA)
    return json.load(urllib.request.urlopen(req))


def sky():
    d = files(SKY)
    get(d["tonemapped"]["url"], os.path.join(SRC, "sky", SKY + ".jpg"))
    get(d["hdri"]["2k"]["hdr"]["url"], os.path.join(SRC, "sky", SKY + "_2k.hdr"))


def ground():
    for t in PH_TEXTURES:
        d = files(t)
        for kind in ("Diffuse", "nor_gl", "Rough", "AO"):
            if kind in d and "1k" in d[kind] and "jpg" in d[kind]["1k"]:
                get(d[kind]["1k"]["jpg"]["url"], os.path.join(SRC, "ground", t, kind + ".jpg"))
    for t in ACG_TEXTURES:
        z = os.path.join(SRC, "ground", t + ".zip")
        get("https://ambientcg.com/get?file=%s_1K-JPG.zip" % t, z)
        out = os.path.join(SRC, "ground", t)
        if not os.path.isdir(out):
            zipfile.ZipFile(z).extractall(out)


def trees():
    for t in TREES:
        d = files(t)["gltf"]["2k"]["gltf"]
        base = os.path.join(SRC, "trees", t)
        get(d["url"], os.path.join(base, t + ".gltf"))
        for rel, inc in d.get("include", {}).items():
            get(inc["url"], os.path.join(base, rel))


if __name__ == "__main__":
    want = sys.argv[1:] or ["sky", "ground", "trees"]
    for g in want:
        {"sky": sky, "ground": ground, "trees": trees}[g]()
    print("done", want, flush=True)
