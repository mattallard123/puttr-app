# Puttr 2: the Unity spike (Phase 0)

A throwaway test of whether the Puttr app could be rebuilt in Unity on the Puttr Arcade engine. It exists to answer the Phase 0 gate in `puttr-app/docs/unity-refactor-plan.md`, and nothing else depends on it. Delete the folder and nothing is lost.

It touches neither `puttr-app` nor `puttr-unity`. The arcade code in `Assets/PuttrArcade` and `Assets/Puttr` is a copy, taken on 2026-11-16 from puttr-unity commit `cfb0232` plus its uncommitted changes. **Since 2026-11-16 the iPhone build uses the real bundle id, `co.puttr.ios` (Android `co.puttr.android`), version 3.0.0 build 300.** Matt's call: the Unity app is meant to ship as an update to Puttr, so it is built as one. Installing it on a device REPLACES the Puttr app there; reinstall Puttr from the App Store to go back. The Mac build keeps `co.puttr.app2`.

## What is in it

| Piece | Where | State |
| --- | --- | --- |
| App shell: four tabs, safe areas, phone-wide column on tablets and desktop | `Assets/PuttrApp/Scripts/App.cs`, UI Toolkit | Built |
| Sign-in with email and password, typed on the device keyboard | `SignInScreen.cs`, `Auth.cs` | Built; a sign-in worked on Matt's iPhone 2026-11-16 |
| Continue with Apple, Continue with Google (iPhone) | `SocialSignIn.cs`, `Assets/Plugins/iOS/PuttrSignIn.m` | Built, not yet tried on a phone. Signs existing Players in; a first-time sign-up does not create the profile row yet |
| Sign-in by a code the Puttr app approves (Android, desktop) | `SignInScreen.cs` (`/tv/session`) | Built, not yet tried |
| Navigation: screens pushed over a tab, with Back | `App.Push` / `App.Pop` | Built |
| Game page (your best, leaderboard) and results after a round | `GameScreens.cs` | Built, checked with made-up data |
| Home from the live API: greeting, streak, Jump back in, You might like, Following feed with photos | `HomeScreen.cs` | Built, checked with made-up data only |
| Play tab: On Fire plus the live trending list | `OtherScreens.cs` | Built |
| One game, On Fire, played from the app and saved to the account | `GameBridge.cs` | Launch checked; saving not yet tried against the API |
| Measuring: start times, API timings, frame rate, idle redraw switch | `Perf.cs`, Profile tab | Built |
| Improve tab | | A note saying it is not in the spike |

The arcade copy has seven small edits, all marked `puttr-app-2`: hooks in `Arcade.cs` so the shell hears game start, each putt, game over and "All games", and the dealt distance and tee exposed on `GameMode`. When embedded it sends nothing to the Arcade analytics.

### What gets saved, and what does not

A round is saved with the same three calls the app makes (`/game/new/`, `/game/savePutt/`, `/game/over/`), and they go to the live API. So:

- Putts from a real Puttr, over Bluetooth or WiFi, are saved to the signed-in Player.
- Keyboard test putts, the demo, and the starter kit simulator on this machine are played but never sent. `-app2SaveTestPutts` overrides that; use it only with a test account.
- A round is opened on its first real putt, so opening a game and leaving writes nothing. Leaving part way marks the round abandoned.

## Run it

Unity 6000.6.8f1. Close the editor on this project before using the script.

```bash
Tools/app-build.sh setup
```

```bash
Tools/app-build.sh test
```

```bash
Tools/app-build.sh mac
```

```bash
Tools/app-build.sh ios
```

```bash
Tools/app-build.sh android
```

- **iPhone:** open `Builds/iOS/Unity-iPhone.xcodeproj`, pick the phone, Run. Signing is automatic with the Puttr team. The scheme is named "PuttrCo" so it is not mistaken for the Arcade in Xcode.
- **iPhone Simulator:** a phone build will not install on a simulator. Run `Tools/app-build.sh iossim` and open `Builds/iOS-Simulator/Unity-iPhone.xcodeproj` instead. The simulator has no Bluetooth, and its timings say nothing about a phone.
- **Android:** install `Builds/Android/Puttr2.apk`. The arcade has no Android Bluetooth, so on Android a Puttr is found over WiFi only.
- **In the editor:** open `Assets/Scenes/PuttrApp.unity` and press Play.

The app's own screens are portrait. A game follows the device: portrait or landscape, whichever way the Player holds or props it, and it is never turned for them. The arcade's game screens were drawn for a wide screen, so in portrait their text is small until each game gets a portrait layout (Phase 2 work).

### Looking at the screens with no account

`-app2Fixture` fills every screen with made-up data and sends nothing anywhere.

| Argument | Does |
| --- | --- |
| `-app2Fixture` | Made-up data, no network, no saving |
| `-app2Tab home\|play\|improve\|profile` | Start on that tab |
| `-app2SignIn` | Start on the sign-in screen |
| `-app2Shot file.png` | Screenshot after 5 seconds, then quit (`-app2ShotAt 12` to wait longer) |
| `-app2Click x,y,seconds` | A click at that point, through the Input System |
| `-puttrNoBluetooth` | Needed when a script starts the Mac build and a game is opened |

On a simulator, pass them in the environment: `SIMCTL_CHILD_PUTTR_ARGS="-app2Fixture"`.

## The gate: what to measure

Do these on the oldest iPhone you still want to support and a mid-range Android, with a real account. The numbers come from the overlay (Profile > Show or hide the numbers) and from `perf.log` in the app's documents folder, which also records every API call, the frame rate while something is moving, and save failures. Copy it off a connected phone with `xcrun devicectl device copy from --device <udid> --domain-type appDataContainer --domain-identifier co.puttr.ios --source Documents/perf.log --destination perf.log`. The thresholds are proposals; agree them before measuring, not after.

| # | Question | How | Proposed pass |
| --- | --- | --- | --- |
| 1 | Cold start | Kill the app, launch, read "first screen laid out" and "home data on screen". Add the time before Unity starts from Xcode's launch log. Compare with puttr-app on the same phone | Within 1 second of puttr-app |
| 2 | Download size | Archive in Xcode and read the App Store size estimate; APK size | Under twice puttr-app |
| 3 | Scrolling | Scroll the Following feed top to bottom, first time (photos loading) and second | 55 or more drawn a second, worst frame under 34 ms |
| 4 | Typing | Sign in by typing. Try autofill, paste, the password manager, moving the cursor | Nothing a Player would notice |
| 5 | Idle battery and heat | Leave Home on screen 15 minutes with low power on, then off. Compare battery drop and warmth with puttr-app | Low power on: no worse than puttr-app |
| 6 | A saved round | Play On Fire on a real Puttr, then check the round and its putts in the app and the streak on Home | Round, putts and score all present |
| 7 | Game after shell | Time from tapping Play to the first putt prompt, first time and second | Under 6 seconds first, under 1 after |
| 8 | Memory | Read memory on Home, in the game, and back on Home | No growth after three rounds |

Not answerable here, and still open for the full plan: whether a signed-in session survives an upgrade from the React Native app (that needs the real Firebase SDK under the real bundle id), Apple and Google sign-in buttons, payments, video and push.

## Firebase

On iPhone, sign-in runs through the native Firebase Auth SDK, version 10.29.0, the same one puttr-app ships (`Assets/Plugins/iOS/PuttrFirebase.m`, `FirebaseNative.cs`). It is added to the Xcode project as a Swift package by `Assets/PuttrApp/Editor/IosFirebase.cs`, with no CocoaPods, and `GoogleService-Info.plist` is copied from `Assets/PuttrApp/Editor/Firebase/`. Because it is the same SDK under the same bundle id, a Player who updates from puttr-app should be found in the Keychain and still be signed in: `perf.log` says "found a signed-in Player in the Keychain" when that happens. The first Xcode build downloads the package, which takes a few minutes.

The editor, the Mac build and Android still use Firebase's REST API (`Auth.cs`). Push notifications are not wired up yet.

## Known gaps

- **Session storage off iPhone.** On Android and desktop the refresh token is in PlayerPrefs. iPhone uses the Firebase SDK and the Keychain.
- **Pictures** decode on the main thread and are never evicted.
- **The first game takes about 4 seconds to open** because all 14 arcade games are built, as the Arcade does at launch. A real app would build one.
- **Colours are solid, not see-through.** The project renders in linear colour for the arcade, and the app's translucent whites blend far too bright that way. The style sheet uses the same values worked out over the background. A real app needs a decision on this.
- **Mouse drag does not scroll** on desktop; use the wheel. Touch scrolls.
