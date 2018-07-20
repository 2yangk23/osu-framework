// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using osu.Framework.Extensions.TypeExtensions;
using osu.Framework.Input.States;
using OpenTK;

namespace osu.Framework.Event
{
    public class ScrollEvent : UIEvent
    {
        public readonly Vector2 ScrollDelta;
        public readonly bool IsPrecise;

        public ScrollEvent(InputState state, Vector2 scrollDelta, bool isPrecise = false)
            : base(state)
        {
            ScrollDelta = scrollDelta;
            IsPrecise = isPrecise;
        }

        public override string ToString() => $"{GetType().ReadableName()}({ScrollDelta}, {IsPrecise})";
    }
}
