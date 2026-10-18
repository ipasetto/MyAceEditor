namespace MyAceEditor.UI;

/// <summary>应用自有提示使用固定中文按钮；原始异常仅作为诊断详情保留。</summary>
internal static class ChineseDialogs
{
    public static bool Confirm(IWin32Window owner, string text, string title) =>
        Show(owner, text, title, confirm: true) == DialogResult.OK;

    public static DialogResult Show(IWin32Window owner, string text, string title,
        bool confirm = false, string? details = null)
    {
        using var font = new Font("Microsoft YaHei UI", 10);
        using var form = new Form
        {
            Text = title, Font = font, Width = 720, Height = details is null ? 290 : 470,
            MinimumSize = new Size(480, 250), StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            BackColor = SystemColors.Window, Padding = new Padding(16),
        };
        var content = new TextBox
        {
            Text = text, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = form.BackColor,
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
        };
        var ok = new Button { Text = "确定", Width = 100, Height = 30, DialogResult = DialogResult.OK };
        buttons.Controls.Add(ok); form.AcceptButton = ok;
        if (confirm)
        {
            var cancel = new Button { Text = "取消", Width = 100, Height = 30, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(cancel); form.CancelButton = cancel;
        }
        else form.CancelButton = ok;
        form.Controls.Add(content);
        if (details is not null)
        {
            var group = new GroupBox { Text = "原始诊断信息（供核对）", Dock = DockStyle.Bottom, Height = 180 };
            group.Controls.Add(new TextBox
            {
                Text = details, ReadOnly = true, Multiline = true, WordWrap = false,
                Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both,
            });
            form.Controls.Add(group);
        }
        form.Controls.Add(buttons);
        return form.ShowDialog(owner);
    }

    public static string Explain(Exception error) => error switch
    {
        InvalidDataException => error.Message,
        InvalidOperationException when error.Message.Any(c => c >= '\u4e00' && c <= '\u9fff') => error.Message,
        FileNotFoundException => "未找到所需文件，请检查文件路径。",
        DirectoryNotFoundException => "未找到指定文件夹，请检查目录。",
        UnauthorizedAccessException => "没有访问此文件或文件夹的权限。",
        EndOfStreamException => "文件内容不完整，读取已到达文件末尾。",
        IOException => "文件读写失败，请检查文件路径、占用情况和可用空间。",
        FormatException => "输入值的格式不正确，请检查数字或文本格式。",
        OverflowException => "输入数值超出允许范围。",
        System.Text.EncoderFallbackException => "文本包含目标编码无法保存的字符。",
        System.ComponentModel.Win32Exception => "无法启动外部程序，请检查程序路径。",
        _ => "操作未完成，请查看下方原始诊断信息。",
    };
}
