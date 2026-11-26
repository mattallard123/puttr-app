# Puttr 3.0 — Google Play listing (phone and tablet)

Draft for the 3.0 release on Google Play (the Unity app, puttr-app-2, package co.puttr.android). Made from
`listing.md` (the App Store copy) on 2026-11-25. Limits (Play Console Help, read 2026-11-25): app name 30, short
description 80, full description 4000; release notes 500. Nothing here has been entered in Play Console.

What differs from the App Store copy, and why:
- No Apple TV, iPad or App Store wording, and no Apple EULA line: a Play listing should not name other platforms.
- No big-screen section: Puttr on Google TV / Android TV is not on Play until the TV form factor is added there.
- No "book video lessons with Coaches" line and no Coaches screenshot: real-coach lessons are not live.

## App name — leave as it is in Play Console
A name change is Matt's call.

## Short description [76]
Every putt measured, an AI coach and 40+ games played with your real putter.

## Full description [1762]
THE ULTIMATE PUTTING EXPERIENCE.

Puttr turns your Puttr putting green into a coach, a stats lab and a games console. Roll a real ball at a real hole: the camera reads the pace and line of every putt, and the app shows you what to work on, on your phone or tablet.

GET BETTER
• Every putt measured: pace, line, start direction and how it entered the hole
• Your Putting Quality Score shows how your putting is trending
• Daily sessions built around your misses
• An AI putting coach you can ask anything, any time
• 3D stroke review: film your stroke and see why you miss, not just what happened

40+ GAMES: YOUR PUTTER IS THE CONTROLLER
Hole it for a home run in Major League. Beat the keeper in Spot Kick. Jump the buses in Send It. Keep the fire alive in On Fire. Every game is played with the putt you just hit — nothing is faked.

• 3D arcade games: Major League, Spot Kick, Send It, On Fire, Derby, Tug of War, Turf War, Archery, Windmill, Lanes, Dead Weight, Under the Cup, Slingshot, Cricket and more
• Skill games and drills: Center Cut, Around the Clock, Speed Demon, Ladder and dozens more
• One to four Players, or two teams. Pass the putter.

TRACKS: PLAY THE WORLD'S GREAT COURSES
Play famous courses hole by hole on your mat. The length of your putt picks the club, the line and pace send it. Track of the Week, a world leaderboard and a caddie for every shot.

COMPETE
• League Night: weekly team matches with a live leaderboard
• Monthly tournaments and challenges
• Versus: play a friend's round any time, or go head to head live

PROFILES FOR THE WHOLE HOUSE
Add a Profile for everyone who putts at home and switch in a tap.

Puttr needs a Puttr putting green. Find out more at puttr.co.

Some games and features need a Puttr Club membership.

## What's new (release notes) [300]
Puttr 3.0 is a new app from the ground up.
• Rebuilt 3D games with new graphics and sound for every game
• Tracks: play the world's great courses, with a caddie and commentary
• League Night, tournaments and challenges
• Profiles for everyone at home
• Faster, smoother, and ready for what comes next

## Graphics (in `../out/play`)
| Use | File | Size |
|---|---|---|
| App icon | `icon-512.png` | 512×512, 32-bit PNG |
| Feature graphic | `feature-graphic-1024x500.png` | 1024×500, no alpha |
| Phone screenshots (8) | `phone/01…08.png` | 1080×1920, no alpha |
| Tablet screenshots (8), for both the 7-inch and 10-inch slots | `tablet/01…08.png` | 1440×2560, no alpha |

Remake: `screenshots/build.py screenshots/shots-play-phone.json screenshots/stills-iphone playphone out/play/phone`
and `... shots-play-tablet.json screenshots/stills-ipad playtablet out/play/tablet`. The feature graphic is the App
Store header (`out/creative/header-3840x1646.png`) scaled to 500 high and cropped to the middle 1024; the icon is
`docs/appicon/out/Icon-1024x1024.png` at 512.

Not made: the Android TV banner (1280×720) and TV screenshots, needed only when the TV form factor is added.
