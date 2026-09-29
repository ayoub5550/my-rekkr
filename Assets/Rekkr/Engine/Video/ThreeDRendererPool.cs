// my-rekkr dev3 stage 4 — multithreaded 3D view: the window is split into vertical strips, each
// rendered by its own ThreeDRenderer instance (own clip lists, openings, drawsegs, vissprites)
// into the shared frame buffer. Every instance walks the whole BSP but its solid-seg clip list
// starts closed outside its strip, and sprites/weapon are clamped to it, so each pixel is
// computed exactly as by a single renderer. Long-lived worker threads pull strips from a counter
// (load balancing); the calling thread works too. Threads = 1 is the original code path.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Threading;

namespace ManagedDoom.Video
{
    public sealed class ThreeDRendererPool : IDisposable
    {
        /// <summary>Render threads (including the caller). 0 = auto: min(4, cores - 1).</summary>
        public static int Threads = 0;

        public static int AutoThreads => Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1));

        private readonly ThreeDRenderer[] strips;
        private readonly Thread[] workers;
        private readonly ManualResetEventSlim[] go;
        private readonly CountdownEvent done;
        private volatile bool quit;
        private int next;
        private Player player;
        private Fixed frameFrac;
        private Angle? localViewTurn;
        private Exception error;
        private readonly DrawScreen screen;
        private Action<int> job;
        private int jobCount;
        private readonly Action<int> renderStrip;

        public int ThreadCount => workers.Length + 1;
        public int StripCount => strips.Length;

        public ThreeDRendererPool(GameContent content, DrawScreen screen, int windowSize)
        {
            this.screen = screen;
            var threads = Threads > 0 ? Threads : AutoThreads;
            var count = threads == 1 ? 1 : threads * 2;
            strips = new ThreeDRenderer[count];
            for (var i = 0; i < count; i++) strips[i] = new ThreeDRenderer(content, screen, windowSize);
            workers = new Thread[threads - 1];
            go = new ManualResetEventSlim[workers.Length];
            done = new CountdownEvent(1);
            for (var i = 0; i < workers.Length; i++)
            {
                var k = i;
                go[k] = new ManualResetEventSlim(false, 2000);
                workers[k] = new Thread(() => WorkerLoop(k)) { IsBackground = true, Name = "REKKR render " + (k + 1) };
                workers[k].Start();
            }
            renderStrip = i => strips[i].Render(player, frameFrac, localViewTurn);
            LayoutStrips();
        }

        private void LayoutStrips()
        {
            // Strips are in window coordinates; the window width depends on the screen size setting.
            var width = strips[0].WindowWidth;
            for (var i = 0; i < strips.Length; i++)
            {
                if (strips.Length == 1) { strips[i].SetStrip(0, int.MaxValue, true); continue; }
                var x0 = width * i / strips.Length;
                var x1 = i == strips.Length - 1 ? int.MaxValue : width * (i + 1) / strips.Length;
                strips[i].SetStrip(x0, x1, i == 0);
            }
        }

        public int WindowSize
        {
            get => strips[0].WindowSize;
            set { foreach (var s in strips) s.WindowSize = value; LayoutStrips(); }
        }

        public void FillBackground(int x, int y, int width, int height) => strips[0].FillBackground(x, y, width, height);

        public void Render(Player player, Fixed frameFrac, Angle? localViewTurn)
        {
            if (strips.Length == 1) { strips[0].Render(player, frameFrac, localViewTurn); return; }
            this.player = player; this.frameFrac = frameFrac; this.localViewTurn = localViewTurn;
            Run(strips.Length, renderStrip);
        }

        /// <summary>Runs job(0..count-1) on the workers + caller and waits. With one thread it
        /// just loops. Pass a cached delegate (no per-frame allocation).</summary>
        public void Run(int count, Action<int> job)
        {
            if (workers.Length == 0) { for (var i = 0; i < count; i++) job(i); return; }
            this.job = job; jobCount = count;
            error = null;
            next = -1;
            done.Reset(workers.Length + 1);
            for (var i = 0; i < go.Length; i++) go[i].Set();
            Work();
            done.Wait();
            if (error != null) throw new InvalidOperationException("render worker failed", error);
        }

        private void Work()
        {
            try
            {
                int i;
                while ((i = Interlocked.Increment(ref next)) < jobCount)
                {
                    job(i);
                }
            }
            catch (Exception e) { error = e; next = jobCount; }
            finally { done.Signal(); }
        }

        private void WorkerLoop(int k)
        {
            while (true)
            {
                go[k].Wait();
                go[k].Reset();
                if (quit) return;
                Work();
            }
        }

        public void Dispose()
        {
            quit = true;
            foreach (var g in go) g.Set();
        }
    }
}
