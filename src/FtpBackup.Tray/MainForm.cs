using System.Diagnostics;
using FtpBackup.Core.Ipc;
using FtpBackup.Core.Models;
using FtpBackup.Core.Services;

namespace FtpBackup.Tray;

public sealed class MainForm : Form
{
    private readonly SettingsStore _settingsStore = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 2000 };

    private readonly TextBox _source = new() { Dock = DockStyle.Fill };
    private readonly TextBox _staging = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _enabled = new() { Text = "Agendamento automático habilitado", AutoSize = true };

    private readonly TextBox _host = new() { Dock = DockStyle.Fill, PlaceholderText = "ftp.exemplo.com ou ftp://ftp.exemplo.com" };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 21, Width = 100 };
    private readonly ComboBox _security = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _username = new() { Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "Deixe em branco para manter a senha atual" };
    private readonly TextBox _remoteFolder = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _passive = new() { Text = "Modo passivo (recomendado)", AutoSize = true };
    private readonly CheckBox _invalidCert = new() { Text = "Aceitar certificado TLS inválido (não recomendado)", AutoSize = true };

    private readonly ComboBox _frequency = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly DateTimePicker _startTime = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 100 };
    private readonly NumericUpDown _hourlyMinute = new() { Minimum = 0, Maximum = 59, Width = 100 };
    private readonly ComboBox _weeklyDay = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _monthlyDay = new() { Minimum = 1, Maximum = 31, Value = 1, Width = 100 };

    private readonly TextBox _prefix = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _retries = new() { Minimum = 0, Maximum = 20, Value = 3, Width = 100 };
    private readonly NumericUpDown _retryDelay = new() { Minimum = 1, Maximum = 3600, Value = 30, Width = 100 };
    private readonly NumericUpDown _localRetention = new() { Minimum = 0, Maximum = 10000, Value = 3, Width = 100 };
    private readonly NumericUpDown _remoteRetention = new() { Minimum = 0, Maximum = 10000, Value = 30, Width = 100 };
    private readonly CheckBox _failUnreadable = new() { Text = "Falhar se algum arquivo não puder ser lido", AutoSize = true };
    private readonly CheckBox _uploadSha = new() { Text = "Enviar arquivo .sha256 junto com o ZIP", AutoSize = true };

    private readonly Label _serviceStatus = new() { AutoSize = true, Text = "Serviço: verificando..." };
    private readonly Label _stage = new() { AutoSize = true, Text = "Etapa: -" };
    private readonly Label _next = new() { AutoSize = true, Text = "Próximo: -" };
    private readonly Label _lastSuccess = new() { AutoSize = true, Text = "Último sucesso: -" };
    private readonly Label _lastError = new() { AutoSize = true, MaximumSize = new Size(720, 0) };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Dock = DockStyle.Fill, Height = 20 };

    private string _loadedEncryptedPassword = string.Empty;
    private bool _allowExit;
    private bool _statusPolling;

    public MainForm()
    {
        Text = "FTP Backup";
        Width = 860;
        Height = 820;
        MinimumSize = new Size(760, 680);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScroll = true;

        _security.DataSource = Enum.GetValues<FtpSecurityMode>();
        _frequency.DataSource = Enum.GetValues<BackupFrequency>();
        _weeklyDay.DataSource = Enum.GetValues<DayOfWeek>();
        _frequency.SelectedIndexChanged += (_, _) => UpdateScheduleControls();

        BuildUi();
        LoadSettings();

        _statusTimer.Tick += async (_, _) => await PollStatusAsync();
        _statusTimer.Start();

        Shown += async (_, _) => await PollStatusAsync();
        FormClosing += MainForm_FormClosing;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                Hide();
        };
    }

    public void AllowApplicationExit() => _allowExit = true;

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(12)
        };

        root.Controls.Add(BuildGeneralGroup());
        root.Controls.Add(BuildFtpGroup());
        root.Controls.Add(BuildScheduleGroup());
        root.Controls.Add(BuildAdvancedGroup());
        root.Controls.Add(BuildStatusGroup());
        root.Controls.Add(BuildButtons());

        Controls.Add(root);
    }

    private Control BuildGeneralGroup()
    {
        var group = NewGroup("Origem do backup");
        var table = NewTable();
        AddRow(table, "Pasta a compactar", _source, BrowseButton(_source));
        AddRow(table, "Pasta temporária / staging", _staging, BrowseButton(_staging));
        AddFullRow(table, _enabled);
        group.Controls.Add(table);
        return group;
    }

    private Control BuildFtpGroup()
    {
        var group = NewGroup("Servidor FTP / FTPS");
        var table = NewTable();
        AddRow(table, "Servidor / URL", _host);
        AddRow(table, "Porta", _port);
        AddRow(table, "Segurança", _security);
        AddRow(table, "Usuário", _username);
        AddRow(table, "Senha", _password);
        AddRow(table, "Pasta remota", _remoteFolder);
        AddFullRow(table, _passive);
        AddFullRow(table, _invalidCert);
        group.Controls.Add(table);
        return group;
    }

    private Control BuildScheduleGroup()
    {
        var group = NewGroup("Agendamento");
        var table = NewTable();
        AddRow(table, "Periodicidade", _frequency);
        AddRow(table, "Horário diário/semanal/mensal", _startTime);
        AddRow(table, "Minuto de cada hora", _hourlyMinute);
        AddRow(table, "Dia da semana", _weeklyDay);
        AddRow(table, "Dia do mês", _monthlyDay);
        group.Controls.Add(table);
        return group;
    }

    private Control BuildAdvancedGroup()
    {
        var group = NewGroup("Confiabilidade e retenção");
        var table = NewTable();
        AddRow(table, "Prefixo do ZIP", _prefix);
        AddRow(table, "Tentativas extras de FTP", _retries);
        AddRow(table, "Intervalo entre tentativas (s)", _retryDelay);
        AddRow(table, "Manter ZIPs locais (0 = ilimitado)", _localRetention);
        AddRow(table, "Manter ZIPs no FTP (0 = ilimitado)", _remoteRetention);
        AddFullRow(table, _failUnreadable);
        AddFullRow(table, _uploadSha);
        group.Controls.Add(table);
        return group;
    }

    private Control BuildStatusGroup()
    {
        var group = NewGroup("Status");
        var table = NewTable();
        AddFullRow(table, _serviceStatus);
        AddFullRow(table, _stage);
        AddFullRow(table, _progress);
        AddFullRow(table, _next);
        AddFullRow(table, _lastSuccess);
        AddFullRow(table, _lastError);
        group.Controls.Add(table);
        return group;
    }

    private Control BuildButtons()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 16)
        };

        panel.Controls.Add(NewButton("Salvar", async (_, _) =>
        {
            if (SaveSettings(showSuccess: true))
                await PollStatusAsync();
        }));
        panel.Controls.Add(NewButton("Testar FTP", async (_, _) => await TestFtpAsync()));
        panel.Controls.Add(NewButton("Backup agora", async (_, _) => await BackupNowAsync()));
        panel.Controls.Add(NewButton("Abrir logs", (_, _) => OpenLogs()));
        return panel;
    }

    private void LoadSettings()
    {
        var s = _settingsStore.Load();
        s.NormalizeDefaults();
        _loadedEncryptedPassword = s.EncryptedPassword;

        _enabled.Checked = s.Enabled;
        _source.Text = s.SourceFolder;
        _staging.Text = s.StagingFolder;
        _host.Text = s.FtpHost;
        _port.Value = Math.Clamp(s.FtpPort, 1, 65535);
        _security.SelectedItem = s.SecurityMode;
        _username.Text = s.FtpUsername;
        _password.Text = string.Empty;
        _remoteFolder.Text = s.RemoteFolder;
        _passive.Checked = s.PassiveMode;
        _invalidCert.Checked = s.AllowInvalidCertificate;

        _frequency.SelectedItem = s.Frequency;
        var time = BackupScheduler.ParseTime(s.StartTime);
        _startTime.Value = DateTime.Today.Add(time.ToTimeSpan());
        _hourlyMinute.Value = Math.Clamp(s.HourlyMinute, 0, 59);
        _weeklyDay.SelectedItem = s.WeeklyDay;
        _monthlyDay.Value = Math.Clamp(s.MonthlyDay, 1, 31);

        _prefix.Text = s.ArchivePrefix;
        _retries.Value = Math.Clamp(s.RetryCount, 0, 20);
        _retryDelay.Value = Math.Clamp(s.RetryDelaySeconds, 1, 3600);
        _localRetention.Value = Math.Clamp(s.LocalRetentionCount, 0, 10000);
        _remoteRetention.Value = Math.Clamp(s.RemoteRetentionCount, 0, 10000);
        _failUnreadable.Checked = s.FailOnUnreadableFile;
        _uploadSha.Checked = s.UploadSha256File;
        UpdateScheduleControls();
    }

    private BackupSettings ReadSettingsFromUi()
    {
        var encrypted = _loadedEncryptedPassword;
        if (!string.IsNullOrEmpty(_password.Text))
            encrypted = _settingsStore.ProtectPassword(_password.Text);

        return new BackupSettings
        {
            Enabled = _enabled.Checked,
            SourceFolder = _source.Text.Trim(),
            StagingFolder = _staging.Text.Trim(),
            FtpHost = _host.Text.Trim(),
            FtpPort = (int)_port.Value,
            FtpUsername = _username.Text.Trim(),
            EncryptedPassword = encrypted,
            RemoteFolder = _remoteFolder.Text.Trim(),
            SecurityMode = (FtpSecurityMode)(_security.SelectedItem ?? FtpSecurityMode.ExplicitTls),
            PassiveMode = _passive.Checked,
            AllowInvalidCertificate = _invalidCert.Checked,
            Frequency = (BackupFrequency)(_frequency.SelectedItem ?? BackupFrequency.Daily),
            StartTime = _startTime.Value.ToString("HH:mm"),
            HourlyMinute = (int)_hourlyMinute.Value,
            WeeklyDay = (DayOfWeek)(_weeklyDay.SelectedItem ?? DayOfWeek.Sunday),
            MonthlyDay = (int)_monthlyDay.Value,
            ArchivePrefix = _prefix.Text.Trim(),
            RetryCount = (int)_retries.Value,
            RetryDelaySeconds = (int)_retryDelay.Value,
            LocalRetentionCount = (int)_localRetention.Value,
            RemoteRetentionCount = (int)_remoteRetention.Value,
            FailOnUnreadableFile = _failUnreadable.Checked,
            UploadSha256File = _uploadSha.Checked
        };
    }

    private bool SaveSettings(bool showSuccess)
    {
        try
        {
            var settings = ReadSettingsFromUi();
            var errors = BackupValidator.Validate(settings);
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join(Environment.NewLine, errors), "Configuração inválida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _settingsStore.Save(settings);
            _loadedEncryptedPassword = settings.EncryptedPassword;
            _password.Clear();

            if (showSuccess)
                MessageBox.Show("Configuração salva.", "FTP Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erro ao salvar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private async Task TestFtpAsync()
    {
        if (!SaveSettings(showSuccess: false))
            return;

        try
        {
            UseWaitCursor = true;
            var response = await ControlClient.SendAsync("test-ftp", TimeSpan.FromSeconds(45));
            MessageBox.Show(response.Message, "Teste FTP", MessageBoxButtons.OK,
                response.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível falar com o serviço: {ex.Message}", "Teste FTP", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async Task BackupNowAsync()
    {
        if (!SaveSettings(showSuccess: false))
            return;

        try
        {
            var response = await ControlClient.SendAsync("backup-now", TimeSpan.FromSeconds(5));
            MessageBox.Show(response.Message, "FTP Backup", MessageBoxButtons.OK,
                response.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            await PollStatusAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Serviço indisponível: {ex.Message}", "FTP Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task PollStatusAsync()
    {
        if (_statusPolling)
            return;

        _statusPolling = true;
        try
        {
            var response = await ControlClient.SendAsync("status", TimeSpan.FromSeconds(2));
            var state = response.State;

            _serviceStatus.Text = state is null ? "Serviço: online" : $"Serviço: online | Estado: {state.Status}";
            if (state is null)
                return;

            _stage.Text = $"Etapa: {state.Stage}";
            _progress.Value = Math.Clamp(state.ProgressPercent, 0, 100);
            _next.Text = $"Próximo backup: {(state.NextScheduledLocal?.ToString("dd/MM/yyyy HH:mm") ?? "-")}";
            _lastSuccess.Text = $"Último sucesso: {(state.LastSuccess?.LocalDateTime.ToString("dd/MM/yyyy HH:mm:ss") ?? "-")}";
            _lastError.Text = string.IsNullOrWhiteSpace(state.LastError) ? "Último erro: -" : $"Último erro: {state.LastError}";
        }
        catch
        {
            _serviceStatus.Text = "Serviço: offline / não instalado";
        }
        finally
        {
            _statusPolling = false;
        }
    }

    private void UpdateScheduleControls()
    {
        var frequency = (BackupFrequency)(_frequency.SelectedItem ?? BackupFrequency.Daily);
        _hourlyMinute.Enabled = frequency == BackupFrequency.Hourly;
        _startTime.Enabled = frequency != BackupFrequency.Hourly;
        _weeklyDay.Enabled = frequency == BackupFrequency.Weekly;
        _monthlyDay.Enabled = frequency == BackupFrequency.Monthly;
    }

    private void OpenLogs()
    {
        AppPaths.EnsureDirectories();
        Process.Start(new ProcessStartInfo { FileName = AppPaths.LogsDirectory, UseShellExecute = true });
    }

    private Button BrowseButton(TextBox target)
    {
        var button = new Button { Text = "...", Width = 42, Height = 27 };
        button.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                SelectedPath = Directory.Exists(target.Text) ? target.Text : string.Empty,
                ShowNewFolderButton = true
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                target.Text = dialog.SelectedPath;
        };
        return button;
    }

    private static GroupBox NewGroup(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        AutoSize = true,
        Padding = new Padding(10),
        Margin = new Padding(0, 0, 0, 10)
    };

    private static TableLayoutPanel NewTable()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 0 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return table;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control, Control? third = null)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 7) }, 0, row);
        control.Margin = new Padding(3, 4, 3, 4);
        table.Controls.Add(control, 1, row);

        if (third is not null)
        {
            third.Margin = new Padding(6, 3, 3, 3);
            table.Controls.Add(third, 2, row);
        }
    }

    private static void AddFullRow(TableLayoutPanel table, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(3, 6, 3, 6);
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 3);
    }

    private static Button NewButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(115, 34), Margin = new Padding(0, 0, 8, 0) };
        button.Click += handler;
        return button;
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowExit || e.CloseReason == CloseReason.WindowsShutDown)
            return;

        e.Cancel = true;
        Hide();
    }
}
