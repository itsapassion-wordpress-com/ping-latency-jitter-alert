// Author: itsapassion.wordpress.com

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TrayPingAlert
{
    static class Program
    {
        private static NotifyIcon trayIcon;
        private static Timer pingTimer;
        private static Timer blinkTimer;
        private static Timer hoverHideTimer;

        private static Icon currentIcon;
        private static long? lastPing;
        private static long currentPing = -1;
        private static long currentJitter;

        private static bool blinkState;
        private static int blinkTicks;

        private const int MaxSamples = 30;
        private static Queue<long> pingHistory = new Queue<long>();
        private static Queue<long> jitterHistory = new Queue<long>();

        private static PingGraphForm graphForm;

        // ===== STARTUP OPTIONS =====
        private static string pingAddress = "8.8.8.8";
        private static int latencyAlertMs = 20;
        private static int jitterAlertMs = 10;
        private static int pingIntervalSec = 3;

        private static bool jitterAlertEnabled = true;
        private static bool showGraphOnHover = true;
        private static bool showJitter = true;
        private static bool alwaysOnTopGraph = true;

        private static TrackBar latencySlider;
        private static TrackBar jitterSlider;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            trayIcon = new NotifyIcon { Visible = true };
            ContextMenuStrip menu = new ContextMenuStrip();



            // ===== Latency alert =====
            ToolStripLabel latencyLabel = new ToolStripLabel($"Ping latency alert: {latencyAlertMs} ms");
            menu.Items.Add(latencyLabel);

            latencySlider = new TrackBar { Minimum = 1, Maximum = 300, Value = latencyAlertMs, Width = 200 };
            latencySlider.Scroll += delegate
            {
                latencyAlertMs = latencySlider.Value;
                latencyLabel.Text = $"Latency alert: {latencyAlertMs} ms";
            };
            menu.Items.Add(new ToolStripControlHost(latencySlider) { Width = 200, Height = 30 });

            menu.Items.Add(new ToolStripSeparator());



            // ===== Jitter alert =====
            ToolStripMenuItem jitterEnable = new ToolStripMenuItem("Enable jitter alert")
            {
                Checked = jitterAlertEnabled,
                CheckOnClick = true
            };
            jitterEnable.CheckedChanged += delegate
            {
                jitterAlertEnabled = jitterEnable.Checked;
                jitterSlider.Enabled = jitterAlertEnabled;
            };
            menu.Items.Add(jitterEnable);

            ToolStripLabel jitterLabel = new ToolStripLabel($"Jitter alert: {jitterAlertMs} ms");
            menu.Items.Add(jitterLabel);

            jitterSlider = new TrackBar { Minimum = 1, Maximum = 200, Value = jitterAlertMs, Width = 200 };
            jitterSlider.Scroll += delegate
            {
                jitterAlertMs = jitterSlider.Value;
                jitterLabel.Text = $"Jitter alert: {jitterAlertMs} ms";
            };
            menu.Items.Add(new ToolStripControlHost(jitterSlider) { Width = 140, Height = 30 });
            menu.Items.Add(new ToolStripSeparator());



            // ===== Ping interval =====
            ToolStripLabel intervalLabel = new ToolStripLabel($"Ping interval: {pingIntervalSec} s");
            menu.Items.Add(intervalLabel);

            TrackBar intervalSlider = new TrackBar { Minimum = 1, Maximum = 30, Value = pingIntervalSec, Width = 200 };
            intervalSlider.Scroll += delegate
            {
                pingIntervalSec = intervalSlider.Value;
                intervalLabel.Text = $"Ping interval: {pingIntervalSec} s";
                pingTimer.Interval = pingIntervalSec * 1000;
            };
            menu.Items.Add(new ToolStripControlHost(intervalSlider) { Width = 140, Height = 30 });
            menu.Items.Add(new ToolStripSeparator());



            // ===== Ping Host =====
            menu.Items.Add(new ToolStripLabel("Ping target:"));

            TextBox hostBox = new TextBox { Text = pingAddress, Width = 100 };
            Button applyBtn = new Button { Text = "Apply" };
            applyBtn.Click += delegate
            {
                if (!string.IsNullOrWhiteSpace(hostBox.Text))
                    pingAddress = hostBox.Text.Trim();
            };

            FlowLayoutPanel hostPanel = new FlowLayoutPanel { Width = 270, Height = 30 };
            hostPanel.Controls.Add(hostBox);
            hostPanel.Controls.Add(applyBtn);
            menu.Items.Add(new ToolStripControlHost(hostPanel) { Width = 270, Height = 35 });
            menu.Items.Add(new ToolStripSeparator());



            // ===== Graph options =====
            ToolStripMenuItem hoverToggle = new ToolStripMenuItem("Graph: show on icon hover")
            {
                Checked = showGraphOnHover,
                CheckOnClick = true
            };

            ToolStripMenuItem alwaysOnTopToggle = new ToolStripMenuItem("Graph: always on top / movable")
            {
                Checked = alwaysOnTopGraph,
                CheckOnClick = true
            };

            hoverToggle.CheckedChanged += delegate
            {
                showGraphOnHover = hoverToggle.Checked;

                if (!showGraphOnHover)
                {
                    alwaysOnTopGraph = false;
                    alwaysOnTopToggle.Checked = false;
                    graphForm.Hide();
                }
            };
            menu.Items.Add(hoverToggle);


            alwaysOnTopToggle.CheckedChanged += delegate
            {
                if (!showGraphOnHover)
                {
                    alwaysOnTopToggle.Checked = false;
                    alwaysOnTopGraph = false;
                    return;
                }

                alwaysOnTopGraph = alwaysOnTopToggle.Checked;
                graphForm.TopMost = alwaysOnTopGraph;
            };
            menu.Items.Add(alwaysOnTopToggle);

            ToolStripMenuItem jitterToggle = new ToolStripMenuItem("Graph: show jitter")
            {
                Checked = showJitter,
                CheckOnClick = true
            };
            jitterToggle.CheckedChanged += delegate { showJitter = jitterToggle.Checked; };
            menu.Items.Add(jitterToggle);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Author: Leszek", null, OpenSite));
            void OpenSite(object sender, EventArgs e)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = "https://itsapassion.wordpress.com", UseShellExecute = true });
            }


            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, Exit));

            trayIcon.ContextMenuStrip = menu;

            graphForm = new PingGraphForm(
                () => pingHistory,
                () => jitterHistory,
                () => showJitter,
                () => pingAddress
            );

            graphForm.TopMost = alwaysOnTopGraph;
            trayIcon.MouseMove += TrayMouseMove;

            hoverHideTimer = new Timer { Interval = 800 };
            hoverHideTimer.Tick += delegate
            {
                hoverHideTimer.Stop();
                if (!alwaysOnTopGraph) graphForm.Hide();
            };

            pingTimer = new Timer { Interval = pingIntervalSec * 1000 };
            pingTimer.Tick += UpdatePing;
            pingTimer.Start();

            blinkTimer = new Timer { Interval = 400 };
            blinkTimer.Tick += BlinkTick;

            UpdateTrayIcon();
            Application.Run();
        }

        private static void UpdatePing(object sender, EventArgs e)
        {
            try
            {
                using (Ping p = new Ping())
                {
                    PingReply r = p.Send(pingAddress, 1000);
                    if (r.Status != IPStatus.Success) return;

                    long ping = r.RoundtripTime;
                    currentJitter = lastPing.HasValue ? Math.Abs(ping - lastPing.Value) : 0;
                    lastPing = ping;
                    currentPing = ping;

                    pingHistory.Enqueue(ping);
                    if (pingHistory.Count > MaxSamples) pingHistory.Dequeue();

                    jitterHistory.Enqueue(currentJitter);
                    if (jitterHistory.Count > MaxSamples) jitterHistory.Dequeue();

                    // Blink on jitter alert
                    if (jitterAlertEnabled && currentJitter > jitterAlertMs)
                    {
                        blinkTicks = 0;
                        blinkTimer.Start();
                    }
                    else
                    {
                        blinkTimer.Stop();
                        blinkState = false;
                    }
                }
            }
            catch { }

            UpdateTrayIcon();
            graphForm.Invalidate();
        }

        private static void BlinkTick(object sender, EventArgs e)
        {
            blinkTicks++;
            blinkState = !blinkState;
            if (blinkTicks >= 6)
            {
                blinkTimer.Stop();
                blinkState = false;
            }
            UpdateTrayIcon();
        }

        private static void UpdateTrayIcon()
        {
            if (currentIcon != null)
            {
                DestroyIcon(currentIcon.Handle);
                currentIcon.Dispose();
            }

            Color bg = currentPing > latencyAlertMs ? Color.Red : Color.LightGreen;
            bool blinkText = jitterAlertEnabled && currentJitter > jitterAlertMs && blinkState;
            currentIcon = CreateTextIcon(currentPing, blinkText, bg);
            trayIcon.Icon = currentIcon;
        }

        private static Icon CreateTextIcon(long ping, bool blinkText, Color bgColor)
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(bgColor);
                string text;
                float fontSize;

                if (ping < 100)
                {
                    text = ping.ToString();
                    fontSize = 10f;
                }
                else if (ping < 1000)
                {
                    text = ping.ToString();
                    fontSize = 8f;
                }
                else
                {
                    text = (ping / 1000) + "s";
                    fontSize = 8f;
                }

                using (Font f = new Font("Segoe UI", fontSize, FontStyle.Regular))
                using (Brush b = new SolidBrush(blinkText ? Color.Orange : Color.Black))
                {
                    SizeF size = g.MeasureString(text, f);
                    g.DrawString(text, f, b, (16 - size.Width) / 2, (16 - size.Height) / 2);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private static void TrayMouseMove(object sender, MouseEventArgs e)
        {
            if (!showGraphOnHover) return;

            // Positioning graph
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            graphForm.Location = new Point(
                wa.Right - graphForm.Width - 10,
                wa.Bottom - graphForm.Height - 10);

            if (!graphForm.Visible)
                graphForm.Show();

            graphForm.BringToFront();

            hoverHideTimer.Stop();
            hoverHideTimer.Start();
        }

        private static void Exit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            graphForm.Close();
            Application.Exit();
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
    }

    class PingGraphForm : Form
    {
        private Func<IEnumerable<long>> getPing;
        private Func<IEnumerable<long>> getJitter;
        private Func<bool> showJitter;
        private Func<string> getHost;

        private bool dragging = false;
        private Point dragStart;

        public PingGraphForm(
            Func<IEnumerable<long>> ping,
            Func<IEnumerable<long>> jitter,
            Func<bool> showJitterFunc,
            Func<string> hostFunc)
        {
            getPing = ping;
            getJitter = jitter;
            showJitter = showJitterFunc;
            getHost = hostFunc;

            Size = new Size(300, 170);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            BackColor = Color.Black;

            // move the graph manually
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; } };
            MouseUp += (s, e) => dragging = false;
            MouseMove += (s, e) => { if (dragging) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            long[] ping = getPing().ToArray();
            if (ping.Length < 2) return;

            Graphics g = e.Graphics;

            int leftAxisX = 45;
            int rightAxisX = ClientSize.Width - 25;
            int bottom = ClientSize.Height - 30;
            int top = 30;
            int width = rightAxisX - leftAxisX - 10;
            int height = bottom - top;

            long maxPing = Math.Max(50, ping.Max());
            long maxJitter = showJitter() ? Math.Max(1, getJitter().Max()) : 1;

            // Labels on OY-Axes
            g.DrawLine(Pens.Gray, leftAxisX, top, leftAxisX, bottom);
            g.DrawLine(Pens.Gray, leftAxisX, bottom, leftAxisX + width, bottom);

            using (Font f = new Font("Segoe UI", 7))
            {
                g.DrawString("Ping", f, Brushes.Lime, leftAxisX - 40, top - 15);
                if (showJitter()) g.DrawString("Jitter", f, Brushes.Orange, rightAxisX - 40, top - 15);
                g.DrawString("Host: " + getHost(), f, Brushes.White, leftAxisX + 5, 3);

                int steps = 4;
                for (int i = 0; i <= steps; i++)
                {
                    float y = bottom - (i / (float)steps) * height;
                    long pingVal = maxPing * i / steps;
                    g.DrawString(pingVal + " ms", f, Brushes.Lime, 2, y - 6);

                    if (showJitter())
                    {
                        long jitterVal = maxJitter * i / steps;
                        g.DrawString(jitterVal + " ms", f, Brushes.Orange, rightAxisX - 25, y - 6);
                    }

                    g.DrawLine(Pens.DimGray, leftAxisX, y, leftAxisX + width, y);
                }
            }

            DrawLine(g, ping, Color.Lime, leftAxisX, bottom, width, height, maxPing);
            if (showJitter())
                DrawLine(g, getJitter().ToArray(), Color.Orange, leftAxisX, bottom, width, height, maxJitter);
        }

        private void DrawLine(Graphics g, long[] data, Color color, int x0, int y0, int w, int h, long max)
        {
            using (Pen pen = new Pen(color, 2))
            {
                for (int i = 1; i < data.Length; i++)
                {
                    float x1 = x0 + (i - 1) * (w / (float)(data.Length - 1));
                    float x2 = x0 + i * (w / (float)(data.Length - 1));
                    float y1 = y0 - (data[i - 1] / (float)max) * h;
                    float y2 = y0 - (data[i] / (float)max) * h;
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }
        }
    }
}