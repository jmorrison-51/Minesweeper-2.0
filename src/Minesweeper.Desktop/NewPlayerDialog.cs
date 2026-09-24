using Minesweeper.Core;

namespace Minesweeper.Desktop;

/// <summary>
/// Asks for a new player's name. Typing the admin user name reveals a password box; the right password
/// signs in as the admin test profile instead of creating a player.
/// </summary>
public sealed class NewPlayerDialog : Form
{
    private readonly ProfileStore _store;
    private readonly TextBox _name = new() { MaxLength = ProfileStore.MaxNameLength, Width = 200 };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Width = 200 };
    private readonly Label _passwordLabel = new() { Text = "Password:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 6) };
    private readonly Label _error = new() { AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(3, 0, 3, 6), MaximumSize = new Size(300, 0) };

    /// <summary>The player created, or the admin user name after a correct admin login.</summary>
    public string? PlayerName { get; private set; }

    public bool IsAdmin { get; private set; }

    public NewPlayerDialog(ProfileStore store)
    {
        _store = store;
        Text = "New Player";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(12),
            Dock = DockStyle.Fill,
        };
        layout.Controls.Add(new Label { Text = "Name:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 6) }, 0, 0);
        layout.Controls.Add(_name, 1, 0);
        layout.Controls.Add(_passwordLabel, 0, 1);
        layout.Controls.Add(_password, 1, 1);
        layout.Controls.Add(_error, 0, 2);
        layout.SetColumnSpan(_error, 2);

        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => TryAccept();
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        _name.TextChanged += (_, _) =>
        {
            SetPasswordVisible(AdminAccess.IsAdminName(_name.Text));
            _error.Text = "";
        };
        SetPasswordVisible(false);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void SetPasswordVisible(bool visible)
    {
        _passwordLabel.Visible = visible;
        _password.Visible = visible;
        if (!visible) _password.Clear();
    }

    private void TryAccept()
    {
        if (AdminAccess.IsAdminName(_name.Text))
        {
            if (!AdminAccess.PasswordMatches(_password.Text))
            {
                _error.Text = "Incorrect password.";
                _password.SelectAll();
                _password.Focus();
                return;
            }
            PlayerName = AdminAccess.UserName;
            IsAdmin = true;
            DialogResult = DialogResult.OK;
            return;
        }

        if (!ProfileStore.IsValidName(_name.Text, out string error))
        {
            _error.Text = error;
            _name.Focus();
            return;
        }
        if (_store.Find(_name.Text) != null)
        {
            _error.Text = "There is already a player with that name.";
            _name.Focus();
            return;
        }

        PlayerName = _store.Create(_name.Text);
        DialogResult = DialogResult.OK;
    }
}
