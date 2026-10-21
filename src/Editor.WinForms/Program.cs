// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Windows.Forms;

namespace TurzxPanelWorks
{
    internal static class EditorProgram
    {
        [STAThread]
        private static void Main(string[] args)
        {
            LayoutCatalog.EnsureTemplates();
            if (args.Length == 1 && args[0] == "--export-templates") return;
            string scene = args.Length > 0 ? Path.GetFullPath(args[0]) : LayoutCatalog.DefaultPath;
            string background = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppLifetime.Run("Editor", delegate { return new DesignerForm(background, scene); });
        }
    }
}
