using System.Windows;

namespace LinkLibraryEditor
{
    public partial class App : Application
    {
        public App()
        {
            // An unhandled exception must never silently close the editor — an admin with
            // twenty unsaved edits deserves a message (and a chance to Save) instead.
            DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show(
                    "Something went wrong:\n\n" + args.Exception.Message +
                    "\n\nThe editor will stay open — save your work, then restart it.",
                    "Link Library Editor", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
