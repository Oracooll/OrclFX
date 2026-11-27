// OrclFX: the Themes window (the palette button in the title bar; menu › Theme › Themes and text…). At the top a
// picture of the window in the theme chosen; then two pages:
//  - Themes: which theme is shown (chosen by hand, light or dark as Windows is set, or by the hour), every theme to
//    choose from (light, dark, your own), and your own themes (new, edit, copy, export, import, delete).
//  - Text and contrast: text size and weight, focus visibility (the active pane's frame), border contrast, touch
//    mode, with presets (Standard, Low vision, Tablet).
// Colours are shown in the main window at once when they can be. A switch between light and dark, the text and
// touch mode need the window rebuilt: that's done when this window closes (MainForm.ShowThemes).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    class ThemesWindow : Form
    {
        readonly MainForm main;
        // What the closing asks for (the window is rebuilt when they differ from what it has).
        public int TextSize, TextWeight;
        public bool Touch;

        static string lastPage = "themes";
        readonly ThemePreview preview = new ThemePreview();
        readonly Panel themesPage = new Panel(), textPage = new Panel();
        readonly Button themesTab = new Button(), textTab = new Button(), closeButton = new Button();
        readonly Label pending = new Label();
        readonly List<Label> headings = new List<Label>(), notes = new List<Label>();
        readonly List<ThemeTile> tiles = new List<ThemeTile>();
        Font bold;

        public ThemesWindow(MainForm owner, string page)
        {
            main = owner;
            TextSize = owner.TextSizeWanted; TextWeight = owner.TextWeightWanted; Touch = owner.TouchWanted;
            Text = "Themes and text";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = ShowInTaskbar = false;
            ShowIcon = false;
            AutoScaleMode = AutoScaleMode.None;
            Font = owner.Font;
            bold = new Font(Font, FontStyle.Bold);
            int w = Native.Px(640);
            int h = Math.Min(Native.Px(860), Screen.FromControl(owner).WorkingArea.Height - Native.Px(60));
            ClientSize = new Size(w, h);

            preview.SetBounds(0, 0, w, Native.Px(200));
            Controls.Add(preview);

            int ty = preview.Bottom + Native.Px(8);
            foreach (Button b in new[] { themesTab, textTab })
            {
                b.FlatStyle = FlatStyle.Flat;
                b.Height = Native.Px(34);
                b.Width = (w - Native.Px(16) * 2 - Native.Px(8)) / 2;
                b.Top = ty;
                Controls.Add(b);
            }
            themesTab.Text = "Themes";
            textTab.Text = "Text and contrast";
            themesTab.Left = Native.Px(16);
            textTab.Left = themesTab.Right + Native.Px(8);
            themesTab.Click += delegate { ShowPage("themes"); };
            textTab.Click += delegate { ShowPage("text"); };

            int bottomH = Native.Px(52);
            foreach (Panel p in new[] { themesPage, textPage })
            {
                p.SetBounds(0, themesTab.Bottom + Native.Px(6), w, h - themesTab.Bottom - Native.Px(6) - bottomH);
                p.AutoScroll = true;
                Controls.Add(p);
            }

            closeButton.Text = "Close";
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.SetBounds(w - Native.Px(16) - Native.Px(96), h - bottomH + Native.Px(10), Native.Px(96), Native.Px(32));
            closeButton.DialogResult = DialogResult.OK;
            Controls.Add(closeButton);
            AcceptButton = CancelButton = closeButton;
            pending.SetBounds(Native.Px(16), h - bottomH + Native.Px(4), closeButton.Left - Native.Px(28), bottomH - Native.Px(8));
            pending.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(pending);

            BuildThemesPage();
            BuildTextPage();
            HandleCreated += delegate
            {
                int dark = Theme.Dark ? 1 : 0;
                Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            };
            Restyle();
            ShowPage(page ?? lastPage);
            // (Test hook: DUALPANE_TEST_THEMES=editor opens a new theme's editor too.)
            if (page == "editor") Shown += delegate { BeginInvoke((MethodInvoker)delegate { EditTheme(null, Theme.Wanted()); }); };
            Disposed += delegate { bold.Dispose(); };
        }

        void ShowPage(string page)
        {
            lastPage = page == "text" ? "text" : "themes";
            themesPage.Visible = lastPage == "themes";
            textPage.Visible = lastPage == "text";
            Restyle();
        }

        // ---- building blocks

        Label Heading(Control parent, string text, ref int y)
        {
            if (y > Native.Px(14)) y += Native.Px(8);
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = bold;
            l.Location = new Point(Native.Px(16), y);
            parent.Controls.Add(l);
            headings.Add(l);
            y += l.PreferredHeight + Native.Px(6);
            return l;
        }

        Label Note(Control parent, string text, ref int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Location = new Point(Native.Px(16), y);
            l.Width = ClientSize.Width - Native.Px(48);
            l.Height = TextRenderer.MeasureText(text, Font, new Size(l.Width, 0), TextFormatFlags.WordBreak).Height + Native.Px(2);
            parent.Controls.Add(l);
            notes.Add(l);
            y += l.Height + Native.Px(6);
            return l;
        }

        Button FlatButton(Control parent, string text, int x, int y)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(Native.Px(6), Native.Px(2), Native.Px(6), Native.Px(2));
            b.Location = new Point(x, y);
            parent.Controls.Add(b);
            return b;
        }

        // ---- Themes page

        RadioButton byHand, asWindows, byHour;
        NumericUpDown fromHour, toHour;
        int themesScroll;

        void BuildThemesPage()
        {
            themesScroll = themesPage.VerticalScroll.Value;
            themesPage.SuspendLayout();
            foreach (Control c in ToList(themesPage.Controls)) c.Dispose(); // (a disposed control leaves its parent)
            headings.RemoveAll(delegate(Label l) { return l.IsDisposed; });
            notes.RemoveAll(delegate(Label l) { return l.IsDisposed; });
            tiles.Clear();
            themesPage.AutoScrollPosition = new Point(0, 0);

            int y = Native.Px(6), x = Native.Px(16);
            Heading(themesPage, "Which theme is shown", ref y);
            byHand = Radio("Chosen by hand", x, ref y);
            asWindows = Radio("Light or dark, as Windows is set", x, ref y);
            byHour = Radio("By the hour: light from", x, ref y);
            y -= Native.Px(28);
            // (Placed by the radio button's text width: its AutoSize width isn't laid out yet.)
            fromHour = Hour(Theme.LightFrom, byHour.Left + byHour.PreferredSize.Width + Native.Px(8), y);
            Label until = new Label();
            until.Text = ":00   until";
            until.AutoSize = true;
            until.Location = new Point(fromHour.Right + Native.Px(4), y + Native.Px(3));
            themesPage.Controls.Add(until);
            toHour = Hour(Theme.LightTo, until.Right + Native.Px(6), y);
            Label after = new Label();
            after.Text = ":00, dark after";
            after.AutoSize = true;
            after.Location = new Point(toHour.Right + Native.Px(4), y + Native.Px(3));
            themesPage.Controls.Add(after);
            y += Native.Px(30);
            byHand.Checked = Theme.Auto == ThemeChoice.ByHand;
            asWindows.Checked = Theme.Auto == ThemeChoice.AsWindows;
            byHour.Checked = Theme.Auto == ThemeChoice.ByHour;
            byHand.CheckedChanged += delegate { if (byHand.Checked) SetAuto(ThemeChoice.ByHand); };
            asWindows.CheckedChanged += delegate { if (asWindows.Checked) SetAuto(ThemeChoice.AsWindows); };
            byHour.CheckedChanged += delegate { if (byHour.Checked) SetAuto(ThemeChoice.ByHour); };
            fromHour.ValueChanged += delegate { Theme.LightFrom = (int)fromHour.Value; Changed(); };
            toHour.ValueChanged += delegate { Theme.LightTo = (int)toHour.Value; Changed(); };
            Note(themesPage, "Click a theme to use it. In the automatic choices, a light theme becomes the light one and a dark theme the dark one. Right-click a theme to copy it into your own.", ref y);

            Heading(themesPage, "Light", ref y);
            y = Tiles(Palettes.BuiltIn.FindAll(delegate(Palette p) { return !p.Dark; }), y);
            Heading(themesPage, "Dark", ref y);
            y = Tiles(Palettes.BuiltIn.FindAll(delegate(Palette p) { return p.Dark; }), y);
            Heading(themesPage, "My themes", ref y);
            if (Theme.Mine.Count == 0) Note(themesPage, "No themes of your own yet. They're kept in your OneDrive, so all your computers have them.", ref y);
            else y = Tiles(Theme.Mine, y);
            Button make = FlatButton(themesPage, "New theme…", x, y);
            make.Click += delegate { EditTheme(null, Theme.Wanted()); };
            Button import = FlatButton(themesPage, "Import…", make.Right + Native.Px(8), y);
            import.Click += delegate { Import(); };
            y = make.Bottom + Native.Px(16);
            Label end = new Label();
            end.SetBounds(0, y, 1, 1);
            themesPage.Controls.Add(end);
            themesPage.ResumeLayout();
            themesPage.AutoScrollPosition = new Point(0, themesScroll);
            RefreshTicks();
            Restyle();
        }

        static List<Control> ToList(Control.ControlCollection c) { List<Control> r = new List<Control>(); foreach (Control x in c) r.Add(x); return r; }

        RadioButton Radio(string text, int x, ref int y)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.AutoSize = true;
            r.Location = new Point(x, y);
            themesPage.Controls.Add(r);
            y += Native.Px(28);
            return r;
        }

        NumericUpDown Hour(int value, int x, int y)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = 0; n.Maximum = 23;
            n.Value = Math.Max(0, Math.Min(23, value));
            n.Width = Native.Px(52);
            n.Location = new Point(x, y);
            n.BorderStyle = BorderStyle.FixedSingle;
            themesPage.Controls.Add(n);
            return n;
        }

        int Tiles(List<Palette> list, int y)
        {
            int x = Native.Px(16), gap = Native.Px(8), tw = (ClientSize.Width - Native.Px(48) - gap) / 2, th = Native.Px(46);
            for (int i = 0; i < list.Count; i++)
            {
                ThemeTile t = new ThemeTile(list[i]);
                t.SetBounds(x + (i % 2) * (tw + gap), y + (i / 2) * (th + gap), tw, th);
                Palette p = list[i];
                t.MouseUp += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left) Pick(p);
                    else if (e.Button == MouseButtons.Right) TileMenu(t, p, e.Location);
                };
                t.DoubleClick += delegate { if (p.Mine) EditTheme(p, p); };
                themesPage.Controls.Add(t);
                tiles.Add(t);
            }
            return y + ((list.Count + 1) / 2) * (th + gap);
        }

        void SetAuto(int auto)
        {
            if (Theme.Auto == auto) return;
            // To "by hand": the theme on screen now stays.
            if (auto == ThemeChoice.ByHand) Theme.Manual = Theme.Wanted().Id;
            else Theme.AutoKind = auto;
            Theme.Auto = auto;
            Changed();
        }

        void Pick(Palette p)
        {
            if (Theme.Auto == ThemeChoice.ByHand) Theme.Manual = p.Id;
            else if (p.Dark) Theme.DarkId = p.Id;
            else Theme.LightId = p.Id;
            Changed();
        }

        // A theme setting changed: the main window shows it now when it can; this window and its picture follow.
        void Changed()
        {
            if (!main.ShowThemeLive()) main.StateChanged();
            RefreshTicks();
            Restyle();
        }

        void RefreshTicks()
        {
            Palette shown = Theme.Wanted();
            foreach (ThemeTile t in tiles)
            {
                if (Theme.Auto == ThemeChoice.ByHand) t.Mark = t.P.SameAs(shown) ? "" : null;
                else t.Mark = t.P.Id == Theme.LightId ? "light" : t.P.Id == Theme.DarkId ? "dark" : null;
                if (t.Mark != null && t.Mark.Length > 0 && t.P.SameAs(shown)) t.Mark += ", now";
                t.Invalidate();
            }
            fromHour.Enabled = toHour.Enabled = Theme.Auto == ThemeChoice.ByHour;
        }

        void TileMenu(Control at, Palette p, Point where)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new MenuRenderer();
            m.Font = Font;
            if (p.Mine)
            {
                m.Items.Add("Edit…", null, delegate { EditTheme(p, p); });
                m.Items.Add("Copy…", null, delegate { EditTheme(null, p); });
                m.Items.Add("Export…", null, delegate { Export(p); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Delete", null, delegate { DeleteTheme(p); });
            }
            else m.Items.Add("Copy to my themes…", null, delegate { EditTheme(null, p); });
            m.Closed += delegate { BeginInvoke((MethodInvoker)m.Dispose); };
            m.Show(at, where);
        }

        // Makes a new theme from basis (editing null), or edits one of yours.
        void EditTheme(Palette editing, Palette basis)
        {
            string name = editing != null ? editing.Name : ThemeFile.FreeName(basis.Mine ? basis.Name : "My " + basis.Name, Theme.Mine);
            Palette made = ThemeEditor.Show(this, basis, name, editing != null ? editing.Name : null);
            if (made == null) return;
            try
            {
                Theme.Mine = ThemeFile.Save(AppPaths.ThemesFile, made, editing != null ? editing.Name : null);
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
                MessageBox.Show(this, "The theme couldn't be saved: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (editing != null && editing.Id != made.Id) Rename(editing.Id, made.Id);
            BuildThemesPage();
            Changed();
        }

        static void Rename(string from, string to)
        {
            if (string.Equals(Theme.Manual, from, StringComparison.OrdinalIgnoreCase)) Theme.Manual = to;
            if (string.Equals(Theme.LightId, from, StringComparison.OrdinalIgnoreCase)) Theme.LightId = to;
            if (string.Equals(Theme.DarkId, from, StringComparison.OrdinalIgnoreCase)) Theme.DarkId = to;
        }

        void DeleteTheme(Palette p)
        {
            if (MessageBox.Show(this, "Delete the theme “" + p.Name + "”? It's deleted on all your computers.", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { Theme.Mine = ThemeFile.Delete(AppPaths.ThemesFile, p.Name); }
            catch (Exception ex)
            {
                Program.LogError(ex);
                MessageBox.Show(this, "The theme couldn't be deleted: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // In use: back to Light or Dark.
            if (string.Equals(Theme.Manual, p.Id, StringComparison.OrdinalIgnoreCase)) Theme.Manual = p.Dark ? "dark" : "light";
            if (string.Equals(Theme.LightId, p.Id, StringComparison.OrdinalIgnoreCase)) Theme.LightId = "light";
            if (string.Equals(Theme.DarkId, p.Id, StringComparison.OrdinalIgnoreCase)) Theme.DarkId = "dark";
            BuildThemesPage();
            Changed();
        }

        void Export(Palette p)
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "OrclFX theme (*.orcltheme)|*.orcltheme";
                d.FileName = string.Join("_", p.Name.Split(Path.GetInvalidFileNameChars())) + ".orcltheme";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(d.FileName, ThemeFile.Serialize(new List<Palette> { p })); }
                catch (Exception ex) { MessageBox.Show(this, "The theme couldn't be exported: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void Import()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "OrclFX themes (*.orcltheme)|*.orcltheme|All files (*.*)|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                List<Palette> found;
                try { found = ThemeFile.Parse(File.ReadAllLines(d.FileName)); }
                catch (Exception ex) { MessageBox.Show(this, "The file couldn't be read: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (found.Count == 0) { MessageBox.Show(this, "There are no themes in that file.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                try
                {
                    foreach (Palette p in found)
                    {
                        string name = ThemeFile.FreeName(p.Name, Theme.Mine);
                        Palette added = Palettes.Derive(Palettes.MineId(name), name, p.Dark, p.BaseWindow, p.BaseText, p.BaseAccent, p.BaseTitle, p.BaseLine);
                        added.Mine = true;
                        Theme.Mine = ThemeFile.Save(AppPaths.ThemesFile, added, null);
                    }
                }
                catch (Exception ex)
                {
                    Program.LogError(ex);
                    MessageBox.Show(this, "The themes couldn't be saved: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                BuildThemesPage();
            }
        }

        // ---- Text and contrast page

        TrackBar sizeBar, weightBar, frameBar, borderBar;
        Label sizeValue, weightValue, frameValue, borderValue;
        CheckBox touchBox;

        void BuildTextPage()
        {
            int y = Native.Px(6), x = Native.Px(16);
            Heading(textPage, "Presets", ref y);
            Button standard = FlatButton(textPage, "Standard", x, y);
            Button lowVision = FlatButton(textPage, "Low vision", standard.Right + Native.Px(8), y);
            Button tablet = FlatButton(textPage, "Tablet (touch)", lowVision.Right + Native.Px(8), y);
            y = standard.Bottom + Native.Px(6);
            standard.Click += delegate { SetText(0, TextStyle.Normal, 0, 1, false); };
            lowVision.Click += delegate { SetText(2, TextStyle.Bold, 2, 2, Touch); };
            tablet.Click += delegate { SetText(TextSize, TextWeight, Pane.Frame, Theme.Borders, true); };

            sizeBar = Slider(textPage, "Text size (the whole window grows with it, so nothing is cut off)", TextStyle.SizeNames, TextSize, ref y, out sizeValue);
            weightBar = Slider(textPage, "Font weight", TextStyle.WeightNames, TextWeight, ref y, out weightValue);
            frameBar = Slider(textPage, "Focus visibility (the active pane's frame)", Pane.FrameNames, Pane.Frame, ref y, out frameValue);
            borderBar = Slider(textPage, "Border contrast", Theme.BorderNames, Theme.Borders, ref y, out borderValue);
            sizeBar.ValueChanged += delegate { TextSize = sizeBar.Value; Staged(); };
            weightBar.ValueChanged += delegate { TextWeight = weightBar.Value; Staged(); };
            frameBar.ValueChanged += delegate { main.SetFocusFrame(frameBar.Value); Staged(); };
            borderBar.ValueChanged += delegate { Theme.Borders = borderBar.Value; Changed(); Staged(); };

            y += Native.Px(4);
            touchBox = new CheckBox();
            touchBox.Text = "Touch mode: bigger buttons, tabs, rows and dividers for fingers";
            touchBox.AutoSize = true;
            touchBox.Location = new Point(x, y);
            touchBox.Checked = Touch;
            touchBox.CheckedChanged += delegate { Touch = touchBox.Checked; Staged(); };
            textPage.Controls.Add(touchBox);
            y += Native.Px(32);
            Note(textPage, "In the light themes, a changed text size or weight also changes the file lists: they then use Windows' older list style (as in Windows 7), the only one that takes a font from a program; its rows are more compact. In the dark themes the file lists keep Windows' own font: the older list can't be made fully dark.", ref y);
            Note(textPage, "Text size, weight and touch mode rebuild the window (your tabs stay) when you close this window. The rest changes at once.", ref y);
            Button reset = FlatButton(textPage, "Reset all", x, y);
            reset.Click += delegate { SetText(0, TextStyle.Normal, 0, 1, false); };
            Staged();
        }

        TrackBar Slider(Control parent, string title, string[] names, int value, ref int y, out Label valueLabel)
        {
            Label l = new Label();
            l.Text = title;
            l.AutoSize = true;
            l.Location = new Point(Native.Px(16), y);
            parent.Controls.Add(l);
            y += l.PreferredHeight + Native.Px(2);
            TrackBar t = new TrackBar();
            t.Minimum = 0; t.Maximum = names.Length - 1;
            t.Value = Math.Max(0, Math.Min(names.Length - 1, value));
            t.TickStyle = TickStyle.BottomRight;
            t.LargeChange = 1;
            t.AutoSize = false;
            t.SetBounds(Native.Px(10), y, Native.Px(300), Native.Px(36));
            parent.Controls.Add(t);
            Label v = new Label();
            v.AutoSize = true;
            v.Location = new Point(t.Right + Native.Px(10), y + Native.Px(6));
            v.Text = names[t.Value];
            parent.Controls.Add(v);
            t.ValueChanged += delegate { v.Text = names[t.Value]; };
            Button def = FlatButton(parent, "Default", ClientSize.Width - Native.Px(140), y + Native.Px(2));
            int defaultValue = names == Theme.BorderNames ? 1 : 0;
            def.Click += delegate { t.Value = defaultValue; };
            valueLabel = v;
            y += Native.Px(44);
            return t;
        }

        void SetText(int size, int weight, int frame, int borders, bool touch)
        {
            sizeBar.Value = size;
            weightBar.Value = weight;
            frameBar.Value = frame;
            borderBar.Value = borders;
            touchBox.Checked = touch;
        }

        // The text page changed: the picture shows it, and the note says what closing will rebuild.
        void Staged()
        {
            preview.Zoom = TextStyle.SizePercents[TextSize] / 100f;
            preview.Weight = TextWeight;
            preview.Frame = Pane.Frame;
            preview.Invalidate();
            UpdatePending();
        }

        void UpdatePending()
        {
            List<string> what = new List<string>();
            if (Theme.Wanted().Dark != Theme.Dark) what.Add(Theme.Wanted().Dark ? "the dark theme" : "the light theme");
            if (TextSize != main.TextSizeWanted || TextWeight != main.TextWeightWanted) what.Add("the text");
            if (Touch != main.TouchWanted) what.Add("touch mode");
            pending.Text = what.Count == 0 ? "" : "When you close this window, the window is rebuilt for " + string.Join(" and ", what.ToArray()) + ".";
        }

        // ---- colours

        void Restyle()
        {
            BackColor = Theme.Menu;
            ForeColor = Theme.Text;
            themesPage.BackColor = textPage.BackColor = Theme.Menu;
            foreach (Label l in headings) if (!l.IsDisposed) l.ForeColor = Theme.Accent;
            foreach (Label l in notes) if (!l.IsDisposed) l.ForeColor = Theme.TextDim;
            pending.ForeColor = Theme.Accent;
            StyleButtons(this);
            themesTab.BackColor = themesPage.Visible ? Theme.Hover : Theme.Input;
            textTab.BackColor = textPage.Visible ? Theme.Hover : Theme.Input;
            themesTab.FlatAppearance.BorderColor = themesPage.Visible ? Theme.Accent : Theme.Border;
            textTab.FlatAppearance.BorderColor = textPage.Visible ? Theme.Accent : Theme.Border;
            themesTab.Font = themesPage.Visible ? bold : Font;
            textTab.Font = textPage.Visible ? bold : Font;
            preview.P = Theme.Wanted();
            preview.Borders = Theme.Borders;
            preview.Invalidate();
            foreach (ThemeTile t in tiles) t.Invalidate();
            UpdatePending();
        }

        static void StyleButtons(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                Button b = c as Button;
                if (b != null)
                {
                    b.BackColor = Theme.Input;
                    b.ForeColor = Theme.Text;
                    b.FlatAppearance.BorderColor = Theme.Border;
                    b.FlatAppearance.MouseOverBackColor = Theme.Hover;
                }
                NumericUpDown n = c as NumericUpDown;
                if (n != null) { n.BackColor = Theme.Input; n.ForeColor = Theme.Text; }
                TrackBar t = c as TrackBar;
                if (t != null) t.BackColor = Theme.Menu;
                if (c.HasChildren) StyleButtons(c);
            }
        }
    }

    // A theme to pick: a small picture of it, its name, and a tick (with "light"/"dark" in the automatic choices).
    class ThemeTile : Control
    {
        public readonly Palette P;
        public string Mark;   // null: not picked; "": picked; "light", "dark" (", now"): picked for that
        bool hover;

        public ThemeTile(Palette p)
        {
            P = p;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Menu);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            bool picked = Mark != null;
            using (SolidBrush b = new SolidBrush(hover ? Theme.Hover : Theme.Input)) g.FillRectangle(b, r);
            using (Pen p = new Pen(picked ? Theme.Accent : Theme.Border, picked ? Native.Px(2) : 1))
                g.DrawRectangle(p, picked ? Rectangle.Inflate(r, -Native.Px(1) / 2, -Native.Px(1) / 2) : r);
            // The picture: the title bar, the background, two lines of writing and an accent.
            int s = Height - Native.Px(14), x = Native.Px(8), y = Native.Px(7), pw = s * 4 / 3;
            Rectangle pic = new Rectangle(x, y, pw, s);
            using (SolidBrush b = new SolidBrush(P.Window)) g.FillRectangle(b, pic);
            using (SolidBrush b = new SolidBrush(P.ColouredTitle ? P.Title : P.Accent)) g.FillRectangle(b, pic.X, pic.Y, pic.Width, Math.Max(3, s / 5));
            using (SolidBrush b = new SolidBrush(P.Text)) g.FillRectangle(b, pic.X + s / 6, pic.Y + s * 2 / 5, pic.Width / 2, Math.Max(2, s / 12));
            using (SolidBrush b = new SolidBrush(P.TextDim)) g.FillRectangle(b, pic.X + s / 6, pic.Y + s * 3 / 5, pic.Width / 3, Math.Max(2, s / 12));
            using (SolidBrush b = new SolidBrush(P.Accent)) g.FillRectangle(b, pic.X + s / 6, pic.Y + s * 4 / 5 - 1, pic.Width / 5, Math.Max(2, s / 10));
            using (Pen p = new Pen(P.Border)) g.DrawRectangle(p, pic);
            Rectangle text = new Rectangle(pic.Right + Native.Px(10), 0, Width - pic.Right - Native.Px(16), Height);
            string mark = Mark == null ? null : Mark.Length == 0 ? "✓" : "✓ " + Mark;
            int markW = mark == null ? 0 : TextRenderer.MeasureText(mark, Font).Width + Native.Px(4);
            TextRenderer.DrawText(g, P.Name, Font, new Rectangle(text.X, 0, text.Width - markW, Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (mark != null)
                TextRenderer.DrawText(g, mark, Font, new Rectangle(text.Right - markW, 0, markW, Height), Theme.Accent,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // A small picture of the OrclFX window in a theme, text size and weight: title bar, tabs, address, an active and an
    // inactive file list, status bar.
    class ThemePreview : Control
    {
        public Palette P;
        public float Zoom = 1f;
        public int Weight, Frame, Borders = 1;

        public ThemePreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        float dpi;
        int S(int v) { return (int)Math.Round(v * dpi * Zoom); }

        Font MakeFont(float pt, bool bold)
        {
            pt *= Zoom;
            if (bold || Weight == TextStyle.Bold) return new Font("Segoe UI", pt, FontStyle.Bold);
            if (Weight == TextStyle.Semibold) return new Font("Segoe UI Semibold", pt);
            return new Font("Segoe UI", pt);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = P ?? Theme.Wanted();
            dpi = Native.Px(100) / (100f * TextStyle.Zoom); // the screen's scaling alone (the window's text size is its own)
            Color border = Theme.BorderFor(p, Borders);
            Color accent = p.WindowsAccent ? Theme.WindowsAccent() : p.Accent;
            Color listText = p.Dark ? Color.White : Color.Black, listDim = p.Dark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(90, 90, 90);
            Color select = p.Dark ? Color.FromArgb(52, 70, 96) : Color.FromArgb(204, 232, 255);
            g.Clear(p.Window);
            using (Font f = MakeFont(9f, false))
            using (Font fb = MakeFont(9f, true))
            {
                int w = Width, h = Height, y = 0;
                // Title bar
                int th = S(28);
                Fill(g, p.ColouredTitle ? p.Title : p.Bar, 0, 0, w, th);
                Fill(g, accent, S(10), (th - S(14)) / 2, S(14), S(14));
                Draw(g, "OrclFX", f, p.ColouredTitle ? p.TitleText : p.Text, S(32), 0, S(200), th);
                for (int i = 0; i < 3; i++)
                    Fill(g, p.ColouredTitle ? p.TitleTextDim : p.TextDim, w - S(26) * (3 - i) - S(10) + S(8), th / 2 - 1, S(10), Math.Max(2, S(2)));
                int lineH = p.ColouredTitle ? Math.Max(2, S(2)) : 1;
                Fill(g, p.ColouredTitle ? p.TitleLine : border, 0, th - lineH, w, lineH);
                y = th;
                // Tabs
                int tabH = S(28);
                Fill(g, p.Bar, 0, y, w, tabH);
                Fill(g, p.Window, S(8), y + S(4), S(120), tabH - S(4));
                Fill(g, accent, S(8), y + S(4), S(120), Math.Max(2, S(2)));
                Draw(g, "Documents", fb, p.Text, S(16), y + S(4), S(110), tabH - S(4));
                Draw(g, "Photos", f, p.TextDim, S(140), y + S(4), S(100), tabH - S(4));
                y += tabH;
                // Address
                int addrH = S(34);
                Fill(g, p.Window, 0, y, w, addrH);
                Rectangle box = new Rectangle(S(10), y + S(5), w - S(20), addrH - S(10));
                Fill(g, p.Input, box.X, box.Y, box.Width, box.Height);
                using (Pen pen = new Pen(accent)) g.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);
                Draw(g, "C:  ›  Users  ›  Documents", f, p.Text, box.X + S(8), box.Y, box.Width - S(16), box.Height);
                y += addrH;
                // Two panes: the active one framed, the other tinted
                int statusH = S(24), paneH = h - y - statusH, half = (w - S(6)) / 2;
                int frame = Math.Max(1, (int)Math.Round(Pane.FrameWidths[Math.Max(0, Math.Min(2, Frame))] * dpi));
                Fill(g, border, half, y, w - 2 * half, paneH);
                Fill(g, accent, 0, y, half, paneH);
                Rectangle left = new Rectangle(frame, y + frame, half - 2 * frame, paneH - 2 * frame);
                Rectangle right = new Rectangle(w - half + frame, y + frame, half - 2 * frame, paneH - 2 * frame);
                Fill(g, p.Window, w - half, y, half, paneH);
                List(g, left, f, p, listText, listDim, select, true);
                List(g, right, f, p, listText, listDim, select, false);
                if (p.Tinted)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(Palettes.ShadeAlpha, Palettes.ShadeColor(p)))) g.FillRectangle(b, right);
                y += paneH;
                // Status bar
                Fill(g, p.Bar, 0, y, w, statusH);
                Fill(g, border, 0, y, w, 1);
                Draw(g, "3 items", f, p.TextDim, S(10), y, S(200), statusH);
            }
        }

        void List(Graphics g, Rectangle r, Font f, Palette p, Color text, Color dim, Color select, bool active)
        {
            Fill(g, p.ListBack, r.X, r.Y, r.Width, r.Height);
            Region old = g.Clip;
            g.SetClip(r);
            int row = S(22), y = r.Y + S(2);
            Draw(g, "Name", f, dim, r.X + S(28), y, r.Width / 2, row);
            Draw(g, "Date modified", f, dim, r.X + r.Width / 2 + S(10), y, r.Width / 2, row);
            y += row;
            Fill(g, p.Dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(225, 225, 225), r.X, y, r.Width, 1);
            string[] names = active ? new[] { "Projects", "Report.docx", "Budget.xlsx", "Notes.txt" } : new[] { "Holiday", "Beach.jpg", "Sunset.jpg", "Family.jpg" };
            for (int i = 0; i < names.Length; i++)
            {
                if (active && i == 1) Fill(g, select, r.X + S(2), y + S(1), r.Width - S(4), row - S(2));
                Color icon = i == 0 ? Color.FromArgb(232, 190, 80) : p.Dark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(120, 140, 170);
                Fill(g, icon, r.X + S(8), y + (row - S(12)) / 2, S(14), S(12));
                Draw(g, names[i], f, text, r.X + S(28), y, r.Width / 2 - S(20), row);
                Draw(g, "18.11.2026", f, dim, r.X + r.Width / 2 + S(10), y, r.Width / 2, row);
                y += row;
            }
            g.Clip = old;
        }

        static void Fill(Graphics g, Color c, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, x, y, w, h);
        }

        static void Draw(Graphics g, string s, Font f, Color c, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            TextRenderer.DrawText(g, s, f, new Rectangle(x, y, w, h), c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // Making or editing one of your own themes: its name, light or dark, and five colours; the rest is worked out
    // (Palettes.Derive), with a picture of the result.
    static class ThemeEditor
    {
        // The theme made, or null when cancelled. oldName: the theme being edited (null for a new one).
        public static Palette Show(IWin32Window owner, Palette basis, string name, string oldName)
        {
            using (Form f = new Form())
            {
                Control o = owner as Control;
                f.Text = oldName == null ? "New theme" : "Edit theme";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = f.ShowInTaskbar = false;
                f.ShowIcon = false;
                f.AutoScaleMode = AutoScaleMode.None;
                if (o != null) f.Font = o.Font;
                f.BackColor = Theme.Menu;
                f.ForeColor = Theme.Text;
                int w = Native.Px(520), x = Native.Px(16), y = Native.Px(12);

                ThemePreview pic = new ThemePreview();
                pic.SetBounds(0, 0, w, Native.Px(190));
                f.Controls.Add(pic);
                y = pic.Bottom + Native.Px(12);

                Label nl = new Label();
                nl.Text = "Name:";
                nl.AutoSize = true;
                nl.Location = new Point(x, y + Native.Px(3));
                f.Controls.Add(nl);
                TextBox nameBox = new TextBox();
                nameBox.Text = name;
                nameBox.MaxLength = 40;
                nameBox.BorderStyle = BorderStyle.FixedSingle;
                nameBox.BackColor = Theme.Input;
                nameBox.ForeColor = Theme.Text;
                nameBox.SetBounds(x + Native.Px(150), y, w - x * 2 - Native.Px(150), Native.Px(24));
                f.Controls.Add(nameBox);
                y += Native.Px(34);

                RadioButton light = new RadioButton(), dark = new RadioButton();
                light.Text = "Light (white file lists)"; dark.Text = "Dark (dark file lists)";
                light.AutoSize = dark.AutoSize = true;
                light.Location = new Point(x + Native.Px(150), y);
                dark.Location = new Point(x + Native.Px(330), y);
                light.Checked = !basis.Dark; dark.Checked = basis.Dark;
                f.Controls.Add(light); f.Controls.Add(dark);
                y += Native.Px(32);

                Color[] colours = { basis.BaseWindow, basis.BaseText, basis.BaseAccent, basis.BaseTitle, basis.BaseLine };
                string[] labels = { "Background", "Writing", "Accent (frames, ticks)", "Title bar", "Line under the title bar" };
                Button[] swatches = new Button[5];
                Label warn = new Label();
                Action update = null;
                for (int i = 0; i < 5; i++)
                {
                    int k = i;
                    Label l = new Label();
                    l.Text = labels[i] + ":";
                    l.AutoSize = true;
                    l.Location = new Point(x, y + Native.Px(5));
                    f.Controls.Add(l);
                    Button b = new Button();
                    b.FlatStyle = FlatStyle.Flat;
                    b.SetBounds(x + Native.Px(150), y, Native.Px(150), Native.Px(28));
                    b.TextAlign = ContentAlignment.MiddleRight;
                    b.Click += delegate
                    {
                        using (ColorDialog d = new ColorDialog())
                        {
                            d.FullOpen = true;
                            d.Color = colours[k];
                            if (d.ShowDialog(f) != DialogResult.OK) return;
                            colours[k] = d.Color;
                            update();
                        }
                    };
                    f.Controls.Add(b);
                    swatches[i] = b;
                    y += Native.Px(34);
                }
                Label hint = new Label();
                hint.Text = "A title bar in the background's own colour is drawn like the other bars.";
                hint.AutoSize = true;
                hint.ForeColor = Theme.TextDim;
                hint.Location = new Point(x, y);
                f.Controls.Add(hint);
                y += Native.Px(24);
                warn.AutoSize = true;
                warn.ForeColor = Theme.Accent;
                warn.Location = new Point(x, y);
                f.Controls.Add(warn);
                y += Native.Px(28);

                Palette result = null;
                update = delegate
                {
                    for (int i = 0; i < 5; i++)
                    {
                        Color c = colours[i];
                        swatches[i].BackColor = c;
                        swatches[i].ForeColor = Palettes.Luminance(c) > 0.4 ? Color.Black : Color.White;
                        swatches[i].Text = ThemeFile.FormatColor(c);
                        swatches[i].FlatAppearance.BorderColor = Theme.Border;
                    }
                    string n = ThemeFile.CleanName(nameBox.Text);
                    result = Palettes.Derive(Palettes.MineId(n), n, dark.Checked, colours[0], colours[1], colours[2], colours[3], colours[4]);
                    result.Mine = true;
                    double c1 = Palettes.Contrast(colours[0], colours[1]);
                    warn.Text = c1 < 4.5 ? "⚠ The writing is hard to read on this background (contrast " + c1.ToString("0.0") + " : 1; 4.5 or more reads well)." : "";
                    pic.P = result;
                    pic.Invalidate();
                };
                // Light <-> dark: a background of the other kind is swapped for that kind's own (the accent and title stay).
                EventHandler kind = delegate
                {
                    bool d = dark.Checked;
                    if (d == Palettes.Luminance(colours[0]) < 0.3) { update(); return; }
                    Palette b = Palettes.Find(d ? "dark" : "light", null);
                    colours[0] = b.BaseWindow; colours[1] = b.BaseText;
                    update();
                };
                light.CheckedChanged += kind;
                nameBox.TextChanged += delegate { update(); };
                update();

                Button ok = new Button(), cancel = new Button();
                ok.Text = "OK"; cancel.Text = "Cancel";
                ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
                foreach (Button b in new[] { ok, cancel })
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = Theme.Input;
                    b.ForeColor = Theme.Text;
                    b.FlatAppearance.BorderColor = Theme.Border;
                    b.Size = new Size(Native.Px(88), Native.Px(30));
                }
                cancel.Location = new Point(w - x - cancel.Width, y);
                ok.Location = new Point(cancel.Left - Native.Px(8) - ok.Width, y);
                f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                f.ClientSize = new Size(w, y + ok.Height + Native.Px(14));
                f.HandleCreated += delegate
                {
                    int dk = Theme.Dark ? 1 : 0;
                    Native.DwmSetWindowAttribute(f.Handle, 20, ref dk, 4);
                };
                // The name must be free (its own old name is).
                f.FormClosing += delegate(object s, FormClosingEventArgs e)
                {
                    if (f.DialogResult != DialogResult.OK) return;
                    string n = ThemeFile.CleanName(nameBox.Text);
                    bool own = oldName != null && string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase);
                    if (n.Length == 0 || !own && ThemeFile.FreeName(n, Theme.Mine) != n)
                    {
                        MessageBox.Show(f, n.Length == 0 ? "Give the theme a name." : "There's a theme called “" + n + "” already.", f.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        e.Cancel = true;
                        nameBox.Focus();
                    }
                };
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                return result;
            }
        }
    }
}
