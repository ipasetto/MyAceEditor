using System.Drawing.Drawing2D;
using MyAceEditor.PerfectWorld;
using MyAceEditor.PerfectWorld.WorldEdit;

namespace MyAceEditor.UI;

/// <summary>
/// Cartão flutuante de informação (usuário 09/11): no canto de cima à esquerda da cena, solto (não grudado na barra de
/// cima nem na borda), mostra o que está escolhido: construção, modelo, árvore, efeito, som, bichos (pílula Objetos) ou o
/// tipo de grama/flor e a textura do pincel (pílula Terreno), com ID e tipo. Some quando nada está escolhido. A legenda de
/// atalhos, que abria nesse canto, foi para baixo.
/// </summary>
internal sealed partial class WorldView
{
    readonly InfoCard _info = new();

    void InitInfoCard()
    {
        // pincéis com o último valor que o usuário deixou (a troca guarda na hora)
        if (BrushMemory.Load() is { } m)
        {
            static int Fit(TrackBar t, int v) => Math.Clamp(v, t.Minimum, t.Maximum);
            _texSize.Value = Fit(_texSize, m.TexSize); _texForce.Value = Fit(_texForce, m.TexForce);
            _brushSize.Value = Fit(_brushSize, m.GrassSize); _brushDens.Value = Fit(_brushDens, m.GrassDensity);
            _texR = _texSize.Value; _texStrength = _texForce.Value / 100f; _brushR = _brushSize.Value; _brushDensity = _brushDens.Value / 100f;
        }
        void Keep() { if (!Headless) BrushMemory.Save(_texSize.Value, _texForce.Value, _brushSize.Value, _brushDens.Value); }
        foreach (var t in new[] { _texSize, _texForce, _brushSize, _brushDens }) t.ValueChanged += (_, _) => Keep();
        _info.Visible = false;
        Controls.Add(_info);
        _gl.Resize += (_, _) => PlaceInfoCard();
    }

    void PlaceInfoCard()
    {
        _info.Location = new Point(_gl.Left + 18, _gl.Top + 18);
        if (_info.Visible) _info.BringToFront();
    }

    /// <summary>Atualiza o cartão pelo que está escolhido agora (chamado na seleção, nos pincéis e ao trocar de ferramenta).</summary>
    void UpdateInfoCard()
    {
        var c = InfoNow();
        if (c is null) { if (_info.Visible) _info.Visible = false; return; }
        _info.Set(c.Value.Kind, c.Value.Name, c.Value.Ids, c.Value.File);
        PlaceInfoCard();
        if (!_info.Visible) { _info.Visible = true; _info.BringToFront(); }
        _tools.BringToFront();
    }

    (string Kind, string Name, string Ids, string File)? InfoNow()
    {
        string Id(object v) => Loc.T("info.id", "ID") + ": " + v;
        string Typ(string v) => Loc.T("info.type", "Tipo") + ": " + v;
        if (_tool == Tool.Scene && _sceneSel is not null)
        {
            if (SelDraft is { } o)
            {
                bool isNew = o.CopyOf is not null || o.Key.StartsWith("new:", StringComparison.Ordinal);
                string idTxt = isNew ? Loc.T("info.new", "nova (ganha ID ao salvar)") : "";
                switch (o.Kind)
                {
                    case ObjKind.Building:
                    {
                        if (!isNew && _ecw is not null && _bsd is not null) idTxt = ObjectFormats.OrnamentIdOf(_ecw, _bsd, o.Source)?.ToString() ?? "?";
                        string name = _buildings.SigOf(o.CopyOf ?? o.Key) is { } sig && BuildingNames.User(sig) is { } un ? un : Path.GetFileNameWithoutExtension(o.Source);
                        return (Loc.T("world.objBuilding", "Construção"), name, Id(idTxt) + "   ·   " + Typ(Loc.T("info.building", "construção (.bmd)")), o.Source);
                    }
                    case ObjKind.Model:
                        if (!isNew) idTxt = o.Key[(o.Key.LastIndexOf(':') + 1)..];
                        return (Loc.T("obj.model", "Modelo"), Path.GetFileNameWithoutExtension(o.Source), Id(idTxt) + "   ·   " + Typ(Loc.T("info.model", "modelo do cenário (.ecm)")), o.Source);
                    case ObjKind.Tree:
                    {
                        if (!isNew) idTxt = o.Key[(o.Key.IndexOf(':') + 1)..];
                        int types = ObjScene?.TreeTypes.Count ?? 0;
                        string typ = o.Type >= 0 ? string.Format(Loc.T("info.treeType", "árvore nº {0} de {1} do mapa"), o.Type, types) : Loc.T("info.tree", "árvore");
                        return (Loc.T("obj.tree", "Árvore"), TextureNameGlossary.Display(o.Source, _element), Id(idTxt) + "   ·   " + Typ(typ), o.Source);
                    }
                }
            }
            return _sceneSel switch
            {
                WorldScene.SceneEffect fx => (Loc.T("world.objEffect", "Efeito"), TextureNameGlossary.Display(fx.Gfx, _element), Id(fx.Id) + "   ·   " + Typ(Loc.T("info.effect", "efeito (.gfx)")), fx.Gfx),
                WorldScene.SceneSound sd => (Loc.T("world.objSound", "Som"), Path.GetFileNameWithoutExtension(sd.File), Id(sd.Id) + "   ·   " + Typ(Loc.T("info.sound", "som do cenário")), sd.File),
                WorldScene.Critter cr => (Loc.T("world.objCritter", "Bichos"), Path.GetFileNameWithoutExtension(cr.Model), Id(cr.Id) + "   ·   " + Typ(string.Format(Loc.T("info.critter", "grupo de bichos nº {0}, {1} bicho(s)"), cr.Type, cr.Count)), cr.Model),
                BuildingSel b => (Loc.T("world.objBuilding", "Construção"), Path.GetFileNameWithoutExtension(b.File), Typ(Loc.T("info.building", "construção (.bmd)")), b.File),
                _ => null,
            };
        }
        if (_tool == Tool.Terrain)
        {
            if (_brushPanel.Visible && _grass.Scene is { } sc && _brushType >= 0 && _brushType < sc.GrassTypes.Count)
            {
                string mox = sc.GrassTypes[_brushType].Mox;
                return (Loc.T("info.grass", "Grama / flor do pincel"), TextureNameGlossary.Display(mox, _element), Id(_brushType) + "   ·   " + Typ(string.Format(Loc.T("info.grassType", "tipo nº {0} de {1} do mapa"), _brushType, sc.GrassTypes.Count)), mox);
            }
            if (!_brushPanel.Visible && _texSel >= 0 && _texSel < _t.TextureNames.Count)
            {
                string tex = _t.TextureNames[_texSel];
                return (Loc.T("info.texture", "Textura do pincel"), TextureNameGlossary.Display(tex, _element), Id(_texSel) + "   ·   " + Typ(string.Format(Loc.T("info.texType", "textura nº {0} de {1} do mapa"), _texSel, _t.TextureNames.Count)), tex);
            }
        }
        return null;
    }

    /// <summary>Teste ("dftinfo"): o cartão aparece com algo escolhido e some sem nada.</summary>
    internal string InfoCardTest()
    {
        string load = DraftLoadTest();
        var r = new List<string>();
        _sceneSel = null; ShowObjPanel(); r.Add($"nada escolhido: visível={_info.Visible}");
        if (_buildings.Nearest(_cam) is { } near) { _sceneSel = new BuildingSel(near.File, near.Center); ShowObjPanel(); r.Add($"construção: visível={_info.Visible} \"{_info.Text}\""); }
        // Ver colisão: só da escolhida, com as arestas das peças sólidas (construção sólida mais perto)
        if (_draft?.Pieces.Values.SelectMany(p => p.Objects).Where(o => o.Kind == ObjKind.Building && o.Solid && !o.Deleted)
                .OrderBy(o => (new OpenTK.Mathematics.Vector3(o.Now.Pos.X, o.Now.Pos.Y, -o.Now.Pos.Z) - _cam).LengthSquared).FirstOrDefault() is { } solid)
        {
            _sceneSel = new BuildingSel(solid.Key, new OpenTK.Mathematics.Vector3(solid.Now.Pos.X, solid.Now.Pos.Y, -solid.Now.Pos.Z));
            _showCollision.Checked = true; ShowObjPanel();
            int segs = SelDraft is { } sd ? SelCollision(sd).Count : -1;
            r.Add($"ver colisão: {Path.GetFileName(solid.Key)} {segs} arestas, barra \"{_status.Text}\"; caixa à vista={_showCollision.Visible && _showCollision.Parent?.ClientRectangle.Contains(_showCollision.Bounds) == true}");
            try
            {
                using var bmp = new Bitmap(_tools.Width, _tools.Height);
                _tools.DrawToBitmap(bmp, new Rectangle(0, 0, _tools.Width, _tools.Height));
                bmp.Save(Path.Combine(_shotDir ?? Path.GetTempPath(), "window-object-card.png"));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException) { AppLog.Error("dftinfo: foto do painel", ex); }
            _showCollision.Checked = false;
        }
        _sceneSel = null; ShowObjPanel(); r.Add($"tirou a escolha: visível={_info.Visible}");
        // JAV de árvores: a mesma contagem depois de prévias de outros mapas e de reler o mapa (como depois de salvar); um
        // tipo cujo número mudou não fica com o desenho antigo
        if (TreeRows() is { } rows0)
        {
            foreach (var row in rows0.Skip(_trees.Scene!.TreeTypes.Count).Take(6)) { int ty = _trees.TypeFor(row.Spt, row.Comp); _trees.Prepare(ty); }
            int mid = TreeRows()!.Count;
            _trees.Reload();
            int after = TreeRows()!.Count;
            r.Add($"JAV árvores: {rows0.Count} -> {mid} (prévias) -> {after} (relido) iguais={rows0.Count == mid && mid == after}");
        }
        SwitchWorldPill(Tool.Terrain);
        if (_t.TextureNames.Count > 0) { _texSel = 0; TexLabels(); r.Add($"textura: visível={_info.Visible} \"{_info.Text}\""); }
        return load + " | dftinfo: " + string.Join("; ", r);
    }
}

/// <summary>
/// Valores dos pincéis guardados entre uma vez e outra (usuário 09/11: "as brushs devem manter o último valor que o
/// usuário deixou"): tamanho e força da textura, tamanho e densidade da grama, num arquivo de texto das preferências.
/// </summary>
internal static class BrushMemory
{
    static string File_ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "brushes.txt");

    public static (int TexSize, int TexForce, int GrassSize, int GrassDensity)? Load()
    {
        try
        {
            if (!File.Exists(File_)) return null;
            var p = File.ReadAllText(File_).Split(';');
            return p.Length >= 4 && int.TryParse(p[0], out int a) && int.TryParse(p[1], out int b) && int.TryParse(p[2], out int c) && int.TryParse(p[3], out int d) ? (a, b, c, d) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("pincéis: ler o último valor", ex); return null; }
    }

    public static void Save(int texSize, int texForce, int grassSize, int grassDensity)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(File_)!); File.WriteAllText(File_, $"{texSize};{texForce};{grassSize};{grassDensity}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("pincéis: guardar o último valor", ex); }
    }
}

/// <summary>Cartão escuro de 4 linhas (tipo, nome, ID e tipo, arquivo) que se mede sozinho.</summary>
internal sealed class InfoCard : Control
{
    string _kind = "", _name = "", _ids = "", _file = "";
    static readonly Font FKind = new("Segoe UI Semibold", 8.5f), FName = new("Segoe UI Semibold", 10.5f), FIds = new("Segoe UI", 9f), FFile = new("Segoe UI", 8f);
    const int Pad = 12, MaxW = 380;

    public InfoCard() { DoubleBuffered = true; BackColor = Color.FromArgb(28, 32, 38); ForeColor = Color.White; SetStyle(ControlStyles.Selectable, false); TabStop = false; }

    public void Set(string kind, string name, string ids, string file)
    {
        if (kind == _kind && name == _name && ids == _ids && file == _file) return;
        _kind = kind; _name = name; _ids = ids; _file = file;
        Text = kind + " | " + name + " | " + ids + " | " + file;
        int w = new[] { (kind, FKind), (name, FName), (ids, FIds), (file, FFile) }.Max(t => TextRenderer.MeasureText(t.Item1, t.Item2).Width);
        w = Math.Clamp(w + Pad * 2, 220, MaxW);
        int tw = w - Pad * 2;
        int h = Pad;
        foreach (var (t, f) in new[] { (kind, FKind), (name, FName), (ids, FIds), (file, FFile) })
            h += TextRenderer.MeasureText(t, f, new Size(tw, 0), TextFormatFlags.WordBreak).Height + 2;
        Size = new Size(w, h + Pad - 2);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(Color.FromArgb(62, 70, 84))) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        using (var bar = new SolidBrush(UiKit.Accent)) g.FillRectangle(bar, 0, 0, 4, Height);
        int y = Pad, tw = Width - Pad * 2;
        foreach (var (t, f, c) in new[] { (_kind, FKind, Color.FromArgb(96, 165, 250)), (_name, FName, Color.White), (_ids, FIds, Color.Gainsboro), (_file, FFile, Color.Gray) })
        {
            var sz = TextRenderer.MeasureText(t, f, new Size(tw, 0), TextFormatFlags.WordBreak);
            TextRenderer.DrawText(g, t, f, new Rectangle(Pad, y, tw, sz.Height), c, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y += sz.Height + 2;
        }
    }
}
