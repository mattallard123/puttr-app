#!/bin/bash
# sheet.sh <take.mov> <out.jpg> : one frame a second, labelled with its time, 8 across
F=/Users/matthewallard/puttr/puttr-app-2/StoreAssets/screenshots/Montserrat-Bold.ttf
ffmpeg -v error -y -i "$1" -vf "fps=1,scale=320:-2,drawtext=fontfile=$F:text='%{pts\:hms}':x=6:y=6:fontsize=22:fontcolor=yellow:box=1:boxcolor=black@0.6,tile=8x8" -frames:v 1 "$2"
