using MyAceEditor.PerfectWorld;
using MyAceEditor.PerfectWorld.WorldEdit;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Ferramenta Terreno (estudo em docs/superpowers/specs/2026-11-08-terreno-textura-estudo.md): pincel de textura do chão
/// (Pincel B, Borracha E, Escolher textura; tamanho com Ctrl + roda do mouse; força), pinta ao vivo, cada arrastada é um
/// passo de Ctrl+Z, Salvar terreno grava os .t2mk (tudo ou nada, barra azul, backup da ferramenta). Só cliente.
/// </summary>
internal sealed partial class WorldView
{
    readonly FlowLayoutPanel _terrainPage = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8), Visible = false };
    TerrainDraft? _terrainDraft;
    int _texSel = -1;                    // textura escolhida (índice na lista do .trn2)
    bool _texActive, _texErase, _texDown;
    float _texR = 6, _texStrength = 0.5f;
    System.Numerics.Vector3? _texLast;
    readonly Label _texTitle = new() { AutoSize = false, Width = 250, Height = 40, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9f), Margin = new Padding(3, 4, 3, 4) };
    readonly TrackBar _texSize = new() { Minimum = 1, Maximum = 60, Value = 6, TickFrequency = 10, Width = 240, Height = 32, AutoSize = false };
    readonly TrackBar _texForce = new() { Minimum = 5, Maximum = 100, Value = 50, TickFrequency = 10, Width = 240, Height = 32, AutoSize = false };
    readonly Label _texSizeLbl = new() { AutoSize = false, Width = 240, Height = 34, ForeColor = Color.Silver };
    readonly Label _texForceLbl = new() { AutoSize = false, Width = 240, Height = 18, ForeColor = Color.Silver };
    readonly Label _terrainState = new() { AutoSize = false, Width = 240, Height = 36, ForeColor = Color.Silver, Margin = new Padding(3, 8, 3, 4) };
    Button? _texPaintBtn, _texEraseBtn;

    bool TerrainDirty => _terrainDraft?.Dirty == true;
    bool TexOn => _tool == Tool.Terrain && _texActive && (_texErase || _texSel >= 0);

    void InitTerrainPage()
    {
        _terrainPage.Controls.Add(Hint(Loc.T("terrain.hint", "Pinte a textura do chão: escolha a textura, ligue o Pincel e arraste no chão. A Borracha volta para a textura de baixo. Nada grava até o Salvar terreno.")));
        _terrainPage.Controls.Add(SectionLabel(Loc.T("terrain.texture", "Textura do chão")));
        var modes = new FlowLayoutPanel { Width = 250, Height = 70, WrapContents = false, Margin = new Padding(0, 2, 0, 8) };
        _texPaintBtn = BigButton("", Loc.T("grass.brush", "Pincel"), "B", TexPaint, 76);
        _texEraseBtn = BigButton("", Loc.T("grass.eraser", "Borracha"), "E", TexEraser, 76);
        var pick = BigButton("", Loc.T("terrain.pick", "Escolher textura"), "", ChooseTexture, 76);
        modes.Controls.Add(_texPaintBtn); modes.Controls.Add(_texEraseBtn); modes.Controls.Add(pick);
        _terrainPage.Controls.Add(modes);
        _terrainPage.Controls.Add(_texTitle);
        _texSize.ValueChanged += (_, _) => { _texR = _texSize.Value; TexLabels(); _gl.Invalidate(); };
        _texForce.ValueChanged += (_, _) => { _texStrength = _texForce.Value / 100f; TexLabels(); };
        NoWheel(_texSize); NoWheel(_texForce);
        foreach (var c in new Control[] { _texSizeLbl, _texSize, _texForceLbl, _texForce, _terrainState }) _terrainPage.Controls.Add(c);
        _terrainPage.Controls.Add(SmallButton("", Loc.T("world.undo", "Desfazer (Ctrl+Z)"), () => UndoAny()));
        _terrainPage.Controls.Add(SmallButton("", Loc.T("world.redo", "Refazer (Ctrl+Y)"), () => RedoAny()));
        var save = SmallButton("", Loc.T("terrain.save", "Salvar terreno"), () => SaveTerrain(), main: true);
        _toolTip.SetToolTip(save, "<mapa>_N.t2mk");
        _terrainPage.Controls.Add(save);
        _terrainPage.Controls.Add(new Label { AutoSize = false, Width = 240, Height = 60, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f), Margin = new Padding(3, 8, 3, 30),
            Text = Loc.T("terrain.note", "Só o cliente muda (a textura do chão não vai para o servidor). Altura do chão, água e luz e sombra chegam nas próximas etapas.") });
        TexLabels();
    }

    void TexLabels()
    {
        _texSizeLbl.Text = string.Format(Loc.T("grass.size", "Tamanho do pincel: {0} m"), _texSize.Value) + Environment.NewLine + Loc.T("grass.wheelHint", "Ctrl + roda do mouse muda o tamanho");
        _texForceLbl.Text = string.Format(Loc.T("terrain.force", "Força: {0}%"), _texForce.Value);
        string name = _texSel >= 0 && _texSel < _t.TextureNames.Count ? Path.GetFileNameWithoutExtension(_t.TextureNames[_texSel]) : "";
        _texTitle.Text = !_texActive ? (name.Length > 0 ? string.Format(Loc.T("terrain.texOff", "Textura: {0} (pincel apagado)"), name) : Loc.T("terrain.noTex", "Nenhuma textura escolhida: use Escolher textura ou a Borracha."))
            : _texErase ? Loc.T("terrain.erasing", "Borracha: arraste para voltar à textura de baixo") : string.Format(Loc.T("terrain.painting", "Pintando: {0}"), name);
        bool p = _texActive && !_texErase, e = _texActive && _texErase;
        if (_texPaintBtn is not null) { _texPaintBtn.BackColor = p ? UiKit.Accent : Color.FromArgb(40, 45, 54); _texPaintBtn.FlatAppearance.BorderColor = p ? UiKit.Accent : Color.FromArgb(62, 70, 84); }
        if (_texEraseBtn is not null) { _texEraseBtn.BackColor = e ? Color.FromArgb(150, 50, 50) : Color.FromArgb(40, 45, 54); _texEraseBtn.FlatAppearance.BorderColor = e ? Color.FromArgb(150, 50, 50) : Color.FromArgb(62, 70, 84); }
        _terrainState.Text = TerrainDirty ? Loc.T("terrain.dirty", "● Mudanças não salvas — use Salvar terreno.") : Loc.T("world.sceneCleanLine", "✓ Tudo salvo.");
        _terrainState.ForeColor = TerrainDirty ? Color.FromArgb(255, 200, 90) : Color.Silver;
    }

    TerrainDraft TerrainDraftOrNew() => _terrainDraft ??= new TerrainDraft(_t);

    void TexPaint() { if (_texSel < 0) { ChooseTexture(); return; } _texErase = false; _texActive = true; TexLabels(); _gl.Invalidate(); }
    void TexEraser() { _texErase = true; _texActive = true; TexLabels(); _gl.Invalidate(); }

    /// <summary>JAV das texturas do chão deste mapa (prévia da imagem, estrela, recentes), agrupadas pela pasta.</summary>
    void ChooseTexture()
    {
        var names = _t.TextureNames;
        if (names.Count == 0) { _status.Text = Loc.T("terrain.noTextures", "Este mapa não tem lista de texturas do chão."); return; }
        var rows = names.Select((n, i) => (Id: i, Name: Path.GetFileNameWithoutExtension(n), Block: Path.GetFileName(Path.GetDirectoryName(n) ?? "") is { Length: > 0 } f ? f : "-")).ToList();
        int SigOf(int i) { unchecked { int h = (int)2166136261; foreach (char c in names[i].ToLowerInvariant()) h = (h ^ c) * 16777619; return h & 0x7FFFFFFF; } }
        JavFavorites? fav = null; List<int> recent = [];
        if (_element is not null)
        {
            var file = JavFavorites.ForFile("groundtex", _element);
            fav = new JavFavorites(() => file.Ids.Select(sig => Enumerable.Range(0, names.Count).FirstOrDefault(i => SigOf(i) == sig, -1)).Where(i => i >= 0).ToList(), i => file.Has(SigOf(i)), i => file.Toggle(SigOf(i)));
            recent = JavRecents.Read("groundtex", _element).Select(sig => Enumerable.Range(0, names.Count).FirstOrDefault(i => SigOf(i) == sig, -1)).Where(i => i >= 0).ToList();
        }
        using var w = new ItemSearchWindow(rows, askQuantity: false, recent: recent, model: (row, _) => _terrain.TextureImage(names[row]), fav: fav) { Text = Loc.T("terrain.chooseTitle", "Escolher textura do chão") };
        if (w.ShowDialog(this) != DialogResult.OK || w.Picked < 0) return;
        _texSel = w.Picked; _texErase = false; _texActive = true;
        if (_element is not null) JavRecents.Add("groundtex", _element, SigOf(w.Picked));
        TexLabels(); _gl.Focus(); _gl.Invalidate();
    }

    bool TerrainMouseDown(MouseEventArgs e)
    {
        if (_tool != Tool.Terrain || !TexOn) return false;
        if (e.Button == MouseButtons.Right) { _texActive = false; TexLabels(); _gl.Invalidate(); return true; }
        if (e.Button != MouseButtons.Left || Ray(e.Location) is not { } g) return e.Button == MouseButtons.Left;
        TerrainDraftOrNew().BeginStroke(); _texDown = true; _texLast = null;
        TexDab(new System.Numerics.Vector3(g.X, g.Y, g.Z));
        return true;
    }

    void TerrainMouseMove()
    {
        if (_tool != Tool.Terrain || !TexOn) return;
        _gl.Invalidate();
        if (!_texDown || _hover is not { } h) return;
        var p = new System.Numerics.Vector3(h.X, h.Y, h.Z);
        if (_texLast is { } last && System.Numerics.Vector3.Distance(last, p) < MathF.Max(0.5f, _texR * 0.3f)) return;
        TexDab(p);
    }

    bool TerrainMouseUp()
    {
        if (!_texDown) return false;
        _texDown = false;
        if (_terrainDraft?.EndStroke() == true) TexLabels();
        if (_terrainDraft?.LastRefusal is { } why) _status.Text = why;
        return true;
    }

    void TexDab(System.Numerics.Vector3 at)
    {
        var d = TerrainDraftOrNew();
        try
        {
            var touched = d.Paint(at.X, at.Z, _texR, _texErase ? _texSel : _texSel, _texStrength, _texErase);
            RefreshTerrainAreas(touched);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Error("terreno: pintar", ex);
            _status.Text = Loc.T("terrain.paintFail", "Não deu para abrir a textura do chão deste pedaço; veja o log.");
        }
        _texLast = at; _gl.Invalidate();
    }

    void RefreshTerrainAreas(IEnumerable<(int Ar, int Ac)> areas)
    {
        if (_terrainDraft is not { } d) return;
        foreach (var (ar, ac) in areas)
        {
            _t.SetGround(ar, ac, d.Layers(ar, ac));
            _terrain.RefreshGround(ar, ac);
        }
    }

    bool TerrainUndo(bool redo)
    {
        if (_tool != Tool.Terrain || _terrainDraft is not { } d) return false;
        var areas = d.Undo(redo);
        if (areas is null) return false;
        RefreshTerrainAreas(areas);
        _status.Text = redo ? Loc.T("grass.redone", "Pincelada refeita.") : Loc.T("grass.undone", "Pincelada desfeita (Ctrl+Y refaz).");
        TexLabels(); _gl.Invalidate();
        return true;
    }

    bool TerrainKey(Keys k)
    {
        if (_tool != Tool.Terrain) return false;
        if (k == Keys.B) { TexPaint(); return true; }
        if (k == Keys.E) { TexEraser(); return true; }
        return false;
    }

    bool TerrainWheel(MouseEventArgs e)
    {
        if (_tool != Tool.Terrain || (ModifierKeys & Keys.Control) == 0) return false;
        _texSize.Value = Math.Clamp(_texSize.Value + Math.Sign(e.Delta) * ((ModifierKeys & Keys.Shift) != 0 ? 5 : 1), _texSize.Minimum, _texSize.Maximum);
        return true;
    }

    /// <summary>Esc no Terreno: 1º apaga o pincel; depois a ferramenta sai (com a pergunta de abandonar se houver mudança).</summary>
    bool TerrainEscape()
    {
        if (_tool != Tool.Terrain || !_texActive) return false;
        if (_texDown) TerrainMouseUp();
        _texActive = false; TexLabels(); _gl.Invalidate();
        return true;
    }

    /// <summary>Saindo do Terreno com mudança: abandonar? Falso = fica.</summary>
    bool AbandonTerrain()
    {
        if (!TerrainDirty) { _texActive = false; return true; }
        if (MessageBox.Show(this, Loc.T("terrain.abandonAsk", "Há mudanças não salvas no terreno. Abandonar e perder essas mudanças?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        ResetTerrainEdit();
        return true;
    }

    /// <summary>Tira o rascunho e relê a textura do chão dos pedaços mexidos (abandonar, salvar, restaurar backup).</summary>
    void ResetTerrainEdit()
    {
        var pieces = _terrainDraft?.DirtyPieces.ToList() ?? [];
        _terrainDraft = null; _texActive = false; _texDown = false;
        foreach (var (ar, ac) in _t.ClearGround(pieces)) _terrain.RefreshGround(ar, ac);
        TexLabels(); _gl.Invalidate();
    }

    bool SaveTerrain()
    {
        if (_terrainDraft is not { Dirty: true } d) { _status.Text = Loc.T("obj.nothing", "Nada para salvar."); return false; }
        Dictionary<string, byte[]> files;
        try { files = d.Files(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Error("terreno: montar o salvar", ex); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        ShowSaveProgress(Loc.T("terrain.savingBar", "Salvando o terreno"), 0);
        var r = TerrainSaver.Write(files, _t.Name, pct => ShowSaveProgress(null, pct));
        bool ok = r.Kind is SaveKind.ClientOnly or SaveKind.Both;
        HideSaveProgress(ok);
        if (!ok) { MessageBox.Show(this, r.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); _status.Text = r.Message; return false; }
        _terrainDraft = null; _texDown = false;   // o cache do terreno já tem o que foi gravado
        TexLabels();
        _status.Text = r.Message;
        BeginInvoke(() => SavedDialog.Show(this, r.Message, null));
        return true;
    }

    void DrawTerrainBrush()
    {
        if (!TexOn || _hover is not { } h) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        GL.LineWidth(2.5f);
        GL.Color3(_texErase ? Color.FromArgb(255, 90, 80) : Color.FromArgb(110, 200, 255));
        GL.Begin(PrimitiveType.LineLoop);
        for (int i = 0; i < 72; i++)
        {
            float t = i / 72f * MathF.Tau, x = h.X + MathF.Sin(t) * _texR, z = h.Z + MathF.Cos(t) * _texR;
            GL.Vertex3(x, (_t.HeightAt(x, z) ?? h.Y) + 0.15f, -z);
        }
        GL.End();
        GL.LineWidth(1); GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>Para a captura ("dfttex"): abre o Terreno, escolhe a 1ª textura que a área não tem, pinta, desfaz/refaz e monta os arquivos.</summary>
    internal string DraftTerrainTest()
    {
        OpenTool(Tool.Terrain);
        var at = new System.Numerics.Vector3(_cam.X + Forward.X * 14, 0, -(_cam.Z + Forward.Z * 14));
        at.Y = _t.HeightAt(at.X, at.Z) ?? 0;
        var d = TerrainDraftOrNew();
        var a = d.AreaAt(at.X, at.Z);
        if (a is null) return " | dfttex: fora do terreno";
        var cur = _t.Ground(a.Value.Ar, a.Value.Ac) ?? [];
        _texSel = Enumerable.Range(0, _t.TextureNames.Count).First(i => !cur.Any(l => l.Texture == _t.TextureNames[i]));
        _texActive = true; _texErase = false; _texStrength = 1; TexLabels();
        d.BeginStroke(); _texDown = true; TexDab(at); TexDab(at + new System.Numerics.Vector3(3, 0, 0)); TerrainMouseUp();
        int after = (_t.Ground(a.Value.Ar, a.Value.Ac) ?? []).Length;
        bool undo = TerrainUndo(false) && (_t.Ground(a.Value.Ar, a.Value.Ac) ?? []).Length == cur.Length;
        bool redo = TerrainUndo(true) && (_t.Ground(a.Value.Ar, a.Value.Ac) ?? []).Length == after;
        int files = d.Files().Count;
        for (int k = 0; k < 20; k++) CaptureFrame()?.Dispose();
        using (var p = ToolsShot()) p.Save(Path.Combine(_shotDir ?? Path.GetTempPath(), "dfttex-painel.png"));
        _hover = new Vector3(at.X, at.Y, at.Z);
        _texStrength = 0.5f;
        return $" | dfttex: área {a.Value.Ar},{a.Value.Ac} camadas {cur.Length}->{after} textura={Path.GetFileName(_t.TextureNames[_texSel])} ctrlz={undo} ctrly={redo} arquivos={files} sujo={TerrainDirty}";
    }
}
