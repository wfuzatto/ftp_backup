using FtpBackup.Core.Ipc;

namespace FtpBackup.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly MainForm _form;

    public TrayApplicationContext(bool startMinimized)
    {
        _form = new MainForm();
        _form.FormClosed += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowMain());
        menu.Items.Add("Backup agora", null, async (_, _) => await BackupNowFromTrayAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair da interface", null, (_, _) =>
        {
            _form.AllowApplicationExit();
            _form.Close();
        });

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "FTP Backup",
            Visible = true,
            ContextMenuStrip = menu
        };

        _tray.DoubleClick += (_, _) => ShowMain();

        if (!startMinimized)
            ShowMain();
    }

    private void ShowMain()
    {
        if (!_form.Visible)
            _form.Show();

        if (_form.WindowState == FormWindowState.Minimized)
            _form.WindowState = FormWindowState.Normal;

        _form.BringToFront();
        _form.Activate();
    }

    private async Task BackupNowFromTrayAsync()
    {
        try
        {
            var response = await ControlClient.SendAsync("backup-now", TimeSpan.FromSeconds(5));
            _tray.ShowBalloonTip(
                3000,
                "FTP Backup",
                response.Message,
                response.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(4000, "FTP Backup", $"Serviço indisponível: {ex.Message}", ToolTipIcon.Error);
        }
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
