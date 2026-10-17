namespace BannerlordAgents.Routes
{
    /// <summary>A made-up map for the self-tests (the dashboard's and the war room's): a river north to south with one bridge, a mountain wall east to west with one pass, a wood on the plain, and eight holdings.</summary>
    public static class Samples
    {
        public static WalkMap SmallMap()
        {
            const int w = 100, h = 60;
            var g = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte t = Ground.Plain;
                    if (x >= 48 && x <= 51) t = y >= 30 && y <= 31 ? Ground.Bridge : Ground.River;   // the river, with a bridge two cells wide
                    if (y >= 45 && y <= 47 && x < 48) t = x >= 10 && x <= 13 ? Ground.Plain : Ground.Mountain;   // the wall, with a pass four wide
                    if (x >= 25 && x <= 35 && y >= 5 && y <= 15) t = Ground.Forest;
                    g[y * w + x] = t;
                }
            var meta = new MapMeta { Version = 1, Crc = "test", MinX = 0, MinY = 0, MaxX = w, MaxY = h, Cell = 1, Width = w, Height = h, SunRise = 2, SunSet = 22,
                                     Walkable = Ground.DefaultWalkable.Select(b => (int)b).ToArray() };
            var m = new WalkMap(meta, g);
            m.Places = new List<Place>
            {
                new Place { Id = "town_a", Name = "Aldburg", Kind = "town", X = 20, Y = 30, Gx = 20, Gy = 29 },
                new Place { Id = "town_b", Name = "Bree", Kind = "town", X = 80, Y = 30, Gx = 80, Gy = 29 },
                new Place { Id = "castle_c", Name = "Caradhras", Kind = "castle", X = 20, Y = 55, Gx = 20, Gy = 54 },
                new Place { Id = "castle_d", Name = "Dol Amroth", Kind = "castle", X = 80, Y = 10, Gx = 80, Gy = 11 },
                new Place { Id = "castle_e", Name = "Edoras", Kind = "castle", X = 40, Y = 25, Gx = 40, Gy = 25 },
                new Place { Id = "town_f", Name = "Fornost", Kind = "town", X = 30, Y = 52, Gx = 30, Gy = 52 },
                new Place { Id = "castle_g", Name = "Gramburg", Kind = "castle", X = 65, Y = 45, Gx = 65, Gy = 45 },
                new Place { Id = "castle_h", Name = "Helm", Kind = "castle", X = 8, Y = 20, Gx = 8, Gy = 20 }
            };
            return m;
        }
    }
}
