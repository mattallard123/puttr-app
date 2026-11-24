# Puttr 2 analytics and feedback

Puttr 2 sends the same events, to the same place, as the React Native app, so one dashboard covers both
while 3.0 rolls out.

    Unity app (Analytics.cs) ──POST /events/batch──▶ puttr-api (src/events.js) ──▶ MongoDB puttr_logs.events
    Unity app (Feedback.cs)  ──POST /feedback──────▶ puttr-api (src/feedback.js) ─▶ MongoDB puttr_logs.app_feedback
                                                     views: scripts/setup_app_analytics_views.js ──▶ Atlas Charts

## What every event carries

| Field | From | Example |
|---|---|---|
| `app` | app | `puttr2` (the React Native app sends nothing: `puttr`) |
| `device_class` | app | `tv`, `tablet`, `mobile`, `desktop` |
| `platform` | app | `ios`, `android`, `tvos`, `androidtv`, `web` (Samsung), `macos` |
| `os`, `os_version` | app | `iPadOS` `27.1`, `Fire OS` `9`, `Tizen` |
| `device_model`, `locale`, `app_version` | app | `iPhone17,1`, `en-US`, `3.0.0` |
| `device_id`, `session_id` | app | install id; a new session after 30 minutes away |
| `screen` | app | the tab or screen that was up |
| `userId` | API, from the token | null when signed out |
| `where` (country, region, city, lat, lng), `ip` | API, from App Engine's headers | nothing is asked of the device |

A Kid profile keeps only the country: no address, no city.

## Events Puttr 2 sends

| Event | When | Properties |
|---|---|---|
| `app_opened` | launch, and back after 30 minutes away | |
| `app_backgrounded` | the app goes to the back | `session_duration_ms` |
| `screen_viewed` | a screen comes to the front | (the screen is on the event) |
| `game_selected` | a game is picked to play | `game_id`, `game_type`, `game`, `source` |
| `game_started` | the API opens the round (first real putt) | `round_id`, `game_id`, `game_type`, `game`, `mode`, `players` |
| `game_completed` | the API takes the score | the same, with `seconds`, `score` |
| `game_abandoned` | the round is left | the same, with `seconds` |
| `feature_blocked`, `membership_modal_opened` | a non-member reaches a Club feature | `feature` / `trigger_feature` |
| `membership_purchase_initiated` / `_succeeded` / `_failed` | buying Puttr Club | `sku`, `trigger_feature`, `error` |
| `feedback_prompt_shown`, `feedback_dismissed` | the feedback card | `trigger` |
| `feedback_submitted` | written by the API with the answer | `rating`, `sentiment`, `trigger`, `tags` |

`mode` is `game`, `tracks`, `tournament`, `league`, `coach`, `practice`, `live`. Game starts and ends are read
off the play records in `Api.Call`, so every way of playing counts the same way. Test putts, made-up data
(`-app2Fixture`), held play (`-app2HoldPlay`) and the editor send nothing.

To add an event: `Analytics.Track("name", "key", value, …)`. No API change is needed.

## The feedback card

How the big games ask, and what this card takes from each:

| Pattern | Seen in | Here |
|---|---|---|
| Ask at a good moment, never mid-game | Apple's and Google's own guidance; most top games ask after a win or a finished level | Back in the app after a **finished** game, never an abandoned one |
| One tap first, words second | post-match thumbs or stars in competitive games; app-store style star rows | Five stars, then one follow-up shaped by the score |
| Send unhappy players to you, happy ones to the store | the common "enjoying it?" pre-prompt | 1 to 4 stars stay private; after 5 stars the system's App Store sheet is asked for (iOS; the system caps it at three a year) |
| Tags before typing | bug/feedback forms in live-service games | Six tags to tap, typing optional |
| Do not nag | Apple caps its own prompt at three a year | Needs 3 finished games and a day installed; 90 days after an answer, 30 after "Not now"; once a run |
| Always reachable | Settings > Feedback / Support in nearly every game | Settings > Send feedback |

A TV gets stars and tags (nothing to type with a remote). A Kid profile the same, and the API drops any words.
Android's in-app review sheet needs Google's Play review library and is not wired yet.

Check the card's layout on the Mac build: `-app2Fixture -app2Open feedback`.

## The views and the dashboard

`node scripts/setup_app_analytics_views.js` (in puttr-api) makes or updates the views; run it again after
changing one, and every chart stays bound.

| View | One row per | Used for |
|---|---|---|
| `vw_app_events` | event | device class and OS slices, map, game funnel, finish rate, top screens, Club funnel |
| `vw_app_places` | event that has a place | the map |
| `vw_app_daily_active` | day and app | DAU, WAU, MAU, stickiness |
| `vw_app_users` | user | new users a day, where users are, lapsed users |
| `vw_app_retention` | cohort week and week number | retention grid |
| `vw_app_sessions` | session | session length, sessions a user, share of sessions with a game |
| `vw_app_feedback` | answer | average rating, net score, ratings by device, tags, the words |

Dashboard "Puttr App Analytics" (Atlas Charts, project Puttr Logging). Built 2026-11-19: items 2, 3, 4, 5 (the map, on `vw_app_places`), 6, 7, 8, 9, 10.
Still to build once data arrives (Charts only offers fields it has seen): the feedback charts (11), the headline number tiles (1) and a dashboard filter on `app_label`.


1. Headline numbers: DAU, WAU, MAU, stickiness (`vw_app_daily_active`, latest day)
2. DAU / WAU / MAU over time
3. New users a day (`vw_app_users` by `first_day`)
4. Active users by device class (TV / Tablet / Mobile), and the OS slices inside each (`os_major`)
5. Where users are: map on `location`, and top countries / cities
6. Games: Selected → Started → Finished (`game_stage`), finish rate (mean of `finished`) by game and by device class, top games, minutes a game
7. Sessions: median minutes, sessions a day, share with a game
8. Retention grid: `cohort_week` × `week_n`, `retained_pct`
9. Top screens (`screen_viewed`)
10. Puttr Club funnel: blocked → modal → purchase started → succeeded, by `feature`
11. Feedback: average rating, net score, ratings by device class and OS, tags, prompts shown vs answered, latest comments
