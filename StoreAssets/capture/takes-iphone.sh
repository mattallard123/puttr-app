#!/bin/bash
# takes-iphone.sh: the reel's games on the iPhone simulator, portrait, playing themselves, into ~/Library/Caches/puttr-app2-capture/iphone
U=$(cut -d' ' -f1 ~/Library/Caches/puttr-app2-capture/sims.txt)   # the private "Store iPhone"
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
sendit SENDIT 30
archery TARGET 35
turfwar TURFWAR:2 35
derby DERBY 40
windmill WINDMILL 35
slingshot SLINGSHOT 30
underthecup UNDERTHECUP 30
LIST
echo all done
