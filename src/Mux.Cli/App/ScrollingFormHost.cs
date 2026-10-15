namespace Mux.Cli.App
{
    using System;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Hosts a TUIKit <see cref="Form"/> inside a modal box that may be shorter than the form. The whole form is
    /// rendered into a buffer and a vertical window of it is copied into the box, scrolled so the focused field
    /// (as the form reports it, including any error drawn under it) stays in view. Mouse events are translated
    /// into the form's own coordinates. Every form modal in the shell shares this, so field errors, hints, and
    /// multi-row widgets never need a hand-maintained row table.
    /// </summary>
    public sealed class ScrollingFormHost
    {
        #region Private-Members

        private Form _Form;
        private Rect _Rect;
        private int _ScrollY;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ScrollingFormHost"/> class.
        /// </summary>
        /// <param name="form">The form to host. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="form"/> is null.</exception>
        public ScrollingFormHost(Form form)
        {
            _Form = Prepare(form ?? throw new ArgumentNullException(nameof(form)));
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Gets or sets the hosted form. Setting a new form (for example one rebuilt with different fields)
        /// prepares it the same way and keeps the scroll position, clamped at the next render.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public Form Form
        {
            get => _Form;
            set => _Form = Prepare(value ?? throw new ArgumentNullException(nameof(value)));
        }


        /// <summary>
        /// Gets a one-line summary of the errors the form currently shows under its fields (for example
        /// "2 fields need fixing"), or null when none do. Errors clear as the user corrects each field.
        /// </summary>
        public string? ErrorSummary
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _Form.FieldCount; i++)
                {
                    if (_Form.IsFieldVisible(i) && _Form.GetFieldError(i) != null)
                    {
                        count++;
                    }
                }

                if (count == 0)
                {
                    return null;
                }

                return count == 1 ? "1 field needs fixing" : count + " fields need fixing";
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Lays the form out at a width and returns its exact height in rows, including section headings, hints,
        /// multi-row widgets, and the errors currently drawn under fields. Use it to size the box before
        /// <see cref="Render"/>.
        /// </summary>
        /// <param name="width">The content width in columns.</param>
        /// <returns>The form's height in rows; at least 1.</returns>
        public int MeasureHeight(int width)
        {
            RenderWhole(Math.Max(1, width), out int height);
            return Math.Max(1, height);
        }

        /// <summary>
        /// Renders the visible window of the form into a rectangle of the surface.
        /// </summary>
        /// <param name="surface">The surface. Must not be null.</param>
        /// <param name="rect">The rectangle the form occupies on the surface.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="surface"/> is null.</exception>
        public void Render(ISurface surface, Rect rect)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            _Rect = rect;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            // Render the whole form, then copy the window around the focused field.
            CellBuffer buffer = RenderWhole(rect.Width, out int formHeight);

            if (_Form.TryGetFocusRect(out Rect focus))
            {
                if (focus.Y < _ScrollY)
                {
                    _ScrollY = focus.Y;
                }
                else if (focus.Y + focus.Height > _ScrollY + rect.Height)
                {
                    _ScrollY = focus.Y + focus.Height - rect.Height;
                }
            }

            _ScrollY = Math.Clamp(_ScrollY, 0, Math.Max(0, formHeight - rect.Height));

            for (int y = 0; y < rect.Height; y++)
            {
                int sourceY = y + _ScrollY;
                if (sourceY >= formHeight)
                {
                    break;
                }

                for (int x = 0; x < rect.Width; x++)
                {
                    surface.Set(rect.X + x, rect.Y + y, buffer.Get(x, sourceY));
                }
            }
        }

        /// <summary>
        /// Forwards a mouse event that falls inside the last rendered rectangle to the form, in the form's
        /// coordinates.
        /// </summary>
        /// <param name="mouse">The mouse event. May be null.</param>
        /// <returns><c>true</c> when the event was inside the form's rectangle and forwarded.</returns>
        public bool HandleMouse(MouseEvent? mouse)
        {
            if (mouse == null || _Rect.Width <= 0
                || mouse.X < _Rect.X || mouse.X >= _Rect.X + _Rect.Width
                || mouse.Y < _Rect.Y || mouse.Y >= _Rect.Y + _Rect.Height)
            {
                return false;
            }

            _Form.HandleMouse(new MouseEvent(mouse.Kind, mouse.Button, mouse.X - _Rect.X, (mouse.Y - _Rect.Y) + _ScrollY, mouse.Modifiers, mouse.ClickCount));
            return true;
        }

        #endregion

        #region Private-Methods

        private CellBuffer RenderWhole(int width, out int formHeight)
        {
            // The form lays itself out top to bottom and stops at the bottom of its surface, recording how far it
            // got. Its estimate before a render misses multi-row widgets and wrapped errors, so render into a
            // buffer with room to spare and grow it until the form ends before the buffer does.
            int height = _Form.ContentHeight + 32;
            while (true)
            {
                CellBuffer buffer = new CellBuffer(width, height);
                _Form.Render(new BufferSurface(buffer));
                formHeight = _Form.ContentHeight;
                if (formHeight < height)
                {
                    return buffer;
                }

                height *= 2;
            }
        }

        private static Form Prepare(Form form)
        {
            // Errors are drawn under the field they belong to; once a field has shown one it re-checks as the
            // user edits it. Text fields get the readline-style line-editing keys the composer also uses.
            form.ShowFieldErrors = true;
            for (int i = 0; i < form.FieldCount; i++)
            {
                if (form.GetField(i) is TextField field)
                {
                    field.UseLineEditingKeys = true;
                }
            }

            return form;
        }

        #endregion
    }
}
