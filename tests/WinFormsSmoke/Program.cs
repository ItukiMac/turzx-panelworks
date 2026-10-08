// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace StandaloneDisplay
{
    internal static class WinFormsSmoke
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string scene = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "scene.xml");
            using (DesignerForm editor = new DesignerForm(null, scene))
            using (DisplayHostForm host = new DisplayHostForm(scene, null))
            {
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
                if (table == null || table.Columns.Count != 4) throw new Exception("Per-display assignment controls missing.");
                foreach (Control control in host.Controls)
                    if (control.Right > host.ClientSize.Width || control.Bottom > host.ClientSize.Height)
                        throw new Exception("Host control is outside the client area.");
            }
            Console.WriteLine("PASS: Editor/Host construction, layout and paint (no live discovery or USB).");
            return 0;
        }

        private static void CreateHandles(Control control)
        {
            IntPtr handle = control.Handle;
            foreach (Control child in control.Controls) CreateHandles(child);
        }
    }
}
