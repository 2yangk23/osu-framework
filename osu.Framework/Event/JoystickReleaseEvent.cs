﻿// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using osu.Framework.Input;
using osu.Framework.Input.States;

namespace osu.Framework.Event
{
    public class JoystickReleaseEvent : JoystickButtonEvent
    {
        public JoystickReleaseEvent(InputState state, JoystickButton button)
            : base(state, button)
        {
        }
    }
}
