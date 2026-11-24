using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using WindowsVoiceAccessFullNumbers;

class Fixture {
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
    [DllImport("user32")] static extern bool PostMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
    [DllImport("user32",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,string text);
    [StructLayout(LayoutKind.Sequential)] struct GuiInfo {
        public int Size, Flags;
        public IntPtr Active, Focus, Capture, Menu, MoveSize, Caret;
        public Native.Rect CaretBounds;
    }
    [DllImport("user32")] static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32")] static extern bool EnumChildWindows(IntPtr hwnd,Native.EnumProc cb,IntPtr l);
    [DllImport("user32",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,System.Text.StringBuilder sb,int max);
    [DllImport("user32")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    static void CheckBarAppearance(IntPtr bar) {
        Native.Rect rect;
        if (!Native.GetWindowRect(bar,out rect)) throw new Exception("Cannot find the number bar bounds");
        var screen=Screen.PrimaryScreen.Bounds;
        if (Math.Abs(rect.Left+rect.Right-screen.Left-screen.Right)>1 || screen.Bottom-rect.Bottom!=3 ||
            (Native.Style(bar,-16)&0x00C00000)!=0) throw new Exception("Expected a borderless bar at the bottom center");
        using (var image=new Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top))
        using (var g=Graphics.FromImage(image)) {
            IntPtr dc=g.GetHdc(); bool painted;
            try {painted=PrintWindow(bar,dc,1);} finally {g.ReleaseHdc(dc);}
            if (!painted || image.GetPixel(2,2).ToArgb()!=Color.Black.ToArgb()) throw new Exception("Number bar is not black");
        }
    }
    static bool BadgeNear(Target t) {
        IntPtr overlay=IntPtr.Zero;
        while ((overlay=FindWindowEx(IntPtr.Zero,overlay,null,"Windows Voice Access Full Numbers overlay"))!=IntPtr.Zero) {
            Native.Rect r; Native.GetWindowRect(overlay,out r);
            if (t.Point.X<r.Left || t.Point.X>=r.Right || t.Point.Y<r.Top || t.Point.Y>=r.Bottom) continue;
            using (var b=new Bitmap(r.Right-r.Left,r.Bottom-r.Top)) {
                using (var g=Graphics.FromImage(b)) {
                    IntPtr dc=g.GetHdc();
                    try { if(!PrintWindow(overlay,dc,1)) return false; } finally { g.ReleaseHdc(dc); }
                }
                int x=t.Point.X-r.Left,y=t.Point.Y-r.Top, matches=0;
                for(int row=Math.Max(0,y-25);row<Math.Min(b.Height,y+25);row++)
                    for(int col=Math.Max(0,x-38);col<Math.Min(b.Width,x+38);col++) {
                        Color color=b.GetPixel(col,row);
                        if(color.R==18 && color.G==55 && color.B==105) matches++;
                    }
                return matches>12;
            }
        }
        return false;
    }
    static string Title(IntPtr hwnd) {
        var sb=new System.Text.StringBuilder(350); GetWindowText(hwnd,sb,sb.Capacity); return sb.ToString();
    }
    [DllImport("user32")] static extern IntPtr GetThreadDesktop(int thread);
    [DllImport("kernel32")] static extern int GetCurrentThreadId();
    [DllImport("user32",CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h,int index,System.Text.StringBuilder name,int length,out int needed);
    static Button Button(string text,int x,int y) {
        var b=new Button {Text=text, Location=new Point(x,y), Size=new Size(150,45)};
        b.Click+=delegate { File.AppendAllText("fixture-clicks.txt",text+Environment.NewLine); }; return b;
    }
    static void CheckDesktop() {
        var n=new System.Text.StringBuilder(128);int needed;
        GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()),2,n,256,out needed);
        if (!n.ToString().StartsWith("WindowsVoiceAccessFullNumbersTest")) throw new Exception("Test desktop not selected");
    }
    [STAThread] static void Main(string[] args) {
        try {
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
            CheckDesktop();
            if (args.Length>0 && args[0]=="modal-check") {
                var names=Targets.Scan(9000).Select(t=>t.Element.Current.Name).ToList();
                if (!names.Contains("Notification action") || names.Contains("Background action")) throw new Exception("Unexpected modal targets");
                File.AppendAllText("fixture-pass.txt","The modal dialog received a label and the disabled parent was excluded."+Environment.NewLine);
                return;
            }
            if (args.Length>0 && args[0]=="check") {
                bool browser=args[1].StartsWith("Browser ");
                bool numeric=args[1].StartsWith("entry-");
                bool cancel=args[1]=="cancel" || args[1]=="entry-invalid";
                var list=Targets.Scan(9000);
                if (browser && !list.Any(t=>t.Element.Current.Name==args[1])) {
                    Thread.Sleep(1800); list=Targets.Scan(9000);
                }
                var names=list.Select(t=>t.Element.Current.Name).ToList();
                File.WriteAllLines(browser ? "browser-labels.txt" : "fixture-labels.txt",names);
                string desired=args[1];
                int n=numeric ? (args[1]=="entry-prefix" ? 10 : 108) : cancel ? 1 : names.IndexOf(desired)+1;
                if (numeric) {
                    if (names.Count<109) throw new Exception("Fixture needs at least 109 labels");
                    desired=names[n-1];
                    if (!desired.StartsWith("Grid ")) throw new Exception("Unexpected numeric fixture target: "+desired);
                }
                if (n==0 || (browser ? !(names.Contains("Browser notification action") || names.Contains("Activated notification")) ||
                    !(names.Contains("Browser background action") || names.Contains("Activated background"))
                    : !names.Contains("Notification action") || !names.Contains("Background action"))) throw new Exception("Missing fixture controls");
                File.AppendAllText("fixture-check.txt","Revalidate "+desired+" before opening: "+Targets.IsCurrent(list[n-1])+Environment.NewLine);
                var helper=new ProcessStartInfo(Environment.GetEnvironmentVariable("FWN_TEST_EXECUTABLE")); helper.UseShellExecute=false;
                using (var p=Process.Start(helper)) {
                    try {
                        AutomationElement input=null;
                        var timer=Stopwatch.StartNew();
                        while (timer.ElapsedMilliseconds<17000) {
                            var form=FindWindowEx(IntPtr.Zero,IntPtr.Zero,null,"Full Numbers");
                            if (form!=IntPtr.Zero) input=AutomationElement.FromHandle(form).FindFirst(TreeScope.Descendants,
                                new PropertyCondition(AutomationElement.NameProperty,"Label number"));
                            if (input!=null && FindWindowEx(IntPtr.Zero,IntPtr.Zero,null,"Windows Voice Access Full Numbers overlay")!=IntPtr.Zero) break;
                            Thread.Sleep(60);
                        }
                        if (input==null || input.Current.ControlType!=ControlType.Edit) throw new Exception("No editable number field");
                        Thread.Sleep(150);
                        CheckBarAppearance(FindWindowEx(IntPtr.Zero,IntPtr.Zero,null,"Full Numbers"));
                        // UIA's global focus can refer to the interactive desktop. Check
                        // the test app's own GUI thread on this isolated desktop instead.
                        var edit=new IntPtr(input.Current.NativeWindowHandle);
                        uint owner;
                        var info=new GuiInfo {Size=Marshal.SizeOf(typeof(GuiInfo))};
                        if (!GetGUIThreadInfo(Native.GetWindowThreadProcessId(edit,out owner),ref info) || info.Focus!=edit)
                            throw new Exception("Number field does not own keyboard focus. Expected "+edit+", focused "+info.Focus+", active "+Title(info.Active)+", foreground "+Title(Native.GetForegroundWindow()));
                        var verify=list.Where((t,i)=>names[i].IndexOf("background action",StringComparison.OrdinalIgnoreCase)>=0 ||
                            names[i].IndexOf("notification action",StringComparison.OrdinalIgnoreCase)>=0).ToList();
                        if (!verify.All(BadgeNear)) throw new Exception("Missing painted label on a fixture control");
                        string before=File.Exists("fixture-clicks.txt") ? File.ReadAllText("fixture-clicks.txt") : "";
                        if (args[1]=="entry-manual") {
                            foreach (char c in "109\b8") {
                                PostMessage(edit,0x102,new IntPtr(c),IntPtr.Zero);
                                Thread.Sleep(280);
                            }
                            Thread.Sleep(750);
                            if (p.HasExited || ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).Current.Value!="108")
                                throw new Exception("Manual digit entry or Backspace failed");
                            PostMessage(edit,0x100,new IntPtr(13),IntPtr.Zero);
                        } else if (numeric && args[1]!="entry-stream") {
                            var value=(ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern);
                            if (cancel) {
                                SendMessage(edit,0x000C,IntPtr.Zero,"108");
                                SendMessage(edit,0x000C,IntPtr.Zero,"1089");
                                Thread.Sleep(950);
                                if (p.HasExited || value.Current.Value!="1089") throw new Exception("An invalid label was submitted");
                                PostMessage(edit,0x100,new IntPtr(27),IntPtr.Zero);
                            } else {
                                value.SetValue(args[1]=="entry-words" ? "Type one hundred and eight." :
                                    args[1]=="entry-spelled" ? "one oh eight" :
                                    args[1]=="entry-prefix" ? "10" : "108");
                            }
                        } else {
                            foreach(char c in n.ToString()) PostMessage(edit,0x102,new IntPtr(c),IntPtr.Zero);
                            if (!numeric) {
                                Thread.Sleep(750);
                                if (p.HasExited) throw new Exception("A single key should only filter until Enter");
                                PostMessage(edit,0x100,new IntPtr(cancel ? 27 : 13),IntPtr.Zero);
                            }
                        }
                        // A private, non-input desktop cannot grant foreground to
                        // a window. Text explicitly sent here must stay pending;
                        // only confirm it with Enter after checking that guard.
                        if (numeric && !cancel && args[1]!="entry-manual" && Native.GetForegroundWindow()==IntPtr.Zero) {
                            Thread.Sleep(800);
                            if (p.HasExited) throw new Exception("Submitted dictated text without foreground ownership");
                            PostMessage(edit,0x100,new IntPtr(13),IntPtr.Zero);
                        }
                        if (!p.WaitForExit(6500)) {
                            Native.EnumWindows(delegate(IntPtr hw,IntPtr unused) {
                                uint pid; Native.GetWindowThreadProcessId(hw,out pid);
                                if(pid==(uint)p.Id && Native.IsWindowVisible(hw)) {
                                    File.AppendAllText("fixture-check.txt","helper window = "+Title(hw)+Environment.NewLine);
                                    EnumChildWindows(hw,delegate(IntPtr child,IntPtr skip) {
                                        File.AppendAllText("fixture-check.txt","child = "+Title(child)+Environment.NewLine); return true;
                                    },IntPtr.Zero);
                                } return true;
                            },IntPtr.Zero);
                            throw new Exception("The helper did not exit");
                        }
                        if (cancel) {
                            if ((File.Exists("fixture-clicks.txt") ? File.ReadAllText("fixture-clicks.txt") : "")!=before) throw new Exception("Cancel triggered an action");
                            File.AppendAllText("fixture-pass.txt","Escape closed the overlay without activating a new target."+Environment.NewLine);
                            return;
                        }
                        if (Native.InputDesktop()) throw new Exception("Scratch desktop unexpectedly receives physical input");
                        if ((File.Exists("fixture-clicks.txt") ? File.ReadAllText("fixture-clicks.txt") : "")!=before ||
                            (browser && Targets.Scan(9000).Any(t=>t.Element.Current.Name.StartsWith("Activated "))))
                            throw new Exception("An inactive desktop received an action");
                        File.AppendAllText("fixture-pass.txt","Accepted label "+n+" and exited on the inactive desktop."+Environment.NewLine);
                    } finally { if (!p.HasExited) p.Kill(); }
                }
                return;
            }
            Application.EnableVisualStyles();
            var main=new Form {Text="Number test main", StartPosition=FormStartPosition.Manual, Bounds=new Rectangle(60,100,760,460)};
            main.Controls.Add(Button("Background action",35,45));
            main.Controls.Add(Button("Another background action",265,45));
            if (args.Length>0 && args[0]=="numbers") {
                for (int i=0;i<156;i++) {
                    var b=Button("Grid "+i,6+(i%13)*54,115+(i/13)*22);
                    b.Size=new Size(51,21); main.Controls.Add(b);
                }
            }
            var popup=new Form {Text="Number test notification", StartPosition=FormStartPosition.Manual,
                Bounds=new Rectangle(470,325,240,145),FormBorderStyle=FormBorderStyle.FixedToolWindow,ShowInTaskbar=false};
            popup.Controls.Add(Button("Notification action",20,25));
            if (args.Length>0 && args[0]=="modal") {
                popup.Shown+=delegate {File.WriteAllText("fixture-ready.txt","ready");};
                main.Shown+=delegate {popup.ShowDialog(main);};
            } else main.Shown+=delegate { popup.Show(main); File.WriteAllText("fixture-ready.txt","ready"); };
            Application.Run(main);
        } catch(Exception e) { File.AppendAllText("fixture-errors.txt",e.ToString()+Environment.NewLine); Environment.ExitCode=1; }
    }
}
