# Puttr 2 performance audit

2026-11-19. Read-only: no code, settings or builds were changed, and nothing was run on a device, simulator or emulator.

**How to read the numbers.** Measured figures come from the build logs, the built players, the Mac build's `perf.log` and `curl` against the API, and are marked *measured*. Every millisecond or MB figure for a phone or TV is an estimate from reading the code. Nothing in this audit was profiled on a device, and no device `perf.log` was on this Mac.

`Logs/android.log` and `Logs/webtv.log` (and the builds beside them) are from Nov 17 and predate most of the audio, the current cards and the Tracks art. Their sizes are not today's: a rebuild would carry roughly the iOS asset set.

## The short answer on the initial load

Fix it; do not cover it with a video.

- **Nothing is downloaded at launch.** Every asset ships in the app. A video would have no download to hide.
- **The shell itself is quick.** On the Mac build (889 fixture launches, *measured*): app awake at 0.07 s, first screen laid out at 0.3 to 0.9 s.
- **The loading bar is waiting on the network.** It stays up until five Home requests answer (`HomeScreen.cs:120`), capped at 6 s (`App.cs:166`). With no response cache, every cold start waits on live calls.
- **Each API call is slow before it starts.** From this Mac, `api.puttr.co` takes about 0.65 s to answer anything, a 404 included; the same service on its regional address (`puttr-287402.wn.r.appspot.com`) answers in about 0.35 s (*measured*, one location, a dozen samples each). The custom-domain mapping appears to add about 0.3 s to every request from every Puttr app.
- **The part before Unity is up cannot show a Unity video.** On iPhone that is dyld loading eight dynamic frameworks (six of them Agora), the IL2CPP start, and Firebase's configure and Keychain read. A native video there would compete with the engine start on the main thread, add to the app's size, and play on every launch for returning Players.

Expected result of the startup fixes below: a returning Player sees Home, drawn from the last session's data, as soon as the first screen is laid out, and it refreshes in place. If a cold start on a Fire TV or Samsung TV is still over about 3 s after that, an animated loader is the cheap answer there (on Samsung the HTML page can animate while the wasm compiles).

There is a second "initial load": the first game page a Player opens builds the whole 3D arcade behind it (`GameBridge.cs:200-204`). See Engine below.

## Done on 2026-11-19 (not yet on a device)

Checked on the Mac only: it compiles, the 253 edit-mode tests pass, and the Mac build starts, shows Home and Play, and plays Center Cut and On Fire with made-up data. The Mac build went from 334 MB to 199.5 MB (sounds 141.5 to 58.1 MB, textures 116.5 to 65.3 MB). Nothing here has run on a phone or TV, and the kept-answer path has not run against the live API (made-up data skips it).

| Item | What changed | Where |
| --- | --- | --- |
| S1 | `Api.Kept`: a GET's last answer is kept per Player in `persistentDataPath/api` and shown first. Home's reads and the Play tab's catalogue and bests use it, so the loader goes as soon as Home is built | `Api.cs`, `HomeScreen.cs`, `PlayTab.cs` |
| S2 | One shared token refresh (Mac, TVs, WebGL). iPhone and Android: the SDK's token is kept in C# for 4 minutes | `Auth.cs` |
| S6 | The session is read a frame after the loader is drawn (`App.Begin`) | `App.cs` |
| S7 | The Puttr console starts 0.7 s after Home is made, not inside `Awake` | `HomeScreen.cs` |
| S9 | The store is asked at most every 6 hours while the build is current; every time while the update screen is up | `UpdateGate.cs` |
| S10 | `perf.log` is written every 2 s and on leaving the front, and is held to 512 KB | `Perf.cs`, `App.cs` |
| A1, A2 | Import rules by folder and name: cards in the high-quality block format (ASTC 4x4 on devices); loops streamed, short sounds unpacked on load, the rest kept packed, quality 0.45 to 0.5, voice in mono. Effects keep their stereo | `Editor/PlayImports.cs` |
| Engine 1 | What a thrown-away scene alone used is freed (`Arcade.FreeUnused`, on a scene change and on leaving a game) | `Arcade.cs`, `RuleStage.cs`, `App.cs` |
| Engine 2 | Light devices (WebGL, under 2.8 GB, Android TV): FXAA, no depth of field, motion blur or grain. Depth of field is off for any stage that blurs nothing. The 3D picture's short side is held to 1440 px (900 on a light device) | `Arcade.cs`, `GameBridge.cs` |
| Engine 3 | Stand-in sounds are synthesized only when a slot has no recording | `GameAudio.cs` |
| Engine 4 | Physics catch-up held to 0.1 s a frame. The 240 Hz rate is unchanged (it sets how the ball rolls) | `World.cs` |
| Engine 5, 6 | Carpet, ball, wood, grass, concrete and the soft dot are made once and shared, with no CPU copy kept; the ball is made behind the arcade's loader | `Make.cs`, `Props.cs`, `Arcade.cs` |
| Engine 11 | `TraceNav` off | `Arcade.cs` |
| Shell 1 | The Play tab's list is built once a frame at most; search waits 180 ms after the last letter | `PlayTab.cs` |
| Shell 4 | The inbox at most once a minute from a tab tap; the Vertex scan once per sign-in | `App.cs` |
| Shell 5 | Six image downloads at a time; a failed picture is not retried for a minute | `Images.cs` |
| Shell 9 | The frame rate wakes when a request starts | `Api.cs` |
| Shell 11, 12 | `Perf.LowPower` read once; Home lets go of `CoachVoice.SettingsChanged` on sign-out and profile switch | `Perf.cs`, `HomeScreen.cs`, `App.cs` |
| Bell | The notification bell listens again each time Home returns | `Notifications.cs` |
| Samsung TV | ETC2 textures, size-optimised code with LTO, 256 MB starting heap. Not built since | `Editor/TvBuilds.cs` |

Left, with the reason:

- **S4** (API domain): held, on Matt's word.
- **S5, A3, A4, A5** (Agora linking, Agora Android extensions, R8 and AAB, managed stripping): each can break capture or reflection and needs a device pass.
- **S3**: the kept answer covers a returning Player; the first launch still waits two round trips when nobody is followed.
- **S8** (splash logo): left as is. Home draws the same picture at 92 px, where it needs its mipmaps.
- **Engine 1, second half**: the backdrop's scene is still built again on Start. Reusing it means beginning one scene twice, across 29 scenes, unchecked.
- **Engine 7, 8, 9, 10, 12, 13**: backdrops on light devices, tearing down visited games, shader warm-up, particles, the HUD canvas, shadows.
- **Shell 2, 3, 6, 7, 8, 10** and the image memory budget: tab re-attach, a shared store for repeated endpoints, the Tracks hole build and render target, the TV focus keeper.
- **Android** resolution and frame pacing.

## Do first: startup

| # | Change | Where | Why |
| --- | --- | --- | --- |
| S1 | Cache Home's answers on disk (keyed by uid and path), draw from the cache at once, call `Loaded()`, then replace as live answers arrive | `Api.Get`, `HomeScreen.cs:118-198` | Takes the network out of the launch for every returning Player |
| S2 | One shared token refresh | `Auth.cs:225-231` | Android, TVs and Mac: a cold start over an hour after the last one sends one `securetoken` POST per request (15 to 20 at once), and every call waits on its own |
| S3 | Start `/profile/communityGames/` alongside `memberGames`, not after it | `HomeScreen.cs:172-180` | A Player who follows nobody waits two round trips in a row |
| S4 | Put `api.puttr.co` behind a load balancer (serverless NEG) instead of the App Engine domain mapping, after confirming the 0.3 s from a phone | puttr-api hosting | About 0.3 s off every request, both apps |
| S5 | iPhone: stop linking Agora from the main target; load it on first capture | `Editor/IosAgora.cs:25-26` | About 47 MB of Agora code is mapped and initialised at every launch though the engine is only made when a camera starts. Measure with `DYLD_PRINT_STATISTICS` first |
| S6 | Run `FirebaseNative.Start_()` a frame after the loader is on screen | `Auth.cs:43`, `PuttrFirebase.m:45,54` | `[FIRApp configure]` and the Keychain read are synchronous in `Awake`, before the first frame |
| S7 | Start the Puttr console after the loader hides | `HomeScreen.cs:45` | It starts the radio inside `Awake`; `Green.cs:13` says the intent is the opposite |
| S8 | Splash logo: mipmaps off, export at a multiple of 4 (1944x480, or smaller) | `LogoHeader.png.meta` | 1943x479 imports as 4.7 MB uncompressed and is the first asset read |
| S9 | Android: check the store once a day, against a small API endpoint | `UpdateGate.cs:73-81`, `App.cs:111,133` | Downloads and regexes the whole Play Store page at launch and on every return to the app |
| S10 | `perf.log`: buffer and flush, rotate at about 256 KB | `Perf.cs:50` | A synchronous open, append and close on every request and mark (about 20 during launch); the file never shrinks |

## Do first: size and memory

| # | Change | Measured now | After (estimate) |
| --- | --- | --- | --- |
| A1 | Game cards: turn compression on (ASTC 6x6, try 4x4 if banding shows) | 50 cards, 77.6 MB of the 102 MB of textures; about 2.2 MB each on the GPU, cached for good (`GameCards.cs:11,22`) | About 12 MB on disk, 80 to 95 MB less texture memory |
| A2 | Audio import settings by group (below) | 141.5 MB, 57% of the assets; all 1,130 clips Vorbis quality 100, Decompress On Load | About 40 MB |
| A3 | Android: drop the 16 unused Agora extension libraries | 46.2 MB (31.8 MB compressed) of the stale APK | About 30 MB less download |
| A4 | Android: R8 on (`AndroidMinifyRelease`), ship an AAB | 46.6 MB of dex; today's assets would pass Play's 200 MB base module without A1 and A2 | Dex roughly halved |
| A5 | Managed stripping Medium; narrow `link.xml` from all of PuttrArcade to the scenes made by name | `libil2cpp.so` 57.9 MB, metadata 10.9 MB read at every launch | 15 to 30% off code and metadata; needs a full test pass for reflection |

Audio groups (A2):

| Group | Clips | Now | Import as |
| --- | --- | --- | --- |
| Loops (`*_loop_*`, 22.5 s stereo) | 56 | 66.2 MB; 3.97 MB of PCM each once played | Streaming, quality about 0.4, stereo |
| Library sound effects | 515 | 65.5 MB | Mono (they play 2D: `GameAudio.cs:116`), quality about 0.5; Compressed In Memory over about 1 s |
| Voice | 533 | 9.1 MB | Mono, quality about 0.35, Compressed In Memory |

With preload off and Decompress On Load, the first play of each clip decodes on the main thread, mid-putt (`GameAudio.cs:138,228`). Clips are never unloaded (`_slots`, `_takes`, `_spoken`). Streaming the loops and loading a game's clips behind its loader removes both.

## Engine (3D games)

Ranked by likely effect on the weakest devices.

1. **Rule scenes are rebuilt on every open and replay, and nothing they make is freed.** `RuleStage.cs:225-232` destroys the GameObject only; procedural textures, meshes and materials stay. One open builds the scene twice (backdrop, then start: `Arcade.cs:1004,1045,1070`). Estimated 15 to 25 MB leaked per build. No `Resources.UnloadUnusedAssets` or `UnloadAudioData` exists anywhere in `Assets`. Fix: cache the shared textures, keep the backdrop's scene when the same game starts, free on `End`.
2. **The full post stack runs on every device.** `Arcade.cs:348-381`: SMAA, bloom, ACES, colour, vignette, Bokeh depth of field, motion blur, film grain; HDR on, render scale 0.8, one URP asset for phones, TVs and WebGL. Depth of field runs for every stage game though only four games set a blur range (`GameMode.cs:53`). Fix: a tier picked at boot. TVs, Android and WebGL: no Bokeh, motion blur, grain or SMAA; depth of field off unless the game asks; internal height capped near 1080 (720 to 900 on Fire TV and WebGL).
3. **About 27 sounds are synthesized at boot and 26 thrown away.** `GameAudio.cs:62-87`: `Slot("knock", MakeClack())` runs the maker before looking for the recording, and every slot but `organ` has one. Estimated 175 MB of temporary arrays in one frame. Fix: pass a `Func<AudioClip>`.
4. **Physics at 240 Hz with 12 solver iterations, set for the whole app** once the arcade is built (`World.cs:34,40`), with a 0.33 s catch-up cap. At the 15 fps idle rate that is 16 steps a frame; after a hitch, up to 80. Fix: 120 Hz, 6 to 8 iterations, `maximumDeltaTime` about 0.05 to 0.1, and no simulation when no ball or pin is live.
5. **Procedural textures are uncached, uncompressed and keep a CPU copy.** `Make.Ball` is called from 12 places, `Make.Carpet` from about 14 scenes; all RGBA32 with `Apply(true)` (`Make.cs:375,411,452`). Major League paints a 2048 field per pixel (`MajorLeagueMode.cs:311`), about 55 MB and likely a second or more on a TV. A whole game is built in one frame (`Arcade.cs:55-61`), so the loader cannot move.
6. **The first putt of a session makes the ball's textures mid-putt** (`Ball.cs:36`). Make one spare ball at boot.
7. **Opening any game page builds the arcade and keeps a live scene drawing behind it** (`GameScreens.cs:477`, `GameBridge.cs:200-204`). On TVs, WebGL and low-memory phones use the card art as the backdrop and build on Play.
8. **Games visited are never torn down** (`Arcade.cs:68`). The code's own note puts all nineteen at about 240 MB. Keep the current one, perhaps one more.
9. **No shader warm-up** (`GraphicsSettings.asset`: no preloaded shaders or state collection). The first use of each material compiles a pipeline: hitches on the first game. Record a GraphicsStateCollection on a device walk and warm it behind the loader.
10. **Particles:** On Fire budgets 900 + 500 + 600 + 300 with noise on each (`OnFireMode.cs:244-292`); Putt Star 1800. Halve on low tiers.
11. **Small per-frame garbage while waiting for a putt:** prompt and tee strings rebuilt every frame (`Arcade.cs:1834`), three arrays a frame in `MatFov` (`Arcade.cs:1741`), and `const bool TraceNav = true` ships (`Arcade.cs:2027`), logging every remote step.
12. **The HUD is one pixel-perfect canvas dirtied every frame** by the prompt dot (`Hud.cs:74,2146`). Put the dot and banner on a nested canvas.
13. **Shadow distances of 1400 to 2600 with one 1024 cascade** (`Arcade.cs:1815`), and anisotropy 8 to 12 on floors (`Make.cs:378`, `MajorLeagueMode.cs:741`). Lower both on low tiers.

## App shell

1. **The Play tab loads every card in one frame and builds its list twice** (`PlayTab.cs:146-147,351`), again on each search keystroke and filter tap. No list in the shell is virtualized. With A1 this is the difference on TVs, where Play is the first screen. Fix: build cards once, filter with `display`, load art as a card nears view, debounce search.
2. **Tab switches detach and re-attach whole trees** (`App.cs:633-635`), and about 40 screens refetch and redraw on every `Shown()`, so going Back reloads the screen underneath. Keep the four tab roots attached and toggle `display`; give screens a staleness window as Improve already has (`ImproveTab.cs:327`).
3. **The same endpoints are fetched again by each tab.** `/profile/` has 13 call sites, `/coach/settings` 7, `/games/ALL` 8, each parsed separately. A small keyed store with a lifetime and in-flight sharing in `Api.Get` gives this and S1 together.
4. **`ShowTabs` refetches the whole inbox and restarts the Vertex scan** on every tab tap, Back to a tab and game exit (`App.cs:604-605`).
5. **Remote images:** the memory cache never evicts, decode and file IO are on the main thread at source size for 28 to 40 px slots, and a Top 100 board starts up to 100 downloads at once (`Images.cs:20,62,75,83`; `GameScreens.cs:671`). Fix: a size budget, 4 to 6 at a time, a small cached copy.
6. **Tracks, hole build:** per-pixel noise over up to 2048x2048, sliced by rows, not time (`TracksWorld.cs:174-202`). Expect multi-second builds with stalls on a TV, on every hole. `BuildCourse` already does it properly (6 ms slices, a precomputed noise tile: `:538-569`); use the same in `BuildHole`.
7. **Tracks, weak devices** get the unbatched path: up to 700 sphere primitives with shadows (`TracksWorld.cs:323-336`) where stronger devices get one merged mesh. About six materials leak per hole.
8. **Tracks render target:** fixed 4x MSAA at up to 1600 px, and the camera is never disabled, so a still hole is redrawn every frame (`TracksWorld.cs:67,523`). `Live3D.cs` renders on demand and is the model.
9. **iPhone:** one native token round trip per API call, stepped at the 15 fps idle rate (`Auth.cs:210-211`). Keep the token in C# for a few minutes, and wake the frame rate when a request starts, not only when it ends (`Perf.cs:73`).
10. **TV focus keeper** walks the whole tree about three times every 0.25 s (`Tv.cs:220-226,430-453`). Recompute on a hierarchy change.
11. **Small, always on:** `Perf.LowPower` reads PlayerPrefs every frame (`Perf.cs:34,88`); the spinner's 16 ms timer never stops once made (`Spinner.cs:63-67`); `Layout()` sets classes every frame (`App.cs:504-541`); every label carries an inline font (`Ui.cs:32`); log stack traces are on for every `Debug.Log`.
12. **Growing caches and subscriptions:** Tracks stills, `Board.Cache`, `ProfileTex.Made`; `CoachVoice.SettingsChanged += ...` in `HomeScreen.cs:33` is never removed, so each sign-out or profile switch keeps the old Home alive. No low-memory handler.

A bug found on the way, not performance: the notification bell unsubscribes when Home is detached (`Notifications.cs:54-55`), and Home is detached on every tab switch, so the count stops updating after the first switch.

## Per platform

- **Android:** renders at the panel's native resolution with no frame pacing (`resolutionScalingMode: 0`, `androidUseSwappy: 0`). Use Fixed DPI around 320 to 400 or cap the long side, and turn Swappy on. A3 and A4 above.
- **Samsung TV (WebGL):**
  - Textures are DXT (`ProjectSettings.asset`, WebGL format `05`), which TV GPUs almost certainly cannot use, so each is unpacked to RGBA32 on the CPU. Set ETC2 or ASTC in `TvBuilds.BuildWebTv`.
  - Code optimisation is "Shorter Build Time" and lives only in `Library/`; set it in the build script (a 41.5 MB wasm, typically 30 to 45% smaller with size LTO).
  - The heap starts at 32 MB and grows in small steps; start at 256 to 384.
  - The whole `.data` file sits in memory: a rebuild today without A1 and A2 would be roughly 150 to 180 MB against 48 MB in the stale build.
- **Apple TV, Fire TV, Android TV:** Engine 2 (the post stack) and 7 (backdrops), and App shell 1 (Play tab), matter most.
- **iPhone:** S5 and S6; the accelerometer is polled at 60 Hz from launch for the Level tool only (check the Level screen still works before setting it to 0).

## Already right

- IL2CPP Release everywhere, no development build, engine stripping on, incremental GC.
- Metal only on iOS; Vulkan then GLES3 on Android; arm64 only.
- Shader variants are stripped hard (Lit: 1,769,472 to 8).
- The mobile URP asset has no MSAA, no depth or opaque copies, one shadow cascade.
- The arcade builds only the game that is opened.
- The console's socket runs off the main thread.
- No retry storms.
- Home's requests are already parallel.
- `Live3D` renders on demand.
- Tracks stills are disk-cached.
- The idle throttle in `Perf.cs` works and allocates nothing.

## Not measured

- Launch time on any phone or TV, and how it splits between the engine start and the network. Copy `perf.log` off the phone (README, "Measuring") for the split after Unity is up; `DYLD_PRINT_STATISTICS` in the Xcode scheme for the part before.
- Frame rate and memory in any game on a device.
- Whether the 0.3 s domain-mapping cost holds from a phone on other networks.
- Whether UI Toolkit culls cards outside the scroll view in this Unity version (affects the Play tab items).
- API payload sizes and avatar source sizes.
