using System.Drawing.Drawing2D;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Editor de territórios (teste 13) sobre o mapa da guerra do cliente aberto (GuildMap.dds). Clicar escolhe; arrastar as bolinhas muda a
/// área; duplo clique na borda põe mais um canto; botão direito tira. Tudo fica aqui até Salvar, que grava o par domain.data e domain.sev.
/// </summary>
internal sealed class DomainEditorWindow : Form
{
    static readonly Color Bg = Color.FromArgb(24, 27, 33), Side = Color.FromArgb(32, 36, 44), Box = Color.FromArgb(44, 50, 60), Line = Color.FromArgb(70, 78, 92);
    static readonly Color[] Classes = [Color.FromArgb(90, 160, 255), Color.FromArgb(90, 220, 150), Color.FromArgb(255, 200, 90), Color.FromArgb(255, 120, 120)];
    const int Grab = 8;

    readonly string _dataPath;
    readonly string? _sevPath;
    readonly DomainMap _map;
    readonly DomainServer? _server;
    readonly IReadOnlySet<int> _usedInWorld;
    readonly Bitmap? _image;
    readonly Panel _canvas = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Bg };
    readonly ListBox _list = new() { Dock = DockStyle.Fill, BackColor = Side, ForeColor = Color.White, BorderStyle = BorderStyle.None, IntegralHeight = false };
    readonly TextBox _name = new() { Width = 250, BackColor = Box, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, MaxLength = DomainMap.NameChars - 1 };
    readonly ComboBox _type = new() { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Box, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly NumericUpDown _reward = new() { Width = 250, Maximum = 2_000_000_000, ThousandsSeparator = true, BackColor = Box, ForeColor = Color.White };
    readonly Label _about = new() { AutoSize = true, MaximumSize = new Size(250, 0), ForeColor = Color.Silver, Margin = new Padding(0, 6, 0, 6) };
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 46, ForeColor = Color.Gainsboro, BackColor = Side, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
    readonly List<List<DomainArea>> _undo = [], _redo = [];
    DomainArea? _sel;
    int _dragVertex = -1;
    bool _dragCenter, _binding, _dirty, _moved;

    sealed class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

    DomainEditorWindow(string dataPath, string? sevPath, DomainMap map, DomainServer? server, Bitmap? image, IReadOnlySet<int> usedInWorld)
    {
        _dataPath = dataPath; _sevPath = sevPath; _map = map; _server = server; _image = image; _usedInWorld = usedInWorld;
        Text = Loc.T("dm.title", "Territórios");
        ClientSize = new Size(1180, 760); MinimumSize = new Size(900, 600); StartPosition = FormStartPosition.CenterParent;
        BackColor = Bg; ForeColor = Color.White; KeyPreview = true; Font = new Font("Segoe UI", 9.5f); ShowInTaskbar = false;

        var side = new Panel { Dock = DockStyle.Right, Width = 290, BackColor = Side, Padding = new Padding(14, 12, 14, 20) };
        var form = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Label Title(string t) => new() { Text = t, AutoSize = true, ForeColor = Color.FromArgb(96, 165, 250), Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(0, 10, 0, 4) };
        Label Name(string t) => new() { Text = t, AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(0, 6, 0, 2) };
        Button Btn(string glyph, string text, Action a, bool main = false)
        {
            var b = new Button { Text = "  " + text, Width = 250, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = main ? UiKit.Accent : Color.FromArgb(40, 45, 54), Image = UiKit.Icon(glyph, Color.White, 16),
                TextAlign = ContentAlignment.MiddleLeft, ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText, Margin = new Padding(0, 2, 0, 2) };
            b.FlatAppearance.BorderColor = main ? UiKit.Accent : Line;
            b.Click += (_, _) => a();
            return b;
        }
        _type.Items.AddRange([Loc.T("dm.class3", "3ª classe"), Loc.T("dm.class2", "2ª classe"), Loc.T("dm.class1", "1ª classe")]);
        foreach (var c in new Control[]
        {
            Title(Loc.T("dm.selected", "Este território")), Name(Loc.T("dm.name", "Nome")), _name, Name(Loc.T("dm.type", "Classe")), _type, Name(Loc.T("dm.reward", "Prêmio por semana (moedas)")), _reward, _about,
            Btn("", Loc.T("dm.new", "Novo território"), NewArea),
            Btn("", Loc.T("dm.delete", "Excluir este território"), DeleteArea),
            Btn("", Loc.T("dm.neighbors", "Recalcular quem faz fronteira"), Neighbors),
            Btn("", Loc.T("dm.save", "Salvar territórios"), () => Save(), main: true),
        }) form.Controls.Add(c);
        side.Controls.Add(_list);
        side.Controls.Add(new Label { Dock = DockStyle.Top, Height = 28, Text = string.Format(Loc.T("dm.list", "{0} territórios"), _map.Areas.Count), ForeColor = Color.FromArgb(96, 165, 250), Font = new Font("Segoe UI Semibold", 10f) });
        side.Controls.Add(form);
        Controls.Add(_canvas); Controls.Add(_status); Controls.Add(side);

        _status.Text = Loc.T("dm.hint", "Clique num território para escolher. Arraste as bolinhas para mudar a área e o quadrado para mudar o ícone. Duplo clique na borda: mais um canto. Botão direito num canto: tira.");
        _canvas.Paint += PaintMap;
        _canvas.MouseDown += Down;
        _canvas.MouseMove += Move2;
        _canvas.MouseUp += (_, _) => { if (_moved && _sel is { } a) a.Faces = DomainMap.Triangulate(a.Vertices); _dragVertex = -1; _dragCenter = false; _moved = false; };
        _canvas.MouseDoubleClick += AddCorner;
        _list.SelectedIndexChanged += (_, _) => { if (!_binding && _list.SelectedIndex >= 0) Choose(_map.Areas[_list.SelectedIndex]); };
        _name.TextChanged += (_, _) => Field(a => a.Name = _name.Text);
        _type.SelectedIndexChanged += (_, _) => Field(a => a.Type = _type.SelectedIndex + 1);   // DOMAIN_TYPE: 1 = 3ª classe, 3 = 1ª
        _reward.ValueChanged += (_, _) => Field(a => a.Reward = (int)_reward.Value);
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) { Step(_undo, _redo); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.Y) { Step(_redo, _undo); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.S) { Save(); e.Handled = true; }
        };
        FormClosing += AskSave;
        FormClosed += (_, _) => _image?.Dispose();
        FillList();
    }

    /// <summary>Abre o editor do cliente aberto; sem o domain.data avisa e não abre.</summary>
    public static void Open(IWin32Window owner, string element, string? serverConfigDir, IReadOnlySet<int> usedInWorld)
    {
        var data = Path.Combine(element, "data", "domain.data");
        if (!File.Exists(data)) { MessageBox.Show(owner, Loc.T("dm.noFile", "Este cliente não tem data\\domain.data."), Loc.T("dm.title", "Territórios")); return; }
        DomainMap map; DomainServer? server = null; string? sev = null;
        try
        {
            map = DomainMap.Parse(File.ReadAllBytes(data));
            sev = serverConfigDir is null ? null : ServerFile(serverConfigDir);
            if (sev is not null) server = DomainServer.Parse(File.ReadAllBytes(sev));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            AppLog.Error("domain.data", ex);
            MessageBox.Show(owner, ex.Message, Loc.T("dm.title", "Territórios"));
            return;
        }
        using var w = new DomainEditorWindow(data, sev, map, server, LoadImage(element), usedInWorld);
        w.ShowDialog(owner);
    }

    /// <summary>O gdeliveryd lê o domain.sev da pasta dele; o de gamed\config é uma cópia velha em alguns servidores.</summary>
    static string? ServerFile(string configDir)
    {
        var home = Path.GetDirectoryName(Path.GetDirectoryName(configDir.TrimEnd('\\', '/')));
        foreach (var f in new[] { home is null ? null : Path.Combine(home, "gdeliveryd", "domain.sev"), Path.Combine(configDir, "domain.sev") })
            if (f is not null && File.Exists(f)) return f;
        return null;
    }

    static Bitmap? LoadImage(string element)
    {
        try
        {
            using var pck = new PckArchive(Path.Combine(element, "surfaces.pck"));
            if (pck.Find(@"surfaces\ingame\guildmap.dds") is not { } e || DdsDecoder.Decode(pck.Read(e)) is not { } img) return null;
            var bmp = new Bitmap(img.Width, img.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, img.Width, img.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
            System.Runtime.InteropServices.Marshal.Copy(img.Pixels, 0, bd.Scan0, img.Pixels.Length);
            bmp.UnlockBits(bd);
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Error("GuildMap", ex); return null; }
    }

    void FillList()
    {
        _binding = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var a in _map.Areas) _list.Items.Add(a.Name.Length > 0 ? a.Name : a.Id.ToString());
        _list.SelectedIndex = _sel is null ? -1 : _map.Areas.IndexOf(_sel);
        _list.EndUpdate();
        _binding = false;
    }

    void Choose(DomainArea? a)
    {
        _sel = a;
        _binding = true;
        _name.Text = a?.Name ?? ""; _type.SelectedIndex = a is null ? -1 : Math.Clamp(a.Type - 1, 0, 2);
        _reward.Value = Math.Clamp(a?.Reward ?? 0, 0, (int)_reward.Maximum);
        _list.SelectedIndex = a is null ? -1 : _map.Areas.IndexOf(a);
        _binding = false;
        ShowAbout();
        _canvas.Invalidate();
    }

    void ShowAbout()
    {
        if (_sel is not { } a) { _about.Text = ""; return; }
        var names = a.Neighbors.Select(id => _map.Areas.Find(x => x.Id == id)?.Name ?? id.ToString());
        _about.Text = string.Format(Loc.T("dm.about", "ID {0}. Faz fronteira com: {1}."), a.Id, a.Neighbors.Count == 0 ? Loc.T("dm.nobody", "ninguém") : string.Join(", ", names));
    }

    void Remember() { _undo.Add(_map.Snapshot()); if (_undo.Count > 100) _undo.RemoveAt(0); _redo.Clear(); _dirty = true; }

    void Step(List<List<DomainArea>> from, List<List<DomainArea>> to)
    {
        if (from.Count == 0) return;
        to.Add(_map.Snapshot());
        int id = _sel?.Id ?? 0;
        _map.Restore(from[^1]); from.RemoveAt(from.Count - 1);
        _dirty = true;
        _sel = _map.Areas.Find(a => a.Id == id);
        FillList(); Choose(_sel);
    }

    void Field(Action<DomainArea> set)
    {
        if (_binding || _sel is not { } a) return;
        Remember();
        set(a);
        _binding = true;
        if (_list.SelectedIndex >= 0) _list.Items[_list.SelectedIndex] = a.Name.Length > 0 ? a.Name : a.Id.ToString();
        _binding = false;
        _canvas.Invalidate();
    }

    (float Scale, float X, float Y) View()
    {
        int w = _image?.Width ?? 1024, h = _image?.Height ?? 768;
        float s = Math.Min((_canvas.Width - 20f) / w, (_canvas.Height - 20f) / h);
        return (s, (_canvas.Width - w * s) / 2, (_canvas.Height - h * s) / 2);
    }

    PointF ToScreen(Point p) { var (s, x, y) = View(); return new PointF(x + p.X * s, y + p.Y * s); }
    Point ToMap(Point p) { var (s, x, y) = View(); return new Point((int)MathF.Round((p.X - x) / s), (int)MathF.Round((p.Y - y) / s)); }

    void PaintMap(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var (s, ox, oy) = View();
        if (_image is not null) g.DrawImage(_image, ox, oy, _image.Width * s, _image.Height * s);
        using var font = new Font("Segoe UI", 8.5f);
        foreach (var a in _map.Areas)
        {
            if (a.Vertices.Count < 3) continue;
            var c = Classes[Math.Clamp(a.Type, 0, Classes.Length - 1)];
            var pts = a.Vertices.Select(ToScreen).ToArray();
            using var fill = new SolidBrush(Color.FromArgb(a == _sel ? 110 : 45, c));
            using var pen = new Pen(a == _sel ? Color.White : Color.FromArgb(200, c), a == _sel ? 2f : 1f);
            g.FillPolygon(fill, pts); g.DrawPolygon(pen, pts);
        }
        if (_sel is not { } sel) return;
        foreach (var v in sel.Vertices.Select(ToScreen)) { g.FillEllipse(Brushes.White, v.X - 5, v.Y - 5, 10, 10); g.DrawEllipse(Pens.Black, v.X - 5, v.Y - 5, 10, 10); }
        var ce = ToScreen(sel.Center);
        var size = g.MeasureString(sel.Name, font);   // só o nome do escolhido: a imagem do mapa já traz os números
        g.DrawString(sel.Name, font, Brushes.Black, ce.X - size.Width / 2 + 1, ce.Y + 8);
        g.DrawString(sel.Name, font, Brushes.White, ce.X - size.Width / 2, ce.Y + 7);
        g.FillRectangle(Brushes.Gold, ce.X - 5, ce.Y - 5, 10, 10); g.DrawRectangle(Pens.Black, ce.X - 5, ce.Y - 5, 10, 10);
    }

    int VertexAt(Point screen)
    {
        if (_sel is not { } a) return -1;
        for (int i = 0; i < a.Vertices.Count; i++)
        {
            var v = ToScreen(a.Vertices[i]);
            if (MathF.Abs(v.X - screen.X) <= Grab && MathF.Abs(v.Y - screen.Y) <= Grab) return i;
        }
        return -1;
    }

    void Down(object? sender, MouseEventArgs e)
    {
        int v = VertexAt(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            if (v < 0 || _sel is not { } a) return;
            if (a.Vertices.Count <= 3) { _status.Text = Loc.T("dm.min3", "Um território precisa de pelo menos 3 cantos."); return; }
            Remember();
            a.Vertices.RemoveAt(v);
            a.Faces = DomainMap.Triangulate(a.Vertices);
            _canvas.Invalidate();
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        if (v >= 0) { _dragVertex = v; return; }
        if (_sel is { } s)
        {
            var c = ToScreen(s.Center);
            if (MathF.Abs(c.X - e.X) <= Grab && MathF.Abs(c.Y - e.Y) <= Grab) { _dragCenter = true; return; }
        }
        var p = ToMap(e.Location);
        Choose(_map.Areas.Where(a => a.Contains(p)).OrderBy(a => a.Vertices.Count).FirstOrDefault() ?? _sel);
    }

    void Move2(object? sender, MouseEventArgs e)
    {
        if (_sel is not { } a || _dragVertex < 0 && !_dragCenter) return;
        if (!_moved) { Remember(); _moved = true; }
        var p = ToMap(e.Location);
        if (_dragCenter) a.Center = p; else a.Vertices[_dragVertex] = p;
        _canvas.Invalidate();
    }

    void AddCorner(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _sel is not { } a || VertexAt(e.Location) >= 0) return;
        int best = -1; float near = 12;
        for (int i = 0; i < a.Vertices.Count; i++)
        {
            PointF p = ToScreen(a.Vertices[i]), q = ToScreen(a.Vertices[(i + 1) % a.Vertices.Count]);
            float dx = q.X - p.X, dy = q.Y - p.Y, t = Math.Clamp(((e.X - p.X) * dx + (e.Y - p.Y) * dy) / Math.Max(1, dx * dx + dy * dy), 0, 1);
            float d = MathF.Sqrt(MathF.Pow(p.X + t * dx - e.X, 2) + MathF.Pow(p.Y + t * dy - e.Y, 2));
            if (d < near) { near = d; best = i; }
        }
        if (best < 0) return;
        Remember();
        a.Vertices.Insert(best + 1, ToMap(e.Location));
        a.Faces = DomainMap.Triangulate(a.Vertices);
        _canvas.Invalidate();
    }

    void NewArea()
    {
        Remember();
        var c = ToMap(new Point(_canvas.Width / 2, _canvas.Height / 2));
        const int H = 28;
        var a = new DomainArea { Id = _map.NextId(), Name = Loc.T("dm.newName", "Novo território"), Type = 1, Center = c,
            Vertices = [new Point(c.X - H, c.Y - H), new Point(c.X + H, c.Y - H), new Point(c.X + H, c.Y + H), new Point(c.X - H, c.Y + H)] };
        a.Faces = DomainMap.Triangulate(a.Vertices);
        _map.Areas.Add(a);
        FillList(); Choose(a);
    }

    void DeleteArea()
    {
        if (_sel is not { } a) return;
        var ask = _usedInWorld.Contains(a.Id)
            ? string.Format(Loc.T("dm.deleteUsed", "Excluir {0}? Áreas do mundo apontam para este território e ficam sem dono."), a.Name)
            : string.Format(Loc.T("dm.deleteAsk", "Excluir {0}?"), a.Name);
        if (MessageBox.Show(this, ask, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Remember();
        _map.Areas.Remove(a);
        foreach (var o in _map.Areas) o.Neighbors.Remove(a.Id);
        _sel = null;
        FillList(); Choose(null);
    }

    void Neighbors()
    {
        Remember();
        _map.ComputeNeighbors();
        ShowAbout();
        _status.Text = Loc.T("dm.neighborsDone", "Fronteiras recalculadas pelos territórios que se encostam.");
    }

    string? Problem()
    {
        foreach (var a in _map.Areas)
        {
            if (a.Name.Trim().Length == 0) return string.Format(Loc.T("dm.noName", "O território {0} está sem nome."), a.Id);
            if (a.Faces.Count == 0) return string.Format(Loc.T("dm.crossed", "A área de {0} cruza com ela mesma: arraste os cantos até abrir."), a.Name);
        }
        return null;
    }

    bool Save()
    {
        if (Problem() is { } bad) { MessageBox.Show(this, bad, Text); return false; }
        try
        {
            BackupStore.Save(_dataPath);
            File.WriteAllBytes(_dataPath, _map.Build());
            if (_server is not null && _sevPath is not null)
            {
                BackupStore.Save(_sevPath);
                _server.Match(_map);
                File.WriteAllBytes(_sevPath, _server.Build());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("domain.data", ex); MessageBox.Show(this, ex.Message, Text); return false; }
        _dirty = false;
        _status.Text = _sevPath is null ? Loc.T("dm.savedClient", "Salvo no cliente. Sem a pasta do servidor o domain.sev não mudou: o gdeliveryd segue com os territórios antigos.")
            : Loc.T("dm.saved", "Salvo no cliente e no servidor. Reinicie o gdeliveryd para valer no jogo.");
        return true;
    }

    void AskSave(object? sender, FormClosingEventArgs e)
    {
        if (!_dirty) return;
        var r = MessageBox.Show(this, Loc.T("dm.closeAsk", "Salvar os territórios antes de fechar?"), Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel || r == DialogResult.Yes && !Save()) e.Cancel = true;
    }

    internal static void Shot(string element, string png)
    {
        using var w = new DomainEditorWindow(Path.Combine(element, "data", "domain.data"), null, DomainMap.Parse(File.ReadAllBytes(Path.Combine(element, "data", "domain.data"))), null, LoadImage(element), new HashSet<int>())
            { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        w.Show(); Application.DoEvents();
        w.Choose(w._map.Areas[5]);
        Application.DoEvents();
        using var b = new Bitmap(w.Width, w.Height);
        w.DrawToBitmap(b, new Rectangle(0, 0, w.Width, w.Height));
        b.Save(png);
        w._dirty = false;
    }
}
