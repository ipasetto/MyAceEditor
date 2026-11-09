using MyAceEditor.PerfectWorld;
using MyAceEditor.PerfectWorld.WorldEdit;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Pincel de grama e flores da ferramenta Objetos do mundo (docs/superpowers/specs/2026-11-08-grama-design.md): escolher o
/// tipo na janela de escolher, círculo no chão seguindo o mouse, arrastar pinta (ou apaga com a Borracha), cada arrastada
/// = um passo de Ctrl+Z; grava no Salvar dos objetos (GrassSaver).
/// </summary>
internal sealed partial class WorldView
{
    GrassDraft? _grassDraft;
    int _brushType = -1;                 // tipo em pincel (−1 = pincel desligado)
    bool _brushErase, _brushDown, _grassLast;
    float _brushR = 4, _brushDensity = 0.5f;
    System.Numerics.Vector3? _lastDab;
    readonly Panel _brushPanel = new() { Width = 250, BackColor = Color.FromArgb(28, 32, 38), Visible = false, Padding = new Padding(6), Margin = new Padding(0, 4, 0, 4) };
    readonly Label _brushTitle = new() { AutoSize = false, Height = 40, Dock = DockStyle.Top, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9f) };
    readonly TrackBar _brushSize = new() { Minimum = 1, Maximum = 30, Value = 4, TickFrequency = 5, Dock = DockStyle.Top, Height = 32, AutoSize = false };
    readonly TrackBar _brushDens = new() { Minimum = 5, Maximum = 100, Value = 50, TickFrequency = 10, Dock = DockStyle.Top, Height = 32, AutoSize = false };
    readonly Label _brushSizeLbl = new() { Dock = DockStyle.Top, Height = 34, ForeColor = Color.Silver };
    readonly Label _brushDensLbl = new() { Dock = DockStyle.Top, Height = 18, ForeColor = Color.Silver };
    Button? _brushPaintBtn, _brushEraseBtn;
    bool _brushActive;   // pincel ou borracha ligado (o painel pode estar aberto com os dois apagados)

    /// <summary>Pincel valendo: painel aberto e Pincel (com tipo) ou Borracha ligados.</summary>
    bool BrushOn => _brushPanel.Visible && _brushActive && (_brushErase || _brushType >= 0);

    /// <summary>Botão grande do painel: ícone em cima, nome e a tecla embaixo (como os modos do objeto).</summary>
    Button BigButton(string glyph, string text, string key, Action a, int width)
    {
        var b = new Button { Width = width, Height = 62, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Text = text + "\n" + key, Image = UiKit.Icon(glyph, Color.White, 18),
            TextImageRelation = TextImageRelation.ImageAboveText, Margin = new Padding(0, 0, 4, 0), Cursor = Cursors.Hand, Font = new Font("Segoe UI", 8.5f), BackColor = Color.FromArgb(40, 45, 54) };
        b.FlatAppearance.BorderColor = Color.FromArgb(62, 70, 84);
        b.Click += (_, _) => { a(); _gl.Focus(); };
        FitBigText(b);
        return b;
    }

    /// <summary>
    /// Botão grande com texto traduzido: diminui a letra até cada palavra caber inteira na largura e o texto inteiro
    /// (nome em até 2 linhas + tecla) caber embaixo do ícone (usuário 09/11: "Smooth" virava "Smoot / h").
    /// </summary>
    static void FitBigText(Button b)
    {
        int w = b.Width - 10, h = b.Height - (b.Image?.Height ?? 0) - 8;
        for (float size = b.Font.Size; size >= 6.5f; size -= 0.5f)
        {
            using var f = new Font(b.Font.FontFamily, size);
            bool words = b.Text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).All(word => TextRenderer.MeasureText(word, f).Width <= w);
            bool tall = TextRenderer.MeasureText(b.Text, f, new Size(w, 0), TextFormatFlags.WordBreak).Height <= h;
            if (words && tall || size <= 6.5f) { if (size != b.Font.Size) b.Font = new Font(b.Font.FontFamily, size); return; }
        }
    }

    /// <summary>Monta o painel da grama: Pincel (B), Borracha (E), Escolher tipo; tamanho e densidade; Concluir.</summary>
    void InitGrassPanel()
    {
        _brushSize.ValueChanged += (_, _) => { _brushR = _brushSize.Value; BrushLabels(); _gl.Invalidate(); };
        _brushDens.ValueChanged += (_, _) => { _brushDensity = _brushDens.Value / 100f; BrushLabels(); };
        NoWheel(_brushSize); NoWheel(_brushDens);
        var modes = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 74, BackColor = _brushPanel.BackColor, Padding = new Padding(0, 4, 0, 8), WrapContents = false };
        _brushPaintBtn = BigButton("\uE76D", Loc.T("grass.brush", "Pincel"), "B", BrushPaint, 76);
        _brushEraseBtn = BigButton("\uE75C", Loc.T("grass.eraser", "Borracha"), "E", BrushEraser, 76);
        var pick = BigButton("\uE790", Loc.T("grass.pickType", "Escolher tipo"), "", ChooseGrassToPaint, 76);
        _toolTip.SetToolTip(_brushPaintBtn, Loc.T("grass.brushTip", "Pinta o tipo escolhido arrastando no chão (Ctrl + roda do mouse muda o tamanho)."));
        _toolTip.SetToolTip(_brushEraseBtn, Loc.T("grass.eraserTip", "Apaga a grama e as flores de qualquer tipo arrastando no chão."));
        _toolTip.SetToolTip(pick, Loc.T("grass.pickTip", "Abre a lista de gramas e flores de todos os mapas."));
        modes.Controls.Add(_brushPaintBtn); modes.Controls.Add(_brushEraseBtn); modes.Controls.Add(pick);
        var dropRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, BackColor = _brushPanel.BackColor, Padding = new Padding(0, 2, 0, 4) };
        var drop = _grassDropBtn = SmallButton("", Loc.T("grass.eyedropper", "Conta-gotas (I): pegar o tipo do chão"), () => StartDropper(2)); drop.Width = 236;
        _toolTip.SetToolTip(drop, Loc.T("grass.eyedropperTip2", "Liga o conta-gotas (I): clique numa grama ou flor e o pincel fica com o mesmo tipo."));
        dropRow.Controls.Add(drop);
        var gap = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = _brushPanel.BackColor };
        var doneRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = _brushPanel.BackColor, Padding = new Padding(0, 8, 0, 0) };
        var done = SmallButton("\uE73E", Loc.T("grass.done", "Concluir"), () => StopBrush(), main: true); done.Width = 236;
        doneRow.Controls.Add(done);
        var note = new Label { Dock = DockStyle.Top, Height = 46, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f), Padding = new Padding(0, 6, 0, 0),
            Text = Loc.T("grass.note2", "Cada arrastada é um passo do Ctrl+Z. Grava no Salvar cenário (só no cliente; o servidor não muda).") };
        foreach (var c in new Control[] { note, doneRow, _brushDens, _brushDensLbl, gap, _brushSize, _brushSizeLbl, dropRow, modes, _brushTitle }) _brushPanel.Controls.Add(c);
        _brushPanel.Height = _brushPanel.Controls.Cast<Control>().Sum(c => c.Height) + 16;
        BrushLabels();
    }

    /// <summary>A roda do mouse em cima de um campo ou barra não muda o valor (só clicar e digitar/arrastar; usuário 08/11).</summary>
    static void NoWheel(Control c) => c.MouseWheel += (_, e) => { if (e is HandledMouseEventArgs h) h.Handled = true; };

    /// <summary>Pincel (B): sem tipo escolhido, abre a lista primeiro.</summary>
    void BrushPaint()
    {
        if (_brushType < 0) { ChooseGrassToPaint(); return; }
        _brushErase = false; _brushActive = true; BrushLabels(); _gl.Invalidate();
    }

    /// <summary>Borracha (E): apaga qualquer grama.</summary>
    void BrushEraser()
    {
        if (GrassDraftOrNew() is null) return;
        _brushErase = true; _brushActive = true; BrushLabels(); _gl.Invalidate();
    }

    /// <summary>Pílula Grama: abre o painel com o pincel apagado (o usuário escolhe Pincel, Borracha ou o tipo).</summary>
    void OpenGrassPanel()
    {
        if (GrassDraftOrNew() is null) return;
        StopPlanting(); StopDropper();
        if (_texDown) TerrainMouseUp();
        _texActive = false; _sculpt = -1; _waterMode = 0; _lightMode = 0; TexLabels(); WaterLabels();   // um pincel por vez na pílula Terreno
        _sceneSel = null; ShowObjPanel();
        _brushActive = false; _brushErase = false;
        _brushPanel.Visible = true; BrushLabels(); FitObjPanel();
        _status.Text = Loc.T("grass.open", "Grama e flores: escolha Pincel (B) com um tipo, ou Borracha (E). Ctrl + roda do mouse muda o tamanho.");
        _gl.Focus(); _gl.Invalidate();
    }

    void BrushLabels()
    {
        _brushSizeLbl.Text = string.Format(Loc.T("grass.size", "Tamanho do pincel: {0} m"), _brushSize.Value) + Environment.NewLine + Loc.T("grass.wheelHint", "Ctrl + roda do mouse muda o tamanho");
        _brushDensLbl.Text = string.Format(Loc.T("grass.density", "Densidade: {0}%"), _brushDens.Value);
        string name = _grass.Scene is { } sc && _brushType >= 0 && _brushType < sc.GrassTypes.Count ? TextureNameGlossary.Display(sc.GrassTypes[_brushType].Mox, _element) : "";
        _brushTitle.Text = !_brushActive ? (name.Length > 0 ? string.Format(Loc.T("grass.typeOff", "Tipo: {0} (pincel apagado)"), name) : Loc.T("grass.noTypeYet", "Nenhum tipo escolhido: use Escolher tipo ou a Borracha."))
            : _brushErase ? Loc.T("grass.erasing", "Borracha: arraste para apagar a grama") : string.Format(Loc.T("grass.painting", "Pintando: {0}"), name);
        bool paintOn = _brushActive && !_brushErase, eraseOn = _brushActive && _brushErase;
        if (_brushPaintBtn is not null) { _brushPaintBtn.BackColor = paintOn ? UiKit.Accent : Color.FromArgb(40, 45, 54); _brushPaintBtn.FlatAppearance.BorderColor = paintOn ? UiKit.Accent : Color.FromArgb(62, 70, 84); }
        DropButtons(); UpdateInfoCard();
        if (_brushEraseBtn is not null) { _brushEraseBtn.BackColor = eraseOn ? Color.FromArgb(150, 50, 50) : Color.FromArgb(40, 45, 54); _brushEraseBtn.FlatAppearance.BorderColor = eraseOn ? Color.FromArgb(150, 50, 50) : Color.FromArgb(62, 70, 84); }
    }

    /// <summary>Rascunho da grama (uma vez; precisa da cena da grama e dos blocos do cenário). null = não deu (status explica).</summary>
    GrassDraft? GrassDraftOrNew()
    {
        if (_grassDraft is not null) return _grassDraft;
        if (_grass.Scene is not { } sc || _draftSrc is null) { _status.Text = Loc.T("grass.noScene", "Este mapa não tem grama para editar (ou o cenário não abriu)."); return null; }
        try
        {
            var ecw = new EcwldFile(File.ReadAllBytes(_draftSrc.EcwldPath));
            var blocks = ecw.Blocks.Select(bl => ObjectFormats.BlockArea(ecw, bl)).Select(a => (a.X0, a.X1, a.Z0, a.Z1)).ToList();
            return _grassDraft = new GrassDraft(sc, blocks, _t.PieceAt, _t.HeightAt);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Error("grama: abrir", ex);
            _status.Text = Loc.T("grass.openFail", "Não deu para abrir a grama deste mapa; veja o log.");
            return null;
        }
    }

    List<GrassKind>? _grassKinds;

    /// <summary>Pílula Grama: tipos deste mapa e de todos os outros (prévia, estrela, recentes); escolher liga o pincel.</summary>
    void ChooseGrassToPaint()
    {
        if (GrassDraftOrNew() is not { } gd) return;
        var sc = gd.Scene;
        try { if (_element is not null) _grassKinds ??= GrassKind.All(_element); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("grama: tipos dos mapas", ex); }
        // linhas: tipos que o arquivo deste mapa já tem; depois os dos outros mapas (world antes), um por .mox + tamanho
        var rowsK = new List<(GrassKind? Kind, int Type, string Block)>();
        for (int i = 0; i < gd.FileTypes; i++) rowsK.Add((null, i, MapLabel(_t.Name)));
        foreach (var k in (_grassKinds ?? []).OrderBy(k => MapRank(k.Maps.OrderBy(MapRank).First())).ThenBy(k => k.Maps.OrderBy(MapRank).First(), StringComparer.OrdinalIgnoreCase))
            if (!Enumerable.Range(0, gd.FileTypes).Any(i => sc.GrassTypes[i].Mox.Equals(k.Mox, StringComparison.OrdinalIgnoreCase) && sc.GrassTypes[i].Size == k.Type.Size))
                rowsK.Add((k, -1, MapLabel(k.Maps.OrderBy(MapRank).First())));
        if (rowsK.Count == 0) { _status.Text = Loc.T("grass.noTypes", "Nenhum tipo de grama no cliente."); return; }
        int TypeOfRow(int row) => row < 0 || row >= rowsK.Count ? -1 : rowsK[row].Kind is { } k ? gd.TypeFor(k) : rowsK[row].Type;
        int SigOf(int row) => rowsK[row].Kind?.Sig ?? new GrassKind([.. GrassProps(sc.GrassTypes[rowsK[row].Type])], sc.GrassTypes[rowsK[row].Type].Mox, []).Sig;
        string MoxOf(int row) => rowsK[row].Kind?.Mox ?? sc.GrassTypes[rowsK[row].Type].Mox;
        string NameOf(int row) => GlossaryRow(MoxOf(row));   // nome no idioma do MyAce + original
        var rows = rowsK.Select((r, i) => (Id: i, Name: NameOf(i), Block: r.Block)).ToList();
        JavFavorites? fav = null; List<int> recent = [];
        if (_element is not null)
        {
            var file = JavFavorites.ForFile("grass", _element);
            fav = new JavFavorites(() => file.Ids.Select(sig => Enumerable.Range(0, rowsK.Count).FirstOrDefault(i => SigOf(i) == sig, -1)).Where(i => i >= 0).ToList(),
                i => i >= 0 && i < rowsK.Count && file.Has(SigOf(i)), i => file.Toggle(SigOf(i)));
            recent = JavRecents.Read("grass", _element).Select(sig => Enumerable.Range(0, rowsK.Count).FirstOrDefault(i => SigOf(i) == sig, -1)).Where(i => i >= 0).ToList();
        }
        using var w = new ItemSearchWindow(rows, askQuantity: false, recent: recent, model: (row, yaw) => TypeOfRow(row) is var t && t >= 0 ? GrassThumb(t, yaw) : null, fav: fav, keepBlockOrder: true,
            rename: row => TextureNamePrompt(MoxOf(row), Loc.T("names.grassTitle", "Renomear grama ou flor")) ? NameOf(row) : null)
            { Text = Loc.T("grass.choose", "Escolher grama ou flor para pintar") };
        if (w.ShowDialog(this) != DialogResult.OK || w.Picked < 0) return;
        int type = TypeOfRow(w.Picked);
        if (type < 0) return;
        if (_element is not null) JavRecents.Add("grass", _element, SigOf(w.Picked));
        StopPlanting();
        _sceneSel = null; ShowObjPanel();
        _brushType = type; _brushErase = false; _brushActive = true;
        _brushPanel.Visible = true; BrushLabels(); FitObjPanel();
        _status.Text = string.Format(Loc.T("grass.start", "Pincel de {0}: arraste no chão para pintar. Esc ou Concluir sai."), rows[w.Picked].Name);
        _gl.Focus(); _gl.Invalidate();
    }

    static byte[] GrassProps(WorldScene.GrassType t)
    {
        var b = new byte[24];
        b[0] = (byte)(t.AlphaBlend ? 1 : 0);
        BitConverter.GetBytes(t.AlphaRef).CopyTo(b, 4); BitConverter.GetBytes(t.Size).CopyTo(b, 8); BitConverter.GetBytes(t.SizeVar).CopyTo(b, 12);
        BitConverter.GetBytes(t.Softness).CopyTo(b, 16); BitConverter.GetBytes(t.Sight).CopyTo(b, 20);
        return b;
    }

    Image? GrassThumb(int type, float yaw)
    {
        float s = MathF.Max(0.5f, _grass.SizeOf(type));
        float half = MathF.Max(1.2f, s * 1.5f);
        return OffscreenThumb(new Vector3(-half, 0, -half), new Vector3(half, s * 1.2f, half), yaw, (_, _) => _grass.DrawThumb(type));
    }

    /// <summary>Sai do pincel (Esc, botão direito ou Concluir). Falso = não estava pintando.</summary>
    bool StopBrush()
    {
        if (!_brushPanel.Visible) return false;
        if (_brushDown) EndDab();
        _brushErase = false; _brushActive = false; _brushPanel.Visible = false;   // o tipo fica lembrado para a próxima vez
        FitObjPanel(); _gl.Invalidate();
        _status.Text = Loc.T("grass.stopped", "Pincel de grama encerrado.");
        return true;
    }

    /// <summary>Esc no painel da grama: 1º apaga o pincel/borracha, 2º fecha o painel. Falso = não estava na grama.</summary>
    bool GrassEscape()
    {
        if (!_brushPanel.Visible) return false;
        if (_brushActive) { if (_brushDown) EndDab(); _brushActive = false; BrushLabels(); _gl.Invalidate(); _status.Text = Loc.T("grass.off", "Pincel apagado (Esc de novo fecha a grama)."); return true; }
        return StopBrush();
    }

    /// <summary>Teclas do painel da grama: B pincel, E borracha. Falso = não era do painel.</summary>
    bool GrassKey(Keys k)
    {
        if (_tool != Tool.Terrain || !_brushPanel.Visible) return false;
        if (k == Keys.B) { BrushPaint(); return true; }
        if (k == Keys.E) { BrushEraser(); return true; }
        if (k == Keys.I) { GrassEyedropper(); return true; }   // conta-gotas: o tipo embaixo do mouse (usuário 09/11)
        return false;
    }

    /// <summary>Ctrl + roda do mouse com o painel da grama aberto: muda o tamanho do pincel (a câmera não anda). Falso = não era.</summary>
    bool GrassWheel(MouseEventArgs e)
    {
        if (_tool != Tool.Terrain || !_brushPanel.Visible || (ModifierKeys & Keys.Control) == 0) return false;
        int v = Math.Clamp(_brushSize.Value + Math.Sign(e.Delta) * ((ModifierKeys & Keys.Shift) != 0 ? 5 : 1), _brushSize.Minimum, _brushSize.Maximum);
        _brushSize.Value = v;
        _status.Text = string.Format(Loc.T("grass.size", "Tamanho do pincel: {0} m"), v);
        return true;
    }

    /// <summary>Começa uma pincelada (botão esquerdo no chão).</summary>
    bool BrushMouseDown(MouseEventArgs e)
    {
        if (_tool != Tool.Terrain || !BrushOn) return false;
        if (e.Button == MouseButtons.Right) { StopBrush(); return true; }
        if (e.Button != MouseButtons.Left || Ray(e.Location) is not { } g || GrassDraftOrNew() is not { } gd) return e.Button == MouseButtons.Left;
        gd.BeginStroke(); _brushDown = true; _lastDab = null;
        Dab(new System.Numerics.Vector3(g.X, g.Y, g.Z));
        return true;
    }

    /// <summary>Arrastando: um toque a cada pedaço do raio andado.</summary>
    void BrushMouseMove()
    {
        if (!BrushOn) return;
        _gl.Invalidate();
        if (!_brushDown || _hover is not { } h) return;
        var p = new System.Numerics.Vector3(h.X, h.Y, h.Z);
        float step = MathF.Max(0.4f, _brushR * 0.35f);
        if (_lastDab is { } last)
        {
            float dist = System.Numerics.Vector3.Distance(last, p);
            if (dist < step) return;
            int n = Math.Min(64, (int)(dist / step));   // um pulo grande entre quadros vira toques ao longo do caminho
            for (int i = 1; i < n; i++) Dab(System.Numerics.Vector3.Lerp(last, p, i / (float)n));
        }
        Dab(p);
    }

    bool BrushMouseUp() { if (!_brushDown) return false; EndDab(); return true; }

    void EndDab()
    {
        _brushDown = false;
        if (_grassDraft?.EndStroke() == true) { _grassLast = true; WorldDid('G'); UpdateSideState(); }
    }

    void Dab(System.Numerics.Vector3 at)
    {
        if (_grassDraft is not { } gd) return;
        int n0 = gd.Scene.Grass.Count;
        var touched = _brushErase ? gd.Erase(at.X, at.Z, _brushR) : gd.Paint(at.X, at.Z, _brushR, _brushType, _brushDensity, Environment.TickCount ^ (int)(at.X * 31) ^ (int)(at.Z * 17));
        _grass.Invalidate(touched, gd.Scene.Grass.Count != n0, at.X, at.Z, _brushR + 1);
        _lastDab = at;
        _gl.Invalidate();
    }

    /// <summary>Ctrl+Z / Ctrl+Y da grama: com o pincel ligado, ou quando a última ação foi uma pincelada.</summary>
    bool GrassUndo(bool redo)
    {
        if (_tool != Tool.Terrain || _grassDraft is null) return false;
        if (!BrushOn && !_grassLast && (redo ? _draft?.RedoTag is not null : _draft?.UndoTag is not null)) return false;
        return GrassUndoCore(redo);
    }

    bool GrassUndoCore(bool redo)
    {
        if (_grassDraft is not { } gd) return false;
        int n0 = gd.Scene.Grass.Count;
        var touched = redo ? gd.Redo() : gd.Undo();
        if (touched is null) return false;
        _grass.Invalidate(touched, gd.Scene.Grass.Count != n0);
        if (!redo && gd.UndoCount == 0) _grassLast = false;
        _status.Text = redo ? Loc.T("grass.redone", "Pincelada refeita.") : Loc.T("grass.undone", "Pincelada desfeita (Ctrl+Y refaz).");
        UpdateSideState(); _gl.Invalidate();
        return true;
    }

    /// <summary>Conta-gotas da grama (I, botão ou Ctrl+C com o pincel): liga o modo (UI/WorldView.Eyedropper.cs).</summary>
    bool GrassEyedropper() { if (!_brushPanel.Visible) return false; StartDropper(2); return true; }

    /// <summary>Círculo do pincel no chão (verde = pinta, vermelho = apaga), acompanhando o relevo.</summary>
    void DrawBrush()
    {
        if (_tool != Tool.Terrain || !BrushOn || _hover is not { } h) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        GL.LineWidth(2.5f);
        GL.Color3(_brushErase ? Color.FromArgb(255, 90, 80) : Color.FromArgb(120, 230, 110));
        GL.Begin(PrimitiveType.LineLoop);
        float gz0 = h.Z;   // _hover: coordenadas do jogo
        for (int i = 0; i < 64; i++)
        {
            float t = i / 64f * MathF.Tau, x = h.X + MathF.Sin(t) * _brushR, z = gz0 + MathF.Cos(t) * _brushR;
            float y = (_t.HeightAt(x, z) ?? h.Y) + 0.15f;
            GL.Vertex3(x, y, -z);
        }
        GL.End();
        GL.LineWidth(1); GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>Depois de salvar, abandonar ou restaurar backup: o rascunho da grama sai e a grama é relida do disco.</summary>
    void ResetGrassEdit()
    {
        StopBrush();
        _grassDraft = null; _grassLast = false;
        _grass.Reload();
    }

    /// <summary>
    /// Para a captura ("dftgrass"): liga o pincel com o 1º tipo do mapa, pinta perto da câmera, desfaz/refaz, apaga com a
    /// borracha, prévia do tipo e o plano do salvar (sem gravar); deixa o círculo na tela para a foto.
    /// </summary>
    internal string DraftGrassTest()
    {
        string load = DraftLoadTest();
        if (GrassDraftOrNew() is not { } gd || _draftSrc is null) return load + " | dftgrass: sem grama";
        if (gd.FileTypes == 0) return load + " | dftgrass: mapa sem tipos";
        _brushType = 0; _brushErase = false; _brushActive = true; _brushPanel.Visible = true; BrushLabels();
        var at = new System.Numerics.Vector3(_cam.X + Forward.X * 12, 0, -(_cam.Z + Forward.Z * 12));   // perto: a grama só aparece até a distância de vista do tipo
        at.Y = _t.HeightAt(at.X, at.Z) ?? 0;
        int t0 = gd.Tufts(gd.Scene.Grass);
        gd.BeginStroke(); Dab(at); Dab(at + new System.Numerics.Vector3(3, 0, 0)); EndDab();
        int t1 = gd.Tufts(gd.Scene.Grass);
        bool undo = GrassUndo(false) && gd.Tufts(gd.Scene.Grass) == t0;
        bool redo = GrassUndo(true) && gd.Tufts(gd.Scene.Grass) == t1;
        _brushErase = true; gd.BeginStroke(); Dab(at); EndDab(); _brushErase = false;
        int t2 = gd.Tufts(gd.Scene.Grass);
        // densidade: 6 toques no mesmo lugar com 25% numa área limpa têm que dar perto de 25% das células do círculo
        var at2 = at + new System.Numerics.Vector3(0, 0, 14);
        _brushErase = true; gd.BeginStroke(); Dab(at2); EndDab(); _brushErase = false;
        int d0 = gd.Tufts(gd.Scene.Grass);
        _brushDensity = 0.25f; gd.BeginStroke(); for (int k = 0; k < 6; k++) { _lastDab = null; Dab(at2 + new System.Numerics.Vector3(k * 0.2f, 0, 0)); } EndDab(); _brushDensity = 0.5f;
        float cells = MathF.PI * _brushR * _brushR / 0.25f;
        float densPct = 100f * (gd.Tufts(gd.Scene.Grass) - d0) / cells;
        GrassUndo(false); GrassUndo(false);
        using (var gp = ToolsShot()) gp.Save(Path.Combine(_shotDir ?? Path.GetTempPath(), "dftgrass-painel.png"));
        using (var th = GrassThumb(0, 30) as Bitmap) th?.Save(Path.Combine(_shotDir ?? Path.GetTempPath(), "dftgrass-thumb.png"));
        string sum;
        try
        {
            var plan = new SavePlan(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), new());
            GrassSaver.Apply(plan, gd, _draftSrc);
            sum = string.Join(" / ", plan.Summary) + " arquivos: " + string.Join(", ", plan.Files.Keys.Select(Path.GetFileName));
        }
        catch (InvalidDataException ex) { sum = ex.Message; }
        _brushDensity = 1; gd.BeginStroke(); Dab(at); EndDab(); _brushDensity = 0.5f;   // pincelada cheia para a foto
        for (int k = 0; k < 120 && (k < 3 || _grass.Pending); k++) CaptureFrame()?.Dispose();   // deixa a grama terminar de desenhar
        bool dirty = SceneDirty;
        _hover = new Vector3(at.X, at.Y, at.Z);   // círculo do pincel na foto
        return load + $" | dftgrass: tufos {t0} -> {t1} ctrlz={undo} ctrly={redo} borracha -> {t2} densidade25%-com-6-toques={densPct:0}% sujo={dirty} [tipo0 tamanho={_grass.SizeOf(0):0.00} grama-ligada={_grass.Visible} areas={gd.Scene.Grass.Count} nova={gd.NewAreas.FirstOrDefault()?.X:0},{gd.NewAreas.FirstOrDefault()?.Z:0} {gd.NewAreas.FirstOrDefault()?.Cols}x{gd.NewAreas.FirstOrDefault()?.Rows} pincel={at.X:0},{at.Z:0}] plano=\"{sum}\"";
    }
}
