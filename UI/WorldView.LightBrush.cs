using MyAceEditor.PerfectWorld;
using MyAceEditor.PerfectWorld.WorldEdit;

namespace MyAceEditor.UI;

/// <summary>
/// Pincel de sombra e luz do chão na pílula Terreno (usuário 09/11): Sombra (7) escurece, Luz (8) clareia, Conta-gotas de
/// luz (9) pega a luz de um ponto e o pincel passa a copiar essa luz (melhor para igualar uma sombra com o resto do chão).
/// Mexe no mapa de luz do dia e da noite juntos (LightDraft); usa o tamanho e a força do pincel do terreno; prévia ao vivo no
/// chão e na cor da grama; Ctrl+Z por pincelada; grava no Salvar mundo.
/// </summary>
internal sealed partial class WorldView
{
    int _lightMode;                      // 0 desligado, 1 sombra, 2 luz, 3 copiar a luz pega, 4 borracha (luz do arquivo)
    (int Day, int Night)? _lightPick;
    readonly Button?[] _lightBtns = new Button?[4];
    // B/E/I seguem a seção em uso na pílula Terreno (usuário 09/11: "B pincéis, E borrachas, I conta-gotas" em toda
    // ferramenta): false = textura do chão, true = sombra e luz. A grama tem as dela com o painel aberto.
    bool _lightFamily;
    int _lastLightBrush = 1;
    bool _lightDown;

    readonly Panel _lightSwatch = new();
    readonly Label _lightSwatchText = new();
    FlowLayoutPanel? _lightPickRow;

    /// <summary>Mostra o tom pego (amostra do dia) e quanto de luz ele tem.</summary>
    void ShowLightPick()
    {
        if (_lightPickRow is null) return;
        _lightPickRow.Visible = _lightPick is not null;
        if (_lightPick is not { } p) return;
        _lightSwatch.BackColor = Color.FromArgb(255, Color.FromArgb(p.Day));
        int lum = (((p.Day >> 16) & 255) + ((p.Day >> 8) & 255) + (p.Day & 255)) / 3;
        _lightSwatchText.Text = string.Format(Loc.T("light.picked2", "Tom pego: {0}% de luz"), lum * 100 / 255);
    }

    LightDraft LightDraftOrNew() => TerrainDraftOrNew().Light ??= new LightDraft(_t);

    void InitLightSection()
    {
        _terrainPage.Controls.Add(SectionLabel(Loc.T("light.section", "Sombra e luz do chão")));
        var row = new FlowLayoutPanel { Width = 250, Height = 70, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
        _lightBtns[0] = BigButton("", Loc.T("light.shadow", "Sombra"), "7", () => SetLightMode(1), 57);
        _lightBtns[1] = BigButton("", Loc.T("light.light", "Luz"), "8", () => SetLightMode(2), 57);
        _lightBtns[2] = BigButton("", Loc.T("light.pickTone", "Pegar tom"), "I", () => StartDropper(3), 57);
        _lightBtns[3] = BigButton("", Loc.T("grass.eraser", "Borracha"), "E", () => SetLightMode(4), 57);
        _toolTip.SetToolTip(_lightBtns[3]!, Loc.T("light.eraseTip", "Borracha da sombra e luz: volta o chão à luz do arquivo (como era antes das pinceladas)."));
        _toolTip.SetToolTip(_lightBtns[0]!, Loc.T("light.shadowTip", "Escurece o chão (o mapa de luz, de dia e de noite). A força do pincel diz quanto."));
        _toolTip.SetToolTip(_lightBtns[1]!, Loc.T("light.lightTip", "Clareia o chão (tira sombra). A força do pincel diz quanto."));
        _toolTip.SetToolTip(_lightBtns[2]!, Loc.T("light.pickTip2", "Conta-gotas de sombra e luz: clique num ponto do chão (na sombra ou no claro) e o pincel passa a pintar com o tom dali."));
        foreach (var b in _lightBtns) row.Controls.Add(b!);
        _terrainPage.Controls.Add(row);
        // tom pego pelo conta-gotas (usuário 09/11: não dava para saber se tinha pego sombra ou luz)
        var pickRow = new FlowLayoutPanel { Width = 250, Height = 26, WrapContents = false, Margin = new Padding(0, 0, 0, 4), Visible = false };
        _lightSwatch.Size = new Size(36, 20); _lightSwatch.Margin = new Padding(2, 2, 6, 2); _lightSwatch.BorderStyle = BorderStyle.FixedSingle;
        _lightSwatchText.AutoSize = true; _lightSwatchText.ForeColor = Color.Gainsboro; _lightSwatchText.Margin = new Padding(0, 4, 0, 0);
        pickRow.Controls.Add(_lightSwatch); pickRow.Controls.Add(_lightSwatchText);
        _lightPickRow = pickRow;
        _terrainPage.Controls.Add(pickRow);
        _terrainPage.Controls.Add(Hint(Loc.T("light.hint", "A cor do chão no jogo é a textura vezes a luz: pintar textura não tira sombra. Aqui você pinta a luz.")));
    }

    void SetLightMode(int m)
    {
        StopBrush(); StopDropper();
        if (_texDown) TerrainMouseUp();
        _texActive = false; _sculpt = -1; _waterMode = 0;
        _lightFamily = true;
        if (m == 3 && _lightPick is null) { StartDropper(3); return; }
        if (m is 1 or 2) _lastLightBrush = m;
        _lightMode = m;   // como B/E da textura: a tecla/botão liga; Esc ou botão direito desligam
        TexLabels(); WaterLabels(); _gl.Focus(); _gl.Invalidate();
    }

    /// <summary>Botões de sombra/luz acesos e o texto do estado (chamado pelo TexLabels).</summary>
    void LightLabels()
    {
        for (int i = 0; i < 4; i++)
            if (_lightBtns[i] is { } b)
            {
                bool on = _lightMode == i + 1 || i == 2 && _dropper == 3 && DropperOn;
                b.BackColor = on ? UiKit.Accent : Color.FromArgb(40, 45, 54);
                b.FlatAppearance.BorderColor = on ? UiKit.Accent : Color.FromArgb(62, 70, 84);
            }
        if (_lightMode > 0)
            _texTitle.Text = _lightMode switch
            {
                1 => Loc.T("light.shadowing", "Sombra: arraste no chão para escurecer"),
                2 => Loc.T("light.lighting", "Luz: arraste no chão para clarear"),
                4 => Loc.T("light.erasing", "Borracha: arraste para voltar à luz do arquivo"),
                _ => Loc.T("light.copyingTone", "Copiando o tom pego: arraste no chão"),
            };
    }

    /// <summary>Conta-gotas da luz: guarda a luz (dia e noite) do ponto e liga o pincel de copiar.</summary>
    bool LightPickAt(System.Numerics.Vector3 at)
    {
        (int Day, int Night)? got;
        try { got = LightDraftOrNew().Pick(at.X, at.Z); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Error("luz do chão: conta-gotas", ex);
            _status.Text = Loc.T("light.paintFail", "Não deu para abrir o mapa de luz deste pedaço; veja o log.");
            return false;
        }
        if (got is not { } p) { _status.Text = Loc.T("light.pickNone", "Este lugar não tem mapa de luz."); return false; }
        _lightPick = p; _lightMode = 3;
        ShowLightPick();
        _status.Text = Loc.T("light.pickedTone", "Tom pego (sombra ou claro, como estava no ponto): arraste no chão para pintar com ele.");
        TexLabels(); _gl.Invalidate();
        return true;
    }

    void LightBegin(System.Numerics.Vector3 at)
    {
        LightDraftOrNew().BeginStroke(); _lightDown = true; _texDown = true; _texLast = null;
        LightDab(at);
    }

    void LightDab(System.Numerics.Vector3 at)
    {
        try
        {
            var mode = _lightMode switch { 1 => LightDraft.Mode.Shadow, 2 => LightDraft.Mode.Light, 4 => LightDraft.Mode.Erase, _ => LightDraft.Mode.Copy };
            var ld = LightDraftOrNew();
            foreach (var (ar, ac, night) in ld.Paint(at.X, at.Z, _texR, mode, _texStrength, _lightPick))
                if (ld.Pixels(ar, ac, night) is { } px) _terrain.SetLight(ar, ac, night, px.Px, px.Side);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Error("luz do chão: pintar", ex);
            _status.Text = Loc.T("light.paintFail", "Não deu para abrir o mapa de luz deste pedaço; veja o log.");
        }
        _texLast = at; _gl.Invalidate();
    }

    /// <summary>Fim da pincelada de luz: entra no Ctrl+Z e a grama em volta pega a luz nova.</summary>
    void LightEnd()
    {
        _lightDown = false;
        if (_terrainDraft?.Light is not { } ld || !ld.EndStroke()) return;
        WorldDid('L');
        LightToGrass(ld, ld.LastBox);
        TexLabels();
    }

    void LightToGrass(LightDraft ld, (float X0, float Z0, float X1, float Z1) box)
    {
        foreach (var key in ld.OpenAreas)
            if (ld.Pixels(key.Ar, key.Ac, key.Night) is { } px) _grass.SetLight(key.Ar, key.Ac, key.Night, px.Px, px.Side);
        if (_grass.Scene is { } gs) _grass.Invalidate(gs.Grass, false, (box.X0 + box.X1) / 2, (box.Z0 + box.Z1) / 2, MathF.Max(box.X1 - box.X0, box.Z1 - box.Z0) / 2 + 2);
    }

    /// <summary>Ctrl+Z / Ctrl+Y de uma pincelada de luz (ordem da Edição de Mundo).</summary>
    bool LightUndoCore(bool redo)
    {
        if (_terrainDraft?.Light is not { } ld || ld.Undo(redo) is not { } areas) return false;
        float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
        foreach (var (ar, ac, night) in areas)
        {
            if (ld.Pixels(ar, ac, night) is { } px) _terrain.SetLight(ar, ac, night, px.Px, px.Side);
            float ax = -_t.Width / 2 + ac * TerrainFile.AreaM, az = _t.Height / 2 - ar * TerrainFile.AreaM;
            x0 = MathF.Min(x0, ax); x1 = MathF.Max(x1, ax + TerrainFile.AreaM); z1 = MathF.Max(z1, az); z0 = MathF.Min(z0, az - TerrainFile.AreaM);
        }
        LightToGrass(ld, (x0, z0, x1, z1));
        _status.Text = redo ? Loc.T("light.redone", "Sombra/luz refeita.") : Loc.T("light.undone", "Sombra/luz desfeita (Ctrl+Y refaz).");
        TexLabels(); _gl.Invalidate();
        return true;
    }

    /// <summary>Abandonar: a luz da tela e da grama volta ao disco.</summary>
    void ForgetLightDraft()
    {
        if (_terrainDraft?.Light is not { } ld) return;
        foreach (var (ar, ac, night) in ld.OpenAreas) { _lightPending.Add((ar, ac)); _grass.ForgetLight(ar, ac); }
        if (_grass.Scene is { } gs) _grass.Invalidate(gs.Grass, true);
        _lightMode = 0; _lightPick = null; ShowLightPick();
    }

    /// <summary>
    /// Teste ("dftlight"): sombra num ponto perto da câmera escurece dia e noite, Ctrl+Z volta igual, Ctrl+Y refaz, o plano
    /// do salvar regrava só blocos de luz e o resto do arquivo fica igual; abandona no fim (não grava nada).
    /// </summary>
    internal string LightBrushTest()
    {
        string load = DraftLoadTest();
        SwitchWorldPill(Tool.Terrain);
        var at = new System.Numerics.Vector3(_cam.X + Forward.X * 20, 0, -(_cam.Z + Forward.Z * 20)); at.Y = _t.HeightAt(at.X, at.Z) ?? 0;
        var ld = LightDraftOrNew();
        var before = ld.Pick(at.X, at.Z);
        SetLightMode(1); _texStrength = 1;
        LightBegin(at); TerrainMouseUp();
        var after = ld.Pick(at.X, at.Z);
        static int Lum(int c) => ((c >> 16) & 255) + ((c >> 8) & 255) + (c & 255);
        bool darker = before is { } b && after is { } a && Lum(a.Day) < Lum(b.Day) && Lum(a.Night) <= Lum(b.Night);
        bool u = UndoAny(); var undone = ld.Pick(at.X, at.Z);
        bool same = undone == before;
        bool y = RedoAny(); var redone = ld.Pick(at.X, at.Z);
        var sum = new List<string>();
        var files = ld.Files(sum);
        int diffBytes = 0, total = 0;
        foreach (var (path, bytes) in files) { var disk = File.ReadAllBytes(path); total += bytes.Length; for (int i = 0; i < bytes.Length; i++) if (bytes[i] != disk[i]) diffBytes++; }
        bool sizes = files.All(kv => kv.Value.Length == new FileInfo(kv.Key).Length);
        // borracha: volta à luz do arquivo no ponto
        SetLightMode(4); LightBegin(at); TerrainMouseUp(); var erased = ld.Pick(at.X, at.Z);
        bool erasedOk = erased == before;
        // B/E/I seguem a seção: com sombra e luz em uso, E = borracha da luz e I = conta-gotas da luz
        SetLightMode(1); TerrainKey(Keys.E); bool eLight = _lightMode == 4; TerrainKey(Keys.I); bool iLight = _dropper == 3; StopDropper();
        if (_t.TextureNames.Count > 0 && _texSel < 0) _texSel = 0;   // sem textura o Pincel abriria a janela de escolher (teste não abre janela)
        TexPaint(); TerrainKey(Keys.E); bool eTex = _texErase && _lightMode == 0;
        // conta-gotas pega SOMBRA: no ponto escurecido pega o tom escuro; copiado em outro ponto, ele escurece também
        SetLightMode(1); LightBegin(at); TerrainMouseUp(); var dark = ld.Pick(at.X, at.Z);
        StartDropper(3); bool picked = LightPickAt(at);
        bool pickDark = picked && dark is { } dk && _lightPick is { } pk && pk.Day == dk.Day;
        var at2 = at + new System.Numerics.Vector3(_texR * 3 + 4, 0, 0); at2.Y = _t.HeightAt(at2.X, at2.Z) ?? at.Y;
        var plain = ld.Pick(at2.X, at2.Z);
        for (int i = 0; i < 4; i++) { LightBegin(at2); TerrainMouseUp(); }
        var copied = ld.Pick(at2.X, at2.Z);
        bool copyDark = plain is { } pl && copied is { } cp && dark is { } dk2 && Math.Abs(Lum(cp.Day) - Lum(dk2.Day)) < Math.Abs(Lum(pl.Day) - Lum(dk2.Day));
        bool swatch = _lightPickRow?.Visible == true;
        try
        {
            using var bmp = new Bitmap(_tools.Width, _tools.Height);
            _tools.DrawToBitmap(bmp, new Rectangle(0, 0, _tools.Width, _tools.Height));
            bmp.Save(Path.Combine(_shotDir ?? Path.GetTempPath(), "window-light-pick.png"));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException) { AppLog.Error("dftlight: foto do painel", ex); }
        // trocar dia/noite: a área recarregada usa a pintura (não o arquivo)
        int ac0 = (int)MathF.Floor((at.X + _t.Width / 2) / TerrainFile.AreaM), ar0 = (int)MathF.Floor((_t.Height / 2 - at.Z) / TerrainFile.AreaM);
        bool keepsNight = _terrain.LightOverride?.Invoke(ar0, ac0, true) is { } ovn && ld.Pixels(ar0, ac0, true) is { } pn && ReferenceEquals(ovn.Px, pn.Px)
            && _terrain.LightOverride?.Invoke(ar0, ac0, false) is not null;
        _texStrength = _texForce.Value / 100f;
        bool ab = AbandonWorld();
        return load + $" | dftlight: antes {before?.Day:X6} depois {after?.Day:X6} mais escuro={darker}; Ctrl+Z={u} voltou igual={same}; Ctrl+Y={y} {(redone == after ? "igual" : "diferente")}; arquivos {files.Count} ({string.Join(",", files.Keys.Select(Path.GetFileName))}) tamanho igual={sizes} bytes mudados {diffBytes} de {total}; {string.Join(" / ", sum)}; borracha voltou ao arquivo={erasedOk}; teclas E/I na luz={eLight}/{iLight} E na textura={eTex}; conta-gotas pega sombra={pickDark} copiou a sombra={copyDark} amostra={swatch}; dia/noite usa a pintura={keepsNight}; abandonou={ab} sujo={WorldDirty} sem pintura depois={_terrain.LightOverride?.Invoke(ar0, ac0, false) is null}";
    }
}
