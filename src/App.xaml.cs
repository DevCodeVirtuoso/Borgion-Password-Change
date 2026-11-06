using System;
using System.Threading;
using System.Windows;

namespace BorgionPasswordChange
{
    public partial class App : Application
    {
        static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, @"Local\BorgionPasswordChange", out var isNew);
            if (!isNew)
            {
                // Er staat al een venster open.
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, args) =>
            {
                MessageBox.Show("Er is een onverwachte fout opgetreden:\n\n" + args.Exception.Message,
                    "Borgion Password Change", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            base.OnStartup(e);
            new MainWindow().Show();
        }
    }
}
