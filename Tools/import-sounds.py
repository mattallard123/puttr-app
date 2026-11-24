#!/usr/bin/env python3
"""Bring library sounds into the arcade.

SoundDrop/<slot>/<any audio files>  ->  Assets/PuttrArcade/Resources/PuttrArcade/Audio/Library/<slot>_<n>.wav

One-shots: silence trimmed off both ends, levelled, capped at 8 seconds with a short fade out.
Loops (slots ending _loop): the steadiest 24 seconds, levelled, with its end
cross-faded into its start so it repeats without a click.
Needs ffmpeg. Run from the project folder: python3 Tools/import-sounds.py

A slot folder with no audio in it yet (a download still on its way, a folder made ahead of time) is skipped with a
warning and its sounds already in the app are left alone; so is a slot none of whose files could be read. A slot's old
takes are replaced only once its new ones have all been written.
"""
import glob, os, shutil, subprocess, sys, tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DROP = os.path.join(ROOT, "SoundDrop")
OUT = os.path.join(ROOT, "Assets/PuttrArcade/Resources/PuttrArcade/Audio/Library")
KINDS = (".wav", ".aif", ".aiff", ".flac", ".mp3", ".ogg", ".m4a")
LOOP_SECONDS, FADE = 24.0, 1.5

def run(args):
    done = subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y"] + args, capture_output=True, text=True)
    if done.returncode != 0: print("   ffmpeg:", done.stderr.strip()[:300])
    return done.returncode == 0

def length(path):
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path], capture_output=True, text=True).stdout.strip()
    return float(out) if out else 0.0

def takes(path):
    """Where the separate sounds in a file are: [(start, end), ...]. Library files often hold several takes."""
    err = subprocess.run(["ffmpeg", "-hide_banner", "-i", path, "-af", "silencedetect=noise=-42dB:d=0.6", "-f", "null", "-"], capture_output=True, text=True).stderr
    total = length(path)
    quiet, start = [], None
    for line in err.splitlines():
        if "silence_start:" in line: start = float(line.split("silence_start:")[1].split()[0])
        elif "silence_end:" in line and start is not None:
            quiet.append((start, float(line.split("silence_end:")[1].split()[0]))); start = None
    if start is not None: quiet.append((start, total))
    spans, at = [], 0.0
    for q0, q1 in quiet:
        if q0 - at > 0.06: spans.append((max(0.0, at - 0.01), min(total, q0 + 0.12)))
        at = q1
    if total - at > 0.06: spans.append((max(0.0, at - 0.01), total))
    return spans or [(0.0, total)]

def peak_of(path, t0, t1):
    err = subprocess.run(["ffmpeg", "-hide_banner", "-ss", str(t0), "-t", str(max(0.02, t1 - t0)), "-i", path, "-af", "volumedetect", "-f", "null", "-"], capture_output=True, text=True).stderr
    for line in err.splitlines():
        if "max_volume:" in line: return float(line.split("max_volume:")[1].split()[0])
    return -90.0

def main():
    os.makedirs(OUT, exist_ok=True)
    made, skipped = 0, []
    for slot in sorted(os.listdir(DROP)):
        folder = os.path.join(DROP, slot)
        if not os.path.isdir(folder) or slot.startswith("_"): continue  # _incoming, _sources: not slots
        files = sorted(f for f in glob.glob(os.path.join(folder, "*")) if f.lower().endswith(KINDS))[:8]
        if not files:
            # Nothing to import (yet): never wipe a slot's sounds because its folder is empty.
            kept = len(glob.glob(os.path.join(OUT, slot + "_[0-9].wav")))
            print("%-22s skipped: SoundDrop/%s/ has no audio files (%d take%s in the app left as they are)" % (slot, slot, kept, "" if kept == 1 else "s"))
            skipped.append(slot)
            continue
        # The new takes are written aside first; the old ones are replaced only if at least one new take was made,
        # so a file taken out of the folder leaves the app too, but a slot whose files all fail keeps what it had.
        staged = tempfile.mkdtemp(prefix="puttr-sound-" + slot + "-")
        n = _import_slot(slot, files, staged)
        made += n
        if n == 0:
            print("%-22s skipped: none of its files could be imported (the app's takes are left as they are)" % slot)
            skipped.append(slot)
        else:
            for old in glob.glob(os.path.join(OUT, slot + "_[0-9].wav")): os.remove(old)
            for new in sorted(glob.glob(os.path.join(staged, slot + "_[0-9].wav"))):
                shutil.move(new, os.path.join(OUT, os.path.basename(new)))
        shutil.rmtree(staged, ignore_errors=True)
    print("%d sounds imported into %s" % (made, os.path.relpath(OUT, ROOT)))
    if skipped: print("Skipped (nothing changed in the app): " + ", ".join(skipped))
    if made == 0: print("Nothing in SoundDrop yet: export from Soundly into the slot folders first.")

def _import_slot(slot, files, OUT):
    """Write the slot's takes into OUT (a folder of its own, not the app's) as <slot>_<n>.wav; say how many were made."""
    made = 0
    n = 0
    for src in files:
        if slot.endswith("_loop"):
            if n >= 1: break  # one loop to a slot
            n += 1
            dst = os.path.join(OUT, "%s_%d.wav" % (slot, n))
            total = length(src)
            take = min(LOOP_SECONDS, max(2.0, total - 1.0))
            start = max(0.0, (total - take) / 2.0)  # the middle is the steadiest part
            fade = min(FADE, take / 4.0)
            body = take - fade
            # The last `fade` seconds are laid over the first, so the join is a cross-fade.
            graph = ("[0:a]atrim=%f:%f,asetpts=PTS-STARTPTS,loudnorm=I=-20:TP=-2:LRA=11,asplit[a][b];"
                     "[a]atrim=0:%f,asetpts=PTS-STARTPTS,afade=t=in:st=0:d=%f[head];"
                     "[b]atrim=%f:%f,asetpts=PTS-STARTPTS,afade=t=out:st=0:d=%f[tail];"
                     "[head][tail]amix=inputs=2:duration=first:normalize=0[out]") % (start, start + take, body, fade, body, take, fade)
            if run(["-i", src, "-filter_complex", graph, "-map", "[out]", "-ar", "44100", "-c:a", "pcm_s16le", dst]):
                made += 1
                print("%-22s %d  %5.1fs  <- %s" % (slot, n, length(dst), os.path.basename(src)))
            continue
        # A one-shot file: each separate sound in it becomes a take, up to four from one file.
        spans = takes(src)
        # Leave out scraps: a tail or a knock far quieter than the file's loudest take.
        if len(spans) > 1:
            loud = [peak_of(src, a, b) for a, b in spans]
            spans = [sp for sp, l in zip(spans, loud) if l > max(loud) - 14.0]
        for t0, t1 in spans[:4]:
            if n >= 8: break
            t1 = min(t1, t0 + 8.0)
            span = t1 - t0
            n += 1
            dst = os.path.join(OUT, "%s_%d.wav" % (slot, n))
            # Peak-levelled (short sounds are too brief for loudness measuring), with the ends eased.
            graph = "atrim=%f:%f,asetpts=PTS-STARTPTS,afade=t=in:st=0:d=0.004,afade=t=out:st=%f:d=%f" % (t0, t1, max(0.0, span - min(0.4, span * 0.3)), min(0.4, span * 0.3))
            tmp = dst + ".tmp.wav"
            ok = run(["-i", src, "-af", graph, "-ar", "44100", "-c:a", "pcm_s16le", tmp])
            peak = -3.0
            if ok:
                err = subprocess.run(["ffmpeg", "-hide_banner", "-i", tmp, "-af", "volumedetect", "-f", "null", "-"], capture_output=True, text=True).stderr
                for line in err.splitlines():
                    if "max_volume:" in line: peak = float(line.split("max_volume:")[1].split()[0])
                ok = run(["-i", tmp, "-af", "volume=%fdB" % (-3.0 - peak), "-c:a", "pcm_s16le", dst])
            if os.path.exists(tmp): os.remove(tmp)
            if ok:
                made += 1
                print("%-22s %d  %5.2fs  <- %s" % (slot, n, length(dst), os.path.basename(src)))
            else: n -= 1
    return made

if __name__ == "__main__":
    sys.exit(main())
