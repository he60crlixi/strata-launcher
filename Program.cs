// Strata Launcher - one window with the web UI on top and a terminal below.
// Never starts the engine on its own: only the "启动模型" button does that.
// Build: see build.bat (VS Roslyn csc + the WebView2 assemblies in .\lib).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

static class Cfg
{
    public static string Root;
    public static string Python;
    public static string ServerPy;
    public static string RunConfig;
    public static string Url;
    public static int Port;
    public static readonly string StartHtml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "start.html");
    public static string Note = "";          // load problems, shown once in the terminal

    static Cfg()
    {
        Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Strata");
        Port = 8080;
        string cfg = null;

        string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.json");
        if (File.Exists(file))
        {
            try
            {
                var d = new System.Web.Script.Serialization.JavaScriptSerializer()
                            .Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
                if (d != null)
                {
                    if (d.ContainsKey("root") && d["root"] != null) Root = d["root"].ToString();
                    if (d.ContainsKey("port") && d["port"] != null) Port = int.Parse(d["port"].ToString().Trim());
                    if (d.ContainsKey("config") && d["config"] != null) cfg = d["config"].ToString();
                }
            }
            catch (Exception ex) { Note = "launcher.json 读取失败(" + ex.Message + "),改用默认值。"; }
        }

        Root = Environment.ExpandEnvironmentVariables(Root);
        Python = Path.Combine(Root, @".venv\Scripts\python.exe");
        ServerPy = Path.Combine(Root, @"serve\server.py");
        Url = "http://127.0.0.1:" + Port;

        RunConfig = (cfg != null && cfg.Length > 0) ? Environment.ExpandEnvironmentVariables(cfg) : FindConfig();
        if (RunConfig == null || !File.Exists(RunConfig))
            Note = (Note.Length > 0 ? Note + "  " : "")
                 + "找不到模型配置(strata-*.json)。先跑一次 setup,或在 launcher.json 里写 \"config\"。";
    }

    // Same rule as setup.py: strata-<model>.json, but not the *.shared-settings.json sidecar.
    static string FindConfig()
    {
        try
        {
            if (!Directory.Exists(Root)) return null;
            foreach (string f in Directory.GetFiles(Root, "strata-*.json"))
                if (!f.EndsWith(".shared-settings.json", StringComparison.OrdinalIgnoreCase))
                    return f;
        }
        catch { }
        return null;
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        // A winexe has no console: without this a startup failure is silent.
        AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
        { Crash(e.ExceptionObject as Exception); };
        Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
        { Crash(e.Exception); };
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    static void Crash(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                DateTime.Now.ToString("s") + "  " + (ex == null ? "(unknown)" : ex.ToString()) + "\r\n\r\n");
        }
        catch { }
        MessageBox.Show(ex == null ? "启动失败（详见 crash.log）" : ex.Message, "Strata 启动器");
    }
}

class MainForm : Form
{
    WebView2 _view;
    RichTextBox _term;
    TextBox _input;
    Label _status;
    Button _start, _stop;
    SplitContainer _split;
    Process _engine;
    readonly List<string> _history = new List<string>();
    int _histIdx;

    static readonly Color Bg = Color.FromArgb(24, 24, 27);
    static readonly Color Fg = Color.Gainsboro;
    static readonly Color Accent = Color.FromArgb(120, 200, 255);
    static readonly Color Warn = Color.FromArgb(255, 170, 90);

    public MainForm()
    {
        Text = "Strata 启动器";
        Size = new Size(1200, 820);
        MinimumSize = new Size(880, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;

        // ---- toolbar ----
        var bar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(32, 32, 36) };
        _start = MkButton("启动模型", 12, StartEngine);
        _stop = MkButton("停止", 116, StopEngine);
        var open = MkButton("打开界面", 196, delegate { Navigate(Cfg.Url); });
        var reload = MkButton("刷新", 292, delegate
        {
            if (_view.CoreWebView2 != null) _view.CoreWebView2.Reload();
        });
        var clear = MkButton("清空终端", 368, delegate { _term.Clear(); });
        _stop.Enabled = false;

        _status = new Label
        {
            Text = "模型未启动",
            AutoSize = false,
            Width = 260,
            Height = 44,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Warn,
            Dock = DockStyle.Right
        };
        bar.Controls.AddRange(new Control[] { _start, _stop, open, reload, clear, _status });

        // ---- terminal ----
        var termPanel = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        _input = new TextBox
        {
            Dock = DockStyle.Bottom,
            BackColor = Color.FromArgb(16, 16, 18),
            ForeColor = Fg,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10f)
        };
        _input.KeyDown += InputKeyDown;
        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 18,
            Text = "  命令在 " + Cfg.Root + " 下执行（回车运行，↑↓ 历史）",
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei UI", 8f)
        };
        _term = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(16, 16, 18),
            ForeColor = Fg,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            Font = new Font("Consolas", 9.5f),
            DetectUrls = false
        };
        termPanel.Controls.Add(_term);
        termPanel.Controls.Add(hint);
        termPanel.Controls.Add(_input);

        // ---- web + split ----
        var webPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        _view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
        webPanel.Controls.Add(_view);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = Color.FromArgb(60, 60, 66),
            SplitterWidth = 5,
            Panel1MinSize = 200,
            Panel2MinSize = 90
        };
        _split.Panel1.Controls.Add(webPanel);
        _split.Panel2.Controls.Add(termPanel);

        Controls.Add(_split);
        Controls.Add(bar);

        Load += OnLoad;
        FormClosing += OnClosing;
    }

    Button MkButton(string text, int x, Action onClick)
    {
        var b = new Button
        {
            Text = text,
            Left = x,
            Top = 8,
            Width = 92,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Fg,
            BackColor = Color.FromArgb(48, 48, 54),
            Font = new Font("Microsoft YaHei UI", 9f),
            TabStop = false
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 78);
        b.Click += delegate { onClick(); };
        return b;
    }

    async void OnLoad(object sender, EventArgs e)
    {
        _split.SplitterDistance = Math.Max(_split.Panel1MinSize, _split.Height - 230);
        Log("Strata 启动器已就绪。模型不会自动启动。\n", Accent);
        Log("  · 点「启动模型」开始加载（约 1-3 分钟，取决于磁盘）\n", Fg);
        Log("  · 下面的终端可以直接敲命令，例如  python setup.py --help\n", Fg);
        Log("  · 根目录：" + Cfg.Root + "\n", Fg);
        if (Cfg.Note.Length > 0) Log("  ! " + Cfg.Note + "\n", Warn);
        Log("\n", Fg);
        try
        {
            await _view.EnsureCoreWebView2Async(null);
            _view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _view.CoreWebView2.Settings.IsStatusBarEnabled = false;
            if (File.Exists(Cfg.StartHtml)) _view.CoreWebView2.Navigate(new Uri(Cfg.StartHtml).AbsoluteUri);
            else _view.CoreWebView2.NavigateToString("<h1>Strata</h1><p>start.html 未找到。</p>");
        }
        catch (Exception ex)
        {
            MessageBox.Show("WebView2 初始化失败：\n" + ex.Message +
                "\n\n需要已安装 WebView2 Runtime（Win10/11 一般自带）。", "Strata 启动器");
        }
    }

    void OnClosing(object sender, FormClosingEventArgs e)
    {
        if (_engine != null && !_engine.HasExited)
        {
            var r = MessageBox.Show("模型还在运行。关闭时一并停止吗？\n\n是 = 停止并退出    否 = 让它继续运行",
                "Strata 启动器", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) { e.Cancel = true; return; }
            if (r == DialogResult.Yes) StopEngine();
        }
    }

    // ---------------- engine ----------------

    void StartEngine()
    {
        if (_engine != null && !_engine.HasExited)
        {
            Log("[模型已经在运行]\n", Warn);
            return;
        }
        if (!File.Exists(Cfg.Python) || !File.Exists(Cfg.ServerPy))
        {
            Log("[找不到 python 或 server.py：" + Cfg.Python + "]\n", Warn);
            return;
        }
        if (Cfg.RunConfig == null || !File.Exists(Cfg.RunConfig))
        {
            Log("[找不到模型配置 strata-*.json，先跑一次 setup]\n", Warn);
            return;
        }
        string args = "\"" + Cfg.ServerPy + "\" --engine strata --config \"" + Cfg.RunConfig + "\" --port " + Cfg.Port;
        Log("> 启动模型\n", Accent);
        try
        {
            _engine = Spawn(Cfg.Python, args, null);
        }
        catch (Exception ex)
        {
            Log("[启动失败] " + ex.Message + "\n", Warn);
            return;
        }
        _start.Enabled = false;
        _stop.Enabled = true;
        SetStatus("正在加载模型…", Warn);
        var t = WaitForServer();
    }

    void StopEngine()
    {
        if (_engine == null) { SetStatus("模型未启动", Warn); return; }
        if (!_engine.HasExited)
        {
            string pid = _engine.Id.ToString();
            Log("> 停止模型 (taskkill /T " + pid + ")\n", Accent);
            try
            {
                var psi = new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi).WaitForExit(15000);
            }
            catch (Exception ex) { Log("[停止失败] " + ex.Message + "\n", Warn); }
        }
        _engine = null;
        _start.Enabled = true;
        _stop.Enabled = false;
        SetStatus("模型未启动", Warn);
    }

    async Task WaitForServer()
    {
        for (int i = 0; i < 900; i++)
        {
            await Task.Delay(2000);
            if (_engine == null || _engine.HasExited)
            {
                Log("[引擎进程已退出，请看上面的输出]\n", Warn);
                SetStatus("模型未启动（进程退出）", Warn);
                _start.Enabled = true;
                _stop.Enabled = false;
                return;
            }
            if (Healthy())
            {
                Log("[界面已就绪] " + Cfg.Url + "\n", Accent);
                SetStatus("运行中 · " + Cfg.Url, Color.FromArgb(130, 220, 150));
                Navigate(Cfg.Url);
                return;
            }
        }
        SetStatus("等待超时", Warn);
    }

    static bool Healthy()
    {
        try
        {
            var req = (HttpWebRequest)WebRequest.Create(Cfg.Url + "/health");
            req.Method = "GET";
            req.Timeout = 2500;
            req.ReadWriteTimeout = 2500;
            using (var r = (HttpWebResponse)req.GetResponse())
                return (int)r.StatusCode == 200;
        }
        catch { return false; }
    }

    void Navigate(string url)
    {
        if (_view.CoreWebView2 != null) _view.CoreWebView2.Navigate(url);
    }

    // ---------------- terminal ----------------

    void InputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            string cmd = _input.Text.Trim();
            _input.Clear();
            e.SuppressKeyPress = true;
            if (cmd.Length == 0) return;
            _history.Add(cmd);
            _histIdx = _history.Count;
            Log("> " + cmd + "\n", Accent);
            try { Spawn("cmd.exe", "/c " + cmd, null); }
            catch (Exception ex) { Log("[无法执行] " + ex.Message + "\n", Warn); }
        }
        else if (e.KeyCode == Keys.Up && _history.Count > 0)
        {
            if (_histIdx > 0) _histIdx--;
            _input.Text = _history[_histIdx];
            _input.SelectionStart = _input.TextLength;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Down && _history.Count > 0)
        {
            if (_histIdx < _history.Count - 1) { _histIdx++; _input.Text = _history[_histIdx]; }
            else { _histIdx = _history.Count; _input.Clear(); }
            e.SuppressKeyPress = true;
        }
    }

    Process Spawn(string exe, string args, string dir)
    {
        var psi = new ProcessStartInfo(exe, args);
        psi.WorkingDirectory = dir ?? Cfg.Root;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        var p = new Process { StartInfo = psi };
        p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e2) { if (e2.Data != null) Log(e2.Data + "\n", null); };
        p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e2) { if (e2.Data != null) Log(e2.Data + "\n", null); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    void SetStatus(string text, Color c)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string, Color>(SetStatus), text, c); return; }
        _status.Text = text;
        _status.ForeColor = c;
    }

    void Log(string s, Color? color)
    {
        if (_term.IsDisposed) return;
        if (_term.InvokeRequired) { _term.BeginInvoke(new Action<string, Color?>(Log), s, color); return; }
        _term.SelectionStart = _term.TextLength;
        _term.SelectionLength = 0;
        _term.SelectionColor = color.HasValue ? color.Value : Fg;
        _term.AppendText(s);
        _term.SelectionColor = Fg;
        _term.ScrollToCaret();
    }
}
