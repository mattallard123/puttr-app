// The 3D scenes for the rule games (PuttrArcade Scenes/*Scene.cs), and the ShotView each putt is shown with.
//
// A game with a scene is played on RuleStageMode (its world in the top of the screen) instead of the plain mat.
// Each game fills ShotView.Data with what its scene needs in its own Views/<Game>View.cs (RuleGame.Describe).
using System;
using System.Collections.Generic;
using System.Linq;
using Puttr;
using PuttrArcade;
using UnityEngine;

namespace PuttrApp
{
    public static class RuleViews
    {
        static readonly Dictionary<string, Func<RuleScene>> Scenes = new Dictionary<string, Func<RuleScene>>();

        static RuleViews() { RuleSceneList.Register(Scenes); }

        public static bool Has(string type) => Scenes.ContainsKey(type);
        public static Func<RuleScene> For(string type) => Scenes.TryGetValue(type, out var make) ? make : null;

        /// <summary>Every rule game with a 3D world, as the arcade's card pass wants them (-puttrShots): each opening view
        /// is a fresh game of that type, started for two Players.</summary>
        public static List<(string type, Func<RuleScene> scene, ShotView view)> CardScenes()
        {
            var list = new List<(string, Func<RuleScene>, ShotView)>();
            foreach (KeyValuePair<string, Func<RuleScene>> s in Scenes)
            {
                ShotView view = null;
                try
                {
                    RuleGame g = RuleCatalog.Make(J.Parse("{\"gameType\":\"" + s.Key + "\"}"));
                    if (g != null) { if (g.MaxPlayers > 1) g.Names = new List<string> { "You", "Player 2" }; g.Start(); view = State(g); }
                }
                catch (Exception) { view = null; }
                list.Add((s.Key, s.Value, view));
            }
            return list;
        }

        /// <summary>The game as it stands, with no putt: for the start, and after a called shot or a picked cup.</summary>
        public static ShotView State(RuleGame g)
        {
            var v = new ShotView();
            Fill(v, g);
            return v;
        }

        /// <summary>The putt the game just scored, and the game after it.</summary>
        public static ShotView Of(RuleGame g, Shot s, AppPutt.Verdict verdict, PuttResult putt)
        {
            var v = new ShotView();
            if (s != null)
            {
                v.Player = s.Player; v.Feet = s.Feet; v.Tee = s.Tee;
                v.Points = s.Points; v.Good = s.Good; v.Title = s.Title ?? ""; v.Sub = s.Sub ?? "";
            }
            if (verdict != null)
            {
                v.Made = verdict.Made;
                v.HasEntry = verdict.HasEntry; v.EntryDeg = (float)verdict.Entry;
                v.OffLineDeg = (float)Scoring.OffLine(verdict, v.Feet > 0 ? v.Feet : 6, v.Tee > 0 ? v.Tee : 2);
                v.BackDoor = Scoring.BackDoor(verdict, v.Feet > 0 ? v.Feet : 6, v.Tee > 0 ? v.Tee : 2);
                v.HasRollout = verdict.Rollout.HasValue; v.RolloutIn = (float)(verdict.Rollout ?? 0);
                v.MissSide = verdict.MissSide ?? "";
            }
            if (putt != null)
            {
                v.LineIn = putt.Launch.LineAtHoleInches;
                v.Reached = putt.Launch.Reached;
                if (putt.Path != null && putt.Hole.Diameter > 0)
                    foreach (BallPoint p in putt.Path)
                    {
                        Vector2 at = PuttrAnalysis.ToInchesIntegrated(p, putt.Hole);
                        v.Path.Add(new Vector2(at.x, -at.y));
                    }
            }
            Fill(v, g);
            return v;
        }

        static void Fill(ShotView v, RuleGame g)
        {
            v.Next = g.P; v.NextFeet = g.Feet; v.NextTee = g.Tee; v.Over = g.Over;
            v.Names = g.Names.ToArray();
            v.Scores = g.Score.ToArray();
            v.ScoreTexts = Enumerable.Range(0, g.Players).Select(g.ScoreText).ToArray();
            v.Progress = g.Progress;
            g.Describe(v.Data);
        }
    }
}
