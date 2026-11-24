// The brand the 3D worlds are dressed in. With no kit applied it is Puttr, and every scene looks as it always has.
// A host (Event Mode, Venue Mode) applies a kit: its logo goes on the boards, signs and floor marks the scenes
// already have, its partners take one board in three, and its colour lights the cup ring and the ball's trail.
// One board in every row stays Puttr (Brand.Panel), and so does the console lid (WorldConsole never asks Brand).
//
// The app owns loading and saving the kit (PuttrApp BrandKit.cs); this file knows nothing of the API.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public static class Brand
    {
        static Texture2D _puttr, _puttrMark, _puttrMarkGreen;
        static Texture2D _logo;
        static readonly List<Texture2D> _partners = new List<Texture2D>();
        static Color _primary, _secondary;

        /// <summary>A host's kit is on. False: everything is Puttr.</summary>
        public static bool Active { get; private set; }
        /// <summary>Goes up on every Apply and Clear. A scene that keeps brand materials compares it to know they are stale.</summary>
        public static int Version { get; private set; }
        /// <summary>The host's title ("Zero Torque Challenge"), for boards that carry words. Empty with no kit.</summary>
        public static string Title { get; private set; } = "";
        public static event Action Changed;

        /// <summary>The Puttr lockup (mark and PUTTR, white letters). Never the host's.</summary>
        public static Texture2D Puttr => _puttr != null ? _puttr : _puttr = Resources.Load<Texture2D>("PuttrArcade/LogoHeader");
        /// <summary>The Puttr mark in white. Never the host's.</summary>
        public static Texture2D PuttrMark => _puttrMark != null ? _puttrMark : _puttrMark = Resources.Load<Texture2D>("PuttrArcade/LogoMarkWhite");
        static Texture2D PuttrMarkGreen => _puttrMarkGreen != null ? _puttrMarkGreen : _puttrMarkGreen = Resources.Load<Texture2D>("PuttrArcade/LogoMark");

        /// <summary>The logo a scene shows where the Puttr lockup used to be: the host's, or Puttr's.</summary>
        public static Texture2D Logo => Active && _logo != null ? _logo : Puttr;
        /// <summary>Where a scene used the white Puttr mark (a drum head, a bullseye): the host's logo, or the mark.</summary>
        public static Texture2D Mark => Active && _logo != null ? _logo : PuttrMark;
        /// <summary>Where a scene used the green Puttr mark (a centre circle): the host's logo, or the mark.</summary>
        public static Texture2D MarkColour => Active && _logo != null ? _logo : PuttrMarkGreen;
        /// <summary>The white Puttr mark is drawn tinted; a host's logo keeps its own colours.</summary>
        public static bool MarkIsTinted => !(Active && _logo != null);

        public static IReadOnlyList<Texture2D> Partners => _partners;

        /// <summary>The host's main colour (lights, cup ring, ball trail), or Puttr's bright green.</summary>
        public static Color Primary => Active ? _primary : Make.Bright;
        public static Color Secondary => Active ? _secondary : Make.Green;

        public enum Slot { Host, Partner, Puttr }

        /// <summary>Whose logo the i-th board of a row of count carries. The last board is always Puttr; every third
        /// is a partner's (when the kit has any); the rest are the host's. With no kit every board is Puttr.</summary>
        public static Slot SlotAt(int i, int count)
        {
            if (!Active || _logo == null) return Slot.Puttr;
            if (count <= 1) return Slot.Host;              // one sign: the host's. The console lid is Puttr's.
            if (i == count - 1) return Slot.Puttr;
            if (i % 3 == 2 && _partners.Count > 0) return Slot.Partner;
            return Slot.Host;
        }

        /// <summary>The texture for the i-th board of a row of count (see SlotAt). Partners take turns along the row.</summary>
        public static Texture2D Panel(int i, int count)
        {
            switch (SlotAt(i, count))
            {
                case Slot.Host: return _logo;
                case Slot.Partner: return _partners[(i / 3) % _partners.Count];
                default: return Puttr;
            }
        }

        /// <summary>Width over height of a logo, 4 when there is none (the lockup's rough shape).</summary>
        public static float Aspect(Texture2D logo) => logo == null || logo.height == 0 ? 4f : logo.width / (float)logo.height;

        /// <summary>A size no wider than width and no taller than tall for this logo: a host's logo can be any
        /// shape, and must not outgrow the board the Puttr lockup was drawn for.</summary>
        public static Vector2 Fit(Texture2D logo, float width, float tall)
        {
            float a = Aspect(logo), h = width / a;
            return h <= tall ? new Vector2(width, h) : new Vector2(tall * a, tall);
        }

        /// <summary>The size a scene gives a logo it used to draw width wide: Puttr's lockup comes out exactly as it
        /// always did, and a taller logo is held to 1.6 times the lockup's height.</summary>
        public static Vector2 Size(Texture2D logo, float width) => Fit(logo, width, width / Aspect(Puttr) * 1.6f);

        /// <summary>Dress the worlds in a host's kit. logo null leaves the Puttr logos and applies the colours only.</summary>
        public static void Apply(Texture2D logo, IList<Texture2D> partners, Color primary, Color secondary, string title = "")
        {
            _logo = logo;
            _partners.Clear();
            if (partners != null) foreach (Texture2D p in partners) if (p != null && _partners.Count < MaxPartners) _partners.Add(p);
            _primary = Opaque(primary);
            _secondary = Opaque(secondary);
            Title = title ?? "";
            Active = true;
            Bump();
        }

        /// <summary>Back to Puttr.</summary>
        public static void Clear()
        {
            if (!Active) return;
            _logo = null;
            _partners.Clear();
            Title = "";
            Active = false;
            Bump();
        }

        public const int MaxPartners = 5;

        static void Bump() { Version++; Changed?.Invoke(); }
        static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        /// <summary>"#E2BD6B" or "E2BD6B" to a colour; fallback when it is not one.</summary>
        public static Color Parse(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            string h = hex.Trim().TrimStart('#');
            if (h.Length == 3) h = "" + h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
            if (h.Length != 6 || !int.TryParse(h, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int rgb)) return fallback;
            return Make.Hex(rgb);
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
