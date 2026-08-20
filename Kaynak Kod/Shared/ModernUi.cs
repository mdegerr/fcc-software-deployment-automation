using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SurumYakma
{
    internal static class ModernUi
    {
        public static readonly Color AppBackground = Color.FromArgb(241, 245, 249);
        public static readonly Color CardBackground = Color.White;
        public static readonly Color InputBackground = Color.FromArgb(248, 250, 252);
        public static readonly Color SelectionBackground = Color.FromArgb(219, 234, 254);
        public static readonly Color HeaderBackground = Color.FromArgb(15, 23, 42);
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);
        public static readonly Color PrimaryHover = Color.FromArgb(29, 78, 216);
        public static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);
        public static readonly Color TextSecondary = Color.FromArgb(100, 116, 139);
        public static readonly Color Border = Color.FromArgb(203, 213, 225);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);

        public static void StyleSectionLabel(Label label)
        {
            label.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
            label.ForeColor = TextPrimary;
            label.BackColor = AppBackground;
        }

        public static void StyleInput(Control control)
        {
            control.Font = new Font("Segoe UI", 10F);
            control.BackColor = InputBackground;
            control.ForeColor = TextPrimary;
            if (control is ComboBox combo)
            {
                // Flat görünüm beyaz kart üzerinde listenin sınırlarını tamamen
                // kaybettiriyordu. Standard çerçeve ve owner draw ile hem kapalı
                // alan hem de açılır listedeki seçim her Windows temasında görünür.
                combo.FlatStyle = FlatStyle.Standard;
                combo.IntegralHeight = false;
                combo.DropDownHeight = 220;
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.ItemHeight = 25;
                combo.DrawItem -= DrawComboItem;
                combo.DrawItem += DrawComboItem;
            }
            else if (control is TextBox text)
            {
                text.BorderStyle = BorderStyle.FixedSingle;
            }
        }

        private static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            if (!(sender is ComboBox combo))
                return;

            bool editArea = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool selected = !editArea && (e.State & DrawItemState.Selected) != 0;
            Color background = selected ? SelectionBackground : InputBackground;
            Color foreground = TextPrimary;

            using (var backgroundBrush = new SolidBrush(background))
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

            string text = e.Index >= 0 && e.Index < combo.Items.Count
                ? Convert.ToString(combo.Items[e.Index])
                : combo.Text;
            Rectangle textBounds = new Rectangle(
                e.Bounds.Left + 7,
                e.Bounds.Top,
                Math.Max(0, e.Bounds.Width - 12),
                e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                text ?? "",
                combo.Font,
                textBounds,
                foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if ((e.State & DrawItemState.Focus) != 0 && !editArea)
                e.DrawFocusRectangle();
        }

        public static void StylePrimaryButton(Button button)
        {
            StyleButton(button, Primary, Color.White, Primary, PrimaryHover);
            button.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            ApplyRoundedRegion(button, 10);
        }

        public static void SetPrimaryButtonEnabled(Button button, bool enabled)
        {
            if (button == null)
                return;
            button.Enabled = enabled;
            button.BackColor = enabled ? Primary : Color.FromArgb(226, 232, 240);
            button.ForeColor = enabled ? Color.White : TextSecondary;
            button.FlatAppearance.BorderColor = enabled ? Primary : Border;
            button.FlatAppearance.MouseOverBackColor = enabled ? PrimaryHover : button.BackColor;
            button.FlatAppearance.MouseDownBackColor = enabled ? PrimaryHover : button.BackColor;
        }

        public static void StyleSecondaryButton(Button button)
        {
            StyleButton(button, Color.White, TextPrimary, Border, Color.FromArgb(248, 250, 252));
            button.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            ApplyRoundedRegion(button, 8);
        }

        public static void StyleDangerButton(Button button)
        {
            StyleButton(button, Color.White, Danger, Color.FromArgb(254, 202, 202), Color.FromArgb(254, 242, 242));
            button.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            ApplyRoundedRegion(button, 8);
        }

        public static void StyleHeaderButton(Button button)
        {
            StyleButton(button, Color.FromArgb(30, 41, 59), Color.White,
                Color.FromArgb(71, 85, 105), Color.FromArgb(51, 65, 85));
            button.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            ApplyRoundedRegion(button, 9);
        }

        public static void StyleConnectionBadge(Label label)
        {
            label.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            label.ForeColor = Color.White;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Padding = new Padding(10, 0, 10, 0);
            ApplyRoundedRegion(label, 8);
        }

        public static void StyleDialog(Control root)
        {
            root.BackColor = AppBackground;
            foreach (Control control in Descendants(root))
            {
                if (control is TextBox || control is ComboBox || control is NumericUpDown)
                    StyleInput(control);
                else if (control is Button button)
                {
                    if (button.DialogResult == DialogResult.Cancel)
                        StyleSecondaryButton(button);
                    else
                        StylePrimaryButton(button);
                    button.Height = Math.Max(button.Height, 36);
                }
                else if (control is CheckBox check)
                {
                    check.Font = new Font("Segoe UI", 9.5F);
                    check.ForeColor = TextPrimary;
                }
                else if (control is Label label && label.ForeColor == SystemColors.ControlText)
                {
                    label.Font = new Font("Segoe UI", 9.5F);
                    label.ForeColor = TextPrimary;
                }
            }
        }

        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in Descendants(child))
                    yield return nested;
            }
        }

        public static void ApplyRoundedRegion(Control control, int radius)
        {
            void UpdateRegion()
            {
                if (control.Width <= 1 || control.Height <= 1)
                    return;
                using GraphicsPath path = RoundedRectangle(
                    new Rectangle(0, 0, control.Width, control.Height), radius);
                control.Region?.Dispose();
                control.Region = new Region(path);
            }

            control.SizeChanged += (s, e) => UpdateRegion();
            UpdateRegion();
        }

        public static void DrawCard(Graphics graphics, Rectangle bounds)
        {
            if (graphics == null || bounds.Width <= 2 || bounds.Height <= 2)
                return;
            Rectangle cardBounds = new Rectangle(
                bounds.Left,
                bounds.Top,
                bounds.Width - 1,
                bounds.Height - 1);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = RoundedRectangle(cardBounds, 14);
            using var fill = new SolidBrush(CardBackground);
            using var border = new Pen(Border);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
        }

        internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void StyleButton(
            Button button,
            Color back,
            Color fore,
            Color border,
            Color hover)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = back;
            button.ForeColor = fore;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderColor = border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = hover;
            button.FlatAppearance.MouseDownBackColor = hover;
        }
    }

    internal sealed class ModernCardPanel : Panel
    {
        public ModernCardPanel()
        {
            BackColor = ModernUi.CardBackground;
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (Width <= 1 || Height <= 1)
                return;
            using GraphicsPath path = ModernUi.RoundedRectangle(
                new Rectangle(0, 0, Width, Height), 14);
            Region?.Dispose();
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = ModernUi.RoundedRectangle(
                new Rectangle(0, 0, Width - 1, Height - 1), 14);
            using var pen = new Pen(ModernUi.Border);
            e.Graphics.DrawPath(pen, path);
        }
    }
}
