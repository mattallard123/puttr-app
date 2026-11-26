#!/usr/bin/env python3
"""build.py <edit.json> <takes dir> <W> <H> <out.mp4>

A 15-second sizzle reel cut to the beat: gameplay takes (simulator recordings of the app playing itself) cut every
couple of beats, EA/FIFA-style text slams, white flashes on the big hits, a music bed and hits under it.

edit.json:
  {"bpm": 148, "seconds": 15, "bed": "audio/bed-148.wav", "bed_start": 0.0,
   "cuts": [ {"take": "majorleague", "at": 12.4, "beats": 2, "text": "t1", "flash": true, "push": 1.08, "pos": "0.5,0.5", "dim": 0.1, "blur": 18} ...],
   "texts": {"t1": {"h": "...", "k": "...", "size": 9, "c": ""}},
   "sfx": [ {"file": "audio/hit.wav", "beat": 0, "gain": 0.9} ... ] }

A cut with "card" instead of "take" is a plain black card (the logo open, the end card); one with "grid"
([{"take", "at"}...], "cols") is a wall of takes playing side by side. "beats" may be a fraction (0.5, 0.25).
"layout" ({"bg": {"take", "at"} or "studio", "cells": [...]}) places whole takes on a blurred take or the drawn studio;
a cell with "device": "phone" | "tablet" | "tv" is shown inside that drawn device (devices/devices.py).
"drift": 0.08 grows the picture that much across the cut.
"hold": true keeps a text still when the cut before carried the same one. "pos" is where the
crop sits when the take's shape is not the output's (x,y from 0 to 1). "push" starts the cut that much larger and
eases it back to 1 over the first beat (the punch-in). Texts slam in: 1.35x to 1x over five frames.
Output: H.264 High, 30 fps, yuv420p, AAC 256k 48 kHz stereo (App Store app preview spec)."""
import sys, json, subprocess, pathlib, tempfile, shutil

edit_path, takes, W, H, out = sys.argv[1], pathlib.Path(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]), pathlib.Path(sys.argv[5])
here = pathlib.Path(__file__).resolve().parent
E = json.load(open(edit_path))
FPS = 30
beat = 60.0 / E['bpm']
total = float(E['seconds'])
work = pathlib.Path(tempfile.mkdtemp(prefix='reel-'))

def run(args):
    r = subprocess.run(args, capture_output=True, text=True)
    if r.returncode != 0:
        sys.stderr.write(' '.join(map(str, args)) + '\n' + r.stderr[-3000:])
        raise SystemExit(1)

# Text cards at this size (rendered once each).
cards = {}
for key, t in E.get('texts', {}).items():
    png = work / f'text-{key}.png'
    size = t.get('size', 10) * (t.get('portrait_scale', 1.7) if H > W else 1.0)
    args = [f"{k}={v}" for k, v in t.items() if k in ('h', 'k', 'a', 'c', 'sub')] + [f'size={size}']
    run(['python3', str(here / 'text.py'), str(W), str(H), str(png)] + args)
    cards[key] = png

# Cut lengths on the beat grid, frame-exact: each cut ends on the frame nearest its beat.
segs, t_beats = [], 0.0
for c in E['cuts']:
    start_f = round(t_beats * beat * FPS)
    t_beats += c['beats']
    end_f = round(t_beats * beat * FPS)
    segs.append((c, start_f, end_f))
# The last cut runs to the end of the reel.
last_c, last_s, _ = segs[-1]
segs[-1] = (last_c, last_s, round(total * FPS))

parts = []
for i, (c, sf, ef) in enumerate(segs):
    n = ef - sf
    dur = n / FPS
    part = work / f'part{i:02d}.mp4'
    push = c.get('push', 1.0)
    px, py = (float(v) for v in c.get('pos', '0.5,0.5').split(','))
    ease = f"max(0,1-t/{beat:.4f})"           # 1 at the cut, 0 a beat later
    drift = c.get('drift', 0.0)               # a slow push across the whole cut (a still screen keeps moving)
    zoom = f"(1+({push}-1)*{ease}*{ease}+{drift}*t/{dur:.4f})"
    if 'take' in c:
        src = takes / f"{c['take']}.mov"
        # cover-fit to W x H at the pushed size, then crop W x H around pos
        vf = (f"fps={FPS},scale=w='if(gt(iw/ih,{W}/{H}),-2,{W})*{zoom}':h='if(gt(iw/ih,{W}/{H}),{H},-2)*{zoom}':eval=frame,"
              f"crop={W}:{H}:x='(iw-{W})*{px}':y='(ih-{H})*{py}',setsar=1")
        inputs = ['-ss', str(c['at']), '-t', f'{dur + 0.5:.3f}', '-i', str(src)]
    elif 'grid' in c:
        # A wall of takes, "cols" across: each cell cover-fits its share of the frame, the wall takes the punch-in.
        cells, cols = c['grid'], c.get('cols', 3)
        rows = (len(cells) + cols - 1) // cols
        cw, ch = W // cols, H // rows
        wall = work / f'wall{i:02d}.mp4'
        gin, gf = [], []
        for j, cell in enumerate(cells):
            gin += ['-ss', str(cell['at']), '-t', f'{dur + 0.5:.3f}', '-i', str(takes / f"{cell['take']}.mov")]
            # "crop": "top,bottom" (0 to 1) keeps that band of the take: a rule game's 3D window without the sheet under it.
            band = ''
            if cell.get('crop'):
                y0, y1 = (float(v) for v in cell['crop'].split(','))
                band = f"crop=iw:ih*{y1 - y0:.4f}:0:ih*{y0:.4f},"
            gf.append(f"[{j}:v]fps={FPS},{band}scale={cw}:{ch}:force_original_aspect_ratio=increase,crop={cw}:{ch},setsar=1[c{j}]")
        layout = '|'.join(f"{(j % cols) * cw}_{(j // cols) * ch}" for j in range(len(cells)))
        gf.append(''.join(f'[c{j}]' for j in range(len(cells))) + f"xstack=inputs={len(cells)}:layout={layout},pad={W}:{H}:(ow-iw)/2:(oh-ih)/2[g]")
        run(['ffmpeg', '-v', 'error', '-y'] + gin + ['-filter_complex', ';'.join(gf), '-map', '[g]', '-frames:v', str(n + 15),
             '-c:v', 'libx264', '-preset', 'fast', '-crf', '12', '-pix_fmt', 'yuv420p', '-r', str(FPS), '-an', str(wall)])
        vf = (f"fps={FPS},scale=w='{W}*{zoom}':h='{H}*{zoom}':eval=frame,crop={W}:{H}:x='(iw-{W})*{px}':y='(ih-{H})*{py}',setsar=1")
        inputs = ['-i', str(wall)]
    elif 'layout' in c:
        # Device shots: takes of any shape placed whole on a blurred, dimmed take. "cells": [{"take", "at", "x", "y",
        # "w", "h"}] in fractions of the frame; each is cover-fitted to its box and given a thin dark edge.
        L = c['layout']
        board = work / f'board{i:02d}.mp4'
        if L['bg'] == 'studio':
            # the drawn studio: dark, a soft lime glow behind the devices (devices/devices.py)
            sys.path.insert(0, str(here / 'devices')); import devices
            bgp = work / 'studio.png'
            if not bgp.exists(): devices.studio(W, H).save(bgp)
            gin = ['-loop', '1', '-t', f'{dur + 0.5:.3f}', '-i', str(bgp)]
            gf = [f"[0:v]fps={FPS},format=rgba,setsar=1[b0]"]
        else:
            gin = ['-ss', str(L['bg']['at']), '-t', f'{dur + 0.5:.3f}', '-i', str(takes / f"{L['bg']['take']}.mov")]
            gf = [f"[0:v]fps={FPS},scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},boxblur=28:2,eq=brightness=-0.3,setsar=1[b0]"]
        k = 1
        for j, cell in enumerate(L['cells']):
            cw, ch = int(cell['w'] * W) // 2 * 2, int(cell['h'] * H) // 2 * 2
            sx, sy = int(cell['x'] * W), int(cell['y'] * H)
            gin += ['-ss', str(cell['at']), '-t', f'{dur + 0.5:.3f}', '-i', str(takes / f"{cell['take']}.mov")]
            vi = k; k += 1
            if cell.get('device'):
                # a drawn device: the take, rounded to the screen, under the device's body
                sys.path.insert(0, str(here / 'devices')); import devices
                fr, ox, oy = devices.frame(cell['device'], cw, ch)
                fpng, mpng = work / f'dev{i:02d}_{j}.png', work / f'mask{i:02d}_{j}.png'
                fr.save(fpng); devices.screen_mask(cell['device'], cw, ch).save(mpng)
                gin += ['-loop', '1', '-t', f'{dur + 0.5:.3f}', '-i', str(mpng), '-loop', '1', '-t', f'{dur + 0.5:.3f}', '-i', str(fpng)]
                mi, fi = k, k + 1; k += 2
                gf.append(f"[{vi}:v]fps={FPS},scale={cw}:{ch}:force_original_aspect_ratio=increase,crop={cw}:{ch},format=rgba,setsar=1[v{j}]")
                gf.append(f"[{mi}:v]fps={FPS},format=gray,setsar=1[m{j}];[v{j}][m{j}]alphamerge[r{j}]")
                gf.append(f"[{fi}:v]fps={FPS},format=rgba,setsar=1[f{j}]")
                gf.append(f"[b{j}][f{j}]overlay={sx - ox}:{sy - oy}:format=auto[s{j}];[s{j}][r{j}]overlay={sx}:{sy}:format=auto[b{j + 1}]")
            else:
                gf.append(f"[{vi}:v]fps={FPS},scale={cw}:{ch}:force_original_aspect_ratio=increase,crop={cw}:{ch},pad={cw + 12}:{ch + 12}:6:6:color=0x1c1f1d,setsar=1[d{j}]")
                gf.append(f"[b{j}][d{j}]overlay={sx - 6}:{sy - 6}[b{j + 1}]")
        gf.append(f"[b{len(L['cells'])}]format=yuv420p[out]")
        run(['ffmpeg', '-v', 'error', '-y'] + gin + ['-filter_complex', ';'.join(gf), '-map', "[out]", '-frames:v', str(n + 15),
             '-c:v', 'libx264', '-preset', 'fast', '-crf', '12', '-pix_fmt', 'yuv420p', '-r', str(FPS), '-an', str(board)])
        vf = (f"fps={FPS},scale=w='{W}*{zoom}':h='{H}*{zoom}':eval=frame,crop={W}:{H}:x='(iw-{W})*{px}':y='(ih-{H})*{py}',setsar=1")
        inputs = ['-i', str(board)]
    else:
        vf = f"fps={FPS},setsar=1"
        inputs = ['-f', 'lavfi', '-t', f'{dur + 0.5:.3f}', '-i', f"color=c=black:s={W}x{H}:r={FPS}"]
    if c.get('blur'): vf += f",boxblur={c['blur']}:2"
    if c.get('dim'): vf += f",eq=brightness=-{c['dim']}"
    if c.get('flash'): vf += f",fade=t=in:st=0:d={min(0.2, dur):.3f}:color=white"
    filt = f"[0:v]{vf}[v0]"
    last = 'v0'
    if c.get('text'):
        png = cards[c['text']]
        slam = "max(1,1.35-t*0.35/0.17)"     # 1.35x at the cut to 1x by frame 5
        if c.get('hold'): slam = "1"         # the same text as the cut before: it stays put
        filt += (f";[1:v]format=rgba,scale=w='iw*{slam}':h='ih*{slam}':eval=frame[t];"
                 f"[{last}][t]overlay=x='(W-w)/2':y='(H-h)/2':eval=frame:shortest=1[v1]")
        last = 'v1'
        inputs += ['-loop', '1', '-t', f'{dur + 0.5:.3f}', '-i', str(png)]
    run(['ffmpeg', '-v', 'error', '-y'] + inputs + ['-filter_complex', filt, '-map', f'[{last}]', '-frames:v', str(n),
         '-c:v', 'libx264', '-preset', 'medium', '-crf', '14', '-pix_fmt', 'yuv420p', '-r', str(FPS), '-an', str(part)])
    parts.append(part)

lst = work / 'list.txt'
lst.write_text(''.join(f"file '{p}'\n" for p in parts))
video = work / 'video.mp4'
run(['ffmpeg', '-v', 'error', '-y', '-f', 'concat', '-safe', '0', '-i', str(lst), '-c', 'copy', str(video)])

# Audio: the bed (faded out over the last beat), and each sound effect at its beat.
ain = ['-ss', str(E.get('bed_start', 0)), '-t', f'{total}', '-i', str(here / E['bed'])]
mix = [f"[0:a]aresample=48000,aformat=channel_layouts=stereo,volume={E.get('bed_gain', 0.9)},afade=t=out:st={total - beat:.3f}:d={beat:.3f}[b]"]
labels = ['[b]']
for j, s in enumerate(E.get('sfx', [])):
    ain += ['-i', str(here / s['file'])]
    ms = int(round(s['beat'] * beat * 1000 + s.get('offset', 0) * 1000))
    mix.append(f"[{j + 1}:a]aresample=48000,aformat=channel_layouts=stereo,volume={s.get('gain', 1.0)},adelay={ms}|{ms},apad[s{j}]")
    labels.append(f'[s{j}]')
mix.append(f"{''.join(labels)}amix=inputs={len(labels)}:normalize=0:duration=first,atrim=0:{total},alimiter=limit=0.89[a]")
audio = work / 'audio.wav'
run(['ffmpeg', '-v', 'error', '-y'] + ain + ['-filter_complex', ';'.join(mix), '-map', '[a]', '-ar', '48000', str(audio)])

out.parent.mkdir(parents=True, exist_ok=True)
run(['ffmpeg', '-v', 'error', '-y', '-i', str(video), '-i', str(audio), '-map', '0:v', '-map', '1:a',
     '-c:v', 'libx264', '-profile:v', 'high', '-level', '4.2', '-preset', 'slow', '-crf', '16', '-pix_fmt', 'yuv420p', '-r', str(FPS),
     '-c:a', 'aac', '-b:a', '256k', '-ar', '48000', '-ac', '2', '-t', f'{total}', '-movflags', '+faststart', str(out)])
shutil.rmtree(work, ignore_errors=True)
d = subprocess.run(['ffprobe', '-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', str(out)], capture_output=True, text=True).stdout.strip()
print(out, f'{W}x{H}', d + 's')
