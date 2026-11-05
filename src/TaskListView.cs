using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TodoLite
{
    public enum TaskCommand
    {
        None = 0,
        Toggle = 1,
        Delete = 2
    }

    public class TaskItemEventArgs : EventArgs
    {
        public ITaskItem Item { get; set; }
        public int Index { get; set; }
        public TaskCommand Command { get; set; }
        public string Text { get; set; }

        public TaskItemEventArgs(ITaskItem item, int index)
        {
            this.Item = item;
            this.Index = index;
            this.Command = TaskCommand.None;
            this.Text = null;
        }
    }

    public class TextEventArgs : EventArgs
    {
        public string Text { get; private set; }

        public TextEventArgs(string text)
        {
            this.Text = text;
        }
    }

    /// <summary>统一的配色与字体，保持"一张好纸"的温暖极简。</summary>
    internal static class Ui
    {
        public static readonly Color WindowBack = Color.FromArgb(246, 245, 244);
        public static readonly Color CardBack = Color.FromArgb(255, 255, 255);
        public static readonly Color CardBorder = Color.FromArgb(234, 234, 234);
        public static readonly Color TextPrimary = Color.FromArgb(97, 93, 89);
        public static readonly Color TextMuted = Color.FromArgb(154, 148, 142);
        public static readonly Color TextDone = Color.FromArgb(183, 177, 171);
        public static readonly Color TextFaint = Color.FromArgb(201, 196, 191);
        public static readonly Color RowHover = Color.FromArgb(245, 243, 241);
        public static readonly Color RowSelect = Color.FromArgb(239, 237, 234);
        public static readonly Color CheckBorder = Color.FromArgb(210, 205, 200);
        public static readonly Color InputBorder = Color.FromArgb(217, 213, 209);
        public static readonly Color Danger = Color.FromArgb(176, 74, 63);
        public static readonly Color Indicator = Color.FromArgb(63, 60, 57);

        public static readonly Font Body;
        public static readonly Font BodyStrike;
        public static readonly Font Small;
        public static readonly Font Title;
        public static readonly Font Empty;

        static Ui()
        {
            string sans = PickFamily(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" });
            if (sans == null) sans = "Microsoft Sans Serif";

            Body = Make(sans, 11F, FontStyle.Regular);
            BodyStrike = Make(sans, 11F, FontStyle.Strikeout);
            Small = Make(sans, 8.5F, FontStyle.Regular);
            Empty = Make(sans, 9F, FontStyle.Regular);

            string serif = PickFamily(new string[] { "Source Han Serif SC", "Noto Serif SC", "Songti SC", "SimSun" });
            if (serif == null) serif = sans;
            Title = Make(serif, 15F, FontStyle.Bold);
        }

        private static string PickFamily(string[] candidates)
        {
            FontFamily[] installed;
            try
            {
                installed = FontFamily.Families;
            }
            catch (Exception)
            {
                return null;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                for (int j = 0; j < installed.Length; j++)
                {
                    if (string.Equals(installed[j].Name, candidates[i], StringComparison.OrdinalIgnoreCase))
                        return candidates[i];
                }
            }
            return null;
        }

        private static Font Make(string family, float size, FontStyle style)
        {
            try
            {
                Font font = new Font(family, size, style);
                if (font.Name != null && font.Name.Length > 0) return font;
                font.Dispose();
            }
            catch (Exception)
            {
            }
            return new Font("Microsoft Sans Serif", size, style);
        }

        public static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return path;
            }
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            int d = radius * 2;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// TODO 列表卡片：标题与未完成计数、可滚动的任务行（行完全自绘）。
    /// 不提供创建入口，任务全部来自收件箱。
    /// </summary>
    public class TaskListView : Control
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int EM_SETMARGINS = 0x00D3;
        private const int EC_LEFTMARGIN = 0x0001;
        private const int EC_RIGHTMARGIN = 0x0002;

        private readonly List<ITaskItem> _items = new List<ITaskItem>();
        private readonly TextBox _editor;

        private string _title = "TODO";
        private int _count;

        private int _selected = -1;
        private int _hover = -1;
        private int _scroll;
        private int _editIndex = -1;
        private int _dropIndex = -1;
        private int _dragSource = -1;

        private TaskCommand _pressedButton = TaskCommand.None;
        private TaskCommand _hoverButton = TaskCommand.None;
        private int _pressedIndex = -1;
        private bool _ending;

        public event EventHandler<TaskItemEventArgs> Command;
        public event EventHandler<TaskItemEventArgs> TextEdited;

        public TaskListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.Selectable, true);
            BackColor = Ui.CardBack;
            TabStop = false;

            _editor = new TextBox();
            _editor.BorderStyle = BorderStyle.None;
            _editor.Font = Ui.Body;
            _editor.ForeColor = Ui.TextPrimary;
            _editor.BackColor = Ui.RowSelect;
            _editor.AutoSize = false;
            _editor.Visible = false;
            _editor.KeyDown += EditorKeyDown;
            _editor.LostFocus += EditorLostFocus;
            Controls.Add(_editor);
        }

        // ------------------------------------------------------------------
        // 公开属性
        // ------------------------------------------------------------------
        public string Title
        {
            get { return _title; }
            set { _title = value == null ? string.Empty : value; Invalidate(); }
        }

        public int Count
        {
            get { return _count; }
            set
            {
                if (_count == value) return;
                _count = value;
                Invalidate();
            }
        }

        public bool IsEditing
        {
            get { return _editIndex >= 0; }
        }

        public TaskCommand PressedButton
        {
            get { return _pressedButton; }
        }

        public bool DragActive { get; set; }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = value;
                if (v < -1) v = -1;
                if (v >= _items.Count) v = _items.Count - 1;
                if (_selected == v) return;
                _selected = v;
                if (v >= 0) EnsureVisible(v);
                Invalidate();
            }
        }

        public ITaskItem SelectedItem
        {
            get
            {
                if (_selected < 0 || _selected >= _items.Count) return null;
                return _items[_selected];
            }
        }

        public int ItemCount
        {
            get { return _items.Count; }
        }

        public List<ITaskItem> Items
        {
            get { return _items; }
        }

        public ITaskItem ItemAt(int index)
        {
            if (index < 0 || index >= _items.Count) return null;
            return _items[index];
        }

        // ------------------------------------------------------------------
        // 尺寸与布局（按系统 DPI 缩放）
        // ------------------------------------------------------------------
        private int Scale(int value)
        {
            int dpi = this.DeviceDpi;
            if (dpi <= 0) dpi = 96;
            return (int)Math.Round(value * dpi / 96.0);
        }

        private int Pad { get { return Scale(20); } }
        private int HeaderTop { get { return Scale(16); } }
        private int HeaderHeight { get { return Ui.Title.Height + Scale(4); } }
        private int HeaderBottom { get { return HeaderTop + HeaderHeight; } }

        public int ListTop { get { return HeaderBottom + Scale(10); } }
        public int ListBottom { get { return Height - Scale(14); } }
        public int RowHeight { get { return Scale(40); } }

        private Rectangle RowBounds(int index)
        {
            int top = ListTop + index * RowHeight - _scroll;
            return new Rectangle(Scale(10), top, Math.Max(0, Width - Scale(20)), RowHeight);
        }

        private Rectangle CheckRect(Rectangle row)
        {
            int size = Scale(15);
            return new Rectangle(row.X + Scale(10), row.Y + (row.Height - size) / 2, size, size);
        }

        private Rectangle DeleteRect(Rectangle row)
        {
            int size = Scale(22);
            return new Rectangle(row.Right - Scale(6) - size, row.Y + (row.Height - size) / 2, size, size);
        }

        private Rectangle TextRect(Rectangle row)
        {
            int x = CheckRect(row).Right + Scale(10);
            int right = DeleteRect(row).X - Scale(6);
            int width = right - x;
            if (width < Scale(24)) width = Scale(24);
            return new Rectangle(x, row.Y, width, row.Height);
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = Ui.RoundRect(new Rectangle(0, 0, Width, Height), Scale(8)))
            {
                Region old = this.Region;
                this.Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
            ClampScroll();
            Invalidate();
        }

        // ------------------------------------------------------------------
        // 数据
        // ------------------------------------------------------------------
        public void SetItems(List<ITaskItem> items)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);

            if (_editIndex >= 0)
            {
                _ending = true;
                _editor.Visible = false;
                _editIndex = -1;
                _ending = false;
            }

            if (_selected >= _items.Count) _selected = _items.Count - 1;
            if (_hover >= _items.Count) _hover = -1;
            if (_dragSource >= _items.Count) _dragSource = -1;

            ClampScroll();
            Invalidate();
        }

        // ------------------------------------------------------------------
        // 命中测试（拖拽由 MainForm 复用）
        // ------------------------------------------------------------------
        public bool TryHitRow(Point p, out int index)
        {
            index = -1;
            if (p.Y < ListTop || p.Y >= ListBottom) return false;
            if (p.X < Scale(6) || p.X > Width - Scale(6)) return false;
            int rowH = RowHeight;
            if (rowH <= 0) return false;
            int i = (p.Y - ListTop + _scroll) / rowH;
            if (i < 0 || i >= _items.Count) return false;
            index = i;
            return true;
        }

        public int GetInsertIndex(Point p)
        {
            if (_items.Count == 0) return 0;
            if (p.Y <= ListTop) return 0;
            int rowH = RowHeight;
            if (rowH <= 0) return _items.Count;
            int contentTop = ListTop - _scroll;
            for (int i = 0; i < _items.Count; i++)
            {
                int top = contentTop + i * rowH;
                if (p.Y < top + rowH / 2) return i;
            }
            return _items.Count;
        }

        public void SetDropIndicator(int index)
        {
            if (_dropIndex == index) return;
            _dropIndex = index;
            Invalidate();
        }

        public void ClearDropIndicator()
        {
            if (_dropIndex < 0) return;
            _dropIndex = -1;
            Invalidate();
        }

        public void SetDragSource(int index)
        {
            _dragSource = index;
            Invalidate();
        }

        public void ClearDragSource()
        {
            _dragSource = -1;
            Invalidate();
        }

        public void ClearHover()
        {
            if (_hover < 0 && _hoverButton == TaskCommand.None) return;
            _hover = -1;
            _hoverButton = TaskCommand.None;
            Invalidate();
        }

        public void ClearPressedState()
        {
            _pressedButton = TaskCommand.None;
            _pressedIndex = -1;
        }

        public void BeginEditSelected()
        {
            BeginEdit(_selected);
        }

        public void BeginEdit(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            EnsureVisible(index);

            Point p = PointToClient(Cursor.Position);
            int hit;
            if (TryHitRow(p, out hit) && hit == index)
            {
                Rectangle hr = RowBounds(index);
                if (CheckRect(hr).Contains(p)) return;
                if (DeleteRect(hr).Contains(p)) return;
            }

            _editIndex = index;
            _selected = index;
            Rectangle row = RowBounds(index);
            Rectangle textRect = TextRect(row);

            // 编辑框比文字高一些，并留出左右内边距，避免文字贴边显得拥挤
            int leftExt = Scale(10);
            int rightExt = Scale(4);
            int boxHeight = Math.Max(_editor.PreferredHeight, row.Height - Scale(8));
            _editor.Text = _items[index].Text;
            _editor.Bounds = new Rectangle(
                textRect.X - leftExt,
                row.Y + (row.Height - boxHeight) / 2,
                Math.Max(Scale(80), textRect.Width + leftExt + rightExt),
                boxHeight);
            _editor.Visible = true;
            _editor.BringToFront();
            ApplyEditorMargins();
            _editor.Focus();
            _editor.SelectAll();
            Invalidate();
        }

        private void ApplyEditorMargins()
        {
            try
            {
                int margin = Scale(8);
                IntPtr handle = _editor.Handle;
                IntPtr lParam = (IntPtr)((margin << 16) | (margin & 0xFFFF));
                SendMessage(handle, EM_SETMARGINS, (IntPtr)(EC_LEFTMARGIN | EC_RIGHTMARGIN), lParam);
            }
            catch (Exception)
            {
            }
        }

        private void EndEditing(bool commit)
        {
            if (_editIndex < 0 || _ending) return;
            _ending = true;
            int index = _editIndex;
            _editIndex = -1;
            string text = _editor.Text.Trim();
            bool hadFocus = _editor.Focused;
            _editor.Visible = false;
            _ending = false;

            if (commit && text.Length > 0 && index >= 0 && index < _items.Count && _items[index].Text != text)
            {
                if (TextEdited != null)
                {
                    TaskItemEventArgs args = new TaskItemEventArgs(_items[index], index);
                    args.Text = text;
                    TextEdited(this, args);
                }
            }

            if (hadFocus) this.Focus();
            Invalidate();
        }

        private void ClampScroll()
        {
            int view = ListBottom - ListTop;
            int content = _items.Count * RowHeight;
            int max = content - view;
            if (max < 0) max = 0;
            if (_scroll > max) _scroll = max;
            if (_scroll < 0) _scroll = 0;
        }

        private void EnsureVisible(int index)
        {
            int view = ListBottom - ListTop;
            if (view <= 0) return;
            int top = index * RowHeight;
            if (top < _scroll) _scroll = top;
            else if (top + RowHeight > _scroll + view) _scroll = top + RowHeight - view;
            ClampScroll();
        }

        // ------------------------------------------------------------------
        // 绘制
        // ------------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (SolidBrush bg = new SolidBrush(Ui.CardBack))
                g.FillRectangle(bg, ClientRectangle);

            Color border = Focused ? Ui.InputBorder : Ui.CardBorder;
            using (GraphicsPath path = Ui.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Scale(8)))
            using (Pen pen = new Pen(border, 1f))
            {
                g.DrawPath(pen, path);
            }

            PaintHeader(g);
            PaintRows(g);
            PaintIndicator(g);
        }

        private void PaintHeader(Graphics g)
        {
            Rectangle rect = new Rectangle(Pad, HeaderTop, Math.Max(0, Width - Pad * 2), HeaderHeight);
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                  | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                                  | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, _title, Ui.Title, rect, Ui.TextPrimary, flags);

            TextFormatFlags countFlags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter
                                       | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, _count.ToString(), Ui.Small, rect, Ui.TextMuted, countFlags);
        }

        private void PaintRows(Graphics g)
        {
            int top = ListTop;
            int bottom = ListBottom;
            if (bottom <= top) return;

            GraphicsState state = g.Save();
            g.SetClip(new Rectangle(0, top, Width, bottom - top));

            if (_items.Count == 0)
            {
                PaintEmpty(g, top, bottom);
            }
            else
            {
                int rowH = RowHeight;
                int first = _scroll / Math.Max(1, rowH);
                if (first < 0) first = 0;
                for (int i = first; i < _items.Count; i++)
                {
                    int rowTop = top + i * rowH - _scroll;
                    if (rowTop >= bottom) break;
                    if (rowTop + rowH > top) PaintRow(g, i, rowTop);
                }
            }

            g.Restore(state);
        }

        private void PaintEmpty(Graphics g, int top, int bottom)
        {
            int h = bottom - top;
            Rectangle area = new Rectangle(Pad, top, Math.Max(0, Width - Pad * 2), h);
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                                  | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            int offset = Scale(8);
            Rectangle main = new Rectangle(area.X, area.Y - offset, area.Width, area.Height);
            TextRenderer.DrawText(g, "\u6682\u65e0\u5f85\u529e", Ui.Empty, main, Ui.TextFaint, flags);

            Rectangle hint = new Rectangle(area.X, area.Y + Scale(14), area.Width, area.Height);
            TextRenderer.DrawText(g, "\u5728\u4e0a\u65b9\u5199\u4e0b\u8981\u505a\u7684\u4e8b\uff0c\u6309 Enter \u8bb0\u5165", Ui.Small, hint, Ui.TextFaint, flags);
        }

        private void PaintRow(Graphics g, int index, int top)
        {
            ITaskItem item = _items[index];
            Rectangle row = new Rectangle(Scale(10), top, Math.Max(0, Width - Scale(20)), RowHeight);
            bool hover = index == _hover;
            bool selected = index == _selected && Focused;
            bool dragging = index == _dragSource;

            if (selected || hover)
            {
                using (GraphicsPath path = Ui.RoundRect(row, Scale(6)))
                using (SolidBrush brush = new SolidBrush(selected ? Ui.RowSelect : Ui.RowHover))
                {
                    g.FillPath(brush, path);
                }
            }

            bool done = (item is TodoItem) && ((TodoItem)item).Done;
            PaintCheckbox(g, row, done);

            Rectangle textRect = TextRect(row);
            Color textColor = done ? Ui.TextDone : (dragging ? Ui.TextFaint : Ui.TextPrimary);
            Font font = done ? Ui.BodyStrike : Ui.Body;
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                  | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                                  | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, item.Text, font, textRect, textColor, flags);

            bool highlight = hover || selected;
            Color deleteColor = (_hoverButton == TaskCommand.Delete && index == _hover)
                ? Ui.Danger
                : (highlight ? Ui.TextMuted : Ui.TextFaint);

            PaintGlyph(g, DeleteRect(row), "\u00d7", deleteColor);
        }

        private void PaintCheckbox(Graphics g, Rectangle row, bool done)
        {
            Rectangle box = CheckRect(row);
            using (GraphicsPath path = Ui.RoundRect(box, Scale(4)))
            {
                if (done)
                {
                    using (SolidBrush brush = new SolidBrush(Ui.TextPrimary))
                        g.FillPath(brush, path);

                    using (Pen pen = new Pen(Color.White, Math.Max(1.5f, Scale(2) * 0.8f)))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                        int x = box.X;
                        int y = box.Y;
                        int w = box.Width;
                        int h = box.Height;
                        g.DrawLines(pen, new Point[]
                        {
                            new Point(x + (int)(w * 0.26), y + (int)(h * 0.52)),
                            new Point(x + (int)(w * 0.43), y + (int)(h * 0.70)),
                            new Point(x + (int)(w * 0.75), y + (int)(h * 0.32))
                        });
                    }
                }
                else
                {
                    using (Pen pen = new Pen(Ui.CheckBorder, 1f))
                        g.DrawPath(pen, path);
                }
            }
        }

        private void PaintGlyph(Graphics g, Rectangle rect, string glyph, Color color)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                                  | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, glyph, Ui.Body, rect, color, flags);
        }

        private void PaintIndicator(Graphics g)
        {
            if (_dropIndex < 0) return;
            int top = ListTop;
            int bottom = ListBottom;
            if (bottom <= top) return;

            int y = ListTop - _scroll + _dropIndex * RowHeight;
            int maxY = Math.Min(bottom, ListTop + Math.Max(0, _items.Count * RowHeight - _scroll));
            if (maxY < top) maxY = top;
            if (y < top) y = top;
            if (y > maxY) y = maxY;

            using (Pen pen = new Pen(Ui.Indicator, Scale(2)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, Scale(14), y, Width - Scale(14), y);
            }
        }

        // ------------------------------------------------------------------
        // 鼠标
        // ------------------------------------------------------------------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressedButton = TaskCommand.None;
            _pressedIndex = -1;

            if (e.Button == MouseButtons.Left)
            {
                if (_editIndex >= 0)
                {
                    Rectangle er = _editor.Bounds;
                    if (!er.Contains(e.Location)) EndEditing(true);
                }

                int index;
                if (TryHitRow(e.Location, out index))
                {
                    Focus();
                    _selected = index;
                    Rectangle row = RowBounds(index);
                    if (CheckRect(row).Contains(e.Location))
                    {
                        _pressedButton = TaskCommand.Toggle;
                        _pressedIndex = index;
                    }
                    else if (DeleteRect(row).Contains(e.Location))
                    {
                        _pressedButton = TaskCommand.Delete;
                        _pressedIndex = index;
                    }
                }

                Capture = true;
            }

            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int index;
            bool overRow = TryHitRow(e.Location, out index);
            int newHover = overRow ? index : -1;
            TaskCommand newButton = overRow ? HitButton(index, e.Location) : TaskCommand.None;

            if (newHover != _hover || newButton != _hoverButton)
            {
                _hover = newHover;
                _hoverButton = newButton;
                if (!DragActive) Cursor = newButton != TaskCommand.None ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        private TaskCommand HitButton(int index, Point p)
        {
            if (index < 0 || index >= _items.Count) return TaskCommand.None;
            Rectangle row = RowBounds(index);
            if (CheckRect(row).Contains(p)) return TaskCommand.Toggle;
            if (DeleteRect(row).Contains(p)) return TaskCommand.Delete;
            return TaskCommand.None;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (Capture) Capture = false;

            TaskCommand command = _pressedButton;
            int pressedIndex = _pressedIndex;
            _pressedButton = TaskCommand.None;
            _pressedIndex = -1;

            if (command != TaskCommand.None)
            {
                int index;
                if (TryHitRow(e.Location, out index) && index == pressedIndex && index < _items.Count && Command != null)
                {
                    TaskItemEventArgs args = new TaskItemEventArgs(_items[index], index);
                    args.Command = command;
                    Command(this, args);
                }
                Invalidate();
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (Capture) return;
            _hover = -1;
            _hoverButton = TaskCommand.None;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            if (DragActive) return;
            Point p = PointToClient(Cursor.Position);
            int index;
            if (!TryHitRow(p, out index)) return;
            Rectangle row = RowBounds(index);
            if (CheckRect(row).Contains(p)) return;
            if (DeleteRect(row).Contains(p)) return;
            BeginEdit(index);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int before = _scroll;
            _scroll -= (e.Delta / 120) * RowHeight * 2;
            ClampScroll();
            if (before != _scroll) Invalidate();
        }

        // ------------------------------------------------------------------
        // 键盘
        // ------------------------------------------------------------------
        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down) MoveSelection(1);
            else if (e.KeyCode == Keys.Up) MoveSelection(-1);
        }

        private void MoveSelection(int delta)
        {
            if (_items.Count == 0) return;
            int next = _selected < 0 ? (delta > 0 ? 0 : _items.Count - 1) : _selected + delta;
            if (next < 0) next = 0;
            if (next >= _items.Count) next = _items.Count - 1;
            _selected = next;
            EnsureVisible(next);
            Invalidate();
        }

        private void EditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                EndEditing(true);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                EndEditing(false);
            }
        }

        private void EditorLostFocus(object sender, EventArgs e)
        {
            if (_ending) return;
            EndEditing(true);
        }
    }
}
