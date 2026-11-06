using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        bool isFirstInstance;
        using (new Mutex(true, @"Local\Mouse2Key", out isFirstInstance))
        {
            if (!isFirstInstance)
            {
                MessageBox.Show("its alr open bro look at sys tray", "Mouse2Key");
                return;
            }

            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Config.Load();
            if (!Engine.Start())
            {
                MessageBox.Show("windows wouldn't let it read the mouse, try again ig", "Mouse2Key");
                return;
            }

            try { Application.Run(new MainForm()); }
            finally { Engine.Stop(); }
        }
    }
}

static class Buttons
{
    public const string Middle = "Middle", Back = "Back", Forward = "Forward",
        WheelUp = "WheelUp", WheelDown = "WheelDown";

    public static readonly string[] All = { Forward, Back, Middle, WheelUp, WheelDown };

    public static bool IsScroll(string id)
    {
        return id == WheelUp || id == WheelDown;
    }

    public static string Label(string id)
    {
        switch (id)
        {
            case Forward: return "Mouse 4 (forward)";
            case Back: return "Mouse 3 (back)";
            case Middle: return "Middle button";
            case WheelUp: return "Scroll up";
            case WheelDown: return "Scroll down";
            default: return id;
        }
    }
}

enum KeyMode { Hold, Toggle, Repeat }

class Binding
{
    public int[] Keys;
    public KeyMode Mode;
}

static class Config
{
    public const string AllApps = "";

    public static readonly Dictionary<string, Dictionary<string, Binding>> Profiles = NewProfiles();

    static Dictionary<string, Dictionary<string, Binding>> NewProfiles()
    {
        var profiles = new Dictionary<string, Dictionary<string, Binding>>(StringComparer.OrdinalIgnoreCase);
        profiles[AllApps] = new Dictionary<string, Binding>();
        return profiles;
    }

    public static void Reset()
    {
        Profiles.Clear();
        Profiles[AllApps] = new Dictionary<string, Binding>();
    }

    public static Binding Find(string profile, string id)
    {
        Dictionary<string, Binding> bindings;
        Binding binding;
        return Profiles.TryGetValue(profile, out bindings) && bindings.TryGetValue(id, out binding) ? binding : null;
    }

    static string FilePath
    {
        get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Mouse2Key.cfg"); }
    }

    public static void Load()
    {
        if (!File.Exists(FilePath)) return;
        string profile = AllApps;
        foreach (string line in File.ReadAllLines(FilePath))
        {
            string text = line.Trim();
            if (text.StartsWith("[") && text.EndsWith("]"))
            {
                profile = text.Substring(1, text.Length - 2).Trim();
                if (!Profiles.ContainsKey(profile)) Profiles[profile] = new Dictionary<string, Binding>();
                continue;
            }

            string[] parts = text.Split('=');
            if (parts.Length != 2) continue;

            string id = parts[0].Trim();
            if (!Buttons.All.Contains(id)) continue;

            string[] value = parts[1].Split('|');
            bool valid = value.Length <= 2;

            KeyMode mode = KeyMode.Hold;
            if (value.Length == 2)
                valid &= Enum.TryParse(value[1].Trim(), out mode) && Enum.IsDefined(typeof(KeyMode), mode);

            var keys = new List<int>();
            foreach (string name in value[0].Split('+'))
            {
                Keys key;
                if (Enum.TryParse(name.Trim(), out key) && (int)key > 0 && (int)key < 255) keys.Add((int)key);
                else valid = false;
            }
            if (valid && keys.Count > 0) Profiles[profile][id] = new Binding { Keys = keys.ToArray(), Mode = mode };
        }
    }

    public static void Save()
    {
        var lines = new List<string>();
        foreach (var profile in Profiles.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (profile.Key != AllApps) lines.Add("[" + profile.Key + "]");
            foreach (var entry in profile.Value)
            {
                string keys = string.Join("+", entry.Value.Keys.Select(vk => ((Keys)vk).ToString()));
                lines.Add(entry.Key + "=" + keys + (entry.Value.Mode == KeyMode.Hold ? "" : "|" + entry.Value.Mode));
            }
        }
        File.WriteAllLines(FilePath, lines.ToArray());
    }
}

static class Engine
{
    public static bool Enabled = true;

    public static Action<string, bool> ButtonChanged;
    public static Action<string> Scrolled;

    const int WH_MOUSE_LL = 14;
    const int WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208, WM_MOUSEWHEEL = 0x20A,
        WM_XBUTTONDOWN = 0x20B, WM_XBUTTONUP = 0x20C;
    const int INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_SCANCODE = 8;
    const uint MAPVK_VK_TO_VSC_EX = 4;

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public int type; public KEYBDINPUT ki; public int pad1, pad2; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetKeyNameText(int lParam, StringBuilder text, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);

    static readonly HookProc hookProc = OnMouseEvent;
    static IntPtr hook = IntPtr.Zero;

    static readonly Dictionary<string, int[]> held = new Dictionary<string, int[]>();

    static readonly Dictionary<string, int[]> toggled = new Dictionary<string, int[]>();
    static uint toggledProcess;

    static readonly Dictionary<string, int[]> repeating = new Dictionary<string, int[]>();
    static readonly HashSet<string> hideNextRelease = new HashSet<string>();

    const int RepeatMilliseconds = 50;
    static readonly System.Windows.Forms.Timer ticker = NewTicker();

    static System.Windows.Forms.Timer NewTicker()
    {
        var timer = new System.Windows.Forms.Timer { Interval = RepeatMilliseconds };
        timer.Tick += (s, e) => OnTick();
        return timer;
    }

    static uint lastProcess;
    static string lastProcessName = "";

    public static bool Start()
    {
        hook = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle(null), 0);
        return hook != IntPtr.Zero;
    }

    public static void Stop()
    {
        ReleaseAll();
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    public static void ReleaseAll()
    {
        foreach (int[] keys in held.Values.ToArray()) Send(keys, false);
        held.Clear();
        repeating.Clear();
        ReleaseToggles();
        ticker.Stop();
    }

    static void ReleaseToggles()
    {
        foreach (var entry in toggled.ToArray())
        {
            Send(entry.Value, false);
            toggled.Remove(entry.Key);
            if (ButtonChanged != null) ButtonChanged(entry.Key, false);
        }
    }

    static void OnTick()
    {
        foreach (int[] keys in repeating.Values.ToArray())
        {
            Send(keys, true);
            Send(keys, false);
        }
        if (toggled.Count > 0 && ForegroundProcess() != toggledProcess) ReleaseToggles();
        if (repeating.Count == 0 && toggled.Count == 0) ticker.Stop();
    }

    public static uint ForegroundProcess()
    {
        uint process;
        GetWindowThreadProcessId(GetForegroundWindow(), out process);
        return process;
    }

    static string ActiveProfile()
    {
        uint process = ForegroundProcess();
        if (process != lastProcess)
        {
            lastProcess = process;
            lastProcessName = ProcessName(process);
        }
        return Config.Profiles.ContainsKey(lastProcessName) ? lastProcessName : Config.AllApps;
    }

    public static string ProcessName(uint process)
    {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process);
        if (handle == IntPtr.Zero) return "";
        try
        {
            var path = new StringBuilder(1024);
            uint size = (uint)path.Capacity;
            return QueryFullProcessImageName(handle, 0, path, ref size) ? Path.GetFileNameWithoutExtension(path.ToString()) : "";
        }
        finally { CloseHandle(handle); }
    }

    static IntPtr OnMouseEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && Handle(wParam.ToInt32(), lParam)) return (IntPtr)1;
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    static bool Handle(int message, IntPtr lParam)
    {
        int mouseData = Marshal.ReadInt32(lParam, 8);
        int flags = Marshal.ReadInt32(lParam, 12);
        short high = (short)((mouseData >> 16) & 0xFFFF);

        if ((flags & 1) != 0) return false;

        switch (message)
        {
            case WM_MBUTTONDOWN: return Press(Buttons.Middle);
            case WM_MBUTTONUP: return Release(Buttons.Middle);
            case WM_XBUTTONDOWN: return Press(high == 1 ? Buttons.Back : Buttons.Forward);
            case WM_XBUTTONUP: return Release(high == 1 ? Buttons.Back : Buttons.Forward);
            case WM_MOUSEWHEEL: return Tap(high > 0 ? Buttons.WheelUp : Buttons.WheelDown);
            default: return false;
        }
    }

    static bool Press(string id)
    {
        if (ButtonChanged != null) ButtonChanged(id, true);
        if (!Enabled) return false;

        int[] keys;
        if (toggled.TryGetValue(id, out keys))
        {
            toggled.Remove(id);
            Send(keys, false);
            hideNextRelease.Add(id);
            return true;
        }

        Binding binding = Config.Find(ActiveProfile(), id);
        if (binding == null) return false;

        switch (binding.Mode)
        {
            case KeyMode.Toggle:
                toggled[id] = binding.Keys;
                toggledProcess = ForegroundProcess();
                hideNextRelease.Add(id);
                Send(binding.Keys, true);
                ticker.Start();
                break;
            case KeyMode.Repeat:
                repeating[id] = binding.Keys;
                Send(binding.Keys, true);
                Send(binding.Keys, false);
                ticker.Start();
                break;
            default:
                held[id] = binding.Keys;
                Send(binding.Keys, true);
                break;
        }
        return true;
    }

    static bool Release(string id)
    {
        if (ButtonChanged != null) ButtonChanged(id, toggled.ContainsKey(id));

        int[] keys;
        if (held.TryGetValue(id, out keys))
        {
            held.Remove(id);
            Send(keys, false);
            return true;
        }
        if (repeating.Remove(id)) return true;
        return hideNextRelease.Remove(id);
    }

    static bool Tap(string id)
    {
        if (Scrolled != null) Scrolled(id);
        if (!Enabled) return false;
        Binding binding = Config.Find(ActiveProfile(), id);
        if (binding == null) return false;
        Send(binding.Keys, true);
        Send(binding.Keys, false);
        return true;
    }

    static void Send(int[] keys, bool down)
    {
        IEnumerable<int> order = down ? keys : keys.Reverse();
        INPUT[] inputs = order.Select(vk => MakeInput(vk, down)).ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    static INPUT MakeInput(int vk, bool down)
    {
        const int VK_PAUSE = 0x13, VK_SNAPSHOT = 0x2C, VK_BROWSER_BACK = 0xA6, VK_LAUNCH_APP2 = 0xB7;

        INPUT input = new INPUT();
        input.type = INPUT_KEYBOARD;
        uint keyFlags = down ? 0 : KEYEVENTF_KEYUP;

        bool virtualOnly = vk == VK_PAUSE || vk == VK_SNAPSHOT || (vk >= VK_BROWSER_BACK && vk <= VK_LAUNCH_APP2);
        uint scan = virtualOnly ? 0 : MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC_EX);

        if (scan == 0)
        {
            input.ki.wVk = (ushort)vk;
        }
        else
        {
            input.ki.wScan = (ushort)(scan & 0xFF);
            keyFlags |= KEYEVENTF_SCANCODE;
            if ((scan & 0xFF00) != 0) keyFlags |= KEYEVENTF_EXTENDEDKEY;
        }

        input.ki.dwFlags = keyFlags;
        return input;
    }

    public static string Describe(int[] keys)
    {
        return string.Join(" + ", keys.Select(KeyName));
    }

    static string KeyName(int vk)
    {
        switch (vk)
        {
            case 0x10: return "Shift";
            case 0x11: return "Ctrl";
            case 0x12: return "Alt";
            case 0x5B: return "Win";
        }

        uint scan = MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC_EX);
        if (scan != 0)
        {
            int param = (int)(scan & 0xFF) << 16;
            if ((scan & 0xFF00) != 0) param |= 1 << 24;
            var name = new StringBuilder(64);
            if (GetKeyNameText(param, name, name.Capacity) > 0) return name.ToString();
        }
        return ((Keys)vk).ToString();
    }
}

class MainForm : ThemedForm
{
    readonly CheckBox enabledBox = new CheckBox { Text = "Bindings on", AutoSize = true, AutoCheck = false, Checked = true };
    readonly Dictionary<string, PlainButton> clearButtons = new Dictionary<string, PlainButton>();
    readonly ToolTip tips = new ToolTip();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly MenuItem trayEnabled = new MenuItem("Enabled");
    readonly Dictionary<string, PlainButton> bindButtons = new Dictionary<string, PlainButton>();
    readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();
    readonly HashSet<string> litRows = new HashSet<string>();
    readonly Dictionary<string, ComboBox> modeBoxes = new Dictionary<string, ComboBox>();
    readonly ComboBox profileBox = new ThemedCombo { Width = 170, Anchor = AnchorStyles.Left };
    readonly Button removeProfile = new PlainButton { Text = "Remove", Anchor = AnchorStyles.Left };
    string currentProfile = Config.AllApps;
    bool refreshing;
    bool restoring;
    string capturingRow;

    const int FlashMilliseconds = 200;
    readonly Dictionary<string, int> flashEnds = new Dictionary<string, int>();
    readonly System.Windows.Forms.Timer flashTimer = new System.Windows.Forms.Timer { Interval = 30 };
    bool exiting;

    public MainForm()
    {
        Text = "Mouse2Key";
        Icon = AppIcon;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = Theme.BodyFont;

        var page = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(6) };

        var profileRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var addGame = new PlainButton { Text = "Add...", Anchor = AnchorStyles.Left, Margin = new Padding(4, 3, 3, 3) };
        profileRow.Controls.Add(new Label { Text = "Profile", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 3, 10, 3) });
        profileRow.Controls.Add(profileBox);
        profileRow.Controls.Add(addGame);
        profileRow.Controls.Add(removeProfile);
        profileBox.SelectedIndexChanged += (s, e) => { if (!refreshing) SelectProfile(); };
        IgnoreScrollWheel(profileBox);
        addGame.Click += (s, e) => AddGame();
        removeProfile.Click += (s, e) => RemoveProfile();
        profileRow.Margin = new Padding(0, 0, 0, 2);
        page.Controls.Add(profileRow);

        var buttonsCard = new Card();
        foreach (string id in Buttons.All.Where(b => !Buttons.IsScroll(b))) buttonsCard.Body.Controls.Add(MakeRow(id));
        page.Controls.Add(buttonsCard);
        var scrollCard = new Card();
        foreach (string id in Buttons.All.Where(Buttons.IsScroll)) scrollCard.Body.Controls.Add(MakeRow(id));
        page.Controls.Add(scrollCard);

        var footer = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 2, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        enabledBox.Anchor = AnchorStyles.Left;
        footer.Controls.Add(enabledBox);
        footer.Controls.Add(new Label { Text = "made by kx", AutoSize = true, Tag = Theme.MutedRole, Anchor = AnchorStyles.None });
        var footerRight = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        var resetButton = new PlainButton { Text = "Reset" };
        resetButton.Click += (s, e) => ResetAll();
        footerRight.Controls.Add(resetButton);
        var exitButton = new PlainButton { Text = "Exit" };
        exitButton.Click += (s, e) => ExitApp();
        footerRight.Controls.Add(exitButton);
        footer.Controls.Add(footerRight);
        page.Controls.Add(footer);

        SetContent(page);

        enabledBox.Click += (s, e) => { SetEnabled(!Engine.Enabled); SaveSettings(); };
        RefreshProfiles();
        SetUpTray();

        flashTimer.Tick += (s, e) => EndFinishedFlashes();
        Engine.ButtonChanged = SetLit;
        Engine.Scrolled = Flash;

        tips.SetToolTip(enabledBox, "turns all your binds on or off");
        tips.SetToolTip(resetButton, "wipes every game and bind");

        LoadSettings();
    }

    static readonly Icon AppIcon = LoadAppIcon();

    static Icon LoadAppIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch (Exception) { return SystemIcons.Application; }
    }

    static string SettingsPath
    {
        get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Mouse2Key.settings"); }
    }

    void LoadSettings()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            foreach (string line in File.ReadAllLines(SettingsPath))
            {
                if (line == "Bindings=False") SetEnabled(false);
                else if (line.StartsWith("Profile="))
                {
                    string saved = line.Substring("Profile=".Length);
                    string match = Config.Profiles.Keys.FirstOrDefault(k => string.Equals(k, saved, StringComparison.OrdinalIgnoreCase));
                    if (match != null) currentProfile = match;
                }
            }
            RefreshProfiles();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void SaveSettings()
    {
        try
        {
            File.WriteAllLines(SettingsPath, new[]
            {
                "Bindings=" + Engine.Enabled,
                "Profile=" + currentProfile
            });
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void SetLit(string id, bool lit)
    {
        if (lit) litRows.Add(id);
        else litRows.Remove(id);

        if (Visible) PaintRow(id);
    }

    void PaintAllRows()
    {
        foreach (string id in Buttons.All) PaintRow(id);
    }

    void PaintRow(string id)
    {
        Color? fill = null, rowColor = null;
        Color text = Theme.Text;
        if (id == capturingRow)
        {
            fill = Theme.SetupFill;
            rowColor = Theme.SetupRow;
            text = Theme.SetupText;
        }
        else if (litRows.Contains(id))
        {
            fill = Theme.PressFill;
            rowColor = Theme.PressRow;
            text = Theme.PressText;
        }

        Row row = rows[id];
        Color background = rowColor ?? Theme.Card;
        row.Bar = fill;
        row.Fill = rowColor;
        row.BackColor = background;
        foreach (Control child in row.Controls)
        {
            if (child is Label) child.BackColor = background;
            child.Invalidate();
        }
        bindButtons[id].Highlight(fill, text);
        row.Invalidate();
    }

    void Flash(string id)
    {
        if (!Visible) return;
        SetLit(id, true);
        flashEnds[id] = Environment.TickCount + FlashMilliseconds;
        flashTimer.Start();
    }

    void EndFinishedFlashes()
    {
        foreach (string id in flashEnds.Keys.ToArray())
        {
            if (Environment.TickCount - flashEnds[id] < 0) continue;
            SetLit(id, false);
            flashEnds.Remove(id);
        }
        if (flashEnds.Count == 0) flashTimer.Stop();
    }

    Row MakeRow(string id)
    {
        var row = new Row();
        var label = new Label { Text = Buttons.Label(id), AutoSize = true, Anchor = AnchorStyles.Left };
        var bind = new PlainButton { AutoSize = false, AutoEllipsis = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        var clear = new PlainButton { Text = "Clear", Anchor = AnchorStyles.Left | AnchorStyles.Right };

        rows[id] = row;
        bindButtons[id] = bind;
        clearButtons[id] = clear;
        bind.Click += (s, e) => CaptureBinding(id);
        clear.Click += (s, e) =>
        {
            Config.Profiles[currentProfile].Remove(id);
            SaveAndRefresh(id);
        };

        row.Controls.Add(label);
        row.Controls.Add(bind);
        if (Buttons.IsScroll(id))
        {
            row.Controls.Add(new Label { AutoSize = true });
        }
        else
        {
            var mode = new ThemedCombo { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            mode.Items.AddRange(Enum.GetNames(typeof(KeyMode)));
            mode.SelectedIndexChanged += (s, e) =>
            {
                Binding binding = Config.Find(currentProfile, id);
                if (refreshing || binding == null) return;
                binding.Mode = (KeyMode)mode.SelectedIndex;
                SaveAndRefresh(id);
            };
            IgnoreScrollWheel(mode);
            modeBoxes[id] = mode;
            row.Controls.Add(mode);
        }
        row.Controls.Add(clear);
        foreach (Control child in row.Controls) child.Margin = new Padding(2, 1, 2, 1);
        return row;
    }

    static void IgnoreScrollWheel(ComboBox box)
    {
        box.MouseWheel += (s, e) => ((HandledMouseEventArgs)e).Handled = true;
    }

    void RefreshProfiles()
    {
        refreshing = true;
        profileBox.Items.Clear();
        profileBox.Items.Add("All apps");
        foreach (string name in Config.Profiles.Keys.Where(k => k != Config.AllApps).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            profileBox.Items.Add(name);
        if (currentProfile == Config.AllApps) profileBox.SelectedIndex = 0;
        else profileBox.SelectedItem = currentProfile;
        refreshing = false;

        removeProfile.Enabled = currentProfile != Config.AllApps;
        foreach (string id in Buttons.All) RefreshRow(id);
    }

    void SelectProfile()
    {
        currentProfile = profileBox.SelectedIndex <= 0 ? Config.AllApps : (string)profileBox.SelectedItem;
        RefreshProfiles();
        SaveSettings();
    }

    void AddGame()
    {
        using (var dialog = new AddGameForm())
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (!Config.Profiles.ContainsKey(dialog.Result)) Config.Profiles[dialog.Result] = new Dictionary<string, Binding>();
            currentProfile = Config.Profiles.Keys.First(k => string.Equals(k, dialog.Result, StringComparison.OrdinalIgnoreCase));
        }
        SaveAll();
    }

    void RemoveProfile()
    {
        using (var dialog = new ConfirmForm("Remove game", "get rid of " + currentProfile + " and its binds?"))
        {
            if (dialog.ShowDialog(this) != DialogResult.Yes) return;
        }
        Config.Profiles.Remove(currentProfile);
        currentProfile = Config.AllApps;
        SaveAll();
    }

    void ResetAll()
    {
        using (var dialog = new ConfirmForm("Reset everything", "wipe everything? all games and binds go"))
        {
            if (dialog.ShowDialog(this) != DialogResult.Yes) return;
        }

        Engine.ReleaseAll();
        SetEnabled(true);
        Config.Reset();
        currentProfile = Config.AllApps;
        SaveAll();
        SaveSettings();
    }

    void SaveAll()
    {
        SaveConfig();
        RefreshProfiles();
        SaveSettings();
    }

    void CaptureBinding(string id)
    {
        bool wasEnabled = Engine.Enabled;
        Engine.Enabled = false;
        Engine.ReleaseAll();
        capturingRow = id;
        PaintRow(id);

        using (var dialog = new CaptureForm(Buttons.Label(id)))
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                Binding existing = Config.Find(currentProfile, id);
                Config.Profiles[currentProfile][id] = new Binding
                {
                    Keys = dialog.Result,
                    Mode = existing != null ? existing.Mode : KeyMode.Hold
                };
                SaveAndRefresh(id);
            }
        }

        capturingRow = null;
        PaintRow(id);
        Engine.Enabled = wasEnabled;
    }

    void SaveAndRefresh(string id)
    {
        SaveConfig();
        RefreshRow(id);
    }

    void SaveConfig()
    {
        try { Config.Save(); }
        catch (Exception e)
        {
            MessageBox.Show(this, "couldn't save it, works till you close it tho\n" + e.Message, "Mouse2Key");
        }
    }

    void RefreshRow(string id)
    {
        Binding binding = Config.Find(currentProfile, id);
        bindButtons[id].Text = binding != null ? Engine.Describe(binding.Keys) : "not set";
        tips.SetToolTip(bindButtons[id], binding != null ? Engine.Describe(binding.Keys) + " (click to change it)" : "click it then hit a key");
        clearButtons[id].Enabled = binding != null;

        ComboBox mode;
        if (!modeBoxes.TryGetValue(id, out mode)) return;
        bool wasRefreshing = refreshing;
        refreshing = true;
        mode.SelectedIndex = binding != null ? (int)binding.Mode : (int)KeyMode.Hold;
        mode.Enabled = binding != null;
        refreshing = wasRefreshing;
    }

    void SetUpTray()
    {
        var menu = new ContextMenu();
        menu.MenuItems.Add("Open", (s, e) => ShowWindow());
        trayEnabled.Checked = true;
        trayEnabled.Click += (s, e) => { SetEnabled(!Engine.Enabled); SaveSettings(); };
        menu.MenuItems.Add(trayEnabled);
        menu.MenuItems.Add("Exit", (s, e) => ExitApp());

        tray.Icon = AppIcon;
        tray.Text = "Mouse2Key";
        tray.ContextMenu = menu;
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
        tray.Visible = true;
    }

    void SetEnabled(bool on)
    {
        Engine.Enabled = on;
        if (!on) Engine.ReleaseAll();
        enabledBox.Checked = on;
        trayEnabled.Checked = on;
    }

    void ShowWindow()
    {
        restoring = true;
        Show();
        WindowState = FormWindowState.Normal;
        restoring = false;
        PaintAllRows();
        Activate();
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    void HideToTray()
    {
        Hide();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        MoveToCorner();
    }

    void MoveToCorner()
    {
        const int gap = 8;
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(area.Right - Width - gap, area.Bottom - Height - gap);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && Visible && !restoring) HideToTray();
    }

    void ExitApp()
    {
        exiting = true;
        tray.Visible = false;
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        Engine.ButtonChanged = null;
        Engine.Scrolled = null;
        flashTimer.Stop();
        tray.Visible = false;
        base.OnFormClosing(e);
    }
}

static class Theme
{
    public const string MutedRole = "muted";

    public static readonly Font BodyFont = SystemFonts.MessageBoxFont;

    public const string LineRole = "line";

    public static readonly Color
        Back = Color.FromArgb(24, 24, 24),
        Input = Color.FromArgb(38, 38, 38),
        Hover = Color.FromArgb(50, 50, 50),
        Border = Color.FromArgb(62, 62, 62),
        Text = Color.FromArgb(232, 232, 232),
        Muted = Color.FromArgb(125, 125, 125),
        Accent = Color.FromArgb(225, 35, 35),
        AccentText = Color.White,
        PressFill = Accent,
        PressText = Color.White,
        PressRow = Color.FromArgb(58, 24, 24),
        SetupFill = Color.FromArgb(255, 185, 0),
        SetupRow = Color.FromArgb(56, 46, 14),
        SetupText = Color.Black;

    public static readonly Color Card = Back;

    public static void Apply(Control control)
    {
        string role = control.Tag as string;
        if (control is Button || control is ComboBox) { }
        else if (control is ListBox)
        {
            control.BackColor = Input;
            control.ForeColor = Text;
        }
        else if (role == LineRole)
        {
            control.BackColor = Border;
        }
        else
        {
            control.BackColor = Back;
            control.ForeColor = role == MutedRole ? Muted : Text;
        }
        foreach (Control child in control.Controls) Apply(child);
    }
}

class Card : Panel
{
    public readonly TableLayoutPanel Body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0) };

    public Card()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Margin = new Padding(0, 0, 0, 6);
        Padding = new Padding(0);
        Controls.Add(Body);
    }
}

class Row : TableLayoutPanel
{
    public Color? Fill, Bar;

    public Row()
    {
        AutoSize = true;
        RowCount = 1;
        ColumnCount = 4;
        Margin = new Padding(0);
        Padding = new Padding(0);
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Fill ?? Theme.Card);
        if (Bar == null) return;
        using (var bar = new SolidBrush(Bar.Value))
            e.Graphics.FillRectangle(bar, 0, 0, 4, Height);
    }
}

class PlainButton : Button
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string app, string idList);

    public PlainButton()
    {
        AutoSize = true;
        Highlight(null, Theme.Text);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { SetWindowTheme(Handle, "DarkMode_Explorer", null); }
        catch (DllNotFoundException) { }
    }

    Color? fill;
    Color fillText;

    public void Highlight(Color? fill, Color text)
    {
        this.fill = fill;
        fillText = text;
        Restyle();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Restyle();
    }

    void Restyle()
    {
        if (fill == null && Enabled)
        {
            FlatStyle = FlatStyle.System;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            return;
        }
        Color back = fill ?? Theme.Back;
        FlatStyle = FlatStyle.Flat;
        BackColor = back;
        ForeColor = fill != null ? fillText : Theme.Muted;
        FlatAppearance.BorderColor = fill ?? Theme.Input;
        FlatAppearance.MouseOverBackColor = back;
        FlatAppearance.MouseDownBackColor = back;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Enabled || FlatStyle == FlatStyle.System) { base.OnPaint(e); return; }
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Back);
        using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 4))
        using (var pen = new Pen(Theme.Border))
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

class ThemedCombo : ComboBox
{
    public ThemedCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        BackColor = Theme.Input;
        ForeColor = Theme.Text;
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string app, string idList);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { SetWindowTheme(Handle, "DarkMode_CFD", null); }
        catch (DllNotFoundException) { }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool picked = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var brush = new SolidBrush(picked ? Theme.Accent : Theme.Input))
            e.Graphics.FillRectangle(brush, e.Bounds);
        if (e.Index < 0) return;
        var area = new Rectangle(e.Bounds.X + 2, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, area, picked ? Theme.AccentText : Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

class ThemedForm : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public ThemedForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        int on = 1;
        try { DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)); }
        catch (DllNotFoundException) { }
    }

    protected void SetContent(Control content)
    {
        content.Margin = new Padding(0);
        Controls.Add(content);
    }

    protected override void OnLoad(EventArgs e)
    {
        Theme.Apply(this);
        base.OnLoad(e);
    }
}

class ConfirmForm : ThemedForm
{
    public ConfirmForm(string title, string question)
    {
        Text = title;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = Theme.BodyFont;

        var yes = new PlainButton { Text = "Yes", DialogResult = DialogResult.Yes };
        var no = new PlainButton { Text = "No", DialogResult = DialogResult.No };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(yes);
        buttons.Controls.Add(no);

        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16) };
        panel.Controls.Add(new Label { Text = question, AutoSize = true });
        panel.Controls.Add(buttons);
        SetContent(panel);

        AcceptButton = no;
        CancelButton = no;
    }
}

class AddGameForm : ThemedForm
{
    public string Result { get; private set; }

    readonly ListBox list = new ListBox { Width = 420, Height = 220, BorderStyle = BorderStyle.FixedSingle, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22 };
    readonly List<string> names = new List<string>();

    public AddGameForm()
    {
        Text = "Add game";
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = Theme.BodyFont;

        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16) };
        panel.Controls.Add(new Label { Text = "open the game first then pick it here", AutoSize = true, Margin = new Padding(3, 3, 3, 8) });
        panel.Controls.Add(list);

        var add = new PlainButton { Text = "Add" };
        var cancel = new PlainButton { Text = "Cancel" };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(add);
        buttons.Controls.Add(cancel);
        panel.Controls.Add(buttons);
        SetContent(panel);

        add.Click += (s, e) => Choose();
        list.DoubleClick += (s, e) => Choose();
        list.DrawItem += DrawListItem;
        CancelButton = cancel;
        LoadOpenApps();
    }

    void DrawListItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var brush = new SolidBrush(selected ? Theme.Accent : Theme.Input))
            e.Graphics.FillRectangle(brush, e.Bounds);
        var area = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, (string)list.Items[e.Index], list.Font, area, selected ? Theme.AccentText : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    void LoadOpenApps()
    {
        var rows = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string self = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
        foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    string title = process.MainWindowTitle;
                    string name = process.ProcessName;
                    if (title.Length > 0 && !string.Equals(name, self, StringComparison.OrdinalIgnoreCase) && !rows.ContainsKey(name))
                        rows[name] = name + "  -  " + title;
                }
                catch (InvalidOperationException) { }
            }
        }
        foreach (var row in rows)
        {
            names.Add(row.Key);
            list.Items.Add(row.Value);
        }
    }

    void Choose()
    {
        if (list.SelectedIndex < 0) return;
        Result = names[list.SelectedIndex];
        DialogResult = DialogResult.OK;
    }
}

class CaptureForm : ThemedForm, IMessageFilter
{
    const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SNAPSHOT = 0x2C, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_PROCESSKEY = 0xE5;

    public int[] Result { get; private set; }

    readonly CheckBox winBox = new CheckBox { AutoSize = true };
    readonly List<int> modifiers = new List<int>();

    public CaptureForm(string buttonLabel)
    {
        Text = "Press a key";
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = Theme.BodyFont;

        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16) };
        panel.Controls.Add(new Label
        {
            Text = "hit a key or combo for\n" + buttonLabel,
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 12)
        });

        winBox.Text = "add the windows key too";
        panel.Controls.Add(winBox);

        var cancel = new PlainButton { Text = "Cancel", Margin = new Padding(3, 12, 3, 3) };
        cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
        panel.Controls.Add(cancel);

        SetContent(panel);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Application.AddMessageFilter(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Application.RemoveMessageFilter(this);
        base.OnFormClosed(e);
    }

    public bool PreFilterMessage(ref Message m)
    {
        bool isDown = m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN;
        bool isUp = m.Msg == WM_KEYUP || m.Msg == WM_SYSKEYUP;
        if (!isDown && !isUp) return false;

        int vk = m.WParam.ToInt32();
        if (vk == VK_LWIN || vk == VK_RWIN || vk == VK_PROCESSKEY) return true;

        bool isModifier = vk == VK_SHIFT || vk == VK_CONTROL || vk == VK_MENU;
        if (isDown)
        {
            if (isModifier)
            {
                if (!modifiers.Contains(vk)) modifiers.Add(vk);
            }
            else if (vk != VK_SNAPSHOT)
            {
                Finish(vk);
            }
        }
        else if (vk == VK_SNAPSHOT)
        {
            Finish(vk);
        }
        else if (isModifier && modifiers.Count > 0)
        {
            Finish(0);
        }
        return true;
    }

    void Finish(int mainKey)
    {
        var keys = new List<int>();
        if (winBox.Checked) keys.Add(VK_LWIN);
        keys.AddRange(modifiers);
        if (mainKey != 0) keys.Add(mainKey);

        Result = keys.ToArray();
        DialogResult = DialogResult.OK;
    }
}
