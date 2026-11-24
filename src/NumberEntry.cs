using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WindowsVoiceAccessFullNumbers {
    // Voice Access can insert a complete value through UI Automation, paste, or a
    // sequence of Unicode characters. A real, focused edit control accepts all three.
    sealed class NumberEntry : Form {
        sealed class EntryBox : TextBox {
            public bool WholeInsertion { get; private set; }
            public bool Composing { get; private set; }
            public event EventHandler CompositionCommitted;
            string beforeComposition;
            bool digitKey;
            protected override void WndProc(ref Message message) {
                if (message.Msg == 0x0100) { // WM_KEYDOWN, including VK_PACKET for Unicode text
                    int key = message.WParam.ToInt32();
                    digitKey = (key >= 48 && key <= 57) || (key >= 96 && key <= 105);
                }
                if (message.Msg == 0x010D) { // WM_IME_STARTCOMPOSITION
                    beforeComposition = Text;
                    Composing = true;
                }
                bool previous = WholeInsertion;
                WholeInsertion = previous || message.Msg == 0x000C || // WM_SETTEXT
                    message.Msg == 0x00C2 || message.Msg == 0x0302 || // EM_REPLACESEL, WM_PASTE
                    (message.Msg == 0x0102 && !digitKey && message.WParam.ToInt64() >= 32);
                try { base.WndProc(ref message); } finally { WholeInsertion = previous; }
                if (message.Msg == 0x0101 || message.Msg == 0x0102) digitKey = false;
                if (message.Msg == 0x010E) { // WM_IME_ENDCOMPOSITION
                    Composing = false;
                    if (Text != beforeComposition && CompositionCommitted != null) CompositionCommitted(this, EventArgs.Empty);
                }
            }
        }

        readonly EntryBox entry = new EntryBox();
        readonly Label prompt = new Label();
        readonly Label title = new Label();
        readonly Timer settle = new Timer { Interval = 600 };
        readonly Timer focusCheck = new Timer { Interval = 100 };
        readonly Stopwatch sinceEdit = Stopwatch.StartNew();
        readonly IList<Target> targets;
        readonly Action<string> filter;
        readonly Action<int> pick;
        readonly Action cancel;
        Dictionary<string,int> spoken;
        string previousText = "";
        bool previousWasAppend;
        int selected;
        bool ready, submitAfterLoad, hasInput, focusKeyAvailable;
        public event Action<bool> InputFocusChanged;

        public NumberEntry(IList<Target> targets, Action<string> filter, Action<int> pick, Action cancel) {
            this.targets = targets; this.filter = filter; this.pick = pick; this.cancel = cancel;
            Text = "Full Numbers"; ShowInTaskbar = true; TopMost = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black; ForeColor = Color.White;
            Font = new Font("Segoe UI", 10, FontStyle.Bold);
            title.Text = "Windows Voice Access Full Numbers   |";
            foreach (Label label in new [] { title, prompt }) {
                label.AutoSize = true;
                label.Click += delegate { ActivateInput(); };
                Controls.Add(label);
            }
            entry.AccessibleName = "Label number";
            entry.BorderStyle = BorderStyle.None; entry.BackColor = Color.Black; entry.ForeColor = Color.White;
            Controls.Add(entry);
            ActiveControl = entry;
            settle.Tick += delegate {
                if (!OwnsInput() && sinceEdit.ElapsedMilliseconds < 1500) return;
                settle.Stop();
                if (OwnsInput() && !entry.Composing && selected > 0) pick(selected);
                else LayoutBar();
            };
            entry.TextChanged += Changed;
            entry.CompositionCommitted += delegate { if (selected > 0) { settle.Stop(); settle.Start(); } };
            focusCheck.Tick += delegate { RefreshFocus(); };
            Click += delegate { ActivateInput(); };
        }
        bool OwnsInput() {
            return IsHandleCreated && entry.IsHandleCreated && Native.OwnsKeyboardInput(Handle, entry.Handle);
        }
        public void ActivateInput() {
            // Request foreground only at launch or on a user action directed to this
            // bar. Polling checks ownership without taking focus back from the user.
            Native.SetForegroundWindow(Handle);
            ActiveControl = entry;
            entry.Focus();
            RefreshFocus();
        }
        void RefreshFocus() {
            bool owns = OwnsInput();
            if (owns == hasInput) return;
            hasInput = owns;
            if (owns) Native.AnnounceEditFocus(entry.Handle);
            if (InputFocusChanged != null) InputFocusChanged(owns);
            LayoutBar();
        }
        public void SetFocusKey(bool available) { focusKeyAvailable = available; LayoutBar(); }
        void LayoutBar() {
            if (!ready) prompt.Text = "Finding controls…";
            else if (!hasInput) prompt.Text = focusKeyAvailable ? "Press F8 or click here to type   |   Esc closes" : "Click here to type   |   Esc closes";
            else if (settle.Enabled) prompt.Text = "Selecting " + selected + "   |   Esc closes";
            else if (selected > 0) prompt.Text = "Enter selects   |   Backspace edits   |   Esc closes";
            else prompt.Text = String.IsNullOrWhiteSpace(entry.Text) ? "Type a label number   |   Esc closes" : "Enter a label from 1 to " + targets.Count;
            entry.Width = TextRenderer.MeasureText("0000", Font).Width;
            int h = Math.Max(title.PreferredHeight, entry.PreferredHeight) + 8;
            title.Location = new Point(8, (h - title.PreferredHeight) / 2);
            entry.Location = new Point(title.Right + 3, (h - entry.PreferredHeight) / 2);
            prompt.Location = new Point(entry.Right + 3, (h - prompt.PreferredHeight) / 2);
            var screen = Screen.PrimaryScreen.Bounds;
            int w = prompt.Right + 8;
            Bounds = new Rectangle(screen.Left + (screen.Width - w) / 2, screen.Bottom - h - 3, w, h);
        }
        public void TargetsReady() {
            ready = true;
            // Refresh only within our foreground window. Never re-activate a different
            // app's background window just because a scan finished.
            if (Native.SameAppInForeground(Handle)) ActivateInput();
            else if (Native.GetForegroundWindow() == IntPtr.Zero) {
                // A non-input desktop can have an active window on this thread
                // without a system foreground window. Prepare its edit locally.
                Native.SetActiveWindow(Handle);
                entry.Focus();
            }
            RefreshFocus();
            UpdateSelection(submitAfterLoad);
        }
        public void SetManualText(string value) {
            entry.Text = value;
            entry.SelectionStart = entry.TextLength;
            settle.Stop(); submitAfterLoad = false;
            LayoutBar();
        }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); LayoutBar(); }
        protected override void OnActivated(EventArgs e) {
            base.OnActivated(e);
            if (entry.CanFocus) entry.Focus();
        }
        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            ActivateInput(); focusCheck.Start();
        }
        protected override bool ProcessCmdKey(ref Message m, Keys key) {
            if (key == Keys.Escape) { cancel(); return true; }
            if (key == Keys.Enter) { if (selected > 0) pick(selected); return true; }
            return base.ProcessCmdKey(ref m, key);
        }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            base.OnFormClosing(e);
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; BeginInvoke(cancel); }
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { settle.Dispose(); focusCheck.Dispose(); }
            base.Dispose(disposing);
        }

        void Changed(object sender, EventArgs e) {
            bool pending = settle.Enabled || (!ready && submitAfterLoad);
            settle.Stop();
            string text = entry.Text;
            bool append = text.Length > previousText.Length && text.StartsWith(previousText, StringComparison.Ordinal);
            bool burst = append && previousWasAppend && sinceEdit.ElapsedMilliseconds < 200;
            bool dictated = entry.WholeInsertion || (append && (text.Length - previousText.Length > 1 || burst || pending));
            previousText = text;
            previousWasAppend = append;
            sinceEdit.Restart();
            if (!ready) { submitAfterLoad = dictated; return; }
            UpdateSelection(dictated);
        }
        void UpdateSelection(bool dictated) {
            selected = ReadLabel(entry.Text);
            string digits = selected > 0 ? selected.ToString(CultureInfo.InvariantCulture) : "";
            filter(digits);
            // A lone key keeps its prefix visible. An insertion or burst selects only
            // after edits settle, so 108 cannot select 1 or 10 on the way in.
            bool lastPossibleDigit = previousWasAppend && selected > targets.Count / 10;
            if ((dictated || lastPossibleDigit) && !entry.Composing && selected > 0 && (hasInput || OwnsInput())) settle.Start();
            LayoutBar();
        }

        int ReadLabel(string value) {
            string text = value.Trim().TrimEnd('.', '!', '?').Trim().ToLowerInvariant();
            if (text.StartsWith("type ", StringComparison.Ordinal)) text = text.Substring(5).Trim();
            if (text.Length == 0) return 0;
            string numeric = Regex.IsMatch(text, @"^[1-9](?:\s+[0-9])+$") ? Regex.Replace(text, @"\s+", "") : text;
            if (Regex.IsMatch(numeric, @"^(?:[1-9][0-9]*|[1-9][0-9]{0,2}(?:,[0-9]{3})+)$")) {
                int n;
                return Int32.TryParse(numeric.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out n)
                    && n <= targets.Count ? n : 0;
            }
            if (spoken == null) {
                spoken = new Dictionary<string,int>(StringComparer.Ordinal);
                for (int n = 1; n <= targets.Count; n++) spoken.Add(Words(n), n);
            }
            string words = Regex.Replace(text.Replace('-', ' '), @"\s+", " ").Replace(" and ", " ");
            int label;
            if (spoken.TryGetValue(words, out label)) return label;
            // Some dictation settings return individual digit words, e.g. "one oh eight".
            long digits = 0;
            foreach (string word in words.Split(' ')) {
                int digit = word == "oh" ? 0 : Array.IndexOf(Small, word, 0, 10);
                if (digit < 0 || (digits == 0 && digit == 0)) return 0;
                digits = digits * 10 + digit;
                if (digits > targets.Count) return 0;
            }
            return (int)digits;
        }
        static readonly string[] Small = "zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen".Split(' ');
        static readonly string[] Tens = "zero ten twenty thirty forty fifty sixty seventy eighty ninety".Split(' ');
        static string Words(int n) {
            if (n < 20) return Small[n];
            if (n < 100) return Tens[n / 10] + Remainder(n % 10);
            if (n < 1000) return Small[n / 100] + " hundred" + Remainder(n % 100);
            if (n < 1000000) return Words(n / 1000) + " thousand" + Remainder(n % 1000);
            return Words(n / 1000000) + " million" + Remainder(n % 1000000);
        }
        static string Remainder(int n) { return n == 0 ? "" : " " + Words(n); }
    }
}
