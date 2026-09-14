using System.Drawing;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal sealed class SingleModifierHotkeyConfirmationForm : Form
    {
        private readonly CheckBox doNotShowAgainCheckBox;

        private SingleModifierHotkeyConfirmationForm()
        {
            Text = "NVDA Sync hotkey";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 190);

            const string message = "This global hotkey uses only one modifier. NVDA Sync allows this for compatibility, but it is more likely to conflict with other apps or keyboard layouts. Keep this hotkey anyway?";
            var messageLabel = new Label
            {
                Text = message,
                Left = 14,
                Top = 14,
                Width = 525,
                Height = 62,
                AccessibleName = message
            };
            doNotShowAgainCheckBox = new CheckBox
            {
                Text = "Do &not show this warning again",
                Left = 14,
                Top = 88,
                Width = 300,
                Height = 24,
                AccessibleDescription = "Turn this warning off. You can turn it back on in NVDA Sync Preferences."
            };
            var keepButton = new Button
            {
                Text = "&Keep Hotkey",
                DialogResult = DialogResult.OK,
                Left = 340,
                Top = 135,
                Width = 105
            };
            var goBackButton = new Button
            {
                Text = "&Go Back",
                DialogResult = DialogResult.Cancel,
                Left = 455,
                Top = 135,
                Width = 85,
                AccessibleDescription = message + " Press Alt+K to keep the hotkey, or press Enter to go back."
            };
            Controls.Add(messageLabel);
            Controls.Add(doNotShowAgainCheckBox);
            Controls.Add(keepButton);
            Controls.Add(goBackButton);
            AcceptButton = goBackButton;
            CancelButton = goBackButton;
            ActiveControl = goBackButton;
        }

        public static bool Ask(IWin32Window owner, out bool suppressFuturePrompts)
        {
            using (var dialog = new SingleModifierHotkeyConfirmationForm())
            {
                var accepted = dialog.ShowDialog(owner) == DialogResult.OK;
                suppressFuturePrompts = accepted && dialog.doNotShowAgainCheckBox.Checked;
                return accepted;
            }
        }
    }
}
