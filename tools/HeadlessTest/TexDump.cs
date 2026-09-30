// dev6 triage: writes wall textures (composite) as PNG + average brightness.  texdump <NAME,NAME,...>
using System; using System.IO; using ManagedDoom;
public static class TexDump {
  public static int Run(GameContent content, string names, string outDir) {
    var pal = content.Wad.ReadLump("PLAYPAL");
    foreach (var nm in names.Split(',')) {
      var t = content.Textures[content.Textures.GetNumber(nm)];
      var p = t.Composite; int w = p.Width, h = p.Height; var rgb = new byte[w * h * 3]; double sum = 0; int n = 0;
      for (int x = 0; x < w; x++) foreach (var c in p.Columns[x])
        for (int k = 0; k < c.Length; k++) { int y = c.TopDelta + k; if (y >= h) continue; var ix = c.Data[c.Offset + k];
          int d = 3 * (y * w + x); rgb[d] = pal[3 * ix]; rgb[d + 1] = pal[3 * ix + 1]; rgb[d + 2] = pal[3 * ix + 2];
          sum += 0.3 * pal[3 * ix] + 0.59 * pal[3 * ix + 1] + 0.11 * pal[3 * ix + 2]; n++; }
      Png.Write(Path.Combine(outDir, $"tex_{nm}.png"), w, h, rgb);
      Console.WriteLine($"tex {nm} {w}x{h} filled={n * 100 / (w * h)}% avg_luma={(n > 0 ? sum / n : 0):F1}");
    }
    return 0;
  }
}
