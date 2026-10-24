using System;
using System.Linq;
using System.Windows.Forms;

namespace ExtractorFotogramas
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (sender, e) => ShowUnexpectedError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ShowUnexpectedError(e.ExceptionObject as Exception);
            Application.Run(new MainForm(args.FirstOrDefault()));
        }

        static void ShowUnexpectedError(Exception exception)
        {
            MessageBox.Show(
                "Ocurrió un error inesperado:\n\n" + (exception == null ? "desconocido" : exception.Message),
                MainForm.WindowTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
