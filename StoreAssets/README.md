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
