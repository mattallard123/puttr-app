#!/bin/bash
# takes-tv.sh: every 3D game on the 1080p Apple TV simulator, playing itself (-puttrDemo), into ~/Library/Caches/puttr-app2-capture/tv
U=C2956218-6822-4564-A151-395280D62AD0
T="$(cd "$(dirname "$0")" && pwd)/take.sh"
cd ~/Library/Caches/puttr-app2-capture/tv
BASE="-app2Fixture -app2Connected -puttrDemo"
while read name type secs; do
  [ -z "$name" ] && continue
  $T $U $name.mov $secs $BASE -app2Open start:$type
done <<LIST
onfire ARCADESTREAK 60
spotkick SPOTKICK 55
sendit SENDIT 55
derby DERBY 60
tugofwar TUGOFWAR:2 60
turfwar TURFWAR:2 60
archery TARGET 50
windmill WINDMILL 60
underthecup UNDERTHECUP 55
deadweight DEADWEIGHT 60
slingshot SLINGSHOT 55
cricket CRICKET 55
lanes BOWLING 55
balloonpop BALLOONPOP 50
puttstar ROCK 55
beerpong BEERPONG:2 55
LIST
LEAD=8 $T $U tracks.mov 75 -app2Fixture -app2Connected -app2TracksAuto -app2Open tracksplay
LEAD=10 $T $U play.mov 6 -app2Fixture -app2Connected
echo all done
