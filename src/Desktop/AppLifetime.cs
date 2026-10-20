// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace TurzxPanelWorks
{
    public sealed class SingleInstance : IDisposable
    {
        private readonly Mutex mutex;
        private readonly EventWaitHandle activation;
        public bool IsOwner { get; private set; }
        public SingleInstance(string role)
        {
            string name = @"Local\TurzxPanelWorks." + WindowsIdentity.GetCurrent().User.Value + "." + role;
            activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
            mutex = new Mutex(false, name + ".Instance");
            try { IsOwner = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { IsOwner = true; }
            if (!IsOwner) activation.Set();
        }
        public bool ConsumeActivation() { return activation.WaitOne(0); }
        public void Dispose()
        {
            if (IsOwner) { mutex.ReleaseMutex(); IsOwner = false; }
            mutex.Dispose();
            activation.Dispose();
        }
    }

    public static class AppLifetime
    {
        public static Icon LoadIcon(Assembly assembly)
        {
            using (System.IO.Stream stream = assembly.GetManifestResourceStream("AppIcon"))
            using (Icon icon = new Icon(stream))
                return (Icon)icon.Clone();
        }
        public static void Restore(Form form)
        {
            if (form.IsDisposed || form.Disposing) return;
            form.ShowInTaskbar = true;
            form.Show();
            form.WindowState = FormWindowState.Normal;
            form.Activate();
        }
        public static void Run(string role, Func<Form> createForm)
        {
            using (SingleInstance instance = new SingleInstance(role))
            {
                if (!instance.IsOwner) return;
                using (Form form = createForm())
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 })
                {
                    timer.Tick += delegate { if (instance.ConsumeActivation()) Restore(form); };
                    timer.Start();
                    Application.Run(form);
                }
            }
        }
    }
}
