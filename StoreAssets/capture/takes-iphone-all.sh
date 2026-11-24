#!/bin/bash
# takes-iphone-all.sh: every reel game plus Tracks on the private "Store iPhone", into ~/Library/Caches/puttr-app2-capture/iphone
U=$(cut -d' ' -f1 ~/Library/Caches/puttr-app2-capture/sims.txt)
T="$(cd "$(dirname "$0")" && pwd)/take.sh"
cd ~/Library/Caches/puttr-app2-capture/iphone
BASE="-app2Fixture -app2Connected -puttrDemo"
while read name type secs; do
  [ -z "$name" ] && continue
  $T $U $name.mov $secs $BASE -app2Open start:$type
done <<LIST
lanes BOWLING 45
spotkick SPOTKICK 40
majorleague MAJORLEAGUE 45
onfire ARCADESTREAK 40
sendit SENDIT 30
puttstar ROCK 35
archery TARGET 40
turfwar TURFWAR:2 35
derby DERBY 40
windmill WINDMILL 35
slingshot SLINGSHOT 30
underthecup UNDERTHECUP 30
LIST
LEAD=8 $T $U tracks.mov 75 -app2Fixture -app2Connected -app2TracksAuto -app2Open tracksplay
echo all done
