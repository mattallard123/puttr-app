// Top scores, kept on this device and nowhere else: for a party or an event,
// where the same screen is played all evening. Ten a game. Settings can wipe
// them.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public static class Leaderboard
    {
        [Serializable] class Entry { public string name; public int score; public long at; }
        [Serializable] class Board { public List<Entry> entries = new List<Entry>(); }

        public struct Row
        {
            public string Name;
            public int Score;
            /// <summary>Set in the game that has just finished.</summary>
            public bool Fresh;
        }

        const int Keep = 10;
        const string Index = "puttr.arcade.boards";
        static long _freshSince = long.MaxValue;

        static string Key(string game) => "puttr.arcade.board." + game;

        static Board Load(string game)
        {
            string json = PlayerPrefs.GetString(Key(game), "");
            Board board = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Board>(json);
            return board ?? new Board();
        }

        /// <summary>Call before adding a finished game's scores, so they can be shown as new.</summary>
        public static void BeginGame() => _freshSince = DateTime.UtcNow.Ticks;

        /// <summary>Record a score. Returns its place on the board (1 is top), or 0 if it did not make it.</summary>
        public static int Add(string game, string name, int score, bool lowerIsBetter)
        {
            Board board = Load(game);
            var entry = new Entry { name = name, score = score, at = DateTime.UtcNow.Ticks };
            board.entries.Add(entry);
            board.entries.Sort((a, b) => a.score != b.score ? (lowerIsBetter ? a.score.CompareTo(b.score) : b.score.CompareTo(a.score)) : a.at.CompareTo(b.at));
            if (board.entries.Count > Keep) board.entries.RemoveRange(Keep, board.entries.Count - Keep);
            PlayerPrefs.SetString(Key(game), JsonUtility.ToJson(board));

            // Remember which games have a board, so they can all be cleared.
            string index = PlayerPrefs.GetString(Index, "");
            if (Array.IndexOf(index.Split('|'), game) < 0) PlayerPrefs.SetString(Index, index.Length == 0 ? game : index + "|" + game);
            PlayerPrefs.Save();
            return board.entries.IndexOf(entry) + 1;
        }

        public static List<Row> Top(string game, int count)
        {
            var rows = new List<Row>();
            foreach (Entry e in Load(game).entries)
            {
                if (rows.Count >= count) break;
                rows.Add(new Row { Name = e.name, Score = e.score, Fresh = e.at >= _freshSince });
            }
            return rows;
        }

        /// <summary>How many scores are saved, over every game.</summary>
        public static int Count()
        {
            int n = 0;
            foreach (string game in PlayerPrefs.GetString(Index, "").Split('|'))
                if (game.Length > 0) n += Load(game).entries.Count;
            return n;
        }

        public static void Clear()
        {
            foreach (string game in PlayerPrefs.GetString(Index, "").Split('|'))
                if (game.Length > 0) PlayerPrefs.DeleteKey(Key(game));
            PlayerPrefs.DeleteKey(Index);
            PlayerPrefs.Save();
        }
    }
}
