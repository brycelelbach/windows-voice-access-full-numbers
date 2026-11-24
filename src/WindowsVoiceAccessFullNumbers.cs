using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

[assembly: AssemblyTitle("Windows Voice Access Full Numbers")]
[assembly: AssemblyProduct("Windows Voice Access Full Numbers")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyInformationalVersion("0.1.0")]

namespace WindowsVoiceAccessFullNumbers {
    public sealed class BadgeWindow : Form {
        readonly List<Target> targets;
        readonly Screen screen;
        readonly bool instructions;
        readonly List<Badge> badges = new List<Badge>();
        readonly Font face;
        public string Digits = "";
        class Badge { public string Number; public Rectangle Box; public Point Anchor; }
        public BadgeWindow(List<Target> targets, Screen screen, bool instructions) {
            this.targets=targets; this.screen=screen; this.instructions=instructions;
            AutoScaleMode=AutoScaleMode.None; FormBorderStyle=FormBorderStyle.None;
            ShowInTaskbar=false; TopMost=true; StartPosition=FormStartPosition.Manual;
            Bounds=screen.Bounds; BackColor=Color.Magenta; TransparencyKey=BackColor;
            Text="Windows Voice Access Full Numbers overlay"; DoubleBuffered=true;
            face=new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Point);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get {
            var cp=base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x20; return cp;
        }}
        protected override void Dispose(bool disposing) { if (disposing) face.Dispose(); base.Dispose(disposing); }
        public void ShowOverlay() {
            using (var g=CreateGraphics()) {
                var placed=new List<Rectangle>();
                for (int i=0; i<targets.Count; i++) {
                    Target t=targets[i];
                    if (!screen.Bounds.Contains(t.Point)) continue;
                    var anchor=new Point(t.Point.X-Left,t.Point.Y-Top);
                    string number=(i+1).ToString();
                    SizeF size=g.MeasureString(number,face);
                    int w=(int)Math.Ceiling(size.Width)+7, h=(int)Math.Ceiling(size.Height)+3;
                    Rectangle box=Rectangle.Empty;
                    // Try positions around the target before displacing a nearby badge.
                    var offsets=new [] { new Point(-w/2,-h/2),new Point(-w/2,-h-3),
                        new Point(-w/2,3),new Point(3,-h/2),new Point(-w-3,-h/2),
                        new Point(-w-4,-h-3),new Point(4,-h-3),new Point(-w-4,3),new Point(4,3) };
                    int smallest=int.MaxValue;
                    foreach (var off in offsets) {
                        var candidate=new Rectangle(Math.Max(1,Math.Min(Width-w-2,anchor.X+off.X)),
                            Math.Max(1,Math.Min(Height-h-2,anchor.Y+off.Y)),w,h);
                        int overlap=0;
                        foreach (var other in placed) { var hit=Rectangle.Intersect(other,candidate); overlap+=hit.Width*hit.Height; }
                        if (overlap<smallest) { smallest=overlap; box=candidate; }
                        if (smallest==0) break;
                    }
                    placed.Add(box);
                    badges.Add(new Badge { Number=number, Box=box, Anchor=anchor });
                }
            }
            // Form.Show() can still assign WinForms' active window to the badge
            // surface. Show the native window without changing keyboard activation.
            Native.ShowTopmostWithoutActivation(Handle, screen.Bounds);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;
            using (var blue=new SolidBrush(Color.FromArgb(18,55,105)))
            using (var line=new Pen(Color.FromArgb(85,160,230),2))
            using (var sf=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center }) {
                foreach (var b in badges) {
                    if (!b.Number.StartsWith(Digits,StringComparison.Ordinal)) continue;
                    if (!b.Box.Contains(b.Anchor))
                        g.DrawLine(line,b.Anchor,new Point(b.Box.Left+b.Box.Width/2,b.Box.Top+b.Box.Height/2));
                    g.FillRectangle(blue,b.Box);
                    g.DrawRectangle(Pens.White,b.Box);
                    g.DrawString(b.Number,face,Brushes.White,b.Box,sf);
                }
                if (instructions) {
                    string label=Digits.Length==0 ? "Windows Voice Access Full Numbers   |   Press digits, then Enter   |   Esc closes" :
                        "Windows Voice Access Full Numbers   |   "+Digits+"   |   Enter selects   |   Backspace edits";
                    var size=g.MeasureString(label,face);
                    int w=(int)Math.Ceiling(size.Width)+16, h=(int)Math.Ceiling(size.Height)+8;
                    var strip=new Rectangle((Width-w)/2, Height-h-3,w,h);
                    g.FillRectangle(Brushes.Black,strip);
                    g.DrawString(label,face,Brushes.White,strip,sf);
                }
            }
        }
    }

    public sealed class NumberSession : ApplicationContext {
        public const int HotkeyStart=1400;
        readonly List<Target> targets;
        readonly List<BadgeWindow> windows=new List<BadgeWindow>();
        readonly List<int> hotkeys=new List<int>();
        readonly KeyWindow receiver;
        readonly NumberEntry entry;
        readonly System.Windows.Forms.Timer expiry=new System.Windows.Forms.Timer();
        string digits="";
        bool finished;
        sealed class KeyWindow : NativeWindow, IDisposable {
            readonly Action<int> handler;
            public KeyWindow(Action<int> handler) {
                this.handler=handler;
                CreateHandle(new CreateParams { Caption="Windows Voice Access Full Numbers input", Parent=new IntPtr(-3) });
            }
            protected override void WndProc(ref Message m) {
                if (m.Msg==0x0312) handler(m.WParam.ToInt32()-HotkeyStart);
                else base.WndProc(ref m);
            }
            public void Dispose() { DestroyHandle(); }
        }
        public NumberSession(List<Target> targets, bool trackingMenu) {
            this.targets=targets;
            receiver=new KeyWindow(Key);
            try {
                expiry.Interval=60000;
                expiry.Tick+=delegate { Finish(null); }; expiry.Start();
                if (!Register(27)) throw new InvalidOperationException("Another overlay is using Escape.");
                SetManualKeys(true);
                if (trackingMenu) ShowBadges(true);
                else {
                    entry = new NumberEntry(targets, Filter, delegate(int n) { Finish(targets[n-1]); }, delegate { Finish(null); });
                    entry.SetFocusKey(Register((int)Keys.F8));
                    entry.InputFocusChanged += delegate(bool owns) { if (!finished) SetManualKeys(!owns); };
                    entry.Shown += LoadTargets;
                    entry.Show();
                    entry.ActivateInput();
                }
            } catch { ReleaseKeysAndWindows(); throw; }
        }
        async void LoadTargets(object sender, EventArgs e) {
            try {
                if (targets.Count == 0) {
                    var found = await Task.Run(() => Targets.Scan(9000));
                    if (finished) return;
                    targets.AddRange(found);
                } else await Task.Yield();
                if (finished) return;
                if (targets.Count == 0) {
                    Finish(null); Program.Notify("Windows did not expose any available controls on this screen."); return;
                }
                ShowBadges(false);
                entry.TargetsReady();
            } catch (Exception ex) {
                if (!finished) { Finish(null); Program.Notify(ex.Message); }
            }
        }
        void ShowBadges(bool menuInstructions) {
            foreach (Screen screen in Screen.AllScreens) {
                var window=new BadgeWindow(targets,screen,menuInstructions && screen.Primary);
                windows.Add(window); window.ShowOverlay();
            }
        }
        void SetManualKeys(bool enabled) {
            foreach (int key in Enumerable.Range(48,10).Concat(Enumerable.Range(96,10)).Concat(new [] {8,13})) {
                if (enabled && !hotkeys.Contains(key)) Register(key);
                else if (!enabled && hotkeys.Remove(key)) Native.UnregisterHotKey(receiver.Handle,HotkeyStart+key);
            }
        }
        bool Register(int key) {
            if (!Native.RegisterHotKey(receiver.Handle, HotkeyStart+key, 0x4000, (uint)key)) return false;
            hotkeys.Add(key); return true;
        }
        void Key(int key) {
            if (finished) return;
            expiry.Stop(); expiry.Start();
            if (key==(int)Keys.F8) { if (entry!=null) entry.ActivateInput(); return; }
            if (key==27) { Finish(null); return; }
            if (key==13) {
                int n;
                if (int.TryParse(digits,out n) && n>0 && n<=targets.Count) Finish(targets[n-1]);
                return;
            }
            if (key==8) { if (digits.Length>0) digits=digits.Substring(0,digits.Length-1); }
            else {
                int d=key>=96 ? key-96 : key-48;
                if (d<0 || d>9) return;
                string next=digits+d;
                int value;
                if (next[0]=='0' || !int.TryParse(next,out value) || value>targets.Count) {
                    System.Media.SystemSounds.Beep.Play(); return;
                }
                digits=next;
            }
            if (entry!=null) entry.SetManualText(digits);
            else Filter(digits);
        }
        void Filter(string value) {
            if (finished) return;
            digits=value;
            expiry.Stop(); expiry.Start();
            foreach (var w in windows) { w.Digits=value; w.Invalidate(); }
        }
        void ReleaseKeysAndWindows() {
            expiry.Stop(); expiry.Dispose();
            if (entry != null) { entry.Hide(); entry.Dispose(); }
            if (receiver != null) {
                foreach (int key in hotkeys) Native.UnregisterHotKey(receiver.Handle,HotkeyStart+key);
                receiver.Dispose();
            }
            foreach (var w in windows) { w.Close(); w.Dispose(); }
        }
        async void Finish(Target target) {
            if (finished) return;
            finished=true;
            expiry.Stop();
            try {
                if (target == null || !Native.InputDesktop()) return;
                foreach (var w in windows) w.Dispose();
                windows.Clear();
                var foreground = Native.GetForegroundWindow();
                // Keep ownership until the selected window can take over. Closing
                // the bar first can uncover or activate a different app at this point.
                if (entry != null && entry.Bounds.Contains(target.Point))
                    entry.Left = SystemInformation.VirtualScreen.Left - entry.Width - 1;
                var bar = entry == null ? IntPtr.Zero : entry.Handle;
                string failure = await Task.Run(() => ClickTarget(target, foreground, bar));
                if (failure != null) {
                    // Leave the desktop usable while the transient message is visible.
                    foreach (int key in hotkeys) Native.UnregisterHotKey(receiver.Handle,HotkeyStart+key);
                    hotkeys.Clear();
                    if (entry != null) entry.Hide();
                    using (var notice = new Notice(failure)) {
                        notice.Show();
                        await Task.Delay(1800);
                    }
                }
            } finally {
                ReleaseKeysAndWindows();
                ExitThread();
            }
        }
        static string ClickTarget(Target target, IntPtr foreground, IntPtr bar) {
            const string unavailable = "Control unavailable   |   Open Full Numbers to refresh";
            try {
                Point point;
                for (int attempt = 0; !Targets.TryClickPoint(target, out point); attempt++) {
                    if (attempt == 3 || !Native.InputDesktop()) return unavailable;
                    Thread.Sleep(50);
                }
                if (Native.GetForegroundWindow() != foreground) return unavailable;
                if (bar != IntPtr.Zero && foreground == bar) {
                    Native.SetForegroundWindow(target.Window);
                    for (int i=0; i<24; i++) {
                        var active=Native.GetForegroundWindow();
                        if (active!=IntPtr.Zero && active!=bar) break;
                        Thread.Sleep(15);
                    }
                }
                var owner = Native.GetForegroundWindow();
                if (owner != foreground && owner != target.Window) return unavailable;
                if (!Targets.TryClickPoint(target, out point) || Native.GetForegroundWindow() != owner ||
                    !Native.PointerAvailable()) return unavailable;
                Cursor.Position=point;
                Thread.Sleep(65);
                if (!Targets.StillAt(target,point) || Cursor.Position!=point || Native.GetForegroundWindow()!=owner) return unavailable;
                if (!Native.ClickAt(point)) return "Windows could not send the click";
                // Let the destination process the mouse before destroying the input bar.
                Thread.Sleep(100);
                return null;
            } catch (Exception ex) {
                if (!Targets.ExpectedError(ex)) throw;
                return unavailable;
            }
        }
    }

    sealed class Notice : Form {
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get {
            var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x20; return cp;
        }}
        public Notice(string message) {
            FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true;
            StartPosition=FormStartPosition.Manual; BackColor=Color.Black; ForeColor=Color.White;
            var label = new Label { AutoSize=true, Text=message, Location=new Point(8,4) };
            Controls.Add(label);
            var screen = Screen.PrimaryScreen.Bounds;
            var bounds = new Rectangle(screen.Left+(screen.Width-label.PreferredWidth-16)/2,
                screen.Bottom-label.PreferredHeight-11, label.PreferredWidth+16, label.PreferredHeight+8);
            Bounds=bounds;
        }
    }
    public static class Program {
        public static void Notify(string text) {
            MessageBox.Show(text,"Windows Voice Access Full Numbers",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        [STAThread] public static void Main() {
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
            using (var mutex=new Mutex(false,@"Local\WindowsVoiceAccessFullNumbers-on-demand-2026")) {
                if (!mutex.WaitOne(0)) return;
                try {
                    Application.EnableVisualStyles();
                    bool menu=Native.TrackingMenu();
                    var targets=menu ? Targets.Scan(9000) : new List<Target>();
                    if (menu && targets.Count==0) Notify("Windows did not expose any available controls on this screen.");
                    else using (var session=new NumberSession(targets,menu)) Application.Run(session);
                } catch (Exception e) { Notify(e.Message); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
