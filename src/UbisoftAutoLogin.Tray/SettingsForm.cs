using UbisoftAutoLogin.Core;

namespace UbisoftAutoLogin.Tray;

internal sealed class SettingsForm : Form
{
    private readonly TrayContext _ctx;
    private readonly TextBox _email = new() { Width = 280 };
    private readonly TextBox _password = new() { Width = 280, UseSystemPasswordChar = true };
    private readonly CheckBox _enabled = new() { Text = "Auto-login enabled", AutoSize = true };
    private readonly CheckBox _startup = new() { Text = "Start with Windows (also Xbox mode)", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(380, 0) };
    private readonly Label _saved = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    public SettingsForm(TrayContext ctx)
    {
        _ctx = ctx;
        Text = "Ubisoft Auto-Login";
        Icon = TrayIcons.For(EngineStatus.LauncherDetected);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var save = new Button { Text = "Save credentials", AutoSize = true };
        var clear = new Button { Text = "Clear credentials", AutoSize = true };
        var test = new Button { Text = "Test detection", AutoSize = true };
        save.Click += (_, _) => Save();
        clear.Click += (_, _) => Clear();
        test.Click += async (_, _) => await _ctx.TestDetection();

        _enabled.Checked = ctx.Settings.AutoLoginEnabled;
        _startup.Checked = StartupManager.IsEnabled();
        _enabled.CheckedChanged += (_, _) => _ctx.SetEnabled(_enabled.Checked);
        _startup.CheckedChanged += (_, _) => _ctx.SetStartup(_startup.Checked);

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { Text = "Ubisoft e-mail:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_email, 1, 0);
        layout.Controls.Add(new Label { Text = "Password:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_password, 1, 1);
        var credButtons = new FlowLayoutPanel { AutoSize = true };
        credButtons.Controls.AddRange(new Control[] { save, clear });
        layout.Controls.Add(credButtons, 1, 2);
        layout.Controls.Add(_saved, 1, 3);
        layout.Controls.Add(_enabled, 1, 4);
        layout.Controls.Add(_startup, 1, 5);
        layout.Controls.Add(test, 1, 6);
        layout.Controls.Add(new Label { Text = "Status:", AutoSize = true }, 0, 7);
        layout.Controls.Add(_status, 1, 7);
        layout.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText,
            Text = "The password is stored in Windows Credential Manager for your Windows account only. " +
                   "It is only ever typed into the verified Ubisoft Connect password prompt."
        }, 1, 8);
        Controls.Add(layout);

        _email.Text = ctx.Store.ReadUserName() ?? "";
        RefreshLabels();
        ctx.Engine.StatusChanged += OnStatus;
        FormClosed += (_, _) => { ctx.Engine.StatusChanged -= OnStatus; _password.Clear(); };
    }

    private void OnStatus(EngineStatus s, string d) => RefreshLabels();

    private void RefreshLabels()
    {
        _status.Text = TrayContext.Describe(_ctx.Engine.Status) + (_ctx.Engine.StatusDetail.Length > 0 ? " – " + _ctx.Engine.StatusDetail : "");
        _saved.Text = _ctx.Store.Exists() ? "Credentials saved." : "No credentials saved yet.";
    }

    private void Save()
    {
        if (_email.Text.Trim().Length == 0 || _password.TextLength == 0)
        {
            MessageBox.Show(this, "Enter both the e-mail and the password.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            _ctx.Store.Save(_email.Text.Trim(), _password.Text);
            _password.Clear();
            AppLog.Info("credentials saved");
            _ctx.Engine.Resume();
        }
        catch (Exception ex)
        {
            AppLog.Error("saving credentials failed: " + ex.GetType().Name);
            MessageBox.Show(this, "Could not save to Windows Credential Manager.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshLabels();
    }

    private void Clear()
    {
        if (MessageBox.Show(this, "Delete the saved Ubisoft credentials?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _ctx.Store.Delete();
        _email.Clear(); _password.Clear();
        AppLog.Info("credentials cleared");
        RefreshLabels();
    }
}
