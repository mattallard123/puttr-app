# Event and Venue Modes: build plan

Mockups and decisions: [index.html](index.html), published at https://claude.ai/artifact/WYkcJiyrdokBxwKGACDsEx.
Written 20 Nov 2026.

**Status (20 Nov):** Phases 1 and 2 are built and uncommitted. They compile and the edit-mode tests pass (310, 20 of them new in `BrandTests.cs`), but nothing has been looked at on a screen yet. Left out of Phase 1 for now: tinting each scene's stage lights, a shared billboard for scenes with no boards, and the arcade HUD's own logo. Logos are picked from the photo library only.

**Phase 3 (20 Nov):** built and uncommitted, 316 tests pass, screens checked on a Mac build with a kit from a file (`check/host-phone.png`, `check/host-tablet.png`). In: host mode on/off kept on the device, boot into the attract screen, host game picker (featured first, custom games, arcade games), typed-name Players on every game page, scores to the host's board under the typed name, staff PIN pad (leave host mode, clear the leaderboard), idle return after 90 s. The board id is the legacy Party board (club admin's club, else display name) fixed in the kit as `hostId` on first start; the random `h-` id for new hosts waits for the API lookup in Phase 4. Not in yet: leaders on the attract screen, remove one score, volume, the score bug accent, leaving a game that was abandoned mid-play. Not exercised: a full game played through in host mode, the live API, the hold gesture on a touch screen. A board keeps one row a game, so a game with several Players posts its best score; one row per Player needs an API change.

**Phase 4 (20 Nov):** built and uncommitted; nothing deployed. App (320 tests): Event Mode boards (`HostMode.BoardFor`: first event = the host's own id, later events `id~N`), Start a new event in the staff menu, Clear the leaderboard sends the board id, the featured game's top three on the attract screen once it has scores. API (`src/hostBoard.js` + 7 jest tests, `src/club.js`): `/club/info` brands an event board as its host and finds a renamed host by the kit's `hostId` (name match first); `/club/leaderboard/clear` takes `clubId` for the caller's own board or event only, and outside a club admin's own club only ever deletes the caller's rows. Web (`ClubLeaderboard.js/.css`): the host's primary colour as a rule under the title and the signage dots. Fixed on the way: the idle check used `UnityEngine.Input`, which this project does not allow, and threw every frame in host mode. Not exercised: the API and web changes against a database or browser, New event and Clear on a real board. Deploy order: API, then web, before any host uses a second event.

**Phase 5 (20 Nov):** built and uncommitted; nothing deployed. App (329 tests; screens in `check/venue-phone.png`, `check/venue-tablet.png`): in Venue Mode every game page shows a QR (`VenueJoin`), a member who scans takes a seat under their own name, `HostSession` keeps the group signed in from game to game until End session or the attract screen returns, member putts save under the member's id and `RoundSaver.EndHosted` closes the round so the API gives each member their own game, the board is Tonight / This week / All time from 6 am local, and a member's board row carries their name, picture and user id. API (`coachCheckin.js`, `club.js`, 10 jest tests): `GET /host/checkin/current`, `POST /host/checkin/clear`; venue rows live in `coach_checkins` under `host:<uid>:<device>` so **no migration** and no coach query can see them; a venue claim never adds a `following` row and refuses Kid profiles; `?since=` on the board route. Differs from the plan above: no `kind`/`deviceId` columns were needed. Not exercised: a real scan from a phone, a member's game landing on their profile, the live API. Not in: bay names, removing one score, the host's own personal best still shows inside On Fire's HUD.

## Decided

- **Entry:** Settings → new **Host** section → **Event & Venue mode**.
- **Who can host:** Puttr Club (`isMember`) only. Kid profiles never.
- **Brand kit:** one per profile, edited in the app and saved through the existing `/party/brandkit`. Legacy Party kits carry over; their background photos are dropped.
- **Branding in play:** goes on in-world surfaces. One LED panel is always Puttr.
- **Players:** type a name and play. No check-in, no email capture, no bay timer.
- **Members:** sign in by QR in Venue Mode only, the same way a Player signs in on the coach dashboard: the device gets their name and saves their putts to their profile, nothing more.
- **Custom games:** the host's Standard and Center Cut custom games show in the game list.

## Assumed (say if wrong)

- When Club lapses, the device goes back to the normal app at next launch. The kit is kept.

---

## Phase 1: brand layer in the 3D engine

Goal: every 3D scene can show a host's logo, partner logos and colours, and looks exactly as it does today when no kit is loaded.

**`Brand.cs` (new, in `PuttrArcade/Scripts`).** It has to live in the PuttrArcade assembly, because PuttrApp references PuttrArcade but not the other way round. It holds:
- `Logo`, `Mark` and `Partners[]` (Texture2D), and `Primary` and `Secondary` (Color).
- `Active` (bool) and a `Changed` event.
- Defaults: `Logo` = `LogoHeader`, `Mark` = `LogoMarkWhite`, `Primary` = `Make.Bright`, `Secondary` = `Make.Green`.
- `Brand.Panel(i)`: the texture for the i-th LED panel in a row. Every third panel is a partner (falls back to `Logo`) and the last panel is always the Puttr logo.
- `Brand.Apply(kit)` / `Brand.Clear()`: called by the app. Neither knows about the API.

**Swap the scene logo loads** (each one `Resources.Load("PuttrArcade/LogoHeader" | "LogoMarkWhite")` today) to use `Brand.Logo` / `Brand.Mark` / `Brand.Panel(i)`, in these files:
- `SendItMode.cs:121`
- `SpotKickMode.cs:122`, a row of seven boards that becomes the `Panel(i)` rotation
- `LanesMode.cs:171`
- `MajorLeagueMode.cs:445`
- `DerbyMode.cs:266`
- `DeadWeightMode.cs:129`
- `BeerPongMode.cs:155`
- `CricketMode.cs:286`
- `PuttStarStage.cs:211`
- `Scenes/HorseScene.cs:75-77`
- `Hud.cs:68`

**Leave these as Puttr on purpose:**
- `WorldConsole.cs` (the console lid)
- `Loader`, `Spinner`, `Onboarding`, `SignInScreen`, `HomeScreen`, `PlayTab`, `TvSignIn`, `ImproveArena`, `TracksMark`

**Colour.** Do not touch the 118 `Make.Green/Bright/Deep` uses. Switch only the places the brand should own to `Brand.Primary`:
- stage spot lights
- cup ring
- flag cloth
- ball trail (`Ball.SetTint` / trail colour)
- tee mark
- score bug accent (`GameFrame.ScoreBug`)

Find each one per scene while doing the logo pass.

**Rule scenes with no boards** (Balloon Pop, Turf War, the Center Cut and Doors scenes, and others): add one shared `Brand.Billboard(parent, pos)` (video board + LED strip) only where it fits the scene. Matt reviews a screenshot of each scene.

**Repaint without a restart.** Scenes build their roots on first use (`Arcade.Ensure`). On `Brand.Changed`, destroy and rebuild the built roots so a kit edit shows at once, including in the editor's live preview.

**Check:**
- Edit-mode test: `Brand.Panel` order, with the last panel always Puttr.
- Play-mode test: building every arcade mode with a test kit throws nothing.
- `-puttrShots` card renders with `-app2Kit <file.json>`, a new flag that loads a local kit, so each scene can be eyeballed with no API.

## Phase 2: kit in the app (Settings, editor, carry-over)

**`BrandKit.cs` (new, in `PuttrApp/Scripts`):**
- `Load()` calls `GET /party/brandkit` and keeps a PlayerPrefs mirror using legacy's newest-wins rule (`savedAt` + dirty flag), so a stale server row never wipes a fresher local edit.
- `Save()` does `POST /party/brandkit`. It is a full replace, so it always merges onto the loaded kit first.
- `Upload(slot, bytes)` calls `POST /party/brandkit/asset`.
- Image URLs download through `UnityWebRequestTexture` and are cached to `persistentDataPath` so a venue keeps its look with no wifi.
- The kit is then handed to `Brand.Apply`.

**Kit JSON.** The same object legacy writes, so carry-over is automatic.

Unity reads:
- `title`, `tagline` (the "line under it"), `brandName`, `logoUri`, `cohostLogoUri`
- `headerColor` (primary), `secondaryColor`
- `leaderboardTitle`, `qrUrl`, `qrText`
- `enabledGames`, `gameOrder`, `featuredGameId`, `includeCustomGames`

Unity ignores: `attractBgUri`, `screensBgUri`, `gameBgUri`, the scrim levels, `welcomeMessage`, `ctaText`, `gameOverText`, `footerText`.

New keys, which the legacy app ignores:
- `hostMode`: `"event"` or `"venue"`.
- `partnerLogos`: up to five URLs.
- `staffPinHash`: a hash, never the PIN itself.
- `hostId`: see Phase 3.
- `eventNo`: an integer, see Phase 4.

Legacy `enabledGames` is keyed by API game id. Map it through the catalogue; ids for retired Standard/Streak catalogue titles drop out.

**Settings:**
- In `Settings.cs`, add a Host section with one row after Tools. Hide it for `Kid.Managed` profiles.
- Non-member: open the existing Club page.
- Member: `HostModeScreen` (Event / Venue), then `BrandKitScreen`.

**`BrandKitScreen`:**
- At the top, a live preview: a RenderTexture of the arcade stage, the way `Live3D.cs` does the Improve cards.
- Sections: Text, Logos, Colours, Partner logos, QR, Staff (PIN, Clear leaderboard).
- A Games page: the host's custom games from `/creator/games`, then catalogue games. Each has a switch, an order and a featured star. New custom game opens the existing `Creator` screens, which already offer Standard and Center Cut.
- The bottom button reads Start Event Mode or Start Venue Mode, following `hostMode`.

**Check:**
- Unit tests: kit parse and merge (a legacy LAB kit JSON fixture from `puttr-api/scripts/brandkit-lab` round-trips with no field loss).
- Newest-wins sync.
- Game-id mapping.
- One run against Matt's account, which holds the LAB kit, showing the carried-over logo and colours in the preview.

## Phase 3: host shell (shared by both modes)

**Mode state:**
- `HostMode.On` is kept in PlayerPrefs.
- `App` boots into the host shell instead of the tabs when it is on, the signed-in profile is still a member, and the kit loaded.
- Otherwise the device boots normally and shows a toast once.

**Screens:**
- **Attract:**
  - The branded stage with a slow camera orbit, using the existing `Arcade.Backdrop` swing.
  - The board on the video board.
  - QR: the web board in Event Mode, sign-in in Venue Mode.
  - Any putt or tap wakes it.
  - After 90 s of no putt and no tap, the shell returns here (no `Perf` impact; the attract loop runs at the idle frame rate).
- **Who is playing:** `PlayerPicker` in a new host variant with typed names only. It never offers the host's own profile or the profiles on the account. 1-4 seats.
- **Game picker:** the kit's games in kit order, with the featured game as the large tile and the house record on each tile.
- **Final:** the existing finals, plus the rank on the host board.
- **Staff PIN:** hold the logo for 2 s, then a four-digit pad. The menu offers Exit host mode, Remove a score, New event (Event Mode only), Volume and Bay name (Venue Mode only).

**Saves:**
- Guests save under the host's account, as legacy does.
- `RoundSaver` and `Rounds.cs` take the `clubId` from `HostMode.ClubId` instead of the literal `"PUTTR"` (`RoundSaver.cs:316`, `:386`; `Rounds.cs:311`; `Tracks.cs:1000`; the board URL at `GameScreens.cs:29`).
- Outside host mode it stays `"PUTTR"`.

**Leaderboard key (fixes the legacy displayName problem):**
- `hostId` is written into the kit the first time host mode starts. For a carried-over kit it is set to the legacy clubId (the display name, or `isClubAdmin`), so Katie's existing board and her `app.puttr.co/leaderboard/KatieLABGolf` link keep working.
- For a new host it is `h-` plus 8 random base32 characters.
- It never changes after that, even if the host renames.

**Check:**
- Tests: clubId routing (host on or off), PIN hash, picker host variant, idle return.
- On the simulator only if Matt asks (standing rule).

## Phase 4: Event Mode

- **Event board:** `clubId = hostId + "~" + eventNo`.
- **New event** (staff PIN) increments `eventNo` and saves the kit. The old board stays in `club_leaderboard` untouched and can still be read at its own URL. No schema change.
- **Attract and final screens:** a QR to `app.puttr.co/leaderboard/<clubId>`. If the kit sets `qrUrl`, that wins.

**API (`club.js`):**
- `getClubInfo` falls back to `brand_kits` by `kit.hostId` (prefix match before `~`), alongside the existing displayName lookup.
- `getClubLeaderboards` already groups `club_leaderboard` by game, so it needs no change.

**Web:** `ClubLeaderboard.js` shows the kit's primary colour. Today it ignores colours.

## Phase 5: Venue Mode

**Seat sign-in: the coach dashboard check-in, reused** (Matt, 20 Nov). `coachCheckin.js` already does this for a coach's studio: the screen shows a standing QR (`/w/checkin/<token>`), the Player scans it and confirms in their own app, and the screen's poll flips to the signed-in Player (name, photo, userId). The device gets a name and a userId only, never a login for the member's account. Changes needed:
- `coach_checkins` gets two columns: `kind` (`'coach'` default, `'host'`) and `deviceId`. A host session is one open row per host + device, so several bays do not retire each other's QR.
- New routes `GET /host/checkin/current?deviceId=` and `POST /host/checkin/clear`, thin wrappers over the same functions with `kind = 'host'`. The host must be a member.
- `claim` stays one route (`POST /coach/checkin/claim`), so the shipped apps can already scan a venue QR. For a `host` row it must **skip the `following` insert**: that row is what gives a coach access to the Player's data, and a venue must not get it.
- `lab.js:93` (the check that lets a coach film for a checked-in Player) adds `AND kind = 'coach'`.
- `resolve` and the `/w/checkin` page show the kit title for a host row, so the confirm screen reads "Sign in at Lucky Lie Sim Bar".
- Flow on the device: poll, a Player appears, the app puts them in the next free seat and calls clear to mint a fresh QR for the next Player. End session (or five minutes idle) empties the seats and clears.

**Member saves: the existing multiplayer path, no API change.** This is what household Players use today:
- Each putt is posted with the putter's own `userId` under the host's `userGameId` (`savePutt` takes `userId` from the body).
- `POST /game/multiplayer/over` with `playerArray` / `scoreArray` then creates a `user_games` row for every other Player who has putts in the game, re-points their putts to it, and runs `userUpdateAfterGame` for them.
- The Unity `RoundSaver` already builds that body for profiles on the account (`RoundSaver.cs:235-279`). The work is: a member seat carries its `userId` the same way a profile seat does, typed guests stay `0`, and the arcade modes that today save only Putter 0's putts (`GameBridge.PlayWith`) post each seat's putts with its seat's `userId`.
- Each member seat also posts its own row to the host board (`/club/leaderboard/add` with the member's `userId` and public name).
- Known limit, same as today: `savePutt` and `multiplayer/over` trust the `userId` in the body. Worth a follow-up to require an open check-in or a household link for any `userId` that is not the caller, but it is not new to this feature.

**Rolling boards:**
- Tonight, This week and All time on the host board.
- Add a `since` parameter (ISO time) to `GET /club/leaderboard/:clubId/:gameId/:distance`. The existing `hours` filter uses `CURDATE() - INTERVAL`, so it is not a real rolling window. The device sends "today 06:00 local" for Tonight and "Monday 06:00 local" for This week.
- Member rows show a verified mark (the row's userId is not the host's).

**Bays:** every device signed in to the host account shares the kit and board. `Bay name` is device-local (PlayerPrefs) and shows on the attract screen.

**Check:**
- API tests: a host claim adds no `following` row, coach filming access ignores host rows, two devices keep separate QRs.
- App tests: the multiplayer-over body with member seats, and per-seat putt `userId` in arcade modes.
- One end-to-end run with two test accounts, only on Matt's go-ahead, since it writes to production.

## Deploy order

1. API: the `coach_checkins` migration (`kind`, `deviceId`), then the club.js / leaderboard `since` / host check-in routes. All of it is additive, so the legacy app is unaffected.
2. Web app: hostId lookup and brand colour on the board.
3. Unity app: ships in the Puttr 2 release. Legacy Party mode keeps working on the same kit and board throughout.

## Size and order of work

| Phase | Where | Rough size |
|---|---|---|
| 1 Brand layer | Unity arcade | 2-3 days, plus Matt's per-scene screenshot review |
| 2 Kit, Settings, editor | Unity app | 3 days |
| 3 Host shell | Unity app + small API reuse | 3 days |
| 4 Event Mode | Unity + club.js + web | 1-2 days |
| 5 Venue Mode | Check-in reuse + member seats in the save path | 2-3 days |

Phases 1 and 2 can run in parallel (different assemblies and files). Phase 3 needs both. Phases 4 and 5 are independent of each other once 3 lands.

## Risks

- **Member seats in arcade modes** change which `userId` each putt is posted with. Keep the host-off path byte-identical and covered by the existing `SaveTests`.
- **Kit image downloads** at venues with poor wifi: always boot from the cached copy, refresh in the background.
- **Repainting scenes on kit change** may leak materials and textures. Check memory in the existing `Perf` numbers after ten kit edits.
- **Host check-in rows must never grant coach access.** The `following` skip and the `lab.js` filter get their own tests.
