// dev6 triage: list the walls a horizontal ray from a point hits (textures, heights, light).
//   dotnet run -- rekkr.wad out ray <e> <m> <x> <y> <angleDeg> [hits]
using System; using System.Linq; using ManagedDoom;
public static class Ray {
  public static void Run(GameContent content, int e, int m, double px, double py, double ang, int n) {
    var o = new GameOptions(); o.Episode = e; o.Map = m; o.Players[0].InGame = true;
    var g = new DoomGame(content, o); g.DeferedInitNew(); g.Update(new TicCmd[]{new TicCmd(),new TicCmd(),new TicCmd(),new TicCmd()});
    var map = g.World.Map; var tex = content.Textures;
    string T(int t) => t == 0 ? "-" : tex[t].Name;
    double dx = Math.Cos(ang * Math.PI / 180), dy = Math.Sin(ang * Math.PI / 180);
    var hits = map.Lines.Select(l => {
      double x1 = l.Vertex1.X.ToDouble(), y1 = l.Vertex1.Y.ToDouble(), x2 = l.Vertex2.X.ToDouble(), y2 = l.Vertex2.Y.ToDouble();
      double ex = x2 - x1, ey = y2 - y1, den = dx * ey - dy * ex;
      if (Math.Abs(den) < 1e-9) return (l, -1.0);
      double t = ((x1 - px) * ey - (y1 - py) * ex) / den, s = ((x1 - px) * dy - (y1 - py) * dx) / den;
      return (s < 0 || s > 1 || t <= 0) ? (l, -1.0) : (l, t);
    }).Where(h => h.Item2 > 0).OrderBy(h => h.Item2).Take(Math.Max(n, 60));
    var start = Geometry.PointInSubsector(Fixed.FromDouble(px), Fixed.FromDouble(py), map).Sector;
    double eye = start.FloorHeight.ToDouble() + 41;
    Console.WriteLine($"start sec{Array.IndexOf(map.Sectors, start)} eye={eye}");
    foreach (var (l, t) in hits) {
      var f = l.FrontSector; var b = l.BackSector;
      string S(Sector s) => $"sec{Array.IndexOf(map.Sectors, s)} {s.FloorHeight.ToIntFloor()}..{s.CeilingHeight.ToIntFloor()} light={s.LightLevel} fl={content.Flats[s.FloorFlat].Name} ce={content.Flats[s.CeilingFlat].Name}";
      string info = b == null ? $"1s mid={T(l.FrontSide.MiddleTexture)} F[{S(f)}]" :
        $"2s F[{S(f)} top={T(l.FrontSide.TopTexture)} mid={T(l.FrontSide.MiddleTexture)} bot={T(l.FrontSide.BottomTexture)}] B[{S(b)} top={T(l.BackSide.TopTexture)} bot={T(l.BackSide.BottomTexture)}]";
      bool block = b == null || eye <= Math.Max(f.FloorHeight.ToDouble(), b.FloorHeight.ToDouble()) || eye >= Math.Min(f.CeilingHeight.ToDouble(), b.CeilingHeight.ToDouble());
      Console.WriteLine($"{(block ? "BLOCK " : "")}d={t:F0} line {Array.IndexOf(map.Lines, l)} special={(int)l.Special} tag={l.Tag} flags={(int)l.Flags} {info}");
      if (block) break;
    }
  }
}
