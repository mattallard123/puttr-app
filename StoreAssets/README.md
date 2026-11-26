# Puttr 3.0 — App Store assets and the sizzle reel

Made from the app itself: the simulators play each game with made-up putts (`-puttrDemo`, fixture data) and are
recorded; the pages here turn those recordings into store assets. Takes live outside the repo in
`~/Library/Caches/puttr-app2-capture/{tv,iphone}` (big; re-record with `capture/takes-*.sh`).

## What's in `out/`
| Use | File |
|---|---|
| Apple TV screenshots (3840×2160) | `screenshots/appletv/01…10.png` |
| iPhone 6.9" screenshots (1290×2796) | `screenshots/iphone-6.9/01…08.png` |
| iPad 13" screenshots (2064×2752) | `screenshots/ipad-13/01…08.png` |
| iPhone Duo screenshots (outer display, 1398×2034) | `screenshots/iphone-duo/01…10.png` |
| Apple TV app preview (1920×1080, 15 s) | `previews/appletv-preview-1920x1080.mp4` |
| iPhone 6.9" app preview (886×1920, 15 s) | `previews/iphone-preview-886x1920.mp4` |
| Social reel 16:9 / 9:16 / 1:1 (15 s) | `previews/reel-16x9-1920x1080.mp4`, `reel-9x16-1080x1920.mp4`, `reel-1x1-1080x1080.mp4` |
| Listing text (name, subtitle, promo, keywords, description, what's new) | `../listing/listing.md` (live 2.9.5 text: `../listing/current.json`) |
| Overviews | `overview-appletv.jpg`, `overview-iphone.jpg`, `overview-ipad.jpg` |

All screenshots are RGB, no alpha. Videos: H.264 High, 30 fps, AAC 256k 48 kHz stereo, exactly 15.0 s.
Upload: `node StoreAssets/upload.js list | upload IOS|TV_OS | listing IOS|TV_OS` (API key 2LQ24P399R, en-US only; app info name/subtitle is never written: it is shared by every platform). Apple TV 1.0 uploaded 2026-11-18 (10 shots + preview + text, all processed). iOS: the 2.9.6 draft was renumbered 3.0.0 (Matt, 2026-11-18) and given 8 iPhone 6.9" + 8 iPad 13" shots, the iPhone preview and the text incl. what's new. Older sets copied from 2.9.5 (iPhone 6.1", 6.5", Duo, Watch) were removed from 3.0.0 on Matt's word; 2.9.5 keeps its own.

## Remaking
1. Build and install the simulator apps (`Tools/app-build.sh iossim|tvossim`, then xcodebuild).
   Launch flags: `-app2Fixture -app2Connected -puttrDemo -app2Open start:TYPE[:players]`.
2. `capture/takes-tv.sh` (Apple TV 4K 1080p sim) and `capture/takes-iphone.sh`; `capture/sheet.py take.mov out.jpg 1`
   makes a labelled contact sheet to pick moments.
3. `screenshots/build.py screenshots/shots-tv.json <tv takes> appletv out/screenshots/appletv` (iphone / ipad take
   stills from `screenshots/stills-*`).
4. `preview/build.py preview/edit-tv.json <tv takes> 1920 1080 out/...mp4` (also 1080 1080); `edit-phone.json` with the
   iPhone takes at 1080 1920 and 886 1920. Edits are on a 148 BPM grid; music bed and hits from Soundly in `preview/audio`.

## Recut 2026-11-18 (late)
Tracks footage (TV + iPhone), the iPhone/iPad app screens and gameplay stills, the TV "40+ games" shot, header/search and
all reels were rebuilt from the latest code (photo-real Tracks). Built from a private copy of the project
(~/Library/Caches/puttr-app2-capture/clone, rsync of Assets/Packages/ProjectSettings/Tools) on private simulators
(sims.txt there), because other sessions build in this project at the same time. Re-recorded with no test-putt buttons (keyboard putts) and re-uploaded 2026-11-19: iOS 3.0.0 shots + preview + header/search, tvOS 1.0 shots + preview.

## v2 (2026-11-19): improvement first — uploaded 2026-11-19 (shots, previews, header/search, listing text; iOS 3.0.0 + tvOS 1.0)
Matt: the app is about improving your putting (AI coaching, stats), the games are one part; header statement is
"The ultimate putting experience". The v2 set is now `out/screenshots`, `out/creative`, `out/previews`; the earlier
games-first set is kept in `out/*-v1` (`out/overview-v2-*.jpg` are the v2 sheets),
and `upload.js` reads those folders. Phone/iPad order: ultimate, AI coach, PQS, analysis, 3D stroke review, Coaches, then
games. New stills: `-app2Open coachchat | analysis (-app2Scroll 420) | labsession | findcoach`, `-app2Tab improve
(-app2Scroll 880 for PQS)`. Apple TV has only the games side, so its set and preview stay games (`edit-appletv.json`).

## iPhone Duo (2026-11-25)
Set `APP_IPHONE_DUO` on iOS 3.0.0, the Duo's outer (folded) display like 2.9.5 used: same captions as the iPhone set
(`screenshots/shots-duo.json` = `shots-phone.json`), stills in `screenshots/stills-duo` shot on the iPhone Duo sim with the
same launch flags (analysis uses `-app2Scroll 200` on this shorter screen; Tracks is the lag-putt frame ~20 s into
`-app2TracksAuto -app2Open tracksplay`). Fold it with Device Hub (Duo row, "Switch to compact window", posture "Closed"),
then `xcrun simctl io <udid> screenshot --display=primary`. The sim sometimes draws a black dot (outer camera) near the top
right: check stills and retake rather than patch. Render: `screenshots/build.py screenshots/shots-duo.json
screenshots/stills-duo duo out/screenshots/iphone-duo` (699×1017 @2: Chrome headless crops canvases under ~500 px wide).
Upload just this set: `node StoreAssets/upload.js upload IOS APP_IPHONE_DUO`.

## Google Play (2026-11-25)
Phone (1080×1920) and tablet (1440×2560) screenshots, the 1024×500 feature graphic and the 512 icon are in `out/play`; the
listing text and the remake commands are in `listing/play.md`. Stills retaken from the 2026-11-25 build. Entered in Play Console (default listing, co.puttr.android) and saved as a
draft on 2026-11-25: 8 phone, 8 seven-inch and 8 ten-inch screenshots and the feature graphic. Not sent for review.
The icon and the listing text were left as they were.

## Dubstep social reel (2026-11-25)
`out/previews/reel-dubstep-9x16-1080x1920.mp4`: 15 s, 9:16, every game on screen (arcade games full frame, rule games,
sensor games and quest stages two up as their 3D windows, a nine-game wall under "40+ games"). Not an App Store preview.
- Music: `preview/audio/dubstep.py` writes `dubstep-140.wav` (140 BPM, synthesised with numpy, so ours to post). Its
  `BARS` pattern and the cuts in `preview/edit-reel.json` share one beat grid: change them together.
- Takes: `capture/takes-iphone-reel.sh` on its own "Reel iPhone" simulator (udid in
  `~/Library/Caches/puttr-app2-capture/reel-sim.txt`; other sessions shoot on the Store iPhone) into
  `~/Library/Caches/puttr-app2-capture/iphone-reel`, with links to the twelve arcade takes in `../iphone`.
  `-app2Connected` now also hides the "Looking for your Puttr" chip and the "Test putts: nothing is saved." note.
  `-app2AutoPutt` still lands a refused putt between counted ones ("That putt did not count"), so rule games are
  cropped to the 3D window (`"crop": "0.072,0.482"` on a grid cell). Beer Pong and High Striker did not play under
  the auto-putter and are not in the reel. No cut in the build is shorter than half a beat (Matt: slow it down so the games can be seen).
- Build: `preview/build.py preview/edit-reel.json ~/Library/Caches/puttr-app2-capture/iphone-reel 1080 1920 out/previews/reel-dubstep-9x16-1080x1920.mp4`.
  The Xcode product for the simulator is `build/Build/Products/ReleaseForRunning-iphonesimulator/Puttr.app`.
- Shape since Matt's notes (2026-11-25): beats 0-8 the build ("Introducing", "The all-new", the logo, over softened
  glimpses), 8-24 every game at half a beat (arcade full frame, the rest two up), 24-28 TV, tablet, mobile and all
  three together (`"layout"` cuts: `tv_majorleague` is a link to the TV take, `ipad_majorleague` was shot on the
  "Reel iPad" simulator, udid in `reel-ipad.txt`), 28 the wall, 30 the end card. Device labels use text class `high`:
  a reel's caption covers the bottom of the frame.
- 20-second cut (Matt chose it, 2026-11-25): `out/previews/reel-dubstep-20s-9x16-1080x1920.mp4` from
  `preview/edit-reel-20.json`, same build, platforms and end; the games run seven bars, mostly a beat each. Its track:
  `python3 preview/audio/dubstep.py preview/audio/dubstep-140-20s.wav 20 ABCDACDE`.
- No two-up cuts in the 20-second reel (Matt, 2026-11-25: full frame only, the most varied games): 28 games a beat each,
  nine of them rule games shown whole. Their cuts start at 14.6 s: with `-app2AutoPutt 6` every rule take is free of
  the amber "did not count" line from 14.25 s to 16.75 s (also 8.25-10.75 and 26.25-28.75).
- Intro fix (Matt, 2026-11-25: intro looked dark and low quality): no blur, dim 0.12, a 1.12 punch-in on every intro cut,
  and each cut starts where its take is moving (the old picks were pauses between putts, so they played as stills).
  Avoid moments with a big in-game banner under the intro words (Putt Star always has one). The reel logo has a drop
  shadow (text.html) so it reads on bright games without dimming them. "Introducing" sits on a black card, no flash (Matt).
- Matt's next round (2026-11-25): the logo cuts read PUTTR APP (text class `logoapp`); Tracks is the drive and the
  approach with their tracers from `tracks_clean.mov` (`-app2TracksAuto -app2TracksNoHud`, 52.8 s and 63.72 s), under
  "Play the world's courses"; Play on TV / Tablet / Mobile are drawn devices on a drawn studio
  (`preview/devices/devices.py`, layout `"bg": "studio"` and cells with `"device"`). Kept at 20 s (Matt asked about
  30: reels lose viewers before the end, and the varied footage runs out). Tracks' dev flags (-app2TracksNoHud,
  -app2TracksCam, -app2TracksHole) now read PUTTR_ARGS too, so they work on a simulator.
- 2026-11-25: "40+ games" is gone (Matt: it undersells the app). In its place Quests, Tournaments and League Night, two
  beats each, as the app's own screens in the drawn phone with a slow `"drift"` (takes `f_quests3`, `f_tournament`,
  `f_league`: `-app2Open playquestsall:3 | tournament | league`). Gatehouse, Tug of War, Turf War and Slingshot left the games.
- 2026-11-25 (Matt): Tournaments beat dropped; Quests (Dune Sea map) and League Night are full screen, not in the drawn
  phone, and "Play on Mobile" is full-screen Ten Pin with no phone drawn ("people will be watching on their phone, so
  it will feel like they are in the app"). Their words sit low in a dark band (text classes `lowmid,band`; `c` takes
  several classes, comma separated). The all-three shot keeps its drawn phone. Slingshot and Turf War are back.
- 2026-11-25 (Matt): Quests and League Night are out ("not translating"). Tracks is now the drive (52.8 s) and a putt
  rolling in: two beats from 46.08 s in `tracks_clean.mov`, timed so the ball goes into the hole on the last frames (Matt: no camera pan, no cut into the cup).
  Tug of War and Under the Cup are back in the games.
- 2026-11-25 (Matt): the all-three devices shot is gone; Play on Mobile, Tablet and TV (in that order, Matt) get two beats each, and Mobile is
  Putt Star on the drawn phone again. Send It and Center Cut left the games to make the time.
- 15-second cut (Matt, 2026-11-25: "speed it up, get it down to 15 seconds"): `out/previews/reel-dubstep-15s-9x16-1080x1920.mp4`
  from `preview/edit-reel-15.json` on `audio/dubstep-140.wav`. Same intro (drop at beat 8), 11 games a beat each, Tracks
  drive + putt (two beats each), Mobile 1.5 / Tablet 1.5 / TV 2 beats, end card on the final hit (beat 28). The 20 s
  cut is kept.
- 2026-11-25 (Matt): never the same game twice in a reel, intro, devices and end card included. The 15 s cut now uses
  26 games once each (Tracks' drive + putt count as one): the intro has its own games, Putt Star / Major League / Derby
  appear only on the phone / tablet / TV, and the end card sits on the drawn studio instead of a wall of takes. Keep
  similar-looking games apart (Dead Weight and Archery read as the same target).
