#!/bin/bash
# take.sh <slot>: wait (up to 90 s) for Soundly's Send to Folder to land in SoundDrop/_incoming, move it to SoundDrop/<slot>/.
IN=/Users/matthewallard/puttr/puttr-app-2/SoundDrop/_incoming; DROP=/Users/matthewallard/puttr/puttr-app-2/SoundDrop; D=$(dirname "$0")  # soundly.log is written next to this script
for i in $(seq 1 60); do f=$(find "$IN" -maxdepth 1 -type f \( -iname '*.wav' -o -iname '*.mp3' -o -iname '*.aif*' -o -iname '*.flac' \) | head -1); [ -n "$f" ] && break; sleep 1.5; done
[ -z "$f" ] && { echo "$1: nothing arrived"; exit 1; }
s1=-1; while true; do s2=$(stat -f %z "$f"); [ "$s1" = "$s2" ] && break; s1=$s2; sleep 1.5; done
mkdir -p "$DROP/$1"; n=0
while IFS= read -r -d '' g; do mv "$g" "$DROP/$1/"; n=$((n+1)); echo "$1 <- $(basename "$g")" >> $D/soundly.log; done < <(find "$IN" -maxdepth 1 -type f \( -iname '*.wav' -o -iname '*.mp3' -o -iname '*.aif*' -o -iname '*.flac' \) -print0)
echo "$1: $n"; tail -n $n $D/soundly.log
