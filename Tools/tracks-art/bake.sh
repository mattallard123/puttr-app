#!/bin/bash
# Makes everything Tracks draws its ground, sky and trees from, out of the CC0 scans in Tools/tracks-art/source
# (python3 Tools/tracks-art/fetch.py gets them: about 2 GB, none of it shipped).
#   Tools/tracks-art/bake.sh            ground + sky + the tree atlas (from cut-outs already rendered)
#   Tools/tracks-art/bake.sh trees      render the tree cut-outs again first (Blender, a few minutes)
H="$(cd "$(dirname "$0")" && pwd)"
if [ "$1" = "trees" ]; then
  one() { blender -b -P "$H/bake_trees.py" -- "$H/source/trees/$1/$1.gltf" "$H/source/cutouts" "$2" $3 2>&1 | grep -E "BAKED|Error|Traceback"; }
  for m in jacaranda_tree island_tree_01 island_tree_02 tree_small_02; do one $m $m; done
  for m in pine_tree_01 fir_tree_01 searsia_lucida; do i=0; for v in a b c; do one $m ${m}_$v $i; i=$((i+1)); done; done
fi
python3 "$H/bake_ground.py" && python3 "$H/bake_atlas.py"
