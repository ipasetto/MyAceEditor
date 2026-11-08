using MyAceEditor.PerfectWorld;
using MyAceEditor.PerfectWorld.WorldEdit;
using OpenTK.Graphics.OpenGL;

namespace MyAceEditor.UI;

/// <summary>
/// Água na ferramenta Terreno (desenho aprovado 08/11): a água é um nível num retângulo e aparece sozinha onde o chão fica
/// abaixo do nível (como o jogo e o servidor). Escolher (5) clica numa água; Nova água (6) põe um quadrado de 256 m;
/// nível, largura, comprimento e cor no painel; Delete apaga; Ctrl+C/Ctrl+V copia e cola (uma por clique); Ctrl+Z em tudo.
/// O pincel Descer cava abaixo do nível e a água aparece. Salvar terreno grava o cliente (bsdata, .ecbsd, .ecwld) e o
/// servidor (watermap\N.wmap).
/// </summary>
internal sealed partial class WorldView
{
    int _waterMode;          // 0 desligado, 1 escolher, 2 nova água, 3 colar
    int _waterSel = -1;      // índice da água escolhida em Water.Areas (as fotos do Ctrl+Z mantêm a ordem)
    WaterArea? _waterClip;
    bool _waterSync;
    Button? _waterPickBtn, _waterNewBtn;
    readonly Label _waterTitle = new() { AutoSize = false, Width = 245, Height = 52, ForeColor = Color.White, Font = new Font("Segoe UI", 8.5f), Margin = new Padding(3, 4, 3, 4) };
    readonly NumericUpDown _waterLevel = new() { Minimum = -2000, Maximum = 5000, DecimalPlaces = 1, Increment = 0.5m, Width = 110 };
    readonly NumericUpDown _waterW = new() { Minimum = 32, Maximum = 1024, Increment = 32, Width = 110 };
    readonly NumericUpDown _waterL = new() { Minimum = 32, Maximum = 1024, Increment = 32, Width = 110 };
    readonly Panel _waterFields = new() { Width = 250, Height = 150, Margin = new Padding(0, 0, 0, 4), Visible = false };
    Button? _waterColorBtn;

    WaterDraft? WaterEd => _terrainDraft?.Water;
    WaterArea? WaterSel => WaterEd is { } w && _waterSel >= 0 && _waterSel < w.Areas.Count ? w.Areas[_waterSel] : null;

    void InitWaterSection()
    {
        _terrainPage.Controls.Add(SectionLabel(Loc.T("water.section", "Água")));
        var row = new FlowLayoutPanel { Width = 250, Height = 70, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
        _waterPickBtn = BigButton("", Loc.T("water.pick", "Escolher"), "5", () => SetWaterMode(1), 76);
        _waterNewBtn = BigButton("", Loc.T("water.new", "Nova água"), "6", () => SetWaterMode(2), 76);
        _toolTip.SetToolTip(_waterPickBtn, Loc.T("water.pickTip", "Clique numa água para mudar o nível, o tamanho ou a cor."));
        _toolTip.SetToolTip(_waterNewBtn, Loc.T("water.newTip", "Clique no chão para pôr uma água de 256 × 256 m, 3 m acima do chão."));
        row.Controls.Add(_waterPickBtn); row.Controls.Add(_waterNewBtn);
        _terrainPage.Controls.Add(row);
        _terrainPage.Controls.Add(_waterTitle);
        // campos da água escolhida: nível, largura, comprimento, cor, apagar
        Label L(string t, int y) => new() { Text = t, AutoSize = false, Width = 128, Height = 22, Left = 3, Top = y + 3, ForeColor = Color.Silver };
        _waterFields.Controls.Add(L(Loc.T("water.level", "Nível da água (m)"), 0)); _waterLevel.Left = 134; _waterLevel.Top = 0;
        _waterFields.Controls.Add(L(Loc.T("water.width", "Largura (m)"), 30)); _waterW.Left = 134; _waterW.Top = 30;
        _waterFields.Controls.Add(L(Loc.T("water.length", "Comprimento (m)"), 60)); _waterL.Left = 134; _waterL.Top = 60;
        foreach (var n in new[] { _waterLevel, _waterW, _waterL }) { n.BackColor = Color.FromArgb(40, 45, 54); n.ForeColor = Color.White; NoWheel(n); _waterFields.Controls.Add(n); }
        _waterLevel.ValueChanged += (_, _) => { if (!_waterSync && WaterSel is { } a) WaterDo(w => w.SetLevel(a, (float)_waterLevel.Value)); };
        _waterW.ValueChanged += (_, _) => { if (!_waterSync && WaterSel is { } a) WaterDo(w => w.Resize(a, (int)(_waterW.Value / 32), a.Rows)); };
        _waterL.ValueChanged += (_, _) => { if (!_waterSync && WaterSel is { } a) WaterDo(w => w.Resize(a, a.Cols, (int)(_waterL.Value / 32))); };
        _waterColorBtn = SmallButton("", Loc.T("water.color", "Cor da água"), WaterColor);
        _waterColorBtn.Left = 0; _waterColorBtn.Top = 92; _waterColorBtn.Width = 120;
        var del = SmallButton("", Loc.T("water.delete", "Apagar"), WaterDelete);
        del.Left = 126; del.Top = 92; del.Width = 120;
        _toolTip.SetToolTip(del, Loc.T("water.deleteTip", "Apaga a água escolhida (tecla Delete; Ctrl+Z volta)."));
        _waterFields.Controls.Add(_waterColorBtn); _waterFields.Controls.Add(del);
        _terrainPage.Controls.Add(_waterFields);
    }

    /// <summary>Liga um modo da água (desliga os pincéis); de novo no mesmo botão desliga.</summary>
    void SetWaterMode(int m)
    {
        if (WaterEd is null && TerrainDraftOrNew().Water is null) { _status.Text = Loc.T("water.none", "Este mapa não tem arquivo de cenário (.ecwld/.ecbsd): sem água para editar."); return; }
        _waterMode = _waterMode == m && m != 3 ? 0 : m;
        if (_waterMode > 0) { _texActive = false; _sculpt = -1; }
        _status.Text = _waterMode switch
        {
            1 => Loc.T("water.pickHint", "Clique numa água para escolher. Ela aparece onde o chão fica abaixo do nível."),
            2 => Loc.T("water.newHint", "Clique no chão para pôr a água. Depois ajuste o nível ou cave com Descer."),
            3 => Loc.T("water.pasteHint", "Clique onde colar a água copiada (Esc cancela)."),
            _ => "",
        };
        TexLabels(); WaterLabels(); _gl.Invalidate();
    }

    void WaterLabels()
    {
        bool p = _waterMode == 1, n = _waterMode is 2 or 3;
        if (_waterPickBtn is not null) { _waterPickBtn.BackColor = p ? UiKit.Accent : Color.FromArgb(40, 45, 54); _waterPickBtn.FlatAppearance.BorderColor = p ? UiKit.Accent : Color.FromArgb(62, 70, 84); }
        if (_waterNewBtn is not null) { _waterNewBtn.BackColor = n ? UiKit.Accent : Color.FromArgb(40, 45, 54); _waterNewBtn.FlatAppearance.BorderColor = n ? UiKit.Accent : Color.FromArgb(62, 70, 84); }
        var a = WaterSel;
        _waterFields.Visible = a is not null;   // sem água escolhida não há campos (usuário 08/11)
        _waterTitle.Text = a is null
            ? (WaterEd is null ? Loc.T("water.intro", "Use Escolher para mexer numa água ou Nova água para pôr uma.") : string.Format(Loc.T("water.count", "{0} água(s) neste mapa. Escolher (5) ou Nova água (6)."), WaterEd.Areas.Count))
            : string.Format(Loc.T("water.selInfo", "Água escolhida: {0:0} × {1:0} m, nível {2:0.0} m, {3} de {4} quadrados com água."), a.Cols * a.Cell, a.Rows * a.Cell, a.Height, a.Wet, a.Cols * a.Rows)
              + (a.Wet == 0 ? Environment.NewLine + Loc.T("water.dry", "O chão está todo acima do nível: suba o nível ou cave com Descer.") : "");
        if (a is null) return;
        _waterSync = true;
        try
        {
            _waterLevel.Value = Math.Clamp((decimal)MathF.Round(a.Height, 1), _waterLevel.Minimum, _waterLevel.Maximum);
            _waterW.Value = Math.Clamp((decimal)(a.Cols * a.Cell), _waterW.Minimum, _waterW.Maximum);
            _waterL.Value = Math.Clamp((decimal)(a.Rows * a.Cell), _waterL.Minimum, _waterL.Maximum);
            if (_waterColorBtn is not null) _waterColorBtn.ForeColor = Color.FromArgb(255, Color.FromArgb(a.Argb));
        }
        finally { _waterSync = false; }
    }

    /// <summary>Uma ação nas águas como passo de Ctrl+Z, com o desenho e o painel atualizados e erro explicado.</summary>
    void WaterDo(Action<WaterDraft> act)
    {
        try { TerrainDraftOrNew().WaterEdit(act); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            AppLog.Error("agua: editar", ex);
            _status.Text = Loc.T("water.editFail", "Não deu para mexer nessa água; veja o log.");
        }
        RefreshWaterView(); TexLabels(); WaterLabels(); _gl.Invalidate();
    }

    /// <summary>Redesenha a água a partir do rascunho (ou relê do disco sem rascunho).</summary>
    void RefreshWaterView()
    {
        if (_terrainDraft is { } d) d.WaterChanged = false;
        _clientWater = WaterEd is { } w ? w.Areas.SelectMany(a => WetStrips(a.ToView())).ToList() : null;
        if (_waterSel >= (WaterEd?.Areas.Count ?? 0)) _waterSel = -1;
    }

    /// <summary>Água nova/colada no ponto (preview e clique usam o mesmo cálculo).</summary>
    WaterArea? WaterPreview(float x, float z)
    {
        if (WaterEd is not { } w) return null;
        int piece = _t.PieceAt(x, z);
        if (piece <= 0) return null;
        var src = _waterMode == 3 ? _waterClip : null;
        var a = new WaterArea { X = x, Z = z, Cols = src?.Cols ?? 8, Rows = src?.Rows ?? 8, Cell = 32, Height = (_t.HeightAt(x, z) ?? 0) + 3 };
        w.Fit(a, piece);
        return a;
    }

    /// <summary>
    /// Clique (soltar sem arrastar) no modo água: escolhe ou põe. Arrastar continua girando a câmera (antes o apertar era
    /// todo da água e a câmera não girava; teste do usuário 08/11).
    /// </summary>
    bool WaterMouseUp(MouseEventArgs e)
    {
        if (_tool != Tool.Terrain || _waterMode == 0 || e.Button != MouseButtons.Left) return false;
        if (Math.Abs(e.X - _down.X) > 4 || Math.Abs(e.Y - _down.Y) > 4) return false;   // arrastou: foi a câmera
        if (Ray(e.Location) is not { } g || WaterEd is not { } w) return true;
        if (_waterMode == 1)
        {
            var hit = w.At(g.X, g.Z);
            _waterSel = hit is null ? -1 : w.Areas.IndexOf(hit);
            _status.Text = hit is null ? Loc.T("water.noneHere", "Não há água aqui. Use Nova água (6) para pôr uma.") : "";
        }
        else
        {
            float level = (_t.HeightAt(g.X, g.Z) ?? g.Y) + 3;
            var clip = _waterMode == 3 ? _waterClip : null;
            WaterArea? made = null;
            WaterDo(dw => made = clip is null ? dw.Create(g.X, g.Z, level) : dw.Paste(clip, g.X, g.Z, level));
            if (made is null) { _status.Text = Loc.T("water.outsideMap", "Fora do mapa: a água não foi posta."); return true; }
            _waterSel = w.Areas.IndexOf(made);
            _waterMode = 1;   // uma por clique: depois de pôr, a nova fica escolhida
            _status.Text = Loc.T("water.made", "Água posta. Ajuste o nível e o tamanho no painel, ou cave com Descer (2).");
        }
        WaterLabels(); _gl.Invalidate();
        return true;
    }

    void WaterColor()
    {
        if (WaterSel is not { } a) return;
        using var dlg = new ColorDialog { Color = Color.FromArgb(255, Color.FromArgb(a.Argb)), FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        int argb = Color.FromArgb(255, dlg.Color).ToArgb();
        WaterDo(w => w.SetColor(a, argb));
    }

    void WaterDelete()
    {
        if (WaterSel is not { } a) return;
        WaterDo(w => w.Delete(a));
        _waterSel = -1;
        _status.Text = Loc.T("water.deleted", "Água apagada (Ctrl+Z volta).");
        WaterLabels();
    }

    void WaterCopy()
    {
        _waterClip = WaterSel?.Copy();
        _status.Text = _waterClip is null ? Loc.T("water.copyNone", "Escolha uma água antes de copiar.") : Loc.T("water.copied", "Água copiada — Ctrl+V e clique onde colar.");
    }

    void WaterPaste()
    {
        if (_waterClip is null) { _status.Text = Loc.T("water.pasteNone", "Copie uma água antes (Ctrl+C)."); return; }
        SetWaterMode(3);
    }

    /// <summary>Contorno das águas no modo água: a escolhida em amarelo, as outras em azul; a prévia da nova segue o mouse.</summary>
    void DrawWaterMarks()
    {
        if (_tool != Tool.Terrain || WaterEd is not { } w || _waterMode == 0 && _waterSel < 0) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        void Rect(WaterArea a, Color c, float width)
        {
            GL.LineWidth(width); GL.Color3(c);
            GL.Begin(PrimitiveType.LineLoop);
            float y = a.Height + 0.3f;
            GL.Vertex3(a.Left, y, -a.Top); GL.Vertex3(a.Right, y, -a.Top); GL.Vertex3(a.Right, y, -a.Bottom); GL.Vertex3(a.Left, y, -a.Bottom);
            GL.End();
        }
        if (_waterMode > 0)
            foreach (var a in w.Areas) if (Math.Abs(a.X - _cam.X) < 3000 && Math.Abs(a.Z + _cam.Z) < 3000) Rect(a, Color.FromArgb(90, 170, 255), 1.5f);
        if (WaterSel is { } s) Rect(s, Color.FromArgb(255, 210, 60), 3f);
        if (_waterMode is 2 or 3 && _hover is { } h && WaterPreview(h.X, h.Z) is { } p) Rect(p, Color.FromArgb(120, 255, 140), 2.5f);
        GL.LineWidth(1); GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>Para a captura ("dfttex"): põe uma água no chão perto, sobe o nível, desfaz/refaz e monta os arquivos.</summary>
    string DraftWaterTest(System.Numerics.Vector3 at)
    {
        SetWaterMode(2);
        if (WaterEd is not { } w) return "água: sem cenário";
        int before = w.Areas.Count;
        float level = (_t.HeightAt(at.X, at.Z) ?? 0) + 3;
        WaterArea? made = null;
        WaterDo(dw => made = dw.Create(at.X, at.Z, level));
        if (made is null) return "água: fora do mapa";
        _waterSel = w.Areas.IndexOf(made); _waterMode = 1; WaterLabels();
        int wet0 = made.Wet;
        _waterLevel.Value += 2;   // passo de Ctrl+Z pelo campo
        int wet1 = WaterSel?.Wet ?? -1;
        bool undo = TerrainUndo(false) && WaterSel?.Wet == wet0;
        bool redo = TerrainUndo(true) && WaterSel?.Wet == wet1;
        var sum = new List<string>();
        var keys = _terrainDraft!.Files(ServerConfigDir is { } sd ? Path.Combine(sd, _t.Name) : null, sum).Keys.Select(Path.GetFileName).ToList();
        string panel = _waterFields.Visible ? "campos" : "sem campos";
        return $"água: {before}->{w.Areas.Count} molhadas {wet0}->{wet1} ctrlz={undo} ctrly={redo} {panel} arquivos=[{string.Join(", ", keys)}] {string.Join(" | ", sum)}";
    }
}
