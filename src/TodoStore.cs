using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TodoLite
{
    /// <summary>
    /// 任务项契约。只有 id 和文本，刻意不含任何时间/日期类字段。
    /// </summary>
    public interface ITaskItem
    {
        string Id { get; set; }
        string Text { get; set; }
    }

    /// <summary>TODO 任务：{ "id": "...", "text": "...", "done": false }</summary>
    public class TodoItem : ITaskItem
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public bool Done { get; set; }

        public TodoItem()
        {
            this.Id = Guid.NewGuid().ToString();
            this.Text = string.Empty;
            this.Done = false;
        }

        public TodoItem(string text)
        {
            this.Id = Guid.NewGuid().ToString();
            this.Text = text;
            this.Done = false;
        }

        public static TodoItem Create(string id, string text, bool done)
        {
            TodoItem item = new TodoItem();
            if (id != null && id.Length > 0) item.Id = id;
            item.Text = text == null ? string.Empty : text;
            item.Done = done;
            return item;
        }
    }

    /// <summary>
    /// 数据文件的读取与写入。
    /// 文件的绝对路径优先取 exe 同目录的 todo-data.json，不可写时回退到 %APPDATA%\TodoLite\。
    /// </summary>
    public class TodoStore
    {
        public const int CurrentVersion = 2;
        private const string FileName = "todo-data.json";

        private readonly string _dataFilePath;

        /// <summary>唯一的任务集合，数组顺序即显示顺序。</summary>
        public List<TodoItem> Todo { get; private set; }

        /// <summary>收件箱里还没提交的草稿文本。</summary>
        public string Draft { get; private set; }

        /// <summary>加载时发现文件损坏并已备份。</summary>
        public bool CorruptOnLoad { get; private set; }

        /// <summary>加载时把旧版收件箱条目迁移进了 TODO，需要落盘一次。</summary>
        public bool MigratedOnLoad { get; private set; }

        public string DataFilePath
        {
            get { return _dataFilePath; }
        }

        public TodoStore()
        {
            this.Todo = new List<TodoItem>();
            this.Draft = string.Empty;
            _dataFilePath = ResolveDataFilePath();
        }

        public void SetDraft(string value)
        {
            this.Draft = value == null ? string.Empty : value;
        }

        // ------------------------------------------------------------------
        // 路径解析
        // ------------------------------------------------------------------
        private static string ResolveDataFilePath()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            if (DirectoryWritable(exeDir))
                return Path.Combine(exeDir, FileName);

            string appData = string.Empty;
            try
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            catch (Exception)
            {
                appData = string.Empty;
            }

            if (appData.Length == 0)
                return Path.Combine(exeDir, FileName);

            string dir = Path.Combine(appData, "TodoLite");
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception)
            {
                // 创建失败时仍返回该路径，保存阶段会再次报错，由界面提示一次。
            }
            return Path.Combine(dir, FileName);
        }

        private static bool DirectoryWritable(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            string probe = Path.Combine(dir, ".todolite.write-probe");
            try
            {
                using (FileStream fs = new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.WriteByte(0);
                }
                File.Delete(probe);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        // 加载
        // ------------------------------------------------------------------
        public void Load()
        {
            this.Todo = new List<TodoItem>();
            this.Draft = string.Empty;
            this.CorruptOnLoad = false;
            this.MigratedOnLoad = false;

            if (!File.Exists(_dataFilePath)) return;

            try
            {
                string json = File.ReadAllText(_dataFilePath, Encoding.UTF8);
                if (json == null || json.Trim().Length == 0) return;

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 32 * 1024 * 1024;
                TodoData data = serializer.Deserialize<TodoData>(json);
                if (data == null)
                {
                    HandleCorrupt();
                    return;
                }

                if (data.Todo != null)
                {
                    foreach (TodoItem item in data.Todo)
                    {
                        if (item == null) continue;
                        if (item.Id == null || item.Id.Length == 0) item.Id = Guid.NewGuid().ToString();
                        if (item.Text == null) item.Text = string.Empty;
                        this.Todo.Add(item);
                    }
                }

                if (data.Draft != null) this.Draft = data.Draft;

                // 旧版数据里的收件箱条目：逐条转成未完成的 TODO，追加到末尾，不丢内容。
                if (data.Inbox != null && data.Inbox.Count > 0)
                {
                    foreach (TodoItem legacy in data.Inbox)
                    {
                        if (legacy == null) continue;
                        string text = legacy.Text == null ? string.Empty : legacy.Text;
                        if (text.Trim().Length == 0) continue;
                        this.Todo.Add(TodoItem.Create(legacy.Id, text, false));
                    }
                    this.MigratedOnLoad = true;
                }
            }
            catch (Exception)
            {
                HandleCorrupt();
            }
        }

        private void HandleCorrupt()
        {
            this.Todo = new List<TodoItem>();
            this.Draft = string.Empty;
            this.CorruptOnLoad = true;

            try
            {
                string backup = _dataFilePath + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                if (File.Exists(_dataFilePath)) File.Move(_dataFilePath, backup);
            }
            catch (Exception)
            {
                // 备份失败不影响空数据启动。
            }
        }

        // ------------------------------------------------------------------
        // 保存
        // ------------------------------------------------------------------
        public bool Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(_dataFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = ToJson();
                string temp = _dataFilePath + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));

                if (File.Exists(_dataFilePath))
                {
                    try
                    {
                        File.Replace(temp, _dataFilePath, null);
                        return true;
                    }
                    catch (Exception)
                    {
                        // 某些文件系统不支持 Replace，退化为直接覆盖。
                        File.WriteAllText(_dataFilePath, json, new UTF8Encoding(false));
                        try { File.Delete(temp); }
                        catch (Exception) { }
                        return true;
                    }
                }

                File.Move(temp, _dataFilePath);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 手写序列化：字段名与顺序完全固定，杜绝多出任何多余字段。
        /// </summary>
        public string ToJson()
        {
            StringBuilder sb = new StringBuilder(1024);
            sb.Append("{\r\n");
            sb.Append("  \"version\": ").Append(CurrentVersion).Append(",\r\n");
            sb.Append("  \"draft\": ").Append(Quote(Draft)).Append(",\r\n");

            sb.Append("  \"todo\": [");
            for (int i = 0; i < Todo.Count; i++)
            {
                TodoItem item = Todo[i];
                if (i > 0) sb.Append(',');
                sb.Append("\r\n    { \"id\": ").Append(Quote(item.Id));
                sb.Append(", \"text\": ").Append(Quote(item.Text));
                sb.Append(", \"done\": ").Append(item.Done ? "true" : "false").Append(" }");
            }
            if (Todo.Count > 0) sb.Append("\r\n  ");
            sb.Append("]\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        private static string Quote(string value)
        {
            if (value == null) return "\"\"";
            StringBuilder sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>反序列化中间结构，字段与 JSON 一一对应。</summary>
        private class TodoData
        {
            public int Version { get; set; }
            public string Draft { get; set; }
            public List<TodoItem> Todo { get; set; }

            /// <summary>仅用于读取旧版文件的收件箱字段（旧结构没有 done，反序列化后即 false）。</summary>
            public List<TodoItem> Inbox { get; set; }
        }
    }
}
