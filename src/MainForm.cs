using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TodoLite
{
    public class MainForm : Form
    {
        private readonly TodoStore _store = new TodoStore();
        private readonly InboxCompose _compose;
        private readonly TaskListView _list;
        private readonly ContextMenuStrip _todoMenu;
        private readonly ToolStripMenuItem _clearDoneItem;
        private readonly Timer _draftTimer;

        private bool _saveWarned;

        private int _dragIndex = -1;
        private Point _dragOrigin;
        private bool _dragging;
        private int _dropIndex = -1;

        public MainForm()
        {
            float scale = ScaleFactor();

            Text = "TodoLite";
            BackColor = Ui.WindowBack;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            ClientSize = new Size((int)(900 * scale), (int)(600 * scale));
            MinimumSize = new Size((int)(740 * scale), (int)(520 * scale));
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            ShowIcon = true;
            Icon = LoadAppIcon();

            _compose = new InboxCompose();
            _compose.Title = "\u6536\u4ef6\u7bb1";
            Controls.Add(_compose);

            _list = new TaskListView();
            _list.Title = "TODO";
            Controls.Add(_list);

            _clearDoneItem = new ToolStripMenuItem("\u6e05\u9664\u5df2\u5b8c\u6210");
            _clearDoneItem.Click += ClearDoneClick;

            _todoMenu = new ContextMenuStrip();
            _todoMenu.Font = Ui.Body;
            _todoMenu.BackColor = Ui.CardBack;
            _todoMenu.ForeColor = Ui.TextPrimary;
            _todoMenu.ShowImageMargin = false;
            _todoMenu.Items.Add(_clearDoneItem);
            _todoMenu.Opening += TodoMenuOpening;
            _list.ContextMenuStrip = _todoMenu;

            _draftTimer = new Timer();
            _draftTimer.Interval = 800;
            _draftTimer.Tick += DraftTimerTick;

            _compose.SubmitRequested += ComposeSubmit;
            _compose.DraftChanged += ComposeDraftChanged;
            _list.Command += ListCommand;
            _list.TextEdited += ListTextEdited;
            _list.MouseDown += ListMouseDown;
            _list.MouseMove += ListMouseMove;
            _list.MouseUp += ListMouseUp;

            _store.Load();
            _compose.SetDraft(_store.Draft);
            // 控件里会把换行规范化，这里同步回去，保持内存与界面一致。
            _store.SetDraft(_compose.DraftText);
            RefreshViews();
        }

        /// <summary>从嵌入资源读取 app.ico 作为窗口图标，保持标题栏与任务栏一致。</summary>
        private static Icon LoadAppIcon()
        {
            try
            {
                Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream("TodoLite.app.ico");
                if (stream == null) return null;
                using (stream)
                {
                    return new Icon(stream, SystemInformation.IconSize);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private float ScaleFactor()
        {
            try
            {
                using (Graphics g = CreateGraphics())
                {
                    float dpi = g.DpiX;
                    if (dpi <= 0) return 1f;
                    return dpi / 96f;
                }
            }
            catch (Exception)
            {
                return 1f;
            }
        }

        private int Scale(int value)
        {
            float factor = ScaleFactor();
            return (int)Math.Round(value * factor);
        }

        // ------------------------------------------------------------------
        // 生命周期与布局
        // ------------------------------------------------------------------
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LayoutCards();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutCards();
        }

        /// <summary>上下两段：上方收件箱约 1/3，下方 TODO 撑满剩余高度。</summary>
        private void LayoutCards()
        {
            if (_compose == null || _list == null) return;

            int pad = Scale(24);
            int gap = Scale(18);
            int width = ClientSize.Width - pad * 2;
            int height = ClientSize.Height - pad * 2;
            if (width < Scale(80) || height < Scale(120)) return;

            int minTop = Scale(130);
            int minBottom = Scale(150);
            int top = (int)Math.Round(height * 0.34);
            if (top < minTop) top = minTop;
            if (top > height - minBottom - gap) top = height - minBottom - gap;
            if (top < Scale(70)) top = Math.Max(Scale(60), height / 3);
            int bottom = height - top - gap;
            if (bottom < Scale(60)) bottom = Math.Max(Scale(40), bottom);

            _compose.SetBounds(pad, pad, width, top);
            _list.SetBounds(pad, pad + top + gap, width, bottom);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (_store.MigratedOnLoad)
            {
                // 旧版收件箱条目已并入 TODO，落盘一次完成格式升级。
                Save();
            }

            _compose.FocusCompose();

            if (_store.CorruptOnLoad)
            {
                MessageBox.Show(this,
                    "\u6570\u636e\u6587\u4ef6\u5df2\u635f\u574f\uff0c\u5df2\u5907\u4efd\u4e3a todo-data.json.bak\uff0c\u7a0b\u5e8f\u5df2\u4ee5\u7a7a\u5217\u8868\u542f\u52a8\u3002",
                    "TodoLite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _draftTimer.Stop();
            if (_compose != null) _store.SetDraft(_compose.DraftText);
            Save();
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------
        // 视图刷新
        // ------------------------------------------------------------------
        private void RefreshViews()
        {
            List<ITaskItem> items = new List<ITaskItem>(_store.Todo.Count);
            int undone = 0;
            for (int i = 0; i < _store.Todo.Count; i++)
            {
                TodoItem item = _store.Todo[i];
                items.Add(item);
                if (!item.Done) undone++;
            }
            _list.SetItems(items);
            _list.Count = undone;
        }

        private void Save()
        {
            if (_store.Save()) return;
            if (_saveWarned) return;
            _saveWarned = true;
            MessageBox.Show(this,
                "\u65e0\u6cd5\u5199\u5165\u6570\u636e\u6587\u4ef6\uff1a\r\n" + _store.DataFilePath,
                "TodoLite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ------------------------------------------------------------------
        // 收件箱：提交与草稿
        // ------------------------------------------------------------------
        private void ComposeSubmit(object sender, TextEventArgs e)
        {
            if (e.Text == null || e.Text.Length == 0) return;
            _store.Todo.Add(new TodoItem(e.Text));
            _store.SetDraft(_compose.DraftText);
            Save();
            RefreshViews();
            _list.SelectedIndex = _store.Todo.Count - 1;
        }

        private void ComposeDraftChanged(object sender, EventArgs e)
        {
            _store.SetDraft(_compose.DraftText);
            _draftTimer.Stop();
            _draftTimer.Start();
        }

        private void DraftTimerTick(object sender, EventArgs e)
        {
            _draftTimer.Stop();
            _store.SetDraft(_compose.DraftText);
            Save();
        }

        // ------------------------------------------------------------------
        // 行内按钮：勾选 / 删除
        // ------------------------------------------------------------------
        private void ListCommand(object sender, TaskItemEventArgs e)
        {
            int index = e.Index;
            if (index < 0 || index >= _store.Todo.Count) return;

            if (e.Command == TaskCommand.Delete)
            {
                _store.Todo.RemoveAt(index);
            }
            else if (e.Command == TaskCommand.Toggle)
            {
                _store.Todo[index].Done = !_store.Todo[index].Done;
            }
            else
            {
                return;
            }

            Save();
            RefreshViews();
        }

        private void ListTextEdited(object sender, TaskItemEventArgs e)
        {
            if (e.Text == null) return;
            if (e.Index < 0 || e.Index >= _store.Todo.Count) return;
            _store.Todo[e.Index].Text = e.Text;
            Save();
            RefreshViews();
        }

        private void TodoMenuOpening(object sender, CancelEventArgs e)
        {
            bool hasDone = false;
            for (int i = 0; i < _store.Todo.Count; i++)
            {
                if (_store.Todo[i].Done) { hasDone = true; break; }
            }
            _clearDoneItem.Enabled = hasDone;
        }

        private void ClearDoneClick(object sender, EventArgs e)
        {
            for (int i = _store.Todo.Count - 1; i >= 0; i--)
            {
                if (_store.Todo[i].Done) _store.Todo.RemoveAt(i);
            }
            Save();
            RefreshViews();
        }

        // ------------------------------------------------------------------
        // 快捷键：F2 编辑、Delete 删除
        // ------------------------------------------------------------------
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.D1) || keyData == (Keys.Control | Keys.NumPad1))
            {
                _compose.FocusCompose();
                return true;
            }
            if (keyData == (Keys.Control | Keys.D2) || keyData == (Keys.Control | Keys.NumPad2))
            {
                _list.Focus();
                return true;
            }

            bool typing = _compose.TextHasFocus || _list.IsEditing;
            if (!typing)
            {
                if (keyData == Keys.F2)
                {
                    _list.BeginEditSelected();
                    return true;
                }
                if (keyData == Keys.Delete)
                {
                    DeleteSelected();
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void DeleteSelected()
        {
            int index = _list.SelectedIndex;
            if (index < 0 || index >= _store.Todo.Count) return;
            _store.Todo.RemoveAt(index);
            Save();
            RefreshViews();
            _list.SelectedIndex = index >= _list.ItemCount ? _list.ItemCount - 1 : index;
        }

        // ------------------------------------------------------------------
        // 拖拽（仅在 TODO 列表内重排）
        // ------------------------------------------------------------------
        private void ListMouseDown(object sender, MouseEventArgs e)
        {
            FinishDrag();
            if (e.Button != MouseButtons.Left) return;
            if (_list.PressedButton != TaskCommand.None) return;

            int index;
            if (!_list.TryHitRow(e.Location, out index)) return;

            _dragIndex = index;
            _dragOrigin = _list.PointToScreen(e.Location);
        }

        private void ListMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragIndex < 0) return;

            if (!_dragging)
            {
                Point screen = _list.PointToScreen(e.Location);
                Size limit = SystemInformation.DragSize;
                if (Math.Abs(screen.X - _dragOrigin.X) < limit.Width
                    && Math.Abs(screen.Y - _dragOrigin.Y) < limit.Height) return;

                _dragging = true;
                _list.DragActive = true;
                _list.SetDragSource(_dragIndex);
                _list.Cursor = Cursors.Default;
            }

            _dropIndex = _list.GetInsertIndex(e.Location);
            _list.SetDropIndicator(_dropIndex);
        }

        private void ListMouseUp(object sender, MouseEventArgs e)
        {
            if (_dragging && _dragIndex >= 0 && _dropIndex >= 0)
            {
                int from = _dragIndex;
                int insert = _dropIndex;
                FinishDrag();
                Reorder(from, insert);
                return;
            }
            FinishDrag();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            FinishDrag();
            base.OnMouseUp(e);
        }

        private void FinishDrag()
        {
            if (_dragging || _dragIndex >= 0)
            {
                _list.DragActive = false;
                _list.ClearDragSource();
                _list.ClearPressedState();
            }
            _list.ClearDropIndicator();
            _dragging = false;
            _dragIndex = -1;
            _dropIndex = -1;
        }

        private void Reorder(int from, int insert)
        {
            if (from < 0 || from >= _store.Todo.Count) return;

            int target = insert;
            if (target > from) target--;
            if (target < 0) target = 0;
            if (target > _store.Todo.Count - 1) target = _store.Todo.Count - 1;
            if (target == from) return;

            TodoItem item = _store.Todo[from];
            _store.Todo.RemoveAt(from);
            _store.Todo.Insert(target, item);

            Save();
            RefreshViews();
            _list.SelectedIndex = target;
            _list.Focus();
        }
    }
}
