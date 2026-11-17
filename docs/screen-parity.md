# Screen and navigation parity with puttr-app

Checked against puttr-app `components/navigation/UserNav.js` (every registered route) on 2026-11-16.
Status: **Built** = in Unity and reachable; **To do** = a screen still to build; **Feature** = a whole
product area, not a screen (needs its own build); **Dropped** = dead in the RN app or deliberately left out.

## Tab roots

| RN | Unity | Status |
|---|---|---|
| Home `MAIN` (MainScreen + cards) | HomeScreen | Built (League Night card opens the scoreboards; bell → Notifications) |
| Play `PlayHome` (PlayLanding, Browse all grid) | PlayScreen (sports UI tiles) | Built, redesigned per docs/puttr2-sports-ui |
| Improve `ImproveContainer` (CoachHome) | ImproveScreen | Built: today's session, 3D, analysis, PQS, practice. Coach chat / Train & learn: Feature |
| Coach tab (coaches) | – | Feature (coach side) |
| Profile `Profile` (UserOverview) | ProfileScreen | Built (gear → Settings, bell → Notifications) |

## Second level

| RN route | Unity | Status |
|---|---|---|
| Notifications, NotificationSettings | NotificationsScreen, NotificationPrefsScreen | Built |
| SettingsScreen, Level, BleStatus, Vertex, VertexSetup | SettingsScreen, LevelScreen, WifiScreen, VertexScreen | Built |
| StreamingSetup, LiveStreamTest | – | Dropped (test feature, Matt 2026-11-16) |
| VideoSetup (camera check) | – | To do (needs a preview-only capture call) |
| PuttCalibration, Green testing [tester] | – | To do (tester tools) |
| Tournaments, TournamentView, TournamentGame | TournamentsScreen, TournamentScreen, rounds on the 3D ramp | Built |
| CreateTournament [member] | – | To do |
| LeagueLeaderboard, LeagueOnboarding, LeagueGame | LeagueScreen, LeagueJoinScreen, league rounds | Built |
| LeagueTeamDetail, LeagueRecap, LeagueSeasons, LeagueFinale | LeagueTeamScreen, LeagueRecapScreen, LeagueSeasonsScreen, LeagueFinaleScreen | Built (recap reel plays full screen; no download) |
| Challenges (list), ChallengeView | ChallengesScreen, ChallengeScreen | Built |
| MemberProfile, MemberGames | ProfileScreen.Open(userId), GamesScreen(memberId) | Built (follow, fist bump) |
| GameView (a played game) | GameViewScreen | Built (putt list, pace, replay video; no scatter chart, Vertex or chipping layout yet) |
| GameLeaderboard (per game) | GameLeaderboardScreen | Built (top 100; no Recent / All time toggle yet) |
| UserGames, UserAchievements | GamesScreen, AchievementsScreen | Built |
| Social, SearchMembers | SocialScreen, FindPlayersScreen | Built |
| FeedScreen | FeedScreen | Built |
| Membership / MEMBERSHIP | MembershipScreen, ClubScreen (paywall) | Built (in-app purchase not in this build) |
| MyPutters, EditPutter, EditProfile, SetUserLocation | PuttersScreen, EditPutterScreen, EditProfileScreen | Built (no photo upload; home town is typed, not searched) |
| AnalysisScreen | AnalysisScreen | Built |
| LabScreen / SessionsScreen, CoachGame, LabSessionView, CoachedReview, LabSessionReplay | LabSessionsScreen, LabIntro/Capture/Processing, LabSessionScreen | Built (review simplified) |
| FITTING (Compare) [member] | – | To do (a game) |
| DrillGame | coach drills via GameBridge.PlayDrill | Built |
| SecondCamera [tester] | – | To do (tester) |

## Features (each its own build)

| Area | RN routes | Notes |
|---|---|---|
| Versus (async 1v1) | VersusInbox, VersusResults, VersusGamePicker, game `{versusId}` | **Built**: inbox (rivals, rematch), results putt by putt, picker, challenge setup, Home card, game page button, notifications. Plays Standard and Center Cut games (the putt-list games on the 3D ramp) |
| Live multiplayer | Multiplayer, MultiplayerGame | **Built**: lobby, setup, live 1v1 on the 3D ramp over the same socket server and host-referee rules (plays against the RN app), Home Live now card, opponent video with your own picture and mute (Agora MP_<gameId>, Opponents.cs LiveVideo). Random-opponent ghost mode: slot-machine pick, their saved putts scored beside yours, multiplayer/over |
| Quests | Quests, QuestLevels, QuestGame | **Built**: quests by tier, level grid with unlocks and stars, Standard / Streak / Precision levels on the 3D ramp, Club gating, Play card, Profile stars |
| Tracks | TracksCourses, TRACKS, TracksLeaderboard | **Built (broadcast golf)**: course list, setup (Front/Back/18, Amateur/Pro/Major), the round on a real 3D hole (TracksWorld: terrain from the course features, ball flight with tracer, Broadcast / Ball follow / Plan / Green cameras), caddie club + mat putt bar, change club, water drops, lag putts, cup window, gimmes, pick up at 12, scorecard strip, results. Saves /game/new + putts, /tracks/round POST/PUT, /game/over, leaderboard. Not yet: multiplayer (1-4 players), the Tracks leaderboard page, Home Track of the Week, community tracks |
| Custom games | Creator, EditGame | **Built**: My games (Play tab), New game sheet, putt editor on the mat grid with reorder and autosave, play on the 3D ramp (Standard, Streak, Center cut). Not yet: the coach tab's Games & Drills, assigning in plans |
| Coach chat and Caddie | PuttrProChat, CoachChatHistory, Caddie | **Built**: streamed coach chat with lesson/game recommendations, history (read-only for past members), Caddie support chat (Settings Need help?, Club page). No coach voice in chat (the app has none either) |
| Train & learn | LearnScreen, LessonView, LessonDrill | **Built**: Improve tiles, library by section (first two free), lesson video, drill on the 3D ramp, up next, rating. SPEED drills score the rollout spread; TEMPO drills tick a metronome (50 to 120 bpm); VIDEO drills open Camera setup |
| Lessons with a coach (Player) | FindCoach, LessonCoach, BookLesson, MyLessons, BookedLesson, PlanDetail, CoachVideos | **Built** (Lessons.cs): coach list and profile, booking with Stripe PaymentSheet (PuttrPay.swift / PuttrPayActivity.java, Apple Pay), My Lessons, the lesson timeline, submit a session, cancel and refund, the delivered video, rating, practice plan with before/after, From your coach. Improve: coach-video card, Real coach review (testers) |
| Coach side | CoachHome, LessonInbox, LessonWorkspace, LessonDeliver(ed), CoachLessonSetup, AssignPlan, PlayerRoster, PlayerMonitor, CoachingDashboard, CoachingVideos, SessionPage, SessionAnalysis, CoachAcademy, AcademyModule, AcademyQuiz, CoachApply, CoachApplicationStatus, CoachLanding | **Built** (CoachDesk.cs, Coaching.cs): Coach tab in Improve's place with the review badge, dashboard with go-live checklist and affiliate card, inbox, workspace, deliver (resumable upload), delivered, coaching profile and Stripe payouts, practice plans, roster, monitor and nudge, Player Review (goals and notes, sessions, studio lessons), video library with upload, Academy, quiz, apply (6 steps), status, Pro plans. Recording: ReplayKit on iPhone (save from Apple's preview, pick in Deliver), system recorder on Android. Not yet: the telestrator drawing, the coach-camera bubble, CoachMainPage as the coach's Home, the tablet 3-column review stage |
| Profiles (family) | Family, FamilyAddPlayer, FamilyCharacter, FamilyPlayer, FamilyPrivacy, SwitchPlayerSheet | **Built**: switch sheet (Home and Profile), PIN, Profiles page with leaderboard, add player, Crew characters, player page, kid privacy, Kid profile gates, front-camera photo (a Kid's stays on the phone), data export through the share sheet |
| Junior membership | JuniorLanding, JuniorApply, JuniorStatus, JuniorParentWaitlist | **Built** (Junior.cs): pitch, 3-step application (AL/HI no-post track, parent consent for minors, under 13 to the parent waitlist), status, parent waitlist. Settings row and Club paywall promo |
| Create tournament | CreateTournament | **Built** (Junior.cs): Giphy cover, dates, distance range, invites, members only |
| Game page extras | GameView | **Built** (GameExtras.cs): insights (getPuttAssessment), Vertex stroke analysis, putt scatter chart on the mat, chipping layout; top 100 with Recent / All Time |
| Body faults | CoachedReview | **Built** (BodyFaults.cs): setup faults per putt from the pose, spoken and shown in the 3D review |
| Camera setup | VideoConfig | **Built**: Settings row, front/rear with the centre line |
| Party / venue | PartyAdmin, PartyNav | Play intent card |
| Admin | AdminScreen, AdminUser, AdminGame, AdminApplication, AdminCertGrade, AdminJuniorApplication | **Built** (Admin.cs), shown only for profile.isAdmin: search, games today, 3D sessions, coach applications and certification grading, juniors, a Player's full overview, membership activation, coach switch. Date activation is typed and confirmed (no native picker) |
| Sign up and onboarding | IntroScreen, EmailAuth, OnboardingGuide | **Built**: Google/Apple/email sign-in and sign-up, password reset, profile created on first sign-in, the 6-step onboarding, and the coach's first run on Improve (pick a coach in their voice, 5 putts at 6 ft, the reveal with /coach/diagnose, daily goal and reminder) |
| Games | 49 game routes | Being moved to the 3D arcade engine one by one (Free Putt is the reference) |

## Platform services

| RN | Unity | Status |
|---|---|---|
| react-native-iaphub (Club subscriptions) | Iap.cs + PuttrIaphub.swift (SDK 4.6.7 vendored) / PuttrIaphub.java (5.2.0) | **Built**: plans, buy, restore, owner-as-user for managed profiles, Pro (coach) plans |
| FCM push | Push.cs + PuttrPush.m / PuttrPush.java + PuttrMessagingService | **Built**: permission, token register/remove, foreground toast, tap routing |
| Deep and universal links | DeepLinks.cs, associated domains, Android intent filters | **Built**: every DeepLinkHandler route, Klaviyo trk links |
| Profile photo, city search | Photo.cs + PuttrPhoto.m / PuttrPhotoActivity.java, Google Places | **Built** (videos too, for uploads) |
| Stripe (lessons) | Lessons.cs Pay + PuttrPay.swift (stripe-ios 26.3.0 SPM) / PuttrPayActivity.java (stripe-android 23.12.0) | **Built**, Apple Pay merchant.co.puttr.puttrapp |
| Share sheet | Photo.cs Share + PuttrShare_File / PuttrShare.java (FileProvider) | **Built** |

## Dead in the RN app (not ported)

Kiosk and Retail modes (KioskNav, RetailNav): deprecated (Matt, 2026-11-16).


OpponentSelect, TournamentRoundView, CoachingLessonView, VideoConfig (alias), LabSession3D, Edit (dup),
CoachVideoLibrary, ChallengesScreen (Profile copy), PlayFun, Help, LeagueDebug, CoachAnalysis,
CoachingLesson, CoachingSession, ProfileScreen (Home copy), MAIN_GAMES, PPA screens.

RN navigation bugs not carried over: `MEMBERSHIP` vs `Membership` in Improve/Profile stacks,
GameLeaderboard missing from the Improve stack, Notifications in Improve targeting routes absent there.
Unity has one stack, so every screen is reachable from every tab.
