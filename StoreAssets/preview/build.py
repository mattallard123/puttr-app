#!/usr/bin/env python3
"""build.py <edit.json> <takes dir> <W> <H> <out.mp4>

A 15-second sizzle reel cut to the beat: gameplay takes (simulator recordings of the app playing itself) cut every
couple of beats, EA/FIFA-style text slams, white flashes on the big hits, a music bed and hits under it.

edit.json:
  {"bpm": 148, "seconds": 15, "bed": "audio/bed-148.wav", "bed_start": 0.0,
   "cuts": [ {"take": "majorleague", "at": 12.4, "beats": 2, "text": "t1", "flash": true, "push": 1.08, "pos": "0.5,0.5", "dim": 0.1, "blur": 18} ...],
   "texts": {"t1": {"h": "...", "k": "...", "size": 9, "c": ""}},
   "sfx": [ {"file": "audio/hit.wav", "beat": 0, "gain": 0.9} ... ] }

A cut with "card" instead of "take" is a plain black card (the logo open, the end card). "pos" is where the
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
    zoom = f"(1+({push}-1)*{ease}*{ease})"
    if 'take' in c:
        src = takes / f"{c['take']}.mov"
        # cover-fit to W x H at the pushed size, then crop W x H around pos
        vf = (f"fps={FPS},scale=w='if(gt(iw/ih,{W}/{H}),-2,{W})*{zoom}':h='if(gt(iw/ih,{W}/{H}),{H},-2)*{zoom}':eval=frame,"
              f"crop={W}:{H}:x='(iw-{W})*{px}':y='(ih-{H})*{py}',setsar=1")
        inputs = ['-ss', str(c['at']), '-t', f'{dur + 0.5:.3f}', '-i', str(src)]
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
