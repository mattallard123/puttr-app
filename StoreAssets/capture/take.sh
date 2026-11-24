#!/bin/bash
# take.sh <udid> <out.mov> <seconds> <PUTTR_ARGS...>
# One simulator take: launch the app with the given args, wait for the game to load, record, stop.
# Simulator recordings run in real time (whatever the simulator renders), H.264.
U=$1; OUT=$2; SECS=$3; shift 3; ARGS="$*"
xcrun simctl terminate $U co.puttr.ios >/dev/null 2>&1
SIMCTL_CHILD_PUTTR_ARGS="$ARGS" xcrun simctl launch $U co.puttr.ios >/dev/null
sleep ${LEAD:-5}
xcrun simctl io $U recordVideo --codec=h264 --force "$OUT" >/dev/null 2>&1 &
REC=$!
sleep $SECS
kill -INT $REC; wait $REC 2>/dev/null
xcrun simctl terminate $U co.puttr.ios >/dev/null 2>&1
echo "$(basename $OUT) $(ffprobe -v error -show_entries format=duration -of csv=p=0 "$OUT" 2>/dev/null)s"
