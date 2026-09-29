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
    /// <summary>my-rekkr dev4 TicCmd extension flags (never recorded in demos).</summary>
    public static class TicCmdExt
    {
        public const byte Jump = 1;        // jump if on the ground
        public const byte NoAutoAim = 2;   // shots follow the view pitch instead of vanilla autoaim
        public const int MaxPitch = 80;
    }

    public sealed class TicCmd
    {
        private sbyte forwardMove;
        private sbyte sideMove;
        private short angleTurn;
        private byte buttons;
        // my-rekkr dev4: extensions that demos never set (0 = vanilla behaviour).
        private short lookPitch;   // free-look pitch in 200-line screen units (-80..80), up = positive
        private byte ext;          // TicCmdExt flags (jump, no autoaim)

        public void Clear()
        {
            forwardMove = 0;
            sideMove = 0;
            angleTurn = 0;
            buttons = 0;
            lookPitch = 0;
            ext = 0;
        }

        public void CopyFrom(TicCmd cmd)
        {
            forwardMove = cmd.forwardMove;
            sideMove = cmd.sideMove;
            angleTurn = cmd.angleTurn;
            buttons = cmd.buttons;
            lookPitch = cmd.lookPitch;
            ext = cmd.ext;
        }

        public sbyte ForwardMove
        {
            get => forwardMove;
            set => forwardMove = value;
        }

        public sbyte SideMove
        {
            get => sideMove;
            set => sideMove = value;
        }

        public short AngleTurn
        {
            get => angleTurn;
            set => angleTurn = value;
        }

        public byte Buttons
        {
            get => buttons;
            set => buttons = value;
        }

        /// <summary>my-rekkr dev4 free look: pitch in 200-line screen units (slope = pitch / 160).</summary>
        public short LookPitch
        {
            get => lookPitch;
            set => lookPitch = value;
        }

        /// <summary>my-rekkr dev4: TicCmdExt flags.</summary>
        public byte Ext
        {
            get => ext;
            set => ext = value;
        }
    }
}
