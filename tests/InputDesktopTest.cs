using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using WindowsVoiceAccessFullNumbers;

// Run only by explicit request on an interactive desktop. The session receives
// targets exclusively from the scratch process we start, including its modal dialog.
class InputDesktopTest {
    [DllImport("user32")] static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string text);
    [DllImport("user32", EntryPoint="SendMessageW")] static extern IntPtr SendKey(IntPtr h, int msg, IntPtr w, IntPtr l);
    static string dir;
    static void Record(string s) { File.AppendAllText(Path.Combine(dir,"events"), s+Environment.NewLine); }
    static List<string> Events() { return File.ReadAllLines(Path.Combine(dir,"events")).ToList(); }
    static void Check(bool ok,string reason) { if (!ok) throw new Exception(reason); }

    sealed class Scratch : Form {
        readonly int driver;
        readonly Button replace;
        readonly Button moving;
        public Scratch(int driver) {
            this.driver=driver;
            Text="Full Numbers scratch test"; StartPosition=FormStartPosition.CenterScreen; TopMost=true;
            ClientSize=new Size(740,420);
            var field=new TextBox { AccessibleName="Destination field", Location=new Point(22,45), Width=250 };
            field.TextChanged+=delegate {Record("field "+field.Text);}; Controls.Add(field);
            var other=new TextBox {AccessibleName="Original field",Location=new Point(292,45)};
            other.TextChanged+=delegate {Record("wrong field");}; Controls.Add(other);
            replace=ButtonAt("Replaceable",22,90); Controls.Add(replace);
            moving=ButtonAt("Moving target",540,42); Controls.Add(moving);
            var modal=ButtonAt("Open test dialog",150,90); Controls.Add(modal);
            modal.Click+=delegate { BeginInvoke((Action)delegate {
                using (var dialog=new Form {Text="Full Numbers scratch dialog",StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(350,170)}) {
                    var initial=ButtonAt("Dismiss dialog",18,18); dialog.Controls.Add(initial);
                    initial.Click+=delegate {dialog.BeginInvoke((Action)dialog.Close);};
                    var edit=new TextBox {AccessibleName="Dialog field",Location=new Point(20,80),Width=250};
                    edit.TextChanged+=delegate {Record("dialog field "+edit.Text);}; dialog.Controls.Add(edit);
                    dialog.ShowDialog(this);
                }
            }); };
            var toggle=new CheckBox {Text="Toggle once",Location=new Point(340,90),AutoSize=true};
            toggle.CheckedChanged+=delegate {Record("toggle "+toggle.Checked);}; Controls.Add(toggle);
            for (int i=0;i<132;i++) {
                var button=ButtonAt("Key "+i,12+(i%12)*60,145+(i/12)*24);
                button.Size=new Size(57,23); Controls.Add(button);
            }
            Shown+=delegate {Activate(); other.Focus(); Record("ready");};
        }
        static Button ButtonAt(string name,int x,int y) {
            var b=new Button { Text=name, AccessibleName=name, Location=new Point(x,y), Size=new Size(120,33) };
            b.MouseDown+=delegate {Record("down "+name);}; b.MouseUp+=delegate {Record("up "+name);};
            b.Click+=delegate {Record("click "+name);}; return b;
        }
        protected override void WndProc(ref Message msg) {
            if (msg.Msg==0x8051) {AllowSetForegroundWindow(driver); return;}
            if (msg.Msg==0x8052) {var decoy=ButtonAt("Decoy",22,90); Controls.Add(decoy); decoy.BringToFront(); replace.Dispose(); return;}
            if (msg.Msg==0x8053) { var old=moving.Location; moving.Top+=50; Controls.Add(ButtonAt("Old position",old.X,old.Y)); return; }
            base.WndProc(ref msg);
        }
    }

    static List<Target> ScratchTargets(Process fixture) {
        var list=Targets.Scan(9000).Where(t=>{
            uint pid; Native.GetWindowThreadProcessId(t.Window,out pid); return pid==fixture.Id;
        }).ToList();
        if (list.Count==0) {
            fixture.Refresh(); var w=fixture.MainWindowHandle;
            Native.Rect r; Native.GetWindowRect(w,out r);
            Console.WriteLine("Scratch window visible={0}, foreground={1}, bounds={2},{3},{4},{5}, center exposed={6}",
                Native.IsWindowVisible(w), Native.GetForegroundWindow()==w,r.Left,r.Top,r.Right,r.Bottom,
                Native.GetAncestor(Native.WindowFromPoint(new Point((r.Left+r.Right)/2,(r.Top+r.Bottom)/2)),2)==w);
        }
        Check(list.Count!=0,"No visible scratch targets"); return list;
    }
    static IntPtr GlobalEdit(Target t) {
        var h=new IntPtr(t.Element.Current.NativeWindowHandle);
        Check(h!=IntPtr.Zero && Native.OwnsKeyboardInput(t.Window,h) && t.Element.Current.HasKeyboardFocus,
            "The selected edit does not own system keyboard focus"); return h;
    }
    static int Select(List<Target> list, int number, Process fixture, bool slow, bool replace) {
        if (fixture!=null) SendKey(fixture.MainWindowHandle,0x8051,IntPtr.Zero,IntPtr.Zero);
        // Seed real scratch controls for the literal 108 test. For all other
        // cases, resolve the scratch target in the session's normal desktop scan.
        var expected=list[number-1];
        var labels=slow ? list : new List<Target>();
        using (var session=new NumberSession(labels,false))
        using (var timer=new System.Windows.Forms.Timer {Interval=80}) {
            var elapsed=Stopwatch.StartNew();
            int i=0; bool sent=false, requestedFocus=false, sawNotice=false;
            bool moving=fixture!=null && list[number-1].Element.Current.Name=="Moving target";
            string value=null;
            Exception failed=null;
            timer.Tick+=delegate {
                try {
                    if (elapsed.ElapsedMilliseconds>18000) throw new Exception("Timed out waiting for selection or input focus");
                    if (sent) {
                        if (replace && Application.OpenForms.Cast<Form>().SelectMany(f=>f.Controls.OfType<Label>())
                            .Any(l=>l.Text.StartsWith("Control unavailable"))) sawNotice=true;
                        return;
                    }
                    var bar=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text=="Full Numbers");
                    if (bar==null) return;
                    var edit=bar.Controls.OfType<TextBox>().Single();
                    if (!Native.OwnsKeyboardInput(bar.Handle,edit.Handle)) {
                        if (!requestedFocus) {
                            var p=edit.PointToScreen(new Point(3,3));
                            if (Native.WindowFromPoint(p)==edit.Handle) requestedFocus=Native.ClickAt(p);
                        }
                        return;
                    }
                    if (!bar.Controls.OfType<Label>().Any(l=>l.Text.StartsWith("Type a label") ||
                        l.Text.StartsWith("Selecting ") || l.Text.StartsWith("Enter selects"))) return;
                    if (value==null) {
                        number=labels.FindIndex(t=>t.Window==expected.Window && Automation.Compare(t.Element,expected.Element))+1;
                        Check(number>0,"The actual overlay did not label the scratch control");
                        value=number.ToString();
                    }
                    if (replace) SendKey(fixture.MainWindowHandle,0x8052,IntPtr.Zero,IntPtr.Zero);
                    if (moving) SendKey(fixture.MainWindowHandle,0x8053,IntPtr.Zero,IntPtr.Zero);
                    if (slow) {
                        SendKey(edit.Handle,0x0102,new IntPtr(value[i++]),IntPtr.Zero);
                        timer.Interval=330; sent=i==value.Length;
                    } else {
                        SendMessage(edit.Handle,0x000C,IntPtr.Zero,value); sent=true;
                    }
                } catch(Exception e) { failed=e; timer.Stop(); Application.ExitThread(); }
            };
            timer.Start(); Application.Run(session);
            if (failed!=null) throw failed;
            Check(sent,"The test did not insert its label");
            if (replace) Check(sawNotice && Application.OpenForms.Count==0,"The stale-control notice did not appear and dismiss itself");
            return number;
        }
    }
    static void Browser(string chrome, string page) {
        var args="--user-data-dir=\""+Path.Combine(dir,"chrome-profile")+"\" --no-first-run --disable-sync "+
            "--no-default-browser-check --disable-background-mode --window-size=900,720 --app=\""+new Uri(page).AbsoluteUri+"\"";
        using (var browser=Process.Start(chrome,args)) {
            try {
                var clock=Stopwatch.StartNew();
                while (browser.MainWindowHandle==IntPtr.Zero && clock.ElapsedMilliseconds<10000) {Thread.Sleep(100); browser.Refresh();}
                var window=browser.MainWindowHandle;
                Check(window!=IntPtr.Zero,"Chrome scratch window missing");
                var screen=Screen.PrimaryScreen.WorkingArea;
                Native.ShowTopmostWithoutActivation(window,new Rectangle(screen.Left+24,screen.Top+24,screen.Width-48,screen.Height-48));
                Thread.Sleep(500);
                foreach(string name in new[]{"Browser background action","Browser notification action","Partly covered action","Scratch field"}) {
                    var list=ScratchTargets(browser);
                    int index=list.FindIndex(t=>t.Element.Current.Name==name);
                    for (int attempt=0; index<0 && attempt<5; attempt++) {
                        Thread.Sleep(500); list=ScratchTargets(browser);
                        index=list.FindIndex(t=>t.Element.Current.Name==name);
                    }
                    Check(index>=0,"Chrome did not expose "+name);
                    if (name=="Partly covered action") {
                        var target=list[index]; Point found;
                        Check(Targets.TryClickPoint(target,out found) && found!=target.Point,"Could not resolve the exposed edge");
                    }
                    Select(list,index+1,null,false,false);
                    if (name=="Scratch field") {
                        Check(Native.GetForegroundWindow()==window && list[index].Element.Current.HasKeyboardFocus &&
                            Automation.Compare(AutomationElement.FocusedElement,list[index].Element),"Browser input did not receive focus");
                    } else {
                        string changed=name=="Partly covered action" ? "Activated exposed edge" :
                            name=="Browser background action" ? "Activated background" : "Activated notification";
                        bool received=false;
                        for(int tries=0; !received && tries<6; tries++) {
                            Thread.Sleep(100); received=ScratchTargets(browser).Any(t=>t.Element.Current.Name==changed);
                        }
                        Check(received,"Page did not receive pointer click: "+name+" cursor="+Cursor.Position+" foreground="+(Native.GetForegroundWindow()==window));
                    }
                    Console.WriteLine("PASS Chrome -> "+name);
                }
            } finally {
                if (!browser.HasExited) {browser.CloseMainWindow(); if (!browser.WaitForExit(3000)) browser.Kill();}
            }
        }
    }
    static void Trial(Process fixture,string name,bool edit,bool stale) {
        var list=ScratchTargets(fixture);
        int n=name==null ? 108 : list.FindIndex(t=>t.Element.Current.Name==name)+1;
        Check(n>0 && n<=list.Count,"Missing scratch control "+name);
        var target=list[n-1]; name=target.Element.Current.Name;
        var before=Events().Count;
        n=Select(list,n,fixture,n==108,stale);
        if (edit) {
            // Send the next character specifically to the verified system focus HWND.
            SendKey(GlobalEdit(target),0x0102,new IntPtr('x'),IntPtr.Zero);
        }
        var events=Events().Skip(before).ToList();
        if (stale) Check(events.Count==0,"A replaced control received input");
        else if (edit) Check(events.SequenceEqual(new[]{name=="Dialog field" ? "dialog field x" : "field x"}),"Wrong text destination");
        else if (name=="Toggle once") Check(events.SequenceEqual(new[]{"toggle True"}),"Checkbox did not toggle exactly once");
        else foreach (string action in new[]{"down ","up ","click "})
            Check(events.Count(s=>s==action+name)==1,"Missing or duplicate "+action+name+" ("+String.Join(", ",events)+")");
        Console.WriteLine("PASS {0} -> {1}",n,stale ? "stale target dismissed without clicking its replacement" : name);
    }
    [STAThread] static int Main(string[] args) {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); Application.EnableVisualStyles();
        dir=args[0];
        int driver;
        if (args.Length==2 && Int32.TryParse(args[1],out driver)) {Application.Run(new Scratch(driver)); return 0;}
        try {
            Check(Native.InputDesktop(),"Requires an interactive input desktop");
            if (args.Length==3) {Browser(args[1],args[2]); return 0;}
            File.WriteAllText(Path.Combine(dir,"events"),"");
            var start=new ProcessStartInfo(Application.ExecutablePath,"\""+dir+"\" "+Process.GetCurrentProcess().Id);
            start.UseShellExecute=false; start.CreateNoWindow=true;
            using (var fixture=Process.Start(start)) {
                try {
                    var clock=Stopwatch.StartNew();
                    while (!Events().Contains("ready") && clock.ElapsedMilliseconds<7000) Thread.Sleep(50);
                    fixture.Refresh();
                    var window=fixture.MainWindowHandle;
                    Native.Rect bounds; Native.GetWindowRect(window,out bounds);
                    var blank=new Point(bounds.Left+45,bounds.Bottom-10);
                    if (Native.GetForegroundWindow()!=window) {
                        Check(window!=IntPtr.Zero && Native.WindowFromPoint(blank)==window,"Scratch window is covered");
                        Check(Native.ClickAt(blank),"Could not focus the scratch window"); Thread.Sleep(150);
                    }
                    Trial(fixture,null,false,false);
                    Trial(fixture,"Destination field",true,false);
                    Trial(fixture,"Toggle once",false,false);
                    Trial(fixture,"Moving target",false,false);
                    Trial(fixture,"Replaceable",false,true);
                    Trial(fixture,"Open test dialog",false,false);
                    Trial(fixture,"Dialog field",true,false);
                    Trial(fixture,"Dismiss dialog",false,false);
                } finally { if (!fixture.HasExited) {fixture.CloseMainWindow(); if (!fixture.WaitForExit(1500)) fixture.Kill();} }
            }
            return 0;
        } catch(Exception e) {Console.Error.WriteLine(e); return 1;}
    }
}
