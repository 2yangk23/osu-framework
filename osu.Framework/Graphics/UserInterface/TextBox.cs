﻿// Copyright (c) 2007-2016 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Cached;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Drawables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Transformations;
using osu.Framework.Input;
using osu.Framework.MathUtils;
using osu.Framework.Threading;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Input;

namespace osu.Framework.Graphics.UserInterface
{
    public class TextBox : Container
    {
        private FlowContainer textFlow;
        private Box background;
        private Box cursor;
        private Container textContainer;

        public int? LengthLimit;

        public bool AllowClipboardExport => true;

        /// <summary>
        /// Should this TextBox accept arrow keys for navigation?
        /// </summary>
        public bool HandleLeftRightArrows = true;

        protected virtual Color4 BackgroundCommit => new Color4(249, 90, 255, 200);
        protected virtual Color4 BackgroundFocused => new Color4(100, 100, 100, 255);
        protected virtual Color4 BackgroundUnfocused => new Color4(100, 100, 100, 120);

        public bool ReadOnly;

        private TextInputSource textInput;

        public delegate void OnCommitHandler(TextBox sender, bool newText);

        public event OnCommitHandler OnCommit;
        public event OnCommitHandler OnChange;

        private Scheduler textUpdateScheduler = new Scheduler();

        public override void Load()
        {
            base.Load();

            Masking = true;

            Add(background = new Box()
            {
                Colour = BackgroundUnfocused,
                SizeMode = InheritMode.XY,
            });

            Add(textContainer = new Container()
            {
                SizeMode = InheritMode.XY
            });

            textFlow = new FlowContainer()
            {
                Direction = FlowDirection.HorizontalOnly,
            };

            cursor = new Box()
            {
                Size = Vector2.One,
                Colour = Color4.Transparent,
                SizeMode = InheritMode.Y,
                Alpha = 0
            };

            textContainer.Add(cursor);
            textContainer.Add(textFlow);
        }

        private void resetSelection()
        {
            selectionStart = selectionEnd = text.Length;
            cursorAndLayout.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            OnChange = null;
            OnCommit = null;

            unbindInput();

            base.Dispose(disposing);
        }

        private float textContainerPosX;

        private string textAtLastLayout = string.Empty;

        protected override void UpdateLayout()
        {
            base.UpdateLayout();

            //have to run this after children flow
            cursorAndLayout.Refresh(delegate
            {
                textUpdateScheduler.Update();

                Vector2 cursorPos = Vector2.Zero;
                if (text?.Length > 0)
                    cursorPos.X = getPositionAt(selectionLeft);

                float cursorPosEnd = getPositionAt(selectionEnd);

                float cursorWidth = 2;

                if (selectionLength > 0)
                    cursorWidth = getPositionAt(selectionRight) - cursorPos.X;

                float cursorRelativePositionInBox = (cursorPosEnd - textContainerPosX) / Width;

                //we only want to reposition the view when the cursor reaches near the extremities.
                if (cursorRelativePositionInBox < 0.1 || cursorRelativePositionInBox > 0.9)
                {
                    textContainerPosX = cursorPosEnd - Width / 2;
                }

                textContainerPosX = MathHelper.Clamp(textContainerPosX, 0, Math.Max(0, textFlow.Width - Width));

                textContainer.MoveToX(-textContainerPosX, 300, EasingTypes.OutExpo);

                if (HasFocus)
                {
                    cursor.ClearTransformations();
                    cursor.MoveTo(cursorPos, 60, EasingTypes.Out);
                    cursor.ScaleTo(new Vector2(cursorWidth, 1), 60, EasingTypes.Out);

                    if (selectionLength > 0)
                    {
                        cursor.FadeTo(0.5f, 200, EasingTypes.Out);
                        cursor.FadeColour(new Color4(249, 90, 255, 255), 200, EasingTypes.Out);
                    }
                    else
                    {
                        cursor.FadeTo(0.5f, 200, EasingTypes.Out);
                        cursor.FadeColour(Color4.White, 200, EasingTypes.Out);
                        cursor.Transforms.Add(new TransformAlpha(Clock)
                              {
                                  StartValue = 0.5f,
                                  EndValue = 0.2f,
                                  StartTime = Time,
                                  EndTime = Time + 500,
                                  Easing = EasingTypes.InOutSine,
                                  LoopCount = -1,
                              });
                    }
                }

                OnChange?.Invoke(this, textAtLastLayout != text);
                textAtLastLayout = text;

                return cursorPos;
            });
        }

        private float getPositionAt(int index)
        {
            if (index > 0)
            {
                if (index < text.Length)
                    return textFlow.Children.ElementAt(index).Position.X + textFlow.Position.X;
                else
                {
                    var d = textFlow.Children.ElementAt(index - 1);
                    return d.Position.X + d.Size.X + textFlow.Padding.X + textFlow.Position.X;
                }
            }
            else
                return 0;
        }

        private int getCharacterClosestTo(Vector2 pos)
        {
            pos = textFlow.GetLocalPosition(pos);

            int i = 0;
            foreach (Drawable d in textFlow.Children)
            {
                if (d.Position.X + d.Size.X / 2 > pos.X)
                    break;
                i++;
            }

            return i;
        }

        int selectionStart;
        int selectionEnd;

        int selectionLength => Math.Abs(selectionEnd - selectionStart);

        int selectionLeft => Math.Min(selectionStart, selectionEnd);
        int selectionRight => Math.Max(selectionStart, selectionEnd);

        Cached<Vector2> cursorAndLayout = new Cached<Vector2>();

        private void moveSelection(int offset, bool expand)
        {
            if (textInput?.ImeActive == true) return;

            int oldStart = selectionStart;
            int oldEnd = selectionEnd;

            if (expand)
                selectionEnd = MathHelper.Clamp(selectionEnd + offset, 0, text.Length);
            else
            {
                if (selectionLength > 0 && Math.Abs(offset) <= 1)
                {
                    //we don't want to move the location when "removing" an existing selection, just set the new location.
                    if (offset > 0)
                        selectionEnd = selectionStart = selectionRight;
                    else
                        selectionEnd = selectionStart = selectionLeft;
                }
                else
                    selectionEnd = selectionStart = MathHelper.Clamp((offset > 0 ? selectionRight : selectionLeft) + offset, 0, text.Length);
            }

            if (oldStart != selectionStart || oldEnd != selectionEnd)
            {
                Game.Audio.Sample.Get(@"Keyboard/key-movement")?.Play();
                cursorAndLayout.Invalidate();
            }
        }

        private bool removeCharacterOrSelection(bool sound = true)
        {
            if (text.Length == 0) return false;
            if (selectionLength == 0 && selectionLeft == 0) return false;

            int count = MathHelper.Clamp(selectionLength, 1, text.Length);
            int start = MathHelper.Clamp(selectionLength > 0 ? selectionLeft : selectionLeft - 1, 0, text.Length - count);

            if (count == 0) return false;

            if (sound)
                Game.Audio.Sample.Get(@"Keyboard/key-delete")?.Play();

            textFlow.Children.Skip(start).Take(count).ToList().ForEach(d =>
                    {
                        textFlow.Remove(d);

                        textContainer.Add(d);
                        d.FadeOut(200);
                        d.MoveToY(d.Size.Y, 200, EasingTypes.InExpo);
                        d.Expire();
                    });

            text = text.Remove(start, count);

            if (selectionLength > 0)
                selectionStart = selectionEnd = selectionLeft;
            else
                selectionStart = selectionEnd = selectionLeft - 1;

            cursorAndLayout.Invalidate();
            return true;
        }

        protected virtual Drawable AddCharacterToFlow(char c)
        {
            int i = selectionLeft;
            foreach (Drawable dd in textFlow.Children.Skip(selectionLeft).Take(text.Length - selectionLeft))
                dd.Depth = i + 1;

            Drawable ch;

            textFlow.Add(ch = new SpriteText()
            {
                Text = c.ToString(),
                TextSize = Size.Y,
                Depth = selectionLeft,
            });

            return ch;
        }

        /// <summary>
        /// Insert an arbitrary string into the text at the current position.
        /// </summary>
        /// <param name="addText"></param>
        private void insertString(string addText)
        {
            if (string.IsNullOrEmpty(addText)) return;

            foreach (char c in addText)
                addCharacter(c);
        }

        private Drawable addCharacter(char c)
        {
            if (char.IsControl(c)) return null;

            if (selectionLength > 0)
                removeCharacterOrSelection();

            if (text.Length + 1 > LengthLimit)
            {
                if (background.Alpha > 0)
                    background.FlashColour(Color4.Red, 200);
                else
                    textFlow.FlashColour(Color4.Red, 200);
                return null;
            }

            Drawable ch = AddCharacterToFlow(c);

            ch.Position = new Vector2(0, Size.Y);
            ch.MoveToY(0, 200, EasingTypes.OutExpo);

            text = text.Insert(selectionLeft, c.ToString());
            selectionStart = selectionEnd = selectionLeft + 1;

            cursorAndLayout.Invalidate();

            return ch;
        }

        private string text = string.Empty;

        public virtual string Text
        {
            get { return text; }
            set
            {
                Debug.Assert(value != null);

                if (value == text)
                    return;

                textUpdateScheduler.Add(delegate
                {
                    int startBefore = selectionStart;
                    selectionStart = selectionEnd = 0;
                    textFlow?.Clear();
                    text = string.Empty;

                    foreach (char c in value)
                        addCharacter(c);

                    selectionStart = MathHelper.Clamp(startBefore, 0, text.Length);
                }, true);

                cursorAndLayout.Invalidate();
            }
        }

        public string SelectedText => selectionLength > 0 ? Text.Substring(selectionLeft, selectionLength) : string.Empty;

        protected override bool OnKeyDown(InputState state, KeyDownEventArgs args)
        {
            if (!HasFocus)
                return false;

            if (textInput?.ImeActive == true) return true;

            switch (args.Key)
            {
                case Key.Tab:
                    return false;
                case Key.End:
                    moveSelection(text.Length, state.Keyboard.ShiftPressed);
                    return true;
                case Key.Home:
                    moveSelection(-text.Length, state.Keyboard.ShiftPressed);
                    return true;
                case Key.Left:
                {
                    if (!HandleLeftRightArrows) return false;

                    if (selectionEnd == 0) return true;

                    int amount = 1;
                    if (state.Keyboard.ControlPressed)
                    {
                        int lastSpace = text.LastIndexOf(' ', Math.Max(0, selectionEnd - 2));
                        if (lastSpace >= 0)
                            amount = selectionEnd - lastSpace - 1;
                        else
                            amount = selectionEnd;
                    }

                    moveSelection(-amount, state.Keyboard.ShiftPressed);
                }
                    return true;
                case Key.Right:
                {
                    if (!HandleLeftRightArrows) return false;

                    if (selectionEnd == text.Length) return true;

                    int amount = 1;
                    if (state.Keyboard.ControlPressed)
                    {
                        int nextSpace = text.IndexOf(' ', selectionEnd + 1);
                        if (nextSpace >= 0)
                            amount = nextSpace - selectionEnd;
                        else
                            amount = text.Length - selectionEnd;
                    }

                    moveSelection(amount, state.Keyboard.ShiftPressed);
                }

                    return true;
                case Key.Enter:
                    TriggerFocusLost(state);
                    return true;
                case Key.Delete:
                    if (selectionLength == 0)
                    {
                        if (text.Length == selectionStart)
                            return true;

                        if (state.Keyboard.ControlPressed)
                        {
                            int spacePos = selectionStart;
                            while (text[spacePos] == ' ' && spacePos < text.Length)
                                spacePos++;

                            spacePos = MathHelper.Clamp(text.IndexOf(' ', spacePos), 0, text.Length);
                            selectionEnd = spacePos;

                            if (selectionStart == 0 && spacePos == 0)
                                selectionEnd = text.Length;

                            if (selectionLength == 0)
                                return true;
                        }
                        else
                        {
                            //we're deleting in front of the cursor, so move the cursor forward once first
                            selectionStart = selectionEnd = selectionStart + 1;
                        }
                    }

                    removeCharacterOrSelection();
                    return true;
                case Key.Back:
                    if (selectionLength == 0 && state.Keyboard.ControlPressed)
                    {
                        int spacePos = selectionLeft >= 2 ? Math.Max(0, text.LastIndexOf(' ', selectionLeft - 2) + 1) : 0;
                        selectionStart = spacePos;
                    }

                    removeCharacterOrSelection();
                    return true;
            }

            if (state.Keyboard.ControlPressed)
            {
                //handling of function keys
                switch (args.Key)
                {
                    case Key.A:
                        selectionStart = 0;
                        selectionEnd = text.Length;
                        cursorAndLayout.Invalidate();
                        return true;
                    case Key.C:
                        if (string.IsNullOrEmpty(SelectedText) || !AllowClipboardExport) return true;
                        //System.Windows.Forms.Clipboard.SetText(SelectedText);
                        return true;
                    case Key.X:
                        if (string.IsNullOrEmpty(SelectedText)) return true;

                        //if (AllowClipboardExport)
                        //    System.Windows.Forms.Clipboard.SetText(SelectedText);
                        removeCharacterOrSelection();
                        return true;
                    case Key.V:
                        //the text is pasted into the hidden textbox, so we don't need any direct clipboard interaction here.
                        insertString(textInput?.GetPendingText());
                        return true;
                }

                return false;
            }

            string str = textInput?.GetPendingText();
            if (!string.IsNullOrEmpty(str))
            {
                if (state.Keyboard.ShiftPressed)
                    Game.Audio.Sample.Get(@"Keyboard/key-caps")?.Play();
                else
                    Game.Audio.Sample.Get($@"Keyboard/key-press-{RNG.Next(1, 5)}")?.Play();
                insertString(str);

                return true;
            }

            return false;
        }

        protected override bool OnDrag(InputState state)
        {
            //if (textInput?.ImeActive == true) return true;

            if (text.Length == 0) return true;

            selectionEnd = getCharacterClosestTo(state.Mouse.NativePosition);
            if (selectionLength > 0)
                TriggerFocus();

            cursorAndLayout.Invalidate();
            return true;
        }

        protected override bool OnDragStart(InputState state)
        {
            //need to handle this so we get onDrag events.
            return true;
        }

        protected override bool OnDoubleClick(InputState state)
        {
            if (textInput?.ImeActive == true) return true;

            if (text.Length == 0) return true;

            int hover = Math.Min(text.Length - 1, getCharacterClosestTo(state.Mouse.NativePosition));

            int lastSeparator = findSeparatorIndex(text, hover, -1);
            int nextSeparator = findSeparatorIndex(text, hover, 1);

            selectionStart = lastSeparator >= 0 ? lastSeparator + 1 : 0;
            selectionEnd = nextSeparator >= 0 ? nextSeparator : text.Length;
            cursorAndLayout.Invalidate();
            return true;
        }

        private int findSeparatorIndex(string input, int searchPos, int direction)
        {
            if (char.IsLetterOrDigit(input[searchPos]))
            {
                for (int i = searchPos; i >= 0 && i < input.Length; i += direction)
                {
                    if (!char.IsLetterOrDigit(input[i]))
                        return i;
                }
            }
            else
            {
                for (int i = searchPos; i >= 0 && i < input.Length; i += direction)
                {
                    if (char.IsSeparator(input[i]))
                        return i;
                }
            }

            return -1;
        }

        protected override bool OnMouseDown(InputState state, MouseDownEventArgs args)
        {
            if (textInput?.ImeActive == true) return true;

            selectionStart = selectionEnd = getCharacterClosestTo(state.Mouse.NativePosition);

            cursorAndLayout.Invalidate();

            return true;
        }

        protected override void OnFocusLost(InputState state)
        {
            unbindInput();

            cursor.ClearTransformations();
            cursor.FadeOut(200);

            if (state.Keyboard.Keys.Contains(Key.Enter))
            {
                background.Colour = BackgroundUnfocused;
                background.ClearTransformations();
                background.FlashColour(BackgroundCommit, 400);

                Game.Audio.Sample.Get(@"Keyboard/key-confirm")?.Play();
                OnCommit?.Invoke(this, true);
            }
            else
            {
                background.ClearTransformations();
                background.FadeColour(BackgroundUnfocused, 200, EasingTypes.OutExpo);
            }

            cursorAndLayout.Invalidate();
        }

        protected override bool OnFocus(InputState state)
        {
            if (ReadOnly) return false;

            bindInput();

            background.ClearTransformations();
            background.FadeColour(BackgroundFocused, 200, EasingTypes.Out);

            cursorAndLayout.Invalidate();
            return true;
        }

        #region Native TextBox handling (winform specific)

        private void unbindInput()
        {
            textInput?.Deactivate(this);
        }

        private void bindInput()
        {
            if (textInput == null)
            {
                textInput = Game.Host.TextInput;
                textInput.OnNewImeComposition += delegate(string s)
                {
                    textUpdateScheduler.Add(() => onImeComposition(s));
                    cursorAndLayout.Invalidate();
                };
                textInput.OnNewImeResult += delegate(string s)
                {
                    textUpdateScheduler.Add(() => onImeResult(s));
                    cursorAndLayout.Invalidate();
                };
            }

            textInput.Activate(this);
        }

        private void onImeResult(string s)
        {
            //we only succeeded if there is pending data in the textbox
            if (imeDrawables.Count > 0)
            {
                Game.Audio.Sample.Get(@"Keyboard/key-confirm")?.Play();

                foreach (Drawable d in imeDrawables)
                {
                    d.Colour = Color4.White;
                    d.FadeTo(1, 200, EasingTypes.Out);
                }
            }

            imeDrawables.Clear();
        }

        private List<Drawable> imeDrawables = new List<Drawable>();

        private void onImeComposition(string s)
        {
            //search for unchanged characters..
            int matchCount = 0;
            bool matching = true;
            bool didDelete = false;

            int searchStart = text.Length - imeDrawables.Count;

            //we want to keep processing to the end of the longest string (the current displayed or the new composition).
            int maxLength = Math.Max(imeDrawables.Count, s.Length);

            for (int i = 0; i < maxLength; i++)
            {
                if (matching && searchStart + i < text.Length && i < s.Length && text[searchStart + i] == s[i])
                {
                    matchCount = i + 1;
                    continue;
                }

                matching = false;

                if (matchCount < imeDrawables.Count)
                {
                    //if we are no longer matching, we want to remove all further characters.
                    removeCharacterOrSelection(false);
                    imeDrawables.RemoveAt(matchCount);
                    didDelete = true;
                }
            }

            if (matchCount == s.Length)
            {
                //in the case of backspacing (or a NOP), we can exit early here.
                if (didDelete)
                    Game.Audio.Sample.Get(@"Keyboard/key-delete")?.Play();
                return;
            }

            //add any new or changed characters
            for (int i = matchCount; i < s.Length; i++)
            {
                Drawable dr = addCharacter(s[i]);
                if (dr != null)
                {
                    dr.Colour = Color4.Aqua;
                    dr.Alpha = 0.6f;
                    imeDrawables.Add(dr);
                }
            }

            Game.Audio.Sample.Get($@"Keyboard/key-press-{RNG.Next(1, 5)}")?.Play();
        }

        #endregion
    }
}
