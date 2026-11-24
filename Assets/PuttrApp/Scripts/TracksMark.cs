// The Puttr mark as a solid: its outline (the logo picture's alpha) cut out of plate, with a front, a back and the cut
// edges between them. Tracks stands one either side of every tee, in steel.
//
// The mesh is one unit wide and as tall as the picture's shape makes it, its foot on y = 0, its faces at z = -+depth/2
// (the front faces -z). Built once: each row of the picture becomes a few strips (one per run of solid pixels), so the
// whole mark is about a thousand quads, and the steps of a row are far too small to see.
using System.Collections.Generic;
using UnityEngine;

namespace PuttrApp
{
    internal static class TracksMark
    {
        const int Rows = 220;
        static Mesh _mesh;
        static Material _steel;
        /// <summary>Steel: under the realistic look it mirrors the course's sky and turf; under the painted look, plain lit metal.</summary>
        public static Material Steel
        {
            get
            {
                if (_steel != null) return _steel;
                Shader mirror = TracksReal.On ? Shader.Find("PuttrApp/TracksSteel") : null;
                _steel = mirror != null && mirror.isSupported ? new Material(mirror) { name = "Tracks steel" } : PuttrArcade.Make.Lit(new Color(0.80f, 0.82f, 0.85f), 0.62f, 0.75f);
                return _steel;
            }
        }

        public static Mesh Mesh(float depth = 0.09f)
        {
            if (_mesh != null) return _mesh;
            var mark = Resources.Load<Texture2D>("PuttrArcade/LogoMark");
            if (mark == null) return null;
            // The picture is not kept readable: draw it into a small target and read that.
            int h = Rows, w = Mathf.RoundToInt(Rows * mark.width / (float)mark.height);
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32), was = RenderTexture.active;
            Graphics.Blit(mark, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = copy.GetPixels32();
            Object.Destroy(copy);
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && px[y * w + x].a > 127;

            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var tris = new List<int>();
            float cell = 1f / w, z = depth / 2;
            Vector3 P(float x, float y, float d) => new Vector3((x - w / 2f) * cell, y * cell, d);
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                int at = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                for (int k = 0; k < 4; k++) normals.Add(n);
                tris.Add(at); tris.Add(at + 1); tris.Add(at + 2); tris.Add(at); tris.Add(at + 2); tris.Add(at + 3);
            }
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!Solid(x, y)) continue;
                    int x0 = x;
                    while (Solid(x + 1, y)) x++;
                    int x1 = x + 1;
                    Quad(P(x0, y, -z), P(x0, y + 1, -z), P(x1, y + 1, -z), P(x1, y, -z), Vector3.back);        // front
                    Quad(P(x1, y, z), P(x1, y + 1, z), P(x0, y + 1, z), P(x0, y, z), Vector3.forward);         // back
                    Quad(P(x0, y, z), P(x0, y + 1, z), P(x0, y + 1, -z), P(x0, y, -z), Vector3.left);          // the cut edge at each end
                    Quad(P(x1, y, -z), P(x1, y + 1, -z), P(x1, y + 1, z), P(x1, y, z), Vector3.right);
                }
                // The cut edges that face up and down: wherever this row is solid and the next (or the last) is not.
                for (int side = 0; side < 2; side++)
                {
                    int other = side == 0 ? y + 1 : y - 1;
                    for (int x = 0; x < w; x++)
                    {
                        if (!Solid(x, y) || Solid(x, other)) continue;
                        int x0 = x;
                        while (Solid(x + 1, y) && !Solid(x + 1, other)) x++;
                        int x1 = x + 1;
                        if (side == 0) Quad(P(x0, y + 1, -z), P(x0, y + 1, z), P(x1, y + 1, z), P(x1, y + 1, -z), Vector3.up);
                        else Quad(P(x0, y, z), P(x0, y, -z), P(x1, y, -z), P(x1, y, z), Vector3.down);
                    }
                }
            }
            _mesh = new Mesh { name = "Puttr mark", indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            _mesh.SetVertices(verts); _mesh.SetNormals(normals); _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateBounds();
            return _mesh;
        }
    }
}
