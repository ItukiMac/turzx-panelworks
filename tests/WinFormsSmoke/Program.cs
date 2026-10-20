// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;

namespace TurzxPanelWorks
{
    internal static class WinFormsSmoke
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.GetType().FullName);
                Console.Error.WriteLine(error.Message);
                Console.Error.WriteLine(error.StackTrace);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 2 && args[0] == "--instance-probe")
                using (SingleInstance instance = new SingleInstance(args[1])) return instance.IsOwner ? 0 : 23;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            CheckSingleInstance();
            string scene = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "scene.xml");
            using (DesignerForm editor = new DesignerForm(null, scene))
            using (DisplayHostForm host = new DisplayHostForm(scene, null))
            {
                if (editor.Icon == null || host.Icon == null) throw new Exception("Application icons missing.");
                using (Icon editorExe = Icon.ExtractAssociatedIcon(typeof(DesignerForm).Assembly.Location))
                using (Icon hostExe = Icon.ExtractAssociatedIcon(typeof(DisplayHostForm).Assembly.Location))
                    if (editorExe == null || hostExe == null) throw new Exception("Executable icons missing.");
                // Do not Show: the Host's live telemetry/discovery lifecycle is never started.
                foreach (Form form in new Form[] { editor, host })
                {
                    CreateHandles(form);
                    form.PerformLayout();
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        if (args.Length == 1)
                        {
                            Directory.CreateDirectory(args[0]);
                            bitmap.Save(Path.Combine(args[0], form == host ? "host.png" : "editor.png"));
                        }
                    }
                }
                DataGridView table = null;
                foreach (Control control in host.Controls) if (control is DataGridView) table = (DataGridView)control;
                if (table == null || table.Columns.Count != 5) throw new Exception("Per-display assignment controls missing.");
                DataGridViewComboBoxColumn rotation = table.Columns["Rotation"] as DataGridViewComboBoxColumn;
                if (rotation == null || rotation.Items.Count != 4) throw new Exception("Rotation choices missing.");
                for (int i = 0; i < 4; i++)
                    if ((int)rotation.Items[i] != i * 90) throw new Exception("Invalid rotation choice.");
                // Never Show Host: doing so would start live discovery.
                host.WindowState = FormWindowState.Minimized;
                typeof(Control).GetMethod("OnResize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, new object[] { EventArgs.Empty });
                NotifyIcon tray = (NotifyIcon)typeof(DisplayHostForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                if (!tray.Visible || host.ShowInTaskbar || host.Visible) throw new Exception("Host did not minimize to tray: tray=" + tray.Visible + " taskbar=" + host.ShowInTaskbar + " visible=" + host.Visible + " state=" + host.WindowState);
                host.WindowState = FormWindowState.Normal;
                typeof(Control).GetMethod("OnResize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, new object[] { EventArgs.Empty });
                // Handle recreation may normalize WindowState while hidden; the tray must remain reachable.
                if (!tray.Visible) throw new Exception("Hidden Host lost its tray icon during handle recreation.");
                foreach (Control control in editor.Controls)
                    if (control.Right > editor.ClientSize.Width || control.Bottom > editor.ClientSize.Height)
                        throw new Exception("Editor outside: " + control.GetType().Name + " " + control.Text + " " + control.Bounds + " client=" + editor.ClientSize);
                foreach (Control control in host.Controls)
                    if (control.Right > host.ClientSize.Width || control.Bottom > host.ClientSize.Height)
                        throw new Exception("Host control is outside the client area.");
            }
            Console.WriteLine("PASS: Editor/Host construction, layout and paint (no live discovery or USB).");
            return 0;
        }

        private static void CheckSingleInstance()
        {
            string role = "Smoke." + Guid.NewGuid().ToString("N");
            using (SingleInstance first = new SingleInstance(role))
            {
                if (!first.IsOwner) throw new Exception("First instance rejected.");
                if (Probe(role) != 23) throw new Exception("Duplicate process was allowed.");
                if (!first.ConsumeActivation()) throw new Exception("Duplicate process did not request activation.");
                if (Probe(role + ".Other") != 0) throw new Exception("Different app role was blocked.");
            }
            if (Probe(role) != 0) throw new Exception("Instance lock was not released.");
            using (Form form = new Form())
            {
                form.ShowInTaskbar = false;
                form.WindowState = FormWindowState.Minimized;
                AppLifetime.Restore(form);
                if (!form.Visible || !form.ShowInTaskbar || form.WindowState != FormWindowState.Normal)
                    throw new Exception("Existing window restoration failed.");
            }
        }

        private static int Probe(string role)
        {
            using (Process child = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                "--instance-probe " + role) { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (!child.WaitForExit(10000)) throw new Exception("Instance probe timed out.");
                return child.ExitCode;
            }
        }

        private static void CreateHandles(Control control)
        {
            IntPtr handle = control.Handle;
            foreach (Control child in control.Controls) CreateHandles(child);
        }
    }
}
