// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Windows.Forms;

namespace TurzxPanelWorks
{
    internal static class DisplayHostProgram
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string scene = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scene.xml");
            string background = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppLifetime.Run("Host", delegate { return new DisplayHostForm(scene, background); });
        }
    }
}
