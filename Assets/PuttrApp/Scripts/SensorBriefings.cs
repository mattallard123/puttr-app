// The "How to play" for each Vertex sensor game: what you do, how it scores, how it ends. Three short lines each
// (Matt, 2026-11-24: the long briefings from puttr-app games/sensor/sensorBriefings.js were far too much to read).
// Metric definitions, tips and the numbers the game shows on screen are left out on purpose: do not add them back.
using System.Collections.Generic;

namespace PuttrApp
{
    public static class SensorBriefings
    {
        static readonly Dictionary<string, string[]> All = new Dictionary<string, string[]>
        {
            { "PENDULUM", new[]
            {
                "Start the putter back on one tick and strike the ball on the next.",
                "The closer your tempo is to the beat, the more you score.",
                "The game ends after 15 putts.",
            } },
            { "WALTZ", new[]
            {
                "Three clicks: start back, reach the top, strike the ball.",
                "The closer your rhythm is to your target, the more you score.",
                "The game ends after 15 putts.",
            } },
            { "PURE", new[]
            {
                "Make your smoothest, squarest stroke on every putt.",
                "Each stroke scores up to 100, and a make doubles it.",
                "The game ends after 15 putts.",
            } },
            { "CARBONCOPY", new[]
            {
                "Your first putt is free and records your signature stroke.",
                "Copy that stroke 14 times. Each copy scores its match, up to 100.",
                "A match of 90 or better scores double.",
            } },
            { "SQUAREUP", new[]
            {
                "Return the putter face to the same angle on every putt.",
                "A putt inside the gate survives, and the gate narrows every 3.",
                "A putt outside the gate costs a ball. Lose all 3 and the game ends.",
            } },
            { "STRETCH", new[]
            {
                "Take a longer backstroke for a longer putt, at the same tempo.",
                "Score for backstroke length, steady tempo and stopping near the hole.",
                "All three on one putt doubles it. The game ends after 12 putts.",
            } },
            { "LOCKDOWN", new[]
            {
                "Your first 3 putts set your stroke. Then repeat it.",
                "Each of the six stroke readings inside the band scores points.",
                "Three putts in a row with a reading outside the band ends the game.",
            } },
            { "THEYIPS", new[]
            {
                "Smooth strokes damage the monster, and a make hits twice as hard.",
                "A twitchy face or broken rhythm lets it hit you back.",
                "Drain its health before it drains yours.",
            } },
        };

        public static string[] Lines(string gameType) => All.TryGetValue(gameType, out string[] lines) ? lines : new string[0];
    }
}
