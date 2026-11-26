#!/bin/bash
# takes-iphone-reel.sh: every game the app plays, for the social reel, on the "Reel iPhone" simulator into
# ~/Library/Caches/puttr-app2-capture/iphone-reel. Arcade games play themselves with -puttrDemo; rule games,
# sensor games and quest stages take a test putt every few seconds (-app2AutoPutt), since -puttrDemo gives them none.
# The twelve arcade takes the store preview already has (../iphone) are not shot again.
U=$(cat ~/Library/Caches/puttr-app2-capture/reel-sim.txt)   # its own "Reel iPhone" simulator: other sessions shoot on the Store iPhone
T="$(cd "$(dirname "$0")" && pwd)/take.sh"
cd ~/Library/Caches/puttr-app2-capture/iphone-reel
FIX="-app2Fixture -app2Connected"
while read name route secs mode; do
  [ -z "$name" ] && continue
  [ -n "$ONLY" ] && [[ " $ONLY " != *" $name "* ]] && continue
  if [ "$mode" = demo ]; then $T $U $name.mov $secs $FIX -puttrDemo -app2Open $route
  else $T $U $name.mov $secs $FIX -app2AutoPutt ${mode:-6} -app2Open $route; fi
done <<LIST
cricket start:CRICKET:2 36 demo
balloonpop start:BALLOONPOP 34 demo
beerpong start:BEERPONG:2 34 demo
deadweight start:DEADWEIGHT 38 demo
tugofwar start:TUGOFWAR:2 34 demo
puttball start:PUTTBALL 34 demo
tourjuice start:TOURJUICE 30
centercut start:CENTERCUT 30
speeddemon start:SPEEDDEMON 30
metal start:METAL 30
lab start:LAB 30
fullworkout start:FULLWORKOUT 30
leapfrog start:LEAPFROG 30
doors start:DOORS 30
highside start:HIGHSIDE 30
switchback start:SWITCHBACK 30
gears start:GEARS 30
groove start:GROOVE 30
clock start:CLOCK 30
aroundtheclock start:AROUNDTHECLOCK 30
deadcenter start:DEADCENTER 30
assessment start:ASSESSMENT 30
horse start:HORSE 30
ohone start:OHONE 30
reducer start:REDUCER 30
match vs:MATCH 30
highstriker start:HIGHSTRIKER 30
pendulum start:PENDULUM 30
pure start:PURE 30
carboncopy start:CARBONCOPY 30
squareup start:SQUAREUP 30
stretch start:STRETCH 30
lockdown start:LOCKDOWN 30
theyips start:THEYIPS 30
quest1 stage:1-2 30
quest2 stage:2-2 30
quest3 stage:3-2 30
quest4 stage:4-2 30
quest5 stage:5-2 30
quest6 stage:6-2 30
quest7 stage:7-2 30
quest8 stage:8-2 30
LIST
[ -z "$ONLY" ] && LEAD=8 $T $U tracks.mov 75 $FIX -app2TracksAuto -app2Open tracksplay
echo all done
