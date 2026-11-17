// The long "how to play" for each Vertex sensor game, word for word from puttr-app games/sensor/sensorBriefings.js
// (generated from it: retune a game there and here together). The app's "screen" notes describe its own dials, so
// they are left out: the 3D worlds here draw the same things their own way.
using System.Collections.Generic;

namespace PuttrApp
{
    public static class SensorBriefings
    {
        static readonly Dictionary<string, string[]> All = new Dictionary<string, string[]>
        {
            { "PENDULUM", new[]
            {
                "Make every stroke take the same amount of time, and make that time match the beat you picked. This is a timing drill. Where the ball finishes barely matters.",
                "Tempo (BPM): BPM means beats per minute. Vertex times your stroke from the moment the putter starts back until it strikes the ball, then turns that time into a rate: 60 divided by the stroke time in seconds. A stroke that takes 1.00 second is 60 BPM. 0.83 seconds is 72 BPM. 0.77 seconds is 78 BPM. A higher number is a quicker stroke, a lower number is a slower stroke.",
                "Too slow / too quick: TOO SLOW means your stroke took longer than the beat, so your BPM came in under the target. TOO QUICK means the stroke was over sooner than the beat, so your BPM came in over the target. It says nothing about how hard you hit the ball.",
                "Rhythm: Shown for reference only. It is backstroke time divided by forward stroke time. 2.0:1 means the backstroke took twice as long as the move from the top to the ball.",
                "Pick a target tempo below. There is no correct number. Start with the default, play a round, and then use your own average tempo from the results screen.",
                "The metronome ticks at your target. The gap from one tick to the next is exactly how long your whole stroke should take.",
                "Start the putter back on one tick and strike the ball on the next tick. Back on \"tick\", hit on \"tick\".",
                "Each putt is dealt from a new distance and tee. A longer putt needs a longer stroke, not a quicker one. Keep the time the same and let the length change.",
                "Within 3 BPM of the target is \"in the pocket\": full tempo credit, and your streak multiplier climbs by one (up to x5).",
                "Outside the pocket the credit fades, reaching zero at 10 BPM off, and the streak resets to x1.",
                "Points per putt = distance in feet x 10 x tempo credit x streak multiplier. A made putt adds 50 percent.",
                "15 putts. The multiplier only listens to the beat, so a miss with perfect tempo keeps your streak alive.",
                "Tip: If you are always too slow, do not rush the hit. Shorten the pause at the top, or pick a lower target that fits your natural stroke.",
            } },
            { "WALTZ", new[]
            {
                "Give every stroke the same proportions: a backstroke that takes a set multiple of the time your forward stroke takes. This is a rhythm drill. How quick the whole stroke is does not score, and where the ball finishes barely matters.",
                "Rhythm (back : through): Vertex times your backstroke (takeaway to the top) and your forward stroke (the top to the ball) and divides the first by the second. 2.0:1 means the backstroke took twice as long as the forward stroke. 1.5:1 means one and a half times as long.",
                "Quick back / slow back: QUICK BACK means your ratio came in under the target: the backstroke was short on time compared with the forward stroke. SLOW BACK means the ratio came in over the target: the backstroke dragged and the forward stroke was a jab.",
                "Tempo: Shown for reference only. It is how quick the whole stroke is, in beats per minute. Rhythm is the split inside that time, and a good rhythm holds at any tempo.",
                "Pick a target rhythm below. 2.0:1 is the default and the common tour pattern. Choose another ratio, such as 1.5:1, if that is the stroke you want to drill.",
                "The metronome plays three clicks for each stroke, then rests. Start the putter back on the first click, reach the top on the second, and strike the ball on the third.",
                "Listen to a couple of cycles first, then join in on a first click.",
                "Every 3 putts the clicks speed up or slow down. Keep the same split and let the whole stroke get quicker or slower with them.",
                "Each putt is dealt from a new distance and tee. A longer putt needs a longer stroke with the same rhythm.",
                "Within 0.2 of your target is \"in time\": 100 points and your streak grows by one.",
                "Outside that the points fade, reaching zero at 0.7 off, and the streak resets.",
                "Three or more in time in a row is a Waltz and doubles the putt. A made putt adds 25 percent.",
                "15 putts in 5 rounds of 3. The streak only listens to the ratio, so a miss with good rhythm keeps it alive.",
                "Tip: Most quick-back strokes come from snatching the putter away. Wait for the second click before you change direction.",
            } },
            { "PURE", new[]
            {
                "Make the best quality stroke you can on every putt. The stroke earns the points. Holing the putt only doubles them.",
                "Face change (ideal 0°): How much the putter face angle at impact differs from where it was at address. 0° means the face came back exactly where you aimed it. Plus is closed, minus is open.",
                "Rhythm (ideal 2.0:1): Backstroke time divided by forward stroke time. 2.0 means the backstroke took twice as long as the move from the top to the ball, which is the common tour pattern.",
                "Lie angle (ideal 0°): How the putter sole sits at impact. 0° is flat to the ground. Plus is toe down, minus is toe up.",
                "Purity (0 to 100): Each of the three is scored 0 to 100 on a bell curve around its ideal, the same scoring Vertex uses. Purity is the average of the three. For a feel of the curve: face change 1.4° off scores about 60 and 3° off scores about 10. Rhythm 0.7 away from 2.0 scores about 60. Lie 2° off scores about 60.",
                "Putt to the dealt distance and tee. Think about the stroke, not the hole.",
                "After each putt the three bars show which part of the stroke cost you points.",
                "Points per putt = purity. A made putt doubles it.",
                "Pure Strike: all three metrics at 75 or better on the same putt. That is roughly face change inside 1°, rhythm between 1.5 and 2.5, and lie inside 1.5°.",
                "15 putts. A square, smooth lip-out beats a jabby make.",
                "Tip: Face change is measured from your own address position. If yours reads the same non-zero number every putt, check your setup (forward press, where the hands sit) before blaming the stroke.",
            } },
            { "CARBONCOPY", new[]
            {
                "Hit one stroke you like, then repeat that exact stroke 14 more times. This is a repeatability drill. There is no ideal stroke here, only yours.",
                "Tempo: How quick the whole stroke is, in BPM (60 divided by the stroke time in seconds). Base tolerance 6 BPM.",
                "Length: How far the putter head travels on the backstroke, in inches. Base tolerance 2.4 in.",
                "Rhythm: Backstroke time divided by forward stroke time. Base tolerance 0.35.",
                "Face change: Face angle at impact compared with address, in degrees. Base tolerance 1.2°.",
                "Match %: Each metric is scored 0 to 100 on how close it is to your signature (a bell curve: one tolerance away scores about 60, two away scores about 14). Match is the average of the four.",
                "Pick your tee and distance below. Every putt is from that same spot, so the only thing changing is you.",
                "Putt 1 is free and unscored. Whatever you do becomes the signature, so make it your normal stroke, not a special one.",
                "Then hit 14 copies. After each one you see your value against the signature value for all four metrics.",
                "Points per copy = match %. A match of 90 or better is a Carbon Copy and doubles.",
                "The tolerance tightens every 3 copies: 100%, 85%, 70%, 55%, then 45% of the base tolerance. The same stroke scores lower late in the game.",
                "Makes are counted but do not score.",
                "Tip: Length and tempo drift together. If length is red, check whether tempo went with it.",
            } },
            { "SQUAREUP", new[]
            {
                "Deliver the putter face the same way every putt and keep it inside a gate that shrinks as you survive. Only the face counts. Makes do not save you and misses do not hurt you.",
                "Face change: The putter face angle at impact compared with the face angle at address, in degrees. It is the whole stroke added together: if the face opens 4° going back and closes 4° coming through, face change is 0°. Plus means the face arrived closed compared with address, minus means open.",
                "What it does not know: The sensor has no idea where the hole is. It only knows where your face pointed at address. So a steady +3° is not automatically a pull: if you set up slightly open and return the face to the target line, you will read +3° and hole putts all day. What costs you putts is the number moving around from stroke to stroke.",
                "Pick your tee and distance below. Every putt is from that same spot.",
                "Choose where the gate is centered. \"My normal\" uses your first 3 putts (free, no balls at risk) to find your usual face change and centers the gate there, so the game tests how repeatable your face is. \"True zero\" centers the gate on 0°, so the game tests whether you return the face exactly to where you aimed it.",
                "Set up the same way every time, then putt. The sensor zeroes on your address position each stroke.",
                "The gate starts at plus or minus 2.0° around the center. Inside the gate the putt survives.",
                "Every 3 putts survived clears a level and the gate narrows by 0.25°, down to a floor of plus or minus 0.5°.",
                "Outside the gate costs a ball. You have 3 balls. Your score is the tightest gate you reached.",
                "Tip: If your normal is a long way from 0° (more than about 2°), play \"My normal\" to build the repeatable face first, then use \"True zero\" as the harder test of setup and aim.",
            } },
            { "STRETCH", new[]
            {
                "Control distance with the length of your backstroke, not with a harder hit. Short putt, short stroke. Long putt, long stroke. Same tempo every time.",
                "Backstroke length (inches): How far the putter head travels from address to the top of the backstroke. Each distance has a target band 4 inches wide. The band center is 8 in plus half an inch per foot of putt: 3 ft asks for 7.5 to 11.5 in, 6 ft asks for 9 to 13 in, 11 ft asks for 11.5 to 15.5 in.",
                "Tempo (BPM): How quick the whole stroke is: 60 divided by the stroke time in seconds. Your first 3 putts set your tempo lock (their average). After that you must stay within 5 BPM of it on every putt, short or long.",
                "Rollout (inches): Where the ball would have stopped compared with the hole. 0 is dying at the hole, plus is past, minus is short. This is the outcome check on your distance control.",
                "Pick your tee line below. Distances alternate short (3 to 6 ft) then long (8 to 11 ft), all on that line.",
                "Before each putt, read the target band on the ruler. After the putt the white marker shows where your backstroke actually finished.",
                "On the long putts, take the putter back further and keep the same beat. Do not speed up.",
                "Backstroke length inside the band: 10 points.",
                "Tempo inside your locked band: 10 points (no tempo points during the first 3 calibration putts).",
                "Rollout: up to 10 points, full marks for stopping at the hole, fading to zero at 12 inches long or short.",
                "Elastic: all three on the same putt doubles it. 12 putts.",
                "Tip: The bands come from real Puttr player data and are still being tuned. If a band feels wrong for your stroke, tell us which distance and by how much.",
            } },
            { "LOCKDOWN", new[]
            {
                "Make the same stroke over and over. Six parts of your stroke are compared with your own average, and all six have to stay inside a band that tightens every time you succeed.",
                "Tempo: How quick the whole stroke is, in BPM (60 divided by stroke time). Allowed plus or minus 4 BPM.",
                "Speed: Putter head speed at impact, in meters per second. Allowed plus or minus 0.4.",
                "Back T: Backstroke time, from first movement to the top. Allowed plus or minus 0.06 s.",
                "Fwd T: Forward stroke time, from the top to impact. Allowed plus or minus 0.04 s.",
                "Length: Backstroke length. Allowed plus or minus 2 inches.",
                "Rot: How far the face rotates open on the backstroke. Allowed plus or minus 1.2°.",
                "The band: Those allowances are the band at 100%. At 80% every allowance is 20 percent smaller. Everything is measured against the average of your own 3 calibration putts, so there is no ideal stroke, only your stroke.",
                "Pick your tee and distance below. Every putt is from that same spot.",
                "Putts 1 to 3 are calibration. Putt normally. Their average becomes the center of every spoke.",
                "From putt 4 on, every stroke is plotted as a hexagon. Keep all six corners outside the dashed ring.",
                "Each spoke inside the band scores 5 points, multiplied by your combo.",
                "All six inside is a Lockdown: combo goes up by one and the band tightens 5 percent.",
                "Any spoke outside is a break: combo resets and the band relaxes 5 percent.",
                "Three putts in a row with a break ends the run. Your score is the tightest band you reached.",
                "Tip: Holing the putt does not matter here. The toast names the spokes that broke, fix one at a time.",
            } },
            { "THEYIPS", new[]
            {
                "Beat the monster with smooth, quiet strokes before your twitches beat you. Good strokes do damage. A flinch in the face or a rushed rhythm lets it hit back.",
                "Stroke quality (purity): The average of three Vertex scores, each 0 to 100: face change (ideal 0°, face at impact compared with address), rhythm (ideal 2.0, backstroke time divided by forward stroke time) and lie angle (ideal 0°, sole flat at impact).",
                "Face twitch: Face change beyond the strike window: more than 2.0° in stage 1, 1.5° in stage 2, 1.2° in stage 3, open or closed.",
                "Rhythm collapse: Rhythm under 1.4 (backstroke rushed) or over 3.0 (backstroke dragged, then a jab).",
                "Putt to the dealt distance and tee. Longer putts hit harder.",
                "Watch both HP bars. The monster has 250 HP, you have 100.",
                "Damage = purity percent x distance in feet x 2. A made putt is a critical hit and doubles it.",
                "Each damaging putt without taking a hit builds your combo, worth 2 extra damage per step.",
                "A face twitch costs you 4 HP plus 3 per degree (max 20). A rhythm collapse costs 8 HP.",
                "Stages change at 66 percent and 33 percent of the monster's HP. In stage 3, strokes under 50 purity do no damage.",
                "Tip: This game judges face change against 0°. If your normal face change sits well away from 0°, the monster will strike almost every putt. Work on Square Up first.",
            } },
        };

        public static string[] Lines(string gameType) => All.TryGetValue(gameType, out string[] lines) ? lines : new string[0];
    }
}
