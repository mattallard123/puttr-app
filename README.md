# Puttr 2

The Puttr app rebuilt in Unity. It started on 2026-11-16 as a Phase 0 spike (`puttr-app/docs/unity-refactor-plan.md`) and is now the app itself: it ships as an update to Puttr, `co.puttr.ios` and `co.puttr.android`, version 3.0.0 build 300. Installing it on a device **replaces** the Puttr app there; reinstall Puttr from the App Store to go back. The plain Mac build (`Builds/Puttr.app`) keeps `co.puttr.app2`; the Mac App Store build (`Builds/macOS`) is `co.puttr.ios`, the same App Store app.

The React Native app (`puttr-app`) is the reference for screens, rules and API calls, and stays live until this one ships. Neither `puttr-app` nor `puttr-unity` is changed by this project.

## What is in it

- **The app**: `Assets/PuttrApp/Scripts`, UI Toolkit, one style sheet (`Assets/PuttrApp/Resources/PuttrApp/App.uss`). Four tabs (Home, Play, Improve, Profile), screens pushed over a tab with Back, phone-wide column on tablets and desktop. Screen-by-screen parity with puttr-app is tracked in [docs/screen-parity.md](docs/screen-parity.md); the visual direction is `puttr-app/docs/puttr2-sports-ui`.
- **The 3D engine**: `Assets/PuttrArcade` and `Assets/Puttr`, copied from puttr-unity (Puttr Arcade) on 2026-11-16 at commit `cfb0232` and since rebuilt for the app. Every game, the game page backdrops and the Puttr connection run on it.
- **Puttr connection**: Bluetooth and WiFi to the Puttr console, putts read through the arcade (`AppPutt.cs` turns a putt into the app's verdict: made, line, pace, entry).

### Games

The Play tab has top tabs Local, Online, Tracks, Quests, Tournaments and League, and filters All, Solo, Together, Line, Pace and Sensor. Every game sets up on one page (`GameScreens.cs`: Players, options, How to play, the game's own scene turning slowly behind it) and every finish goes back to the tab it came from.

| Kind | Where | Notes |
| --- | --- | --- |
| Rule games (Center Cut, HORSE, Cricket, Match Play, Metal, Tour Juice, Speed Demon and the rest of the RN putt-list games) | `RuleGames*.cs`, `RuleCatalog`, scenes in `Assets/PuttrArcade/Scripts/Scenes` | Rules ported from puttr-app; each has its own 3D scene (`RuleSceneList`) |
| Arcade games (On Fire, Archery, Cricket, Derby, Major League, Lanes, Balloon Pop, Beer Pong, Putt Star) | `Assets/PuttrArcade/Scripts/*Mode.cs`, `GameBridge.ArcadeGames` | API games mapped by gameType |
| Arcade extras (Dead Weight, Send It, Slingshot, Spot Kick, Tug of War, Turf War, Under the Cup, Windmill) | `ArcadeExtras.cs` | games rows 1611 to 1618, **published 0** so the RN app never lists them (`puttr-api/migrations/add_arcade_games.sql`) |
| Vertex sensor games (Pendulum, Pure, Carbon Copy, Square Up, Stretch, Lockdown, The Yips) | `SensorGames.cs`, `SensorRuleGames.cs`, `SensorStroke.cs` | rows 1601 to 1607, published 0 |
| Practice (Free putt), Compare, Custom games | Improve tab, Practice | Compare compares putters, not Players |
| Tracks (18-hole courses) | `Tracks*.cs` | |

Up to four Players: you, other profiles on the account, guests, or Puttr (a bot whose putts are simulated and never saved). The Puttr catalog's Standard and Streak games stay in the legacy app (`GameBridge.Retired`); a Player's own custom games still play.

Card art is `Assets/PuttrApp/Resources/PuttrApp/Cards/<GAMETYPE>.png`, 960 x 576 (imported with no mipmaps, no compression, no power-of-two scaling, so they stay sharp): run the Mac build with `-puttrShots <dir>` (writes `card_<Name>.png` per arcade game and `card_rule_<TYPE>.png` per rule scene), then `python3 docs/cards/make_cards.py <dir>`.

Sounds are `Assets/PuttrArcade/Resources/PuttrArcade/Audio/Library/<slot>_N.wav`: drop Soundly exports in `SoundDrop/<slot>/` and run `python3 Tools/import-sounds.py`.

### What gets saved, and what does not

**Nothing a game plays is written to production yet.** `Api.Held` answers every play-record write (`/game/new`, `/game/savePutt`, `/game/over`, `/club/leaderboard/add`, tournament, league, versus, multiplayer, Tracks and Lab saves) locally with a stand-in id, so games, putts and leaderboards stay off the live API. Reads, sign-in and profile changes are real. `-app2WritePlay` turns saving on; use it only with a test account.

When saving is on, a round is opened on its first real putt (opening a game and leaving writes nothing), only the signed-in Player's putts are saved, and keyboard test putts, the demo and Puttr's putts are never sent.

## Build and run

Unity 6000.6.8f1. Close the editor on this project before using the script.

```bash
Tools/app-build.sh setup
```

```bash
Tools/app-build.sh test
```

```bash
Tools/app-build.sh mac
Tools/app-build.sh macstore   # Builds/macOS: the Mac App Store Xcode project (scheme PuttrCo, co.puttr.ios, sandboxed, Intel + Apple silicon)
```

```bash
Tools/app-build.sh ios
```

```bash
Tools/app-build.sh iossim
```

```bash
Tools/app-build.sh android
```

- **iPhone:** open `Builds/iOS/Unity-iPhone.xcodeproj`, pick the phone, Run. Signing is automatic with the Puttr team. The scheme is "PuttrCo". `Builds/iOS` is device-only.
- **iPhone/iPad Simulator:** build `iossim`, then from `Builds/iOS-Simulator`:

  ```bash
  xcodebuild -project Unity-iPhone.xcodeproj -scheme PuttrCo -sdk iphonesimulator -configuration Release -derivedDataPath build CODE_SIGN_IDENTITY=- CODE_SIGNING_ALLOWED=YES build
  ```

  Install with `xcrun simctl install <udid> build/Build/Products/Release-iphonesimulator/*.app`, then `xcrun simctl terminate` and `launch <udid> co.puttr.ios` (a running copy keeps the old build). The simulator has no Bluetooth.
- **Android:** install `Builds/Android/Puttr2.apk`. Every build looks for a Puttr over Bluetooth first, then on the WiFi (`PuttrConsole.WifiTurn`).
- **In the editor:** open `Assets/Scenes/PuttrApp.unity` and press Play.
- **Tests:** `Tools/app-build.sh test` runs the edit-mode tests in `Assets/PuttrApp/Tests`.

The app's own screens are portrait. A game follows the device, portrait or landscape.

### TV

On a TV the app runs in TV mode (`Tv.cs`): QR sign-in, the Play tab driven by the remote, leaderboards to look at. A TV looks for the Puttr over Bluetooth first, then on the wifi, as a phone does (the Samsung web build has no Bluetooth, so the wifi only). Build settings are in `Assets/PuttrApp/Editor/TvBuilds.cs`, run-time remote setup in `TvPlatform.cs`. The TV icons and banner are `Assets/PuttrApp/TvIcons`, made from the app icon's art by `python3 docs/appicon/tv.py`.

```bash
Tools/app-build.sh tvos
```

```bash
Tools/app-build.sh tvossim
```

```bash
Tools/app-build.sh webtv
```

- **Apple TV:** `Builds/tvOS/Unity-iPhone.xcodeproj`, scheme "PuttrCo", bundle id `co.puttr.ios` (tvOS is on the iPhone app's App Store record), the iPhone's version and build. No Firebase SDK (sign-in is Firebase's REST API), no Apple or Google sign-in, camera, push, payments or photo picker: the iPhone plugins and post-processors are iOS only.
- **Apple TV Simulator:** build `tvossim`, then from `Builds/tvOS-Simulator`:

  ```bash
  xcodebuild -project Unity-iPhone.xcodeproj -scheme PuttrCo -sdk appletvsimulator -configuration Release -derivedDataPath build CODE_SIGNING_ALLOWED=NO build
  ```

  Install `build/Build/Products/Release-appletvsimulator/Puttr.app` on an "Apple TV 4K" simulator with `xcrun simctl install`, then `SIMCTL_CHILD_PUTTR_ARGS="-app2Fixture" xcrun simctl launch <udid> co.puttr.ios`. The Mac keyboard's arrows, Enter and Escape stand in for the remote.
- **Google TV / Android TV:** the Android build is also the TV app: leanback launcher, the banner, nothing a TV lacks required. `adb install Builds/Android/Puttr.apk` on the TV.
- **Samsung TV (Tizen):** `webtv` builds the WebGL app into `Builds/WebTV` (page: `Assets/WebGLTemplates/PuttrTV`, nothing compressed) and, with the Tizen Studio CLI and the `PuttrTV` signing profile (puttr-tv `docs/samsung.md`), packages `Builds/WebTV-Tizen/Puttr.wgt` with `Tools/tizen/config.xml`. Check it in a browser at 1920x1080: `python3 -m http.server 8093` in `Builds/WebTV`, then `http://localhost:8093/?app2Fixture` (query parameters are the command line: `?app2Tab=play` is `-app2Tab play`; `?puttrHost=192.168.1.40:3000` points it at a Puttr). The Puttr socket uses the browser's WebSocket there (`Assets/Plugins/WebGL/PuttrSocket.jslib`).

### Looking at screens without an account

| Argument | Does |
| --- | --- |
| `-app2Fixture` | Made-up data, no network, no saving |
| `-app2Tablet` | Lay out as a tablet |
| `-app2Tab home\|play\|improve\|profile` | Start on that tab |
| `-app2Open <route>` | Open a screen: `play:TYPE` (a game's page), `start:TYPE` (straight into it), `vs:TYPE` (you vs Puttr), `beerpong`, `tracks`, `league`, `connect` and more in `App.cs` |
| `-app2SignIn` | Start on the sign-in screen |
| `-app2Shot file.png` | Screenshot after 5 seconds, then quit (`-app2ShotAt 12` to wait longer) |
| `-app2Click x,y,seconds` | A click at that point |
| `-puttrDemo` | Play demo putts in a game |
| `-puttrShots <dir>` | Write the game card renders |
| `-puttrNoBluetooth` | Needed when a script starts the Mac build and a game is opened |
| `-app2Kit <file.json>` | Dress the 3D worlds in a brand kit from a file (Event/Venue host branding), nothing saved or sent. `-app2Open host`, `brandkit` and `brandgames` open the host screens |
| `-app2WritePlay` | Send play records to the live API (test accounts only) |

Example: `open -W -n Builds/Puttr.app --args -screen-width 430 -screen-height 932 -screen-fullscreen 0 -app2Fixture -puttrNoBluetooth -app2Open play:CENTERCUT -app2ShotAt 6 -app2Shot out.png`. On a simulator, pass them in the environment: `SIMCTL_CHILD_PUTTR_ARGS="-app2Fixture"`.

## Keys

The client keys are kept out of git (GitHub flagged them on the first commit). A fresh clone needs three local files before it builds:

| File | From |
| --- | --- |
| `Assets/PuttrApp/Scripts/Keys.cs` | `Tools/keys/Keys.example.cs`: the Firebase web key, the Google Places key, the Giphy key (puttr-app's, for the tournament cover search) and the IAPHUB client key (puttr-app `Navigator.js`, for the Puttr Club plans) |
| `Assets/Plugins/Android/PuttrApp.androidlib/res/values/firebase.xml` | `Tools/keys/firebase.example.xml`: the Android Firebase key (puttr-app `google-services.json`) |
| `Assets/PuttrApp/Editor/Firebase/GoogleService-Info.plist` | puttr-app's iOS `GoogleService-Info.plist` |

## Firebase

On iPhone, sign-in runs through the native Firebase Auth SDK 10.29.0, the same one puttr-app ships (`Assets/Plugins/iOS/PuttrFirebase.m`, `FirebaseNative.cs`), added as a Swift package by `Assets/PuttrApp/Editor/IosFirebase.cs` with `GoogleService-Info.plist` from `Assets/PuttrApp/Editor/Firebase/`. Same SDK, same bundle id, so a Player updating from puttr-app should still be signed in (`perf.log` says "found a signed-in Player in the Keychain"). Apple and Google sign-in are in `SocialSignIn.cs` and `Assets/Plugins/iOS/PuttrSignIn.m`. The editor, Mac build and Android use Firebase's REST API (`Auth.cs`). On the Mac, Apple and Google sign-in come from `Assets/Plugins/macOS/PuttrSignIn.bundle` (source and `build.sh` in `Assets/PuttrApp/Native~`); Sign in with Apple shows only in the Mac App Store build (`co.puttr.ios`), which carries its entitlement.

## Measuring

The Profile tab can show a numbers overlay, and `perf.log` in the app's documents folder records start times, every API call, frame rate while something moves, and save failures. Copy it off a phone with `xcrun devicectl device copy from --device <udid> --domain-type appDataContainer --domain-identifier co.puttr.ios --source Documents/perf.log --destination perf.log`.

## Before it ships

- Turn on saving (remove the `Api.Held` gate) once the app is ready to write real games.
- Decide when the unpublished games (sensor, arcade extras) get `published = 1`: only once the RN app is retired or can play them.
- Push notifications, payments and video still need checking on devices; see [docs/screen-parity.md](docs/screen-parity.md) for the screens still to build.
