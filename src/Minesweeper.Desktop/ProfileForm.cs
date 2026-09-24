using Minesweeper.Core;

namespace Minesweeper.Desktop;

/// <summary>"Who's playing?" screen shown at startup and from the start screen's Switch Player button.</summary>
public sealed class ProfileForm : Form
{
    private static readonly Color Gray = Color.FromArgb(192, 192, 192);

    private readonly ProfileStore _store;
    private readonly Label _title = new();
    private readonly Label _hint = new();
    private readonly ListBox _list = new();
    private readonly ModeButton _play = new("Play", null);
    private readonly ModeButton _new = new("New Player...", null);
    private readonly ModeButton _delete = new("Delete", null);
    private readonly ModeButton _exit = new("Exit", null);

    /// <summary>The chosen player, or null if the window was closed. The admin login comes back as IsAdmin.</summary>
    public (string Name, bool IsAdmin)? Result { get; private set; }

    public ProfileForm(ProfileStore store)
    {
        _store = store;
        Text = "Minesweeper 2.0 - Players";
        BackColor = Gray;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        _title.Text = "WHO'S PLAYING?";
        _title.TextAlign = ContentAlignment.MiddleCenter;
        _title.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
        _hint.TextAlign = ContentAlignment.MiddleCenter;
        _hint.Font = new Font("Segoe UI", 10f);

        _list.Font = new Font("Segoe UI", 14f);
        _list.IntegralHeight = false;
        _list.BorderStyle = BorderStyle.FixedSingle;

        _play.Click += (_, _) => PlaySelected();
        _new.Click += (_, _) => AddPlayer();
        _delete.Click += (_, _) => DeleteSelected();
        _exit.Click += (_, _) => Close();
        _list.DoubleClick += (_, _) => PlaySelected();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { PlaySelected(); e.Handled = true; }
        };
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();

        Controls.Add(_title);
        Controls.Add(_hint);
        Controls.Add(_list);
        Controls.Add(_play);
        Controls.Add(_new);
        Controls.Add(_delete);
        Controls.Add(_exit);

        LayoutControls();
        Reload(_store.LastProfile);

        // A first-time install has nobody yet, so go straight to creating a player.
        Shown += (_, _) =>
        {
            if (_list.Items.Count == 0) AddPlayer();
            else _list.Focus();
        };
    }

    private void Reload(string? select)
    {
        _list.Items.Clear();
        foreach (string name in _store.List()) _list.Items.Add(name);

        int index = select == null ? -1 : _list.Items.IndexOf(select);
        _list.SelectedIndex = index >= 0 ? index : _list.Items.Count > 0 ? 0 : -1;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool any = _list.SelectedItem != null;
        _play.Enabled = _delete.Enabled = any;
        _play.Locked = _delete.Locked = !any;
        _hint.Text = _list.Items.Count == 0
            ? "Create a player to get started. Each player has their own scores."
            : "Pick your name, or add a new player. Each player has their own scores.";
    }

    private void PlaySelected()
    {
        if (_list.SelectedItem is not string name) return;
        Result = (name, false);
        Close();
    }

    private void AddPlayer()
    {
        using var dialog = new NewPlayerDialog(_store);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.PlayerName == null) return;

        Result = (dialog.PlayerName, dialog.IsAdmin);
        Close();
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not string name) return;

        var answer = MessageBox.Show(this,
            $"Delete {name} and all of their scores and progress?\n\nThis cannot be undone.",
            "Delete Player", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        try
        {
            _store.Delete(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not delete {name}: {ex.Message}", "Delete Player",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Reload(null);
    }

    private void LayoutControls()
    {
        int pad = LogicalToDeviceUnits(24);
        int width = LogicalToDeviceUnits(420);
        int gap = LogicalToDeviceUnits(12);
        int buttonHeight = LogicalToDeviceUnits(40);

        int y = pad;
        _title.SetBounds(pad, y, width, LogicalToDeviceUnits(44));
        y += _title.Height;
        _hint.SetBounds(pad, y, width, LogicalToDeviceUnits(40));
        y += _hint.Height + gap;
        _list.SetBounds(pad, y, width, LogicalToDeviceUnits(220));
        y += _list.Height + gap;

        int half = (width - gap) / 2;
        _play.SetBounds(pad, y, half, buttonHeight);
        _new.SetBounds(pad + half + gap, y, half, buttonHeight);
        y += buttonHeight + gap;
        _delete.SetBounds(pad, y, half, buttonHeight);
        _exit.SetBounds(pad + half + gap, y, half, buttonHeight);

        ClientSize = new Size(width + 2 * pad, _exit.Bottom + pad);
    }
}
