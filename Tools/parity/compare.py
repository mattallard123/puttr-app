"""Compares app.json (puttr-app's JavaScript) with unity.json (the C# port) putt by putt.

Every field must match: booleans and text exactly, numbers to 1e-6 (rollout and scores to
0.001). Exits 1 on any difference. Also reports, for information, where the putt SDK's own
reading (used by the 3D replay) disagrees with the app's.
"""
import json, sys
folder = sys.argv[1] if len(sys.argv) > 1 else "Tools/parity"
app = {str(r["id"]): r for r in json.load(open(folder + "/app.json"))}
unity = {str(r["id"]): r for r in json.load(open(folder + "/unity.json"))}
fields = ["ball", "rollUnder", "count", "entry", "teeLine", "description", "missResult", "missSide", "rollout", "arrival", "score", "grade", "startLine"]
loose = {"rollout": 1e-3, "arrival": 1e-6, "score": 1e-3, "startLine": 1e-6, "teeLine": 1e-9, "entry": 1e-9}
bad = {}
examples = {}
for pid, a in app.items():
    u = unity.get(pid)
    if u is None or u.get("unreadable"):
        bad.setdefault("missing", 0); bad["missing"] += 1; continue
    for f in fields:
        x, y = a.get(f), u.get(f)
        same = x == y
        if not same and isinstance(x, (int, float)) and isinstance(y, (int, float)) and not isinstance(x, bool):
            same = abs(x - y) <= loose.get(f, 1e-9)
        if not same:
            bad[f] = bad.get(f, 0) + 1
            examples.setdefault(f, (pid, x, y))
n = len(app)
print(f"{n} putts compared")
if not bad:
    print("PARITY: every field matches the app")
else:
    print("DIFFERENCES:")
    for f, c in bad.items():
        print(f"  {f}: {c} putts, e.g. putt {examples.get(f)}")
# The SDK, for information.
sdk_made = sum(1 for pid, a in app.items() if pid in unity and "sdkMade" in unity[pid] and unity[pid]["sdkMade"] != (a["count"] > 1))
sdk_entry = sum(1 for pid, a in app.items() if pid in unity and a["count"] > 1 and unity[pid].get("sdkEntry") is not None and unity[pid]["sdkEntry"] != a["entry"])
sdk_side = sum(1 for pid, a in app.items() if pid in unity and a["count"] <= 1 and unity[pid].get("sdkMissSide") != a["missSide"])
saved = sum(1 for a in app.values() if a["count"] == 1)
print(f"SDK vs app (information): made differs on {sdk_made}, entry on {sdk_entry}, miss side on {sdk_side}")
print(f"Putts the app SAVES as made but scores as a miss (count exactly 1): {saved}")
sys.exit(1 if bad else 0)
