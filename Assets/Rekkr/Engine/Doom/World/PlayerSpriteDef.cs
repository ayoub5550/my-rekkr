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

namespace ManagedDoom
{
    public sealed class PlayerSpriteDef
    {
        private MobjStateDef state;
        private int tics;
        private Fixed sx;
        private Fixed sy;

        // my-rekkr dev8: the values at the start of the current tic (render-side interpolation only;
        // not saved, never read by the simulation).
        public Fixed OldSx, OldSy;
        public MobjStateDef OldState;
        public Sprite OldSprite;

        public void UpdateFrameInterpolationInfo()
        {
            OldSx = sx; OldSy = sy; OldState = state;
            OldSprite = state != null ? state.Sprite : default;
        }

        public void Clear()
        {
            state = null;
            tics = 0;
            sx = Fixed.Zero;
            sy = Fixed.Zero;
            OldState = null;
        }

        public MobjStateDef State
        {
            get => state;
            set => state = value;
        }

        public int Tics
        {
            get => tics;
            set => tics = value;
        }

        public Fixed Sx
        {
            get => sx;
            set => sx = value;
        }

        public Fixed Sy
        {
            get => sy;
            set => sy = value;
        }
    }
}
