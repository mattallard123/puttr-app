// The console itself, modelled on the Puttr V2 product shots and renders:
// one sheet of matte black powder-coated aluminium folded over the top with
// big round corners, its thin edge showing at the front; inner side panels set
// back from that edge; a header across the top of the opening with the two
// camera clips on it; the camera housing and the light strip under the lid;
// on the lid a recessed window slot and the mark with PUTTR under it, etched
// a shade lighter than the paint; and the ball exit on the right-hand side.
//
// Only what you see is built here. The colliders the ball meets are made in
// World.BuildRoom exactly where they always were.
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed partial class World
    {
        /// <summary>Where the console's front edge is drawn (its colliders keep their old place, BoxFrontZ).</summary>
        const float ShellFrontZ = -11f;
        const float ShellSheet = 0.12f;       // the sheet's thickness, seen at the front edge
        const float ShellRadius = 1.1f;       // the fold over the top
        const float HeaderBottom = 12.6f;     // kept high: the camera passes under it on its way to the hall
        const float LinerInset = 0.3f;        // the inner panels sit back from the front edge

        void BuildConsole(float floorY)
        {
            // Matte black powder coat: a soft sheen on the folds, no mirror.
            Material paint = Make.Lit(new Color(0.034f, 0.034f, 0.037f), 0.38f, 0.15f);
            Material inner = Make.Lit(new Color(0.022f, 0.023f, 0.025f), 0.3f, 0.1f);
            Material trim = Make.Lit(new Color(0.07f, 0.072f, 0.076f), 0.62f, 0.6f);
            Material glass = Make.Lit(new Color(0.006f, 0.007f, 0.008f), 0.93f, 0f);

            // The back of the shell runs on into the black wall, so its rounded
            // back edge never shows a seam against it.
            float backZ = WallZ + 0.6f;
            float shellLen = backZ - ShellFrontZ;
            GameObject shell = Make.MeshObject("Console shell", Root, Wrap(BoxOuter, floorY, BoxTop, ShellRadius, ShellSheet, ShellFrontZ, backZ, 10), paint);

            // Inside: side panels and a ceiling, set back a little from the edge,
            // and a strip at the front closing the gap between them and the shell.
            float wallIn = BoxInner - 0.06f;
            float ceiling = BoxTop - ShellSheet;
            float linerZ0 = ShellFrontZ + LinerInset, linerLen = backZ - linerZ0;
            for (int side = -1; side <= 1; side += 2)
            {
                Make.Box("Console liner", Root, new Vector3(side * (wallIn + 0.03f), (floorY + ceiling) * 0.5f, linerZ0 + linerLen * 0.5f), new Vector3(0.06f, ceiling - floorY, linerLen), inner, null, false);
                Make.MeshObject("Console flange", Root, Flange(side, wallIn, BoxOuter, floorY, BoxTop, ShellRadius, ShellSheet, linerZ0, 10), inner);
            }
            Make.Box("Console ceiling", Root, new Vector3(0, ceiling - 0.04f, linerZ0 + linerLen * 0.5f), new Vector3(wallIn * 2f, 0.08f, linerLen), inner, null, false);

            // The header across the top of the opening, with the two clips on it.
            float headerH = ceiling - 0.08f - HeaderBottom;
            float headerZ = ShellFrontZ + 0.22f;
            Make.Panel("Console header", Root, new Vector3(0, HeaderBottom + headerH * 0.5f, headerZ), new Vector3(wallIn * 2f, headerH, 0.16f), 0.05f, paint);
            for (int side = -1; side <= 1; side += 2)
                Make.Panel("Console clip", Root, new Vector3(side * 6.2f, HeaderBottom + 0.32f, headerZ - 0.13f), new Vector3(0.9f, 0.42f, 0.12f), 0.05f, trim);

            // Under the lid: the camera housing just behind the header, and the
            // light strip further back over the hole.
            Make.Panel("Console camera", Root, new Vector3(0, ceiling - 0.38f, headerZ + 1.6f), new Vector3(7.5f, 0.6f, 1.6f), 0.12f, paint);
            Make.Box("Console lens", Root, new Vector3(0, ceiling - 0.69f, headerZ + 1.6f), new Vector3(1.4f, 0.03f, 0.7f), glass, null, false);
            Make.Box("Light bar", Root, new Vector3(0, ceiling - 0.13f, 0.5f), new Vector3(BoxInner * 2 - 3f, 0.08f, 0.9f), Make.Emissive(Color.black, new Color(0.82f, 0.95f, 1f) * 4.5f), null, false);

            // The lid: the window slot, sunk in a bezel, and the logo in front of it.
            float slotZ = WallZ - shellLen * 0.42f;
            Make.Panel("Console slot bezel", Root, new Vector3(0, BoxTop + 0.004f, slotZ), new Vector3(14.6f, 0.04f, 1.8f), 0.02f, trim);
            Make.Box("Console slot glass", Root, new Vector3(0, BoxTop + 0.03f, slotZ), new Vector3(14.0f, 0.01f, 1.25f), glass, null, false);
            var lidLogo = Resources.Load<Texture2D>("PuttrArcade/Console/LidLogo");
            if (lidLogo == null) lidLogo = Resources.Load<Texture2D>("PuttrArcade/LogoMarkWhite");
            if (lidLogo != null)
            {
                const float lidW = 4.6f;
                GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
                mark.name = "Console logo";
                Make.Kill(mark.GetComponent<Collider>());
                mark.transform.SetParent(Root, false);
                mark.transform.localPosition = new Vector3(0, BoxTop + 0.02f, slotZ - 0.9f - lidW * 0.5f);
                mark.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                mark.transform.localScale = new Vector3(lidW, lidW * lidLogo.height / lidLogo.width, 1f);
                // Etched, so a shade lighter than the paint and a touch warm.
                mark.GetComponent<MeshRenderer>().sharedMaterial = Make.Transparent(new Color(0.3f, 0.285f, 0.26f, 0.9f), 0.5f, lidLogo);
            }

            // The ball exit, low on the right-hand side near the back.
            Quaternion facingRight = Quaternion.Euler(0f, 0f, -90f);
            Vector3 exit = new Vector3(BoxOuter + 0.012f, floorY + 1.7f, WallZ - 2.6f);
            Make.MeshObject("Console exit rim", Root, Make.Ring(1.05f, 1.3f, 40), Make.Lit(new Color(0.13f, 0.13f, 0.14f), 0.55f, 0.4f)).transform.SetPositionAndRotation(exit, facingRight);
            Make.MeshObject("Console exit", Root, Make.Disc(1.06f, 40), glass).transform.SetPositionAndRotation(exit + Vector3.right * 0.004f, facingRight);

            // The black wall cannot cover the corners the fold rounds off, so
            // they get a scrap of black of their own.
            Material black = Make.Glow(new Color(0f, 0f, 0f, 1f), false);
            Make.MeshObject("Black corners", Root, CornerFill(BoxOuter, BoxTop, ShellRadius, 10, WallZ - 0.05f), black);
            shell.layer = ShellLayer;
        }

        /// <summary>
        /// One sheet folded over the top: a U in X/Y, square at the bottom, rounded
        /// over the top, extruded along Z. Smooth on the folds, sharp at the ends.
        /// </summary>
        static Mesh Wrap(float halfW, float y0, float y1, float radius, float thick, float z0, float z1, int seg)
        {
            var outer = new List<Vector2>(); var inner = new List<Vector2>(); var norm = new List<Vector2>();
            float cy = y1 - radius;
            void Point(float x, float y, Vector2 n)
            {
                outer.Add(new Vector2(x, y));
                inner.Add(new Vector2(x, y) - n * thick);
                norm.Add(n);
            }
            Point(-halfW, y0, Vector2.left);
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * (1f - 0.5f * i / seg);
                var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Point(-halfW + radius + n.x * radius, cy + n.y * radius, n);
            }
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * (0.5f - 0.5f * i / seg);
                var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Point(halfW - radius + n.x * radius, cy + n.y * radius, n);
            }
            Point(halfW, y0, Vector2.right);

            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var tris = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
            {
                int s = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                normals.Add(na); normals.Add(nb); normals.Add(nc); normals.Add(nd);
                // Unity: a triangle faces the way of Cross(v1 - v0, v2 - v0).
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), na + nb + nc + nd) < 0f;
                if (!flip) { tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3); }
                else { tris.Add(s); tris.Add(s + 2); tris.Add(s + 1); tris.Add(s); tris.Add(s + 3); tris.Add(s + 2); }
            }
            Vector3 P(Vector2 p, float z) => new Vector3(p.x, p.y, z);
            Vector3 N(Vector2 n) => new Vector3(n.x, n.y, 0f);
            for (int k = 0; k < outer.Count - 1; k++)
            {
                Vector3 na = N(norm[k]), nb = N(norm[k + 1]);
                Quad(P(outer[k], z0), P(outer[k + 1], z0), P(outer[k + 1], z1), P(outer[k], z1), na, nb, nb, na);
                Quad(P(inner[k], z0), P(inner[k + 1], z0), P(inner[k + 1], z1), P(inner[k], z1), -na, -nb, -nb, -na);
                Quad(P(outer[k], z0), P(outer[k + 1], z0), P(inner[k + 1], z0), P(inner[k], z0), Vector3.back, Vector3.back, Vector3.back, Vector3.back);
                Quad(P(outer[k], z1), P(outer[k + 1], z1), P(inner[k + 1], z1), P(inner[k], z1), Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward);
            }
            foreach (int k in new[] { 0, outer.Count - 1 })
                Quad(P(outer[k], z0), P(inner[k], z0), P(inner[k], z1), P(outer[k], z1), Vector3.down, Vector3.down, Vector3.down, Vector3.down);

            var mesh = new Mesh { name = "Console shell" };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, new Vector2[verts.Count]);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The front edge of one side's cavity, between the inner panel (x = inX)
        /// and the sheet, up into the fold: flat at z, facing the front.
        /// </summary>
        static Mesh Flange(int side, float inX, float halfW, float y0, float y1, float radius, float thick, float z, int seg)
        {
            float r = radius - thick, cx = halfW - radius, cy = y1 - radius, sheetIn = halfW - thick;
            var pts = new List<Vector2> { new Vector2(inX, y0), new Vector2(sheetIn, y0) };
            float aEnd = Mathf.Acos(Mathf.Clamp((inX - cx) / r, -1f, 1f));
            for (int i = 0; i <= seg; i++)
            {
                float a = aEnd * i / seg;
                pts.Add(new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r));
            }
            var verts = new List<Vector3>(); var tris = new List<int>();
            foreach (Vector2 p in pts) verts.Add(new Vector3(side * p.x, p.y, z));
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 a = verts[0], b = verts[i], c = verts[i + 1];
                if (Vector3.Cross(b - a, c - a).z < 0f) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
                else { tris.Add(0); tris.Add(i + 1); tris.Add(i); }
            }
            var mesh = new Mesh { name = "Flange" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, new Vector2[verts.Count]);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The two scraps outside the rounded top corners, flat at z, from each corner to its arc.</summary>
        static Mesh CornerFill(float halfW, float top, float radius, int seg, float z)
        {
            var verts = new List<Vector3>(); var tris = new List<int>();
            for (int side = -1; side <= 1; side += 2)
            {
                int s = verts.Count;
                verts.Add(new Vector3(side * halfW, top, z));
                Vector2 c = new Vector2(side * (halfW - radius), top - radius);
                for (int i = 0; i <= seg; i++)
                {
                    float a = Mathf.PI * 0.5f * i / seg;
                    verts.Add(new Vector3(c.x + side * Mathf.Cos(a) * radius, c.y + Mathf.Sin(a) * radius, z));
                }
                for (int i = 0; i < seg; i++) { tris.Add(s); tris.Add(s + 1 + i); tris.Add(s + 2 + i); }
            }
            var mesh = new Mesh { name = "Corner fill" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, new Vector2[verts.Count]);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
