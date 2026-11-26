# Puttr 2 design guidelines: Broadcast

Decided by Matt on 2026-11-24. Every new screen, component and feature in puttr-app-2 is built to these rules. The review that led here and the mockups are in [design-guidelines/index.html](design-guidelines/index.html) (Proposal A).

Status (2026-11-24): every page follows these rules: the four tabs, every pushed page, sheets and the in-game panels (in-game keeps its layout; only colour, corners, type and buttons changed). Contact sheets of about 90 pages, before and after, are in `design-guidelines/check/`. Checked on the Mac build at phone size, plus one wide in-game screen; not yet seen on a device, a tablet or a TV. Backups from before the change: `Builds/pre-broadcast-2026-11-24.tgz` (before the tabs) and `Builds/pre-pages-2026-11-24.tgz` (before the pushed pages).

## The one idea

The interface is a sports broadcast graphic: flat, solid, sharp-cornered, with heavy italic numerals. 3D is the game, never the interface.

## 2D and 3D

1. No interface element has depth. No raised bottom edges, drop shadows, glows, gloss, sheen, scan lines, gradients or text shadows.
2. A 3D picture, live or baked, always sits in a rectangular **window** with a 1 px border.
3. Text never sits on a 3D picture. A window's title, tags and numbers go on a solid bar under it. One exception (Matt, 2026-11-25): on a tablet, Home's session card lays its title over the bottom left corner of its window.
4. In-game screens are the exception: the world is full screen and the HUD sits over it. They follow the game layout rules (tee top left, ideal putt top right, game centred), not these.
5. No photographs and no hand-drawn illustration in the tabs. A picture is either a 3D window or a flat icon.
6. Card art (`Resources/PuttrApp/Cards`) is shot clean: no score, gauge or other in-game HUD in frame, subject centred, nothing important in the outer 8%.

## Colour

Solid colours only (the project is linear colour, so see-through colours blend too bright). Every colour comes from this list. No new greens.

| Token | Value | Use |
| --- | --- | --- |
| `base` | rgb(10, 12, 13) | Screen background |
| `surface` | rgb(21, 24, 27) | Cards, tiles, info bars, icon buttons |
| `surface-2` | rgb(34, 38, 42) | Secondary button, tracks, empty state of a meter |
| `surface-3` | rgb(48, 53, 58) | Hover and pressed state of `surface-2` |
| `line` | rgb(38, 42, 46) | 1 px borders and dividers |
| `text` | rgb(255, 255, 255) | Primary text and numerals |
| `text-soft` | rgb(196, 201, 205) | Running sentences under a title |
| `text-2` | rgb(139, 147, 153) | Secondary text, labels, tags, inactive tab |
| `brand` | rgb(45, 140, 60) | **Puttr green, the same as the Order Puttr button on puttr.co.** The main button and every green fill: progress, selected cells, avatar ground |
| `brand-hover` / `brand-press` | rgb(52, 158, 68) / rgb(36, 112, 48) | The main button's two other states |
| `bright` | rgb(122, 212, 70) | Green **text and icons** on a dark ground only (eyebrows, the section slash, the active tab, a best score), where brand green is too dim to read. Never a fill |
| `amber` | rgb(255, 179, 0) | Time running out, streaks |
| `red` | rgb(255, 82, 82) | Live, destructive actions, errors |
| `gold` | rgb(255, 210, 74) | A new personal best, trophies |

**Rank colours** (Matt, 2026-11-25): a Player's XP rank has one colour, and it is status, so it is the one place the palette grows. They are used only on the rank badge, the ring round an avatar, the left edge of the level card and the rank's name on the rank-up page. Never on a button, a bar, a chart or a section header.

| Rank | From level | Value | Text on it |
| --- | --- | --- | --- |
| Rookie | 1 | rgb(139, 147, 153) (`text-2`) | black |
| Dance Floor | 5 | rgb(45, 140, 60) (`brand`) | white |
| Weekender | 10 | rgb(0, 168, 150) | black |
| Scratch | 20 | rgb(86, 135, 255) | white |
| Club Champ | 50 | rgb(160, 110, 255) | white |
| State Am | 100 | rgb(232, 90, 170) | black |
| Tour | 150 | rgb(255, 122, 58) | black |
| Major | 250 | rgb(255, 255, 255) | black |
| Legend | 500 | rgb(255, 210, 74) (`gold`) | black |

Text on brand green is white. Colour carries meaning only. Amber, red and gold are never decoration. There are two greens and no others: brand for fills, bright for small text and icons.

## Type

Montserrat only. Six sizes: **44, 28, 20, 15, 13, 11**. The app ships Montserrat Bold, Medium and Regular; "Bold Italic" below is Bold with the italic style (`.ital`).

| Style | Face | Used for |
| --- | --- | --- |
| Title | Bold Italic, caps | Screen titles (28), hero titles (28), game names (20), main button label (16 is the one allowed extra size) |
| Numeral | Bold Italic | Every score and stat: 44 hero, 28 strip, 20 inline |
| Label | 700, caps, letter spacing 0.14 em | Section headers, eyebrows, chips, tags, stat captions, tab bar (11) |
| Body | 700 for names and row titles, 500 for sentences | 15, with 13 for secondary lines |

Sentence case in body text. Digits that line up use tabular figures.

## Space and shape

- Spacing steps: **4, 8, 12, 16, 24, 32**. Side margin 16. Gap between cards 12. Gap between sections 24. Padding inside a card 16.
- Corner radius: **4** on everything (cards, tiles, buttons, chips, inputs, avatars, sheets). A full circle only for status dots and the hole. Nothing else.
- Borders: 1 px `line` on cards and tiles. Dividers inside a card are 1 px `line`.
- Anything you tap is at least **44 x 44**.

## Components

Each kind of content has one component, used on every tab. If a new feature needs something that is not here, add it to this file first.

**Header.** One row, 44 high, same on all four tabs. Left: the tab name in Title style (the logo on Home). Right: up to two actions, each an icon button or a status pill. Nothing overlaps it, no divider under it. Pushed pages use the same row with Back on the left.

**Main button.** Brand green, 52 high, full width, white Title-style label, slanted ends (10 px parallelogram). **At most one per screen.** A screen with no single main action has none.

**Secondary button.** `surface-2`, 44 high, white Title-style label, same slanted shape. Destructive is the same button in `red`. Continue with Apple / Google is the same button in white with black words and the provider's mark before them (`Ui.Button(text, onTap, "btn btn-white", icon)`).

**Icon button.** 44 x 44, `surface`, radius 4, one 24 px stroke icon.

**Text link.** 13, `text-2`, used on the right of a section header ("See all").

**Section header.** A bright green slash, then the name in Label style: `/ LIVE NOW`. Optional text link on the right. One level only: no sub-headers that look the same as headers.

**Card.** `surface`, 1 px `line`, radius 4, padding 16. A card is for one object. Plain lists do not get a card per row.

**List row.** Avatar or icon square, title (15, 700), one secondary line (13, `text-2`), then a value, pill or chevron on the right. Rows in one group share a single bordered block with dividers.

**Game tile.** Art window on top (5:3, the card art), then a solid `surface` bar: game name in Title 20 on the left, "Best 57" in bright green numerals or "Not played" in `text-2` on the right, and under it the three tags as Label text joined with dots (`SOLO · STREAK · UNTIL YOU MISS`). No tags over the art, no raised edge. The small tile (Jump back in) is the same without tags.

**Session card.** Window on top, then eyebrow (Label, bright green), title (Title 28), one line of reason if needed, streak and progress on one row, main button. One design wherever a session is offered.

**Stat strip.** One bordered block, cells split by dividers, each a numeral over a Label caption. Not a tile per stat.

**Score block.** One 44 numeral with its Label caption, with supporting rows beside it (label left, numeral right).

**Chip.** Transparent, 1 px `line`, Label text, radius 4. On: white fill, black text. Two states, one style. No coloured chips.

**Status pill.** Small rectangle, Label text: red for live, amber for time left. A streak is amber text with the flame icon, no fill.

**Avatar.** Rounded square, radius 4. Letter avatars keep the brand-green hash colours.

**Progress.** A 4 px bar, `surface-2` track, brand fill. A week of days is seven of these with the day letter under each.

**Charts.** Solid bright green line, brand green bars, grid in `line`, labels in Label style. A filled area is one solid darker green, never a gradient. No glow.

**Achievements.** Flat crests: one glyph on a `surface` square. Earned in white or gold, locked in `text-2`. No gloss.

**Icons.** One stroke set, 2 px stroke, 24 px box. No filled colour tiles behind icons.

**Game over.** Every game ends on one page, `GameOver.Build(app, spec)` (GameOver.cs): the eyebrow, the score (or who won, with the standings), the personal best in gold when it was set, the game's own block and numbers, PQS, then Play again (the one main button), Review game and Exit, then a "See your analysis" list row when the game counted towards the Player's own putting, then the rating, then the putts. A phone scrolls it as one column. A wide screen never scrolls the page: the result, actions and rating are the left column and the putts scroll in their own column on the right. No game ends in a modal or on a page of its own design.

**Rank badge.** `Xp.Badge(level)` (Xp.cs): a small rectangle in the rank's colour, radius 4, with the level on it in Numeral style (11; 15 on the big one). It follows a Player's name wherever the name is shown: Profile, another Player's profile, boards, the matchup, the feed. Where the avatar is shown too, `Xp.Ring(avatar, level)` puts a 2 px ring of the same colour round it. A badge never changes an order or a pairing.

**XP panel.** `Xp.Panel` on the game over page, after PQS and before the buttons: a `surface` block with "XP earned" as a bright green eyebrow and the total as a 28 numeral, one row for each award the game paid, the 4 px progress bar (brand green for what the Player had, white for what was just added), and under it the level and rank on the left and the XP to the next level on the right. Shown only to a signed-in Player for a saved game; never in host mode, for guests or for test putts. A tap opens the Levels sheet.

**Level card.** On Profile under the name, under a `/ LEVEL` section header whose link is "?": a card with a 4 px left edge in the rank's colour, the level as a 28 numeral with the rank's name, the XP in the level on the right, the 4 px bar, then the XP to the next level and the levels to the next rank. Home shows XP in one place only (Matt, 2026-11-25): the profile switch beside the greeting has a 2 px ring in the rank's colour round its avatar, and the rank's name in muted Label text under "Switch". A press there always opens the profile switch, never the Levels sheet. The card and the "?" open the Levels sheet.

**Levels sheet.** `LevelsScreen`: the one page that explains XP. The Player's level card, then `/ THE RANKS` as one block of nine rows (big badge, name, levels and XP to reach, a check on ranks passed, "You" on the current one), then `/ HOW TO EARN XP` as rows with the value on the right in Numeral style, grouped Every game, Every day and Once each, read from the API. Status only: a rank carries no perk.

**Level up.** `LevelUpScreen`, a pushed page after game over when a game carries the Player over: "LEVEL UP" eyebrow, the level as the page's one oversize numeral (the only place a numeral is larger than 44), the rank's name, the levels to the next rank, and the Player's own row with ring and badge. At a new rank the rank's name leads, in its colour. Continue is the main button; See the levels is the secondary.

**Tab bar.** Flat black, 1 px `line` on top. Active tab: bright green icon and label with a 2 px bright green bar above it.

## Motion and sound

Press: the control switches to its pressed colour for about 100 ms. No bounce, no scale. Page push 240 ms. No pulsing or shimmering decoration. The one exception is loading (Matt, 2026-11-24): placeholder blocks (`Skeleton.cs`) carry a single soft `surface-3` band that crosses the whole page left to right about every 1.6 s, because there it means "loading" and is not decoration. Every press of a main button has its sound.

## Content rules

- Never show the same information twice on one screen.
- Home owns Today's session, as the session card. The card shows the session in 3D in a window (Matt, 2026-11-24): the mat, a green circle on every spot it is putted from with the count beside each (×3), and one ball that putts from each spot in turn (Matt, 2026-11-25: no balls waiting on the spots). The counts are the scene's own labels; the card's words stay outside the window.
- Once today's session is done, Home's card has no main button: Practice again and See your analysis are both secondary.
- **Improve leads with 3D stroke sessions**: filming a session is the thing we most want Players doing, so it is the first block and holds the tab's one main button, Record session, which is always there: a Player can film one every day (Matt, 2026-11-25). Review session is the secondary button under it once a session exists. Straight after it comes the analysis card (Matt, 2026-11-25): the 30-day stat strip, then one card with the mat as a flat grid (a row per tee, a column per distance, each spot a `surface-2` square with its score in red, amber or bright green), one sentence naming the strongest and weakest spot, and a secondary Full analysis button. Every Player sees the card; the Club gate is on the page behind it. PQS and the rest follow.
- **Profile has no main button.** Editing is a pencil icon button on the corner of the avatar. Switch profile is a secondary button.
- Always "Players" and "Coaches".
- No tab opens with a paragraph explaining itself. Play's sub-tabs lost their one-sentence introductions, and the one-time "first look" strips tried in their place were removed the same day (Matt, 2026-11-25): they added little to what was already on screen. A tab explains itself by its content.

## Building it

- Colours, sizes, spacing and radius come from this file only. No literal colour in a feature style sheet or in C# that is not in the colour table (`Ui.Green`, `Ui.Bright`, `Ui.Base`, `Ui.Surface`, `Ui.Surface2`, `Ui.Line`, `Ui.Soft`, `Ui.Faint`, `Ui.Amber`, `Ui.Red`, `Ui.Gold`).
- Shared components are built once in `Ui.cs` and styled at the end of `App.uss`; a feature style sheet lays things out and does not restyle them:
  - `Ui.TabHeader(title or logo, actions...)`, `Ui.IconSquare(icon, onTap)`
  - `Ui.Button(text, onTap)` main, `Ui.Secondary(text, onTap)`
  - `Ui.Section(title, link, onLink)`
  - `Ui.Title(text)`, `Ui.Caps(text)`, `Ui.Num(text)` with classes `title`, `title-20`, `caps`, `caps-green`, `num`, `num-44`, `num-20`, `body`, `body-2`
  - `Ui.StatStrip((value, caption), ...)`, classes `card`, `window`, `fchip` / `fchip-on`
  - `GameCards.Make` is the game tile.
  - Lists: rows inside one bordered block with dividers (`Rows.Row` / `Rows.Card` in Settings.cs; `set-row`, `set-row-last`). A two-or-three-way switch is `FreePuttScreen.Choice` (white when on); a filter is `scope` / `scope-on`.
  - `Dialog.Ask(..., danger: true)` gives the red button; `Page.Note` is the flat note card.
- `python3 Tools/design/uss_tokens.py` lists any colour or corner radius in the style sheets that is off this file; `--fix` rewrites neutrals, greens and radii to the nearest token. Run it before finishing UI work.
- Before finishing any screen, check it against this file: one main button at most, one header, no depth effects, no text on 3D, no repeated information, 44 pt touch targets.
