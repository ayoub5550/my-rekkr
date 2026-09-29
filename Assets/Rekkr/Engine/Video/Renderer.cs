//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//



using System;
using System.Runtime.InteropServices;

namespace ManagedDoom.Video
{
    public sealed class Renderer
    {
        private static double[] gammaCorrectionParameters = new double[]
        {
            1.00,
            0.95,
            0.90,
            0.85,
            0.80,
            0.75,
            0.70,
            0.65,
            0.60,
            0.55,
            0.50
        };

        private Config config;

        private Palette palette;

        private DrawScreen screen;

        private MenuRenderer menu;
        private ThreeDRendererPool threeD;   // my-rekkr dev3: parallel strips (1 thread = original)
        private StatusBarRenderer statusBar;
        private IntermissionRenderer intermission;
        private OpeningSequenceRenderer openingSequence;
        private AutoMapRenderer autoMap;
        private FinaleRenderer finale;

        private Patch pause;

        private int wipeBandWidth;
        private int wipeBandCount;
        private int wipeHeight;
        private byte[] wipeBuffer;

        public Renderer(Config config, GameContent content) : this(config, content, 0)
        {
        }

        /// <summary>my-rekkr: <paramref name="wideWidth"/> &gt; 0 requests a widescreen frame
        /// (width in 640x400 pixels; 4:3 is 640). 2D screens stay centred, the 3D view is Hor+.</summary>
        public Renderer(Config config, GameContent content, int wideWidth)
        {
            this.config = config;

            palette = content.Palette;

            if (config.video_highresolution)
            {
                var w = Math.Max(640, wideWidth & ~1);
                screen = new DrawScreen(content.Wad, w, 400);
            }
            else
            {
                var w = Math.Max(320, (wideWidth / 2) & ~1);
                screen = new DrawScreen(content.Wad, w, 200);
            }

            config.video_gamescreensize = Math.Clamp(config.video_gamescreensize, 0, MaxWindowSize);
            config.video_gammacorrection = Math.Clamp(config.video_gammacorrection, 0, MaxGammaCorrectionLevel);

            menu = new MenuRenderer(content.Wad, screen);
            threeD = new ThreeDRendererPool(content, screen, config.video_gamescreensize);
            statusBar = new StatusBarRenderer(content.Wad, screen);
            intermission = new IntermissionRenderer(content.Wad, screen);
            openingSequence = new OpeningSequenceRenderer(content.Wad, screen, this);
            autoMap = new AutoMapRenderer(content.Wad, screen);
            finale = new FinaleRenderer(content, screen);

            pause = Patch.FromWad(content.Wad, "M_PAUSE");

            var scale = screen.Height / 200;
            wipeBandWidth = 2 * scale;
            wipeBandCount = screen.Width / wipeBandWidth + 1;
            wipeHeight = screen.Height / scale;
            wipeBuffer = new byte[screen.Data.Length];

            palette.ResetColors(gammaCorrectionParameters[config.video_gammacorrection]);
        }

        private void ClearIfWide()
        {
            if (screen.CenterOffset > 0)
            {
                screen.OffsetX = 0;
                screen.FillRect(0, 0, screen.Width, screen.Height, 0);
            }
        }

        /// <summary>my-rekkr test hook: the palette-index frame (column-major x*height+y) for HOM scans.</summary>
        public byte[] ScreenDataForTest => screen.Data;

        public void RenderDoom(Doom doom, Fixed frameFrac)
        {
            screen.OffsetX = 0;
            if (doom.State == DoomState.Opening)
            {
                if (doom.Opening.State != OpeningSequenceState.Demo)
                {
                    ClearIfWide();
                    screen.OffsetX = screen.CenterOffset;
                }
                openingSequence.Render(doom.Opening, frameFrac);
                screen.OffsetX = 0;
            }
            else if (doom.State == DoomState.DemoPlayback)
            {
                RenderGame(doom.DemoPlayback.Game, frameFrac);
            }
            else if (doom.State == DoomState.Game)
            {
                RenderGame(doom.Game, frameFrac);
            }

            if (!doom.Menu.Active)
            {
                if (doom.State == DoomState.Game &&
                    doom.Game.State == GameState.Level &&
                    doom.Game.Paused)
                {
                    var scale = screen.Height / 200;
                    screen.DrawPatch(
                        pause,
                        (screen.Width - scale * pause.Width) / 2,
                        4 * scale,
                        scale);
                }
            }
        }

        public void RenderMenu(Doom doom)
        {
            if (doom.Menu.Active)
            {
                screen.OffsetX = screen.CenterOffset;
                menu.Render(doom.Menu);
                screen.OffsetX = 0;
            }
        }

        private void RenderStatusBar(Player player)
        {
            var scale = screen.Scale;
            var off = screen.CenterOffset;
            if (off > 0)
            {
                var y = screen.Height - StatusBarRenderer.Height * scale;
                screen.OffsetX = 0;
                threeD.FillBackground(0, y, off, StatusBarRenderer.Height * scale);
                threeD.FillBackground(off + screen.BaseWidth, y, screen.Width - off - screen.BaseWidth, StatusBarRenderer.Height * scale);
            }
            screen.OffsetX = off;
            statusBar.Render(player, true);
            screen.OffsetX = 0;
        }

        /// <summary>my-rekkr dev3 "smooth look": turn (BAM) the player has input since the last tic.
        /// When set, the 3D view uses the current tic angle + this offset instead of the interpolated
        /// angle, so touch/gyro look is applied every rendered frame (the sim is unchanged). The app
        /// sets it only for a live game (never for demos).</summary>
        public Angle? LocalViewTurn;

        public int RenderThreads => threeD.ThreadCount;

        /// <summary>Stops the render worker threads (call when the renderer is replaced).</summary>
        public void Dispose() => threeD.Dispose();

        public void RenderGame(DoomGame game, Fixed frameFrac)
        {
            if (game.Paused)
            {
                frameFrac = Fixed.One;
            }

            if (game.State == GameState.Level)
            {
                var consolePlayer = game.World.ConsolePlayer;
                var displayPlayer = game.World.DisplayPlayer;

                screen.OffsetX = 0;
                if (game.World.AutoMap.Visible)
                {
                    autoMap.Render(consolePlayer);
                    RenderStatusBar(consolePlayer);
                }
                else
                {
                    threeD.Render(displayPlayer, frameFrac,
                        displayPlayer == consolePlayer && !game.Paused ? LocalViewTurn : null);
                    if (threeD.WindowSize < 8)
                    {
                        RenderStatusBar(consolePlayer);
                    }
                    else if (threeD.WindowSize == ThreeDRenderer.MaxScreenSize)
                    {
                        // my-rekkr: compact transparent HUD instead of the floating status-bar numbers.
                        screen.OffsetX = screen.CenterOffset;
                        statusBar.RenderFullscreenHud(consolePlayer);
                    }
                }

                if (config.video_displaymessage || ReferenceEquals(consolePlayer.Message, (string)DoomInfo.Strings.MSGOFF))
                {
                    if (consolePlayer.MessageTime > 0)
                    {
                        var scale = screen.Height / 200;
                        screen.OffsetX = screen.CenterOffset;
                        screen.DrawText(consolePlayer.Message, 0, 7 * scale, scale);
                    }
                }
                screen.OffsetX = 0;
            }
            else if (game.State == GameState.Intermission)
            {
                ClearIfWide();
                screen.OffsetX = screen.CenterOffset;
                intermission.Render(game.Intermission);
                screen.OffsetX = 0;
            }
            else if (game.State == GameState.Finale)
            {
                ClearIfWide();
                screen.OffsetX = screen.CenterOffset;
                finale.Render(game.Finale);
                screen.OffsetX = 0;
            }
        }

        public void Render(Doom doom, byte[] destination, Fixed frameFrac) => Render(doom, destination.AsSpan(), frameFrac);

        /// <summary>my-rekkr dev3: renders straight into any RGBA32 buffer (e.g. the texture memory).</summary>
        public void Render(Doom doom, Span<byte> destination, Fixed frameFrac)
        {
            if (doom.Wiping)
            {
                RenderWipe(doom, destination);
                return;
            }

            RenderDoom(doom, frameFrac);
            RenderMenu(doom);

            var colors = palette[0];
            if (doom.State == DoomState.Game &&
                doom.Game.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(doom.Game.World.ConsolePlayer)];
            }
            else if (doom.State == DoomState.Opening &&
                doom.Opening.State == OpeningSequenceState.Demo &&
                doom.Opening.DemoGame.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(doom.Opening.DemoGame.World.ConsolePlayer)];
            }
            else if (doom.State == DoomState.DemoPlayback &&
                doom.DemoPlayback.Game.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(doom.DemoPlayback.Game.World.ConsolePlayer)];
            }

            WriteData(colors, destination);
        }

        private void RenderWipe(Doom doom, Span<byte> destination)
        {
            RenderDoom(doom, Fixed.One);

            var wipe = doom.WipeEffect;
            var scale = screen.Height / 200;
            for (var i = 0; i < wipeBandCount - 1; i++)
            {
                var x1 = wipeBandWidth * i;
                var x2 = x1 + wipeBandWidth;
                var y1 = Math.Max(scale * wipe.Y[i], 0);
                var y2 = Math.Max(scale * wipe.Y[i + 1], 0);
                var dy = (float)(y2 - y1) / wipeBandWidth;
                for (var x = x1; x < x2; x++)
                {
                    var y = (int)MathF.Round(y1 + dy * ((x - x1) / 2 * 2));
                    var copyLength = screen.Height - y;
                    if (copyLength > 0)
                    {
                        var srcPos = screen.Height * x;
                        var dstPos = screen.Height * x + y;
                        Array.Copy(wipeBuffer, srcPos, screen.Data, dstPos, copyLength);
                    }
                }
            }

            RenderMenu(doom);

            WriteData(palette[0], destination);
        }

        public void InitializeWipe()
        {
            Array.Copy(screen.Data, wipeBuffer, screen.Data.Length);
        }

        private void WriteData(uint[] colors, Span<byte> destination)
        {
            var screenData = screen.Data;
            var p = MemoryMarshal.Cast<byte, uint>(destination);
            for (var i = 0; i < p.Length; i++)
            {
                p[i] = colors[screenData[i]];
            }
        }

        private static int GetPaletteNumber(Player player)
        {
            var count = player.DamageCount;

            if (player.Powers[(int)PowerType.Strength] != 0)
            {
                // Slowly fade the berzerk out.
                var bzc = 12 - (player.Powers[(int)PowerType.Strength] >> 6);
                if (bzc > count)
                {
                    count = bzc;
                }
            }

            int palette;

            if (count != 0)
            {
                palette = (count + 7) >> 3;

                if (palette >= Palette.DamageCount)
                {
                    palette = Palette.DamageCount - 1;
                }

                palette += Palette.DamageStart;
            }
            else if (player.BonusCount != 0)
            {
                palette = (player.BonusCount + 7) >> 3;

                if (palette >= Palette.BonusCount)
                {
                    palette = Palette.BonusCount - 1;
                }

                palette += Palette.BonusStart;
            }
            else if (player.Powers[(int)PowerType.IronFeet] > 4 * 32 ||
                (player.Powers[(int)PowerType.IronFeet] & 8) != 0)
            {
                palette = Palette.IronFeet;
            }
            else
            {
                palette = 0;
            }

            return palette;
        }

        public int Width => screen.Width;
        public int CenterOffset => screen.CenterOffset;
        public int Scale => screen.Scale;
        public int Height => screen.Height;

        public int WipeBandCount => wipeBandCount;
        public int WipeHeight => wipeHeight;

        public int MaxWindowSize
        {
            get
            {
                return ThreeDRenderer.MaxScreenSize;
            }
        }

        public int WindowSize
        {
            get
            {
                return threeD.WindowSize;
            }

            set
            {
                config.video_gamescreensize = value;
                threeD.WindowSize = value;
            }
        }

        public bool DisplayMessage
        {
            get
            {
                return config.video_displaymessage;
            }

            set
            {
                config.video_displaymessage = value;
            }
        }

        public int MaxGammaCorrectionLevel
        {
            get
            {
                return gammaCorrectionParameters.Length - 1;
            }
        }

        public int GammaCorrectionLevel
        {
            get
            {
                return config.video_gammacorrection;
            }

            set
            {
                config.video_gammacorrection = value;
                palette.ResetColors(gammaCorrectionParameters[config.video_gammacorrection]);
            }
        }
    }
}
