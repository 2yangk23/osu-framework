﻿//Copyright (c) 2007-2016 ppy Pty Ltd <contact@ppy.sh>.
//Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Drawables;
using osu.Framework.Input;
using OpenTK;

namespace osu.Framework.Graphics.Cursor
{
    public class CursorContainer : LargeContainer
    {
        private Cursor cursor;

        public CursorContainer()
        {
            Add(cursor = new Cursor());
        }

        protected override bool OnMouseMove(InputState state)
        {
            cursor.Position = GetLocalPosition(state.Mouse.Position);
            return base.OnMouseMove(state);
        }

        class Cursor : Box
        {
            public Cursor()
            {
                Size = new Vector2(5, 5);
                Origin = Anchor.Centre;
            }
        }
    }
}
