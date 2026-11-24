using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Bounds = System.Drawing.Rectangle;

namespace WindowsVoiceAccessFullNumbers {
    public static class Native {
        public delegate bool EnumProc(IntPtr hwnd, IntPtr state);
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct GuiThreadInfo {
            public int Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public Rect CaretBounds;
        }
        [DllImport("user32")] static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32")] public static extern IntPtr SetActiveWindow(IntPtr hwnd);
        [DllImport("user32", SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        public static void ShowTopmostWithoutActivation(IntPtr window, Bounds bounds) {
            if (!SetWindowPos(window, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0040))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        [DllImport("user32")] static extern void NotifyWinEvent(uint evt, IntPtr hwnd, int objectId, int child);
        public static void AnnounceEditFocus(IntPtr edit) {
            NotifyWinEvent(0x8005, edit, -4, 0); // EVENT_OBJECT_FOCUS, OBJID_CLIENT, CHILDID_SELF
        }
        public static bool SameAppInForeground(IntPtr window) {
            uint ours, foreground;
            GetWindowThreadProcessId(window, out ours);
            GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
            return ours != 0 && ours == foreground;
        }
        public static bool OwnsKeyboardInput(IntPtr bar, IntPtr edit) {
            var info = new GuiThreadInfo { Size = Marshal.SizeOf(typeof(GuiThreadInfo)) };
            return GetForegroundWindow() == bar && GetGUIThreadInfo(0, ref info) && info.Focus == edit;
        }
        public static bool TrackingMenu() {
            var info = new GuiThreadInfo { Size = Marshal.SizeOf(typeof(GuiThreadInfo)) };
            const int menuModes = 0x04 | 0x08 | 0x10;
            return GetGUIThreadInfo(0, ref info) && (info.Flags & menuModes) != 0;
        }
        [DllImport("user32")] public static extern bool EnumWindows(EnumProc proc, IntPtr state);
        [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32")] public static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32")] public static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32", EntryPoint="GetWindowLongW")] public static extern int Style(IntPtr hwnd, int which);
        [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("user32")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flag);
        [DllImport("user32")] public static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
        [DllImport("user32")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("kernel32")] static extern uint GetCurrentThreadId();
        [DllImport("user32")] static extern IntPtr GetThreadDesktop(uint thread);
        [DllImport("user32", CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle, int index, out int value, int size, out int required);
        public static bool InputDesktop() {
            int active, required;
            return GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 6, out active, 4, out required) && active != 0;
        }
        [StructLayout(LayoutKind.Sequential)] struct MouseInput {
            public int X, Y;
            public uint Data, Flags, Time;
            public UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Sequential)] struct Input {
            public uint Type;
            public MouseInput Mouse;
        }
        [DllImport("user32")] static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32")] static extern short GetAsyncKeyState(int key);
        public static bool PointerAvailable() {
            return InputDesktop() && GetAsyncKeyState(1)>=0 && GetAsyncKeyState(2)>=0 && GetAsyncKeyState(4)>=0;
        }
        public static bool ClickAt(Point point) {
            var screen = SystemInformation.VirtualScreen;
            if (!PointerAvailable() || !screen.Contains(point)) return false;
            // Aim at the center of the physical pixel on the whole virtual desktop.
            var move = new MouseInput {
                X = (int)(((long)(point.X - screen.Left) * 65536 + 32768) / screen.Width),
                Y = (int)(((long)(point.Y - screen.Top) * 65536 + 32768) / screen.Height),
                Flags = 0x8000 | 0x4000 | 0x0001 // ABSOLUTE | VIRTUALDESK | MOVE
            };
            var events = new [] { new Input { Mouse = move },
                new Input { Mouse = new MouseInput { Flags = 0x0002 } },
                new Input { Mouse = new MouseInput { Flags = 0x0004 } } };
            return SendInput(3, events, Marshal.SizeOf(typeof(Input))) == 3;
        }
    }

    public sealed class Target {
        public AutomationElement Element;
        public IntPtr Window;
        public Bounds Bounds;
        public Point Point;
        internal int[] RuntimeId;
    }

    public static class Targets {
        static readonly AutomationProperty[] Properties = {
            AutomationElement.ControlTypeProperty, AutomationElement.BoundingRectangleProperty, AutomationElement.RuntimeIdProperty,
            AutomationElement.IsEnabledProperty, AutomationElement.IsOffscreenProperty,
            AutomationElement.IsKeyboardFocusableProperty, AutomationElement.IsInvokePatternAvailableProperty,
            AutomationElement.IsTogglePatternAvailableProperty, AutomationElement.IsSelectionItemPatternAvailableProperty,
            AutomationElement.IsExpandCollapsePatternAvailableProperty
        };
        static readonly HashSet<ControlType> Controls = new HashSet<ControlType> {
            ControlType.Button, ControlType.Hyperlink, ControlType.Edit, ControlType.CheckBox,
            ControlType.RadioButton, ControlType.ComboBox, ControlType.ListItem, ControlType.MenuItem,
            ControlType.TabItem, ControlType.TreeItem, ControlType.SplitButton, ControlType.Slider,
            ControlType.Spinner, ControlType.DataItem, ControlType.HeaderItem
        };
        static bool Flag(AutomationElement e, AutomationProperty p) {
            return true.Equals(e.GetCachedPropertyValue(p, true));
        }
        static bool Actionable(AutomationElement e) {
            ControlType type = e.Cached.ControlType;
            if (!e.Cached.IsEnabled || e.Cached.IsOffscreen) return false;
            if (Controls.Contains(type)) return true;
            if (Flag(e, AutomationElement.IsInvokePatternAvailableProperty) ||
                Flag(e, AutomationElement.IsTogglePatternAvailableProperty) ||
                Flag(e, AutomationElement.IsSelectionItemPatternAvailableProperty) ||
                Flag(e, AutomationElement.IsExpandCollapsePatternAvailableProperty)) return true;
            return (type == ControlType.Custom || type == ControlType.Image) && e.Cached.IsKeyboardFocusable;
        }
        static bool RectangleOf(System.Windows.Rect r, out Bounds bounds) {
            bounds = Bounds.Empty;
            if (r.IsEmpty || r.Width < 2 || r.Height < 2 || Math.Abs(r.Left) > 100000 ||
                Math.Abs(r.Top) > 100000 || r.Width > 200000 || r.Height > 200000) return false;
            bounds = Bounds.FromLTRB((int)Math.Ceiling(r.Left), (int)Math.Ceiling(r.Top),
                                     (int)Math.Floor(r.Right), (int)Math.Floor(r.Bottom));
            return bounds.Width > 1 && bounds.Height > 1;
        }
        internal static bool ExpectedError(Exception e) {
            return e is ElementNotAvailableException || e is InvalidOperationException ||
                   e is UnauthorizedAccessException || e is COMException || e is NotSupportedException ||
                   e is ArgumentException || e is System.TimeoutException ||
                   e is System.ComponentModel.Win32Exception;
        }
        static List<Target> WindowTargets(IntPtr hwnd, Bounds screen) {
            var result = new List<Target>();
            var root = AutomationElement.FromHandle(hwnd);
            var cache = new CacheRequest();
            foreach (var p in Properties) cache.Add(p);
            AutomationElementCollection elements;
            using (cache.Activate()) elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement e in elements) {
                try {
                    Bounds bounds;
                    if (!Actionable(e) || !RectangleOf(e.Cached.BoundingRectangle, out bounds)) continue;
                    bounds.Intersect(screen);
                    if (bounds.Width < 2 || bounds.Height < 2) continue;
                    Point point = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                    if (Native.GetAncestor(Native.WindowFromPoint(point), 2) != hwnd) continue;
                    result.Add(new Target { Element=e, Window=hwnd, Bounds=bounds, Point=point,
                        RuntimeId=e.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty,true) as int[] });
                } catch (Exception ex) { if (!ExpectedError(ex)) throw; }
            }
            return result;
        }
        static void Collect(List<Target> sink, Bounds screen, Func<bool> expired) {
            uint self = (uint)Process.GetCurrentProcess().Id;
            var windows = new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr unused) {
                if (Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd)) windows.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            using (var remaining = new Region(screen)) {
                foreach (var hwnd in windows) {
                    if (expired()) return;
                    int cloaked;
                    uint pid;
                    Native.GetWindowThreadProcessId(hwnd, out pid);
                    if (pid == self || (Native.Style(hwnd, -20) & 0x20) != 0 ||
                        (Native.DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0)) continue;
                    Native.Rect rect;
                    if (!Native.GetWindowRect(hwnd, out rect)) continue;
                    var bounds = Bounds.Intersect(screen, Bounds.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
                    if (bounds.Width < 2 || bounds.Height < 2 || !remaining.IsVisible(bounds)) continue;
                    // Layered windows may have transparent interiors, so let point tests resolve them.
                    if ((Native.Style(hwnd, -20) & 0x80000) == 0) remaining.Exclude(bounds);
                    if (!Native.IsWindowEnabled(hwnd) || Native.IsHungAppWindow(hwnd)) continue;
                    try {
                        var found = WindowTargets(hwnd, bounds);
                        lock (sink) sink.AddRange(found);
                    } catch (Exception ex) { if (!ExpectedError(ex)) throw; }
                }
            }
        }
        public static List<Target> Scan(int timeout) {
            var sink = new List<Target>();
            var clock = Stopwatch.StartNew();
            var screen = SystemInformation.VirtualScreen;
            Exception failure = null;
            var worker = new Thread(delegate() {
                try { Collect(sink, screen, delegate { return clock.ElapsedMilliseconds >= timeout; }); }
                catch (Exception ex) { failure = ex; }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            if (worker.Join(timeout) && failure != null) throw failure;
            List<Target> result;
            lock (sink) result = new List<Target>(sink);
            result.Sort(delegate(Target a, Target b) {
                int c = a.Point.Y.CompareTo(b.Point.Y);
                if (c != 0) return c;
                c = a.Point.X.CompareTo(b.Point.X);
                return c != 0 ? c : ((long)a.Bounds.Width * a.Bounds.Height).CompareTo((long)b.Bounds.Width * b.Bounds.Height);
            });
            var points = new HashSet<Point>();
            result.RemoveAll(delegate(Target t) { return !points.Add(t.Point); });
            return result;
        }
        static bool HitsTarget(Target t, Point point, IntPtr control) {
            var hit = Native.WindowFromPoint(point);
            if (Native.GetAncestor(hit, 2) != t.Window) return false;
            // Some HWND-backed controls expose different accessibility fragments
            // through FromPoint and enumeration. The actual destination HWND is exact.
            if (control != IntPtr.Zero && control != t.Window && hit == control &&
                Automation.Compare(AutomationElement.FromHandle(control), t.Element)) return true;
            // Chromium may answer a hit test with the result of the previous
            // coordinate while its renderer resolves the new one asynchronously.
            var location = new System.Windows.Point(point.X, point.Y);
            AutomationElement.FromPoint(location);
            Thread.Sleep(50);
            if (Native.GetAncestor(Native.WindowFromPoint(point),2) != t.Window) return false;
            var leaf = AutomationElement.FromPoint(location);
            if (leaf == null || !leaf.Current.BoundingRectangle.Contains(location)) return false;
            for (int depth = 0; leaf != null && depth < 48; depth++) {
                if (Automation.Compare(leaf, t.Element)) return true;
                leaf = TreeWalker.RawViewWalker.GetParent(leaf);
            }
            return false;
        }
        // Re-read the original element, including its provider's clickable point.
        // A badge can be over adjacent text or a child even while the control is valid.
        static bool FindPoint(Target t, out Point point) {
            point = t.Point;
            try {
                if (!Native.IsWindowEnabled(t.Window) || !t.Element.Current.IsEnabled ||
                    t.Element.Current.IsOffscreen) return false;
                Bounds current;
                if (!RectangleOf(t.Element.Current.BoundingRectangle, out current)) return false;
                current.Intersect(SystemInformation.VirtualScreen);
                if (current.Width < 2 || current.Height < 2) return false;
                var control = new IntPtr(t.Element.Current.NativeWindowHandle);
                if (current.Contains(point) && HitsTarget(t, point, control)) return true;
                System.Windows.Point clickable;
                if (t.Element.TryGetClickablePoint(out clickable)) {
                    point = new Point((int)Math.Floor(clickable.X), (int)Math.Floor(clickable.Y));
                    if (current.Contains(point) && HitsTarget(t, point, control)) return true;
                }
                for (int x=1; x<=3; x++) {
                    for (int y=1; y<=3; y++) {
                        point = new Point(current.Left + x * current.Width / 4, current.Top + y * current.Height / 4);
                        if (HitsTarget(t, point, control)) return true;
                    }
                }
            } catch (Exception ex) { if (!ExpectedError(ex)) throw; }
            return false;
        }
        public static bool TryClickPoint(Target t, out Point point) {
            if (FindPoint(t, out point)) return true;
            // A provider proxy can expire between scan and selection. Rebind only
            // to the original runtime ID under the original top-level window.
            point = t.Point;
            if (t.RuntimeId == null || t.RuntimeId.Length == 0 ||
                Native.GetAncestor(Native.WindowFromPoint(point),2) != t.Window) return false;
            try {
                var hit = AutomationElement.FromPoint(new System.Windows.Point(point.X,point.Y));
                for (int depth=0; hit!=null && depth<48; depth++) {
                    if (Automation.Compare(t.RuntimeId,hit.GetRuntimeId())) {
                        t.Element=hit;
                        return FindPoint(t,out point);
                    }
                    hit=TreeWalker.RawViewWalker.GetParent(hit);
                }
            } catch (Exception ex) { if (!ExpectedError(ex)) throw; }
            return false;
        }
        public static bool IsCurrent(Target t) { Point point; return TryClickPoint(t, out point); }
        internal static bool StillAt(Target t, Point point) {
            Bounds bounds;
            return Native.IsWindowEnabled(t.Window) && t.Element.Current.IsEnabled && !t.Element.Current.IsOffscreen &&
                RectangleOf(t.Element.Current.BoundingRectangle,out bounds) && bounds.Contains(point) &&
                HitsTarget(t,point,new IntPtr(t.Element.Current.NativeWindowHandle));
        }
    }
}
