// A putt as a button, on a TV. The Player is standing at the green with a putter, and the remote is on the sofa: a
// screen can say what a putt does while it is the one showing (Quests: play the stage picked, and from a stage's result
// go on to the next one). The arcade's own screens have always done this ("Putt to start", "Putt to play again").
//
// Only a real roll counts (a hand or a club taking the ball back is not a putt), never while a game is on or a sheet is
// open, and not in the first moments of a screen: the ball that ended a stage is still being fetched.
using System;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class TvPutt
    {
        sealed class Entry { public VisualElement Element; public Func<Action> Act; public float Since; }

        static readonly List<Entry> Entries = new List<Entry>();
        static bool _listening;

        /// <summary>The first putts on a screen are not taken for this long (seconds).</summary>
        public const float Settle = 2.5f;

        /// <summary>A putt may be taken now: long enough after the screen came up, and after the last one taken.</summary>
        public static bool Ready(float now, float since, float lastTaken) => now - since >= Settle && now - lastTaken >= Settle;

        static float _taken = -99f;

        /// <summary>
        /// While `element` is showing on a TV, a putt does what `act` returns then (null: nothing).
        /// </summary>
        public static void Does(VisualElement element, Func<Action> act)
        {
            if (!Tv.On || element == null || act == null) return;
            var entry = new Entry { Element = element, Act = act };
            element.RegisterCallback<AttachToPanelEvent>(_ => { entry.Since = Time.realtimeSinceStartup; if (!Entries.Contains(entry)) Entries.Add(entry); Listen(); });
            element.RegisterCallback<DetachFromPanelEvent>(_ => Entries.Remove(entry));
            if (element.panel != null) { entry.Since = Time.realtimeSinceStartup; Entries.Add(entry); Listen(); }
        }

        /// <summary>The line that says so, for under the button. Null when this is not a TV.</summary>
        public static Label Hint(string words) => Tv.On ? Ui.Text(words, "muted center tv-putt-hint", Ui.Medium) : null;

        static void Listen()
        {
            if (_listening) return;
            _listening = true;
            Green.Console.Putt += OnPutt;
        }

        static void OnPutt(PuttResult putt)
        {
            if (!Tv.On || Entries.Count == 0) return;
            App app = App.I;
            if (app == null || (app.Games != null && app.Games.Busy) || Tv.ModalOpen) return;
            // The newest screen that is showing: the one in front.
            Entry front = null;
            for (int i = Entries.Count - 1; i >= 0 && front == null; i--) if (Tv.Shown(Entries[i].Element)) front = Entries[i];
            float now = Time.realtimeSinceStartup;
            if (front == null || !Ready(now, front.Since, _taken)) return;
            if (!Verdicts.Judge(putt, 6, 2)) return;   // not a ball
            Action act = front.Act();
            if (act == null) return;
            _taken = now;
            Perf.Wake();
            act();
        }

        /// <summary>A putt, as if one had just been hit (-app2TvKeys "putt"): for checking from a script.</summary>
        public static void Test()
        {
            Entry front = null;
            for (int i = Entries.Count - 1; i >= 0 && front == null; i--) if (Tv.Shown(Entries[i].Element)) front = Entries[i];
            Action act = front?.Act();
            Debug.LogWarning("[tv] putt: " + (front == null ? "no screen takes one" : act == null ? "nothing to do" : "taken"));
            act?.Invoke();
        }
    }
}
