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
    readonly Button[] _sculptBtns = new Button[4];
    int _sculpt = -1;                    // modo de altura ligado (−1 = nenhum): 0 subir, 1 descer, 2 alisar, 3 plainar
    float _flatTarget;
    bool SculptOn => _tool == Tool.Terrain && _sculpt >= 0;

    bool TerrainDirty => _terrainDraft?.Dirty == true;
    bool TexOn => _tool == Tool.Terrain && (_texActive && (_texErase || _texSel >= 0) || _sculpt >= 0);

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
        // altura do chão: subir, descer, alisar, plainar (teclas 1 a 4); o tamanho e a força valem para os dois pincéis
        _terrainPage.Controls.Add(SectionLabel(Loc.T("terrain.height", "Altura do chão")));
        var hrow = new FlowLayoutPanel { Width = 250, Height = 70, WrapContents = false, Margin = new Padding(0, 2, 0, 8) };
        (string G, string K, string Pt, string Tip)[] hdefs =
        {
            ("\uE70E", "terrain.raise", "Subir", "terrain.raiseTip"), ("\uE70D", "terrain.lower", "Descer", "terrain.lowerTip"),
            ("\uE7A9", "terrain.smooth", "Alisar", "terrain.smoothTip"), ("\uE7C2", "terrain.flatten", "Plainar", "terrain.flattenTip"),
        };
        string[] tips = { "Sobe o chão dentro do círculo.", "Desce o chão dentro do círculo.", "Suaviza morros e buracos (média dos vizinhos).", "Deixa o chão na altura do ponto onde começou a arrastar." };
        for (int i = 0; i < 4; i++)
        {
            int m = i;
            var b = BigButton(hdefs[i].G, Loc.T(hdefs[i].K, hdefs[i].Pt), (i + 1).ToString(), () => SetSculpt(m), 57);
            _toolTip.SetToolTip(b, Loc.T(hdefs[i].Tip, tips[i]));
            _sculptBtns[i] = b; hrow.Controls.Add(b);
        }
        _terrainPage.Controls.Add(hrow);
        _texSize.ValueChanged += (_, _) => { _texR = _texSize.Value; TexLabels(); _gl.Invalidate(); };
        _texForce.ValueChanged += (_, _) => { _texStrength = _texForce.Value / 100f; TexLabels(); };
        NoWheel(_texSize); NoWheel(_texForce);
        foreach (var c in new Control[] { _texSizeLbl, _texSize, _texForceLbl, _texForce }) _terrainPage.Controls.Add(c);
        InitWaterSection();
        _terrainPage.Controls.Add(_terrainState);
        _terrainPage.Controls.Add(SmallButton("", Loc.T("world.undo", "Desfazer (Ctrl+Z)"), () => UndoAny()));
        _terrainPage.Controls.Add(SmallButton("", Loc.T("world.redo", "Refazer (Ctrl+Y)"), () => RedoAny()));
        var save = SmallButton("", Loc.T("terrain.save", "Salvar terreno"), () => SaveTerrain(), main: true);
        _toolTip.SetToolTip(save, "<mapa>_N.t2mk / .t2bk, bsdata, .ecwld + map\\N.hmap, watermap\\N.wmap");
        _terrainPage.Controls.Add(save);
        _terrainPage.Controls.Add(new Label { AutoSize = false, Width = 240, Height = 60, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f), Margin = new Padding(3, 8, 3, 30),
            Text = Loc.T("terrain.note3", "Textura: só o cliente. Altura e água: cliente e servidor (envie depois). A passagem dos NPCs e a luz e sombra chegam nas próximas etapas.") });
        TexLabels(); WaterLabels();
    }

    void TexLabels()
    {
        _texSizeLbl.Text = string.Format(Loc.T("grass.size", "Tamanho do pincel: {0} m"), _texSize.Value) + Environment.NewLine + Loc.T("grass.wheelHint", "Ctrl + roda do mouse muda o tamanho");
        _texForceLbl.Text = string.Format(Loc.T("terrain.force", "Força: {0}%"), _texForce.Value);
        string name = _texSel >= 0 && _texSel < _t.TextureNames.Count ? TextureNameGlossary.Display(_t.TextureNames[_texSel], _element) : "";
        string[] sname = { Loc.T("terrain.raise", "Subir"), Loc.T("terrain.lower", "Descer"), Loc.T("terrain.smooth", "Alisar"), Loc.T("terrain.flatten", "Plainar") };
        for (int i = 0; i < 4; i++)
            if (_sculptBtns[i] is { } sb) { bool on = _sculpt == i; sb.BackColor = on ? UiKit.Accent : Color.FromArgb(40, 45, 54); sb.FlatAppearance.BorderColor = on ? UiKit.Accent : Color.FromArgb(62, 70, 84); }
        _texTitle.Text = _sculpt >= 0 ? string.Format(Loc.T("terrain.sculpting", "Altura: {0} (arraste no chão)"), sname[_sculpt]) : !_texActive ? (name.Length > 0 ? string.Format(Loc.T("terrain.texOff", "Textura: {0} (pincel apagado)"), name) : Loc.T("terrain.noTex", "Nenhuma textura escolhida: use Escolher textura ou a Borracha."))
            : _texErase ? Loc.T("terrain.erasing", "Borracha: arraste para voltar à textura de baixo") : string.Format(Loc.T("terrain.painting", "Pintando: {0}"), name);
        bool p = _texActive && !_texErase, e = _texActive && _texErase;
        if (_texPaintBtn is not null) { _texPaintBtn.BackColor = p ? UiKit.Accent : Color.FromArgb(40, 45, 54); _texPaintBtn.FlatAppearance.BorderColor = p ? UiKit.Accent : Color.FromArgb(62, 70, 84); }
        if (_texEraseBtn is not null) { _texEraseBtn.BackColor = e ? Color.FromArgb(150, 50, 50) : Color.FromArgb(40, 45, 54); _texEraseBtn.FlatAppearance.BorderColor = e ? Color.FromArgb(150, 50, 50) : Color.FromArgb(62, 70, 84); }
        _terrainState.Text = TerrainDirty ? Loc.T("terrain.dirty", "● Mudanças não salvas — use Salvar terreno.") : Loc.T("world.sceneCleanLine", "✓ Tudo salvo.");
        _terrainState.ForeColor = TerrainDirty ? Color.FromArgb(255, 200, 90) : Color.Silver;
    }

    TerrainDraft TerrainDraftOrNew()
    {
        if (_terrainDraft is not null) return _terrainDraft;
        _terrainDraft = new TerrainDraft(_t);
        try { _terrainDraft.Water = WaterDraft.Load(_t); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            AppLog.Error("agua: abrir", ex);
            _status.Text = Loc.T("water.loadFail", "Não deu para abrir a água deste mapa; veja o log.");
        }
        return _terrainDraft;
    }

    void TexPaint() { if (_texSel < 0) { ChooseTexture(); return; } _sculpt = -1; _waterMode = 0; _texErase = false; _texActive = true; TexLabels(); WaterLabels(); _gl.Invalidate(); }
    void TexEraser() { _sculpt = -1; _waterMode = 0; _texErase = true; _texActive = true; TexLabels(); WaterLabels(); _gl.Invalidate(); }

    /// <summary>Liga um modo de altura (o pincel de textura apaga); de novo no mesmo botão desliga.</summary>
    void SetSculpt(int m)
    {
        _sculpt = _sculpt == m ? -1 : m; _texActive = false;
        if (_sculpt >= 0) _waterMode = 0;
        TexLabels(); WaterLabels(); _gl.Invalidate();
        if (_sculpt >= 0) _status.Text = Loc.T("terrain.sculptHint", "Arraste no chão. Árvores e construções não acompanham: use Assentar no chão nos Objetos do mundo depois.");
    }

    /// <summary>JAV das texturas do chão deste mapa (prévia da imagem, estrela, recentes), agrupadas pela pasta.</summary>
    void ChooseTexture()
    {
        var names = _t.TextureNames;
        if (names.Count == 0) { _status.Text = Loc.T("terrain.noTextures", "Este mapa não tem lista de texturas do chão."); return; }
        // nome no idioma do MyAce (glossário + renomeados do cliente) com o original junto (a busca acha pelos dois); a pasta também
        string RowName(int i) { var d = TextureNameGlossary.Display(names[i], _element); var b = TextureNameGlossary.Base(names[i]); return d == b ? b : d + "  (" + b + ")"; }
        var rows = names.Select((n, i) => (Id: i, Name: RowName(i), Block: Path.GetFileName(Path.GetDirectoryName(n) ?? "") is { Length: > 0 } f ? TextureNameGlossary.Compose(f, AppLang.Current) : "-")).ToList();
        int SigOf(int i) { unchecked { int h = (int)2166136261; foreach (char c in names[i].ToLowerInvariant()) h = (h ^ c) * 16777619; return h & 0x7FFFFFFF; } }
        JavFavorites? fav = null; List<int> recent = [];
        if (_element is not null)
        {
            var file = JavFavorites.ForFile("groundtex", _element);
            fav = new JavFavorites(() => file.Ids.Select(sig => Enumerable.Range(0, names.Count).FirstOrDefault(i => SigOf(i) == sig, -1)).Where(i => i >= 0).ToList(), i => file.Has(SigOf(i)), i => file.Toggle(SigOf(i)));
            recent = JavRecents.Read("groundtex", _element).Select(sig => Enumerable.Range(0, names.Count).FirstOrDefault(i => SigOf(i) == sig, -1)).Where(i => i >= 0).ToList();
        }
        using var w = new ItemSearchWindow(rows, askQuantity: false, recent: recent, model: (row, _) => _terrain.TextureImage(names[row]), fav: fav,
            rename: i => _element is not null && TextureNamePrompt(names[i]) ? RowName(i) : null) { Text = Loc.T("terrain.chooseTitle", "Escolher textura do chão") };
        if (w.ShowDialog(this) != DialogResult.OK || w.Picked < 0) return;
        _texSel = w.Picked; _texErase = false; _texActive = true;
        if (_element is not null) JavRecents.Add("groundtex", _element, SigOf(w.Picked));
        TexLabels(); _gl.Focus(); _gl.Invalidate();
    }

    /// <summary>
    /// Renomear textura (F2 na JAV): só o nome que aparece, no glossário deste cliente e no idioma atual; o arquivo não muda.
    /// true = mudou.
    /// </summary>
    bool TextureNamePrompt(string path)
    {
        if (_element is null) return false;
        string b = TextureNameGlossary.Base(path), auto = TextureNameGlossary.Compose(b, AppLang.Current);
        using var f = new Form { Text = Loc.T("texnames.title", "Renomear textura"), Width = 480, Height = 230, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, BackColor = Color.FromArgb(28, 32, 38), ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f) };
        var info = new Label { Dock = DockStyle.Top, Height = 54, Padding = new Padding(10, 8, 10, 0), ForeColor = Color.Silver,
            Text = string.Format(Loc.T("texnames.info", "Só muda o nome que aparece no editor, neste cliente e neste idioma (o arquivo do jogo não muda). Arquivo: {0} · automático: {1}"), b, auto) };
        var box = new TextBox { Dock = DockStyle.Top, Text = TextureNameGlossary.UserName(path, _element) ?? "", PlaceholderText = auto, BackColor = Color.FromArgb(20, 22, 26), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var wrap = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 6, 10, 6) }; wrap.Controls.Add(box);
        var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 10, 10, 12) };
        string? why = null; bool changed = false;
        Button Btn(string key, string pt, Action a, bool main = false)
        {
            var bt = new Button { Text = Loc.T(key, pt), AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = main ? UiKit.Accent : Color.FromArgb(60, 66, 78), Margin = new Padding(6, 0, 0, 0) };
            bt.FlatAppearance.BorderSize = 0; bt.Click += (_, _) => a(); return bt;
        }
        row.Controls.Add(Btn("names.save", "Salvar nome", () => { why = TextureNameGlossary.Rename(path, _element, box.Text); changed = why is null; f.DialogResult = DialogResult.OK; }, main: true));
        row.Controls.Add(Btn("texnames.auto", "Voltar ao automático", () => { why = TextureNameGlossary.Rename(path, _element, null); changed = why is null; f.DialogResult = DialogResult.OK; }));
        f.Controls.Add(row); f.Controls.Add(wrap); f.Controls.Add(info);
        f.AcceptButton = row.Controls[0] as Button;
        f.ShowDialog(this);
        if (why is not null) MessageBox.Show(this, why, f.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (changed) TexLabels();
        return changed;
    }

    bool TerrainMouseDown(MouseEventArgs e)
    {
        if (_tool != Tool.Terrain || !TexOn) return false;
        if (e.Button == MouseButtons.Right) { _texActive = false; _sculpt = -1; TexLabels(); _gl.Invalidate(); return true; }
        if (e.Button != MouseButtons.Left || Ray(e.Location) is not { } g) return e.Button == MouseButtons.Left;
        TerrainDraftOrNew().BeginStroke(); _texDown = true; _texLast = null;
        _flatTarget = _t.HeightAt(g.X, g.Z) ?? g.Y;   // plainar: altura do ponto onde começou
        TexDab(new System.Numerics.Vector3(g.X, g.Y, g.Z));
        return true;
    }

    void TerrainMouseMove()
    {
        if (_tool == Tool.Terrain && _waterMode is 2 or 3) _gl.Invalidate();   // prévia da água segue o mouse
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
        if (_terrainDraft?.EndStroke() == true) { TexLabels(); WaterLabels(); }
        if (_terrainDraft?.LastRefusal is { } why) _status.Text = why;
        return true;
    }

    void TexDab(System.Numerics.Vector3 at)
    {
        var d = TerrainDraftOrNew();
        try
        {
            if (_sculpt >= 0)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var areas = d.Sculpt(at.X, at.Z, _texR, (TerrainDraft.SculptMode)_sculpt, _texStrength, _flatTarget);
                PerfSculpt += sw.Elapsed.TotalMilliseconds; sw.Restart();
                _terrain.RefreshHeights(areas);
                PerfMesh += sw.Elapsed.TotalMilliseconds; sw.Restart();
                if (_grass.Scene is { } gs) _grass.Invalidate(gs.Grass, false, at.X, at.Z, _texR + 2);   // grama acompanha o chão
                PerfGrass += sw.Elapsed.TotalMilliseconds;
                if (d.WaterChanged) RefreshWaterView();   // cavou abaixo do nível: a água aparece
            }
            else
            {
                var touched = d.Paint(at.X, at.Z, _texR, _texSel, _texStrength, _texErase);
                RefreshTerrainAreas(touched);
            }
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
            _groundPending.Add((ar, ac));   // a placa recebe no próximo quadro, com o contexto certo e uma vez só
        }
    }

    readonly HashSet<(int, int)> _groundPending = new();
    internal double PerfSculpt, PerfMesh, PerfGrass;   // diagnóstico da captura (tempo por parte da pincelada de altura)

    /// <summary>No desenho (contexto do OpenGL ativo): manda as máscaras pintadas desde o último quadro.</summary>
    void FlushGround()
    {
        if (_groundPending.Count == 0) return;
        foreach (var (ar, ac) in _groundPending) _terrain.RefreshGround(ar, ac);
        _groundPending.Clear();
    }

    bool TerrainUndo(bool redo)
    {
        if (_tool != Tool.Terrain || _terrainDraft is not { } d) return false;
        var areas = d.Undo(redo);
        if (areas is null) return false;
        RefreshTerrainAreas(areas);
        _terrain.RefreshHeights(areas);
        if (_grass.Scene is { } gs) _grass.Invalidate(gs.Grass, true);
        if (d.WaterChanged) RefreshWaterView();
        _status.Text = redo ? Loc.T("grass.redone", "Pincelada refeita.") : Loc.T("grass.undone", "Pincelada desfeita (Ctrl+Y refaz).");
        TexLabels(); WaterLabels(); _gl.Invalidate();
        return true;
    }

    bool TerrainKey(Keys k)
    {
        if (_tool != Tool.Terrain) return false;
        if (k == Keys.B) { TexPaint(); return true; }
        if (k == Keys.E) { TexEraser(); return true; }
        if (k is >= Keys.D1 and <= Keys.D4) { SetSculpt(k - Keys.D1); return true; }
        if (k is Keys.D5 or Keys.D6) { SetWaterMode(k == Keys.D5 ? 1 : 2); return true; }
        if (k == Keys.Delete && WaterSel is not null) { WaterDelete(); return true; }
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
        if (_tool == Tool.Terrain && _waterMode > 0) { SetWaterMode(_waterMode is 2 or 3 ? 1 : 0); if (_waterMode == 0) { _waterSel = -1; WaterLabels(); } return true; }
        if (_tool != Tool.Terrain || !_texActive && _sculpt < 0) return false;
        if (_texDown) TerrainMouseUp();
        _texActive = false; _sculpt = -1; TexLabels(); _gl.Invalidate();
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
        // altura: volta o que estava antes de cada pincelada (desfaz tudo; o cache do terreno é o mesmo vetor)
        if (_terrainDraft is { } td) { while (td.UndoCount > 0 && td.Undo(false) is { } a) _terrain.RefreshHeights(a); }
        _terrainDraft = null; _texActive = false; _sculpt = -1; _texDown = false; _waterMode = 0; _waterSel = -1;
        _clientWater = null;   // relê a água do disco
        foreach (var k in _t.ClearGround(pieces)) _groundPending.Add(k);
        if (_grass.Scene is { } gs) _grass.Invalidate(gs.Grass, true);
        TexLabels(); _gl.Invalidate();
    }

    bool SaveTerrain()
    {
        if (_terrainDraft is not { Dirty: true } d) { _status.Text = Loc.T("obj.nothing", "Nada para salvar."); return false; }
        Dictionary<string, byte[]> files;
        var sum = new List<string>();
        try { files = d.Files(ServerConfigDir is { } sd ? Path.Combine(sd, _t.Name) : null, sum); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Error("terreno: montar o salvar", ex); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        ShowSaveProgress(Loc.T("terrain.savingBar", "Salvando o terreno"), 0);
        var r = TerrainSaver.Write(files, _t.Name, pct => ShowSaveProgress(null, pct));
        bool ok = r.Kind is SaveKind.ClientOnly or SaveKind.Both;
        HideSaveProgress(ok);
        if (!ok) { MessageBox.Show(this, r.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); _status.Text = r.Message; return false; }
        _terrainDraft = null; _texDown = false;   // o cache do terreno já tem o que foi gravado
        _waterSel = -1; _waterMode = 0; _clientWater = null;   // a água relê do disco (IDs novos)
        TexLabels(); WaterLabels();
        string msg = r.Message + (sum.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, sum) : "");
        _status.Text = r.Message;
        bool server = r.Kind == SaveKind.Both;
        if (server) foreach (var f in files.Keys.Where(k => k.EndsWith(".hmap", StringComparison.OrdinalIgnoreCase))) ServerNotice.Notify(Path.Combine(_t.Name, "map", Path.GetFileName(f)));
        if (server) foreach (var f in files.Keys.Where(k => k.EndsWith(".wmap", StringComparison.OrdinalIgnoreCase))) ServerNotice.Notify(Path.Combine(_t.Name, "watermap", Path.GetFileName(f)));
        BeginInvoke(() => SavedDialog.Show(this, msg, server ? SaveAndSendAll : null));
        return true;
    }

    void DrawTerrainBrush()
    {
        DrawWaterMarks();
        if (!TexOn || _hover is not { } h) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        GL.LineWidth(2.5f);
        GL.Color3(_sculpt >= 0 ? Color.FromArgb(255, 190, 70) : _texErase ? Color.FromArgb(255, 90, 80) : Color.FromArgb(110, 200, 255));
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
    string _heightTxt = "";

    internal string DraftTerrainTest()
    {
        ServerConfigDir ??= Environment.GetEnvironmentVariable("MYACE_SERVER");   // captura: a pasta do servidor
        OpenTool(Tool.Terrain);
        var at = new System.Numerics.Vector3(_cam.X + Forward.X * 14, 0, -(_cam.Z + Forward.Z * 14));
        at.Y = _t.HeightAt(at.X, at.Z) ?? 0;
        var d = TerrainDraftOrNew();
        var a = d.AreaAt(at.X, at.Z);
        if (a is null) return " | dfttex: fora do terreno";
        var cur = _t.Ground(a.Value.Ar, a.Value.Ac) ?? [];
        _texSel = Enumerable.Range(0, _t.TextureNames.Count).First(i => !cur.Any(l => l.Texture == _t.TextureNames[i]));
        _texActive = true; _texErase = false; _texStrength = 1; TexLabels();
        for (int k = 0; k < 30; k++) CaptureFrame()?.Dispose();   // tudo carregado antes de medir
        _texR = 49;
        var fb = new List<int>();
        d.BeginStroke(); _texDown = true;
        var sw = System.Diagnostics.Stopwatch.StartNew(); double dabMs = 0, frameMs = 0;
        for (int k = 0; k < 6; k++)
        {
            sw.Restart(); TexDab(at + new System.Numerics.Vector3(k * 4, 0, 0)); dabMs += sw.Elapsed.TotalMilliseconds;
            sw.Restart(); CaptureFrame()?.Dispose(); frameMs += sw.Elapsed.TotalMilliseconds; fb.Add(_terrain.Fallbacks);
        }
        string perfT = $"pintar: pincelada {dabMs / 6:0.0} ms, quadro {frameMs / 6:0.0} ms";
        TerrainMouseUp();
        string flickT = string.Join(",", fb);
        int after = (_t.Ground(a.Value.Ar, a.Value.Ac) ?? []).Length;
        bool undo = TerrainUndo(false) && (_t.Ground(a.Value.Ar, a.Value.Ac) ?? []).Length == cur.Length;
        bool redo = TerrainUndo(true) && (_t.Ground(a.Value.Ar, a.Value.Ac) ?? []).Length == after;
        // altura: sobe 2 toques no ponto, Ctrl+Z volta
        float h0 = _t.HeightAt(at.X, at.Z) ?? 0;
        _sculpt = 0; d.BeginStroke(); _texDown = true; _texStrength = 1; fb.Clear();
        dabMs = frameMs = 0;
        for (int k = 0; k < 6; k++)
        {
            sw.Restart(); TexDab(at); dabMs += sw.Elapsed.TotalMilliseconds; _texLast = null;
            sw.Restart(); CaptureFrame()?.Dispose(); frameMs += sw.Elapsed.TotalMilliseconds; fb.Add(_terrain.Fallbacks);
        }
        string perfH = $"esculpir: pincelada {dabMs / 6:0.0} ms (altura {PerfSculpt / 6:0.0}, malha {PerfMesh / 6:0.0}, grama {PerfGrass / 6:0.0}), quadro {frameMs / 6:0.0} ms";
        sw.Restart(); for (int k = 0; k < 6; k++) CaptureFrame()?.Dispose();
        perfH += $"; quadro parado {sw.Elapsed.TotalMilliseconds / 6:0.0} ms";
        // câmera andando com o pincel de textura ligado x desligado (pior quadro)
        string CamRun(bool brush)
        {
            _sculpt = -1; _texActive = brush; var c0 = _cam; double worst = 0, sum = 0;
            for (int k = 0; k < 20; k++)
            {
                _cam = c0 + new Vector3(k * 3f, 0, 0); _hover = Ray(new Point(_gl.Width / 2, _gl.Height / 2));
                sw.Restart(); TerrainMouseMove(); CaptureFrame()?.Dispose(); double ms = sw.Elapsed.TotalMilliseconds; sum += ms; worst = Math.Max(worst, ms);
            }
            _cam = c0;
            return $"{sum / 20:0.0}/{worst:0}";
        }
        perfH += $"; câmera andando (média/pior ms) desligado {CamRun(false)} ligado {CamRun(true)} desligado {CamRun(false)} ligado {CamRun(true)}";
        TerrainMouseUp();
        string flickH = string.Join(",", fb);
        float h1 = _t.HeightAt(at.X, at.Z) ?? 0;
        bool hUndo = TerrainUndo(false) && MathF.Abs((_t.HeightAt(at.X, at.Z) ?? 0) - h0) < 1e-3f;
        TerrainUndo(true);
        _sculpt = 3; _flatTarget = h0; d.BeginStroke(); _texDown = true; TexDab(at); TerrainMouseUp();
        float h2 = _t.HeightAt(at.X, at.Z) ?? 0;
        var keys = d.Files(ServerConfigDir is { } sdir ? Path.Combine(sdir, _t.Name) : null).Keys.Select(Path.GetFileName).ToList();
        int files = keys.Count;
        _heightTxt = $"{perfT}; {perfH} | pisca textura={flickT} altura={flickH} | altura {h0:0.00}->{h1:0.00} ctrlz={hUndo} plainar->{h2:0.00} arquivos=[{string.Join(", ", keys)}]";
        _sculpt = -1;
        _heightTxt += " | " + DraftWaterTest(at + new System.Numerics.Vector3(60, 0, 0));
        for (int k = 0; k < 20; k++) CaptureFrame()?.Dispose();
        using (var p = ToolsShot()) p.Save(Path.Combine(_shotDir ?? Path.GetTempPath(), "dfttex-painel.png"));
        _hover = new Vector3(at.X, at.Y, at.Z);
        _texStrength = 0.5f;
        return $" | {_heightTxt} | dfttex: área {a.Value.Ar},{a.Value.Ac} camadas {cur.Length}->{after} textura={Path.GetFileName(_t.TextureNames[_texSel])} ctrlz={undo} ctrly={redo} arquivos={files} sujo={TerrainDirty}";
    }
}
