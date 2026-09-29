using System; using System.Linq; using ManagedDoom;
public static class SecInfo {
  public static void Run(GameContent content, int e, int m, int px, int py, int radius) {
    var o = new GameOptions(); o.Episode = e; o.Map = m; o.Players[0].InGame = true;
    var g = new DoomGame(content, o); g.DeferedInitNew(); g.Update(new TicCmd[]{new TicCmd(),new TicCmd(),new TicCmd(),new TicCmd()});
    var map = g.World.Map; var sky = map.SkyFlatNumber; var tex = content.Textures;
    string T(int t) => t == 0 ? "-" : tex[t].Name;
    foreach (var l in map.Lines) {
      var mx = (l.Vertex1.X.ToIntFloor() + l.Vertex2.X.ToIntFloor()) / 2; var my = (l.Vertex1.Y.ToIntFloor() + l.Vertex2.Y.ToIntFloor()) / 2;
      if (Math.Abs(mx - px) > radius || Math.Abs(my - py) > radius) continue;
      var f = l.FrontSector; var b = l.BackSector;
      string info = b == null ? $"1s mid={T(l.FrontSide.MiddleTexture)}" :
        $"2s F[{f.FloorHeight.ToIntFloor()}..{f.CeilingHeight.ToIntFloor()} top={T(l.FrontSide.TopTexture)} bot={T(l.FrontSide.BottomTexture)}] B[{b.FloorHeight.ToIntFloor()}..{b.CeilingHeight.ToIntFloor()} top={T(l.BackSide.TopTexture)} bot={T(l.BackSide.BottomTexture)}]";
      Console.WriteLine($"line {Array.IndexOf(map.Lines, l)} ({l.Vertex1.X.ToIntFloor()},{l.Vertex1.Y.ToIntFloor()})-({l.Vertex2.X.ToIntFloor()},{l.Vertex2.Y.ToIntFloor()}) special={(int)l.Special} tag={l.Tag} {info}");
    }
  }
}
