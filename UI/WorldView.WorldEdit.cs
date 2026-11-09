using MyAceEditor.PerfectWorld;
using MyAceEditor.PerfectWorld.WorldEdit;

namespace MyAceEditor.UI;

/// <summary>
/// Edição de Mundo (usuário 09/11): Objetos do mundo e Terreno viraram UMA ferramenta com duas pílulas no topo
/// (Objetos | Terreno; a grama e as flores ficam na pílula Terreno). Trocar de pílula não pergunta nada; o Salvar é um só
/// (objetos e grama primeiro, depois terreno e água, que releem o .ecwld/.ecbsd já gravados); Esc/voltar/fechar
/// perguntam uma vez só; Ctrl+Z desfaz a última ação, seja de qual pílula for. Por dentro continuam Tool.Scene (Objetos)
/// e Tool.Terrain (Terreno).
/// </summary>
internal sealed partial class WorldView
{
    Tool _worldPill = Tool.Scene;   // pílula da última vez
    bool WorldEditOpen => _tool is Tool.Scene or Tool.Terrain;
    bool WorldDirty => SceneDirty || TerrainDirty;
    readonly List<Button> _pillButtons = new();

    /// <summary>Pílulas Objetos | Terreno no topo das duas páginas.</summary>
    Control WorldPills()
    {
        var row = new FlowLayoutPanel { Width = 250, Height = 40, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
        foreach (var (t, glyph, key, pt) in new[] { (Tool.Scene, "", "world.pillObjects", "Objetos"), (Tool.Terrain, "", "world.pillTerrain", "Terreno") })
        {
            var b = new NoFocusButton { Width = 121, Height = 34, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 6, 0),
                Text = "  " + Loc.T(key, pt), Image = UiKit.Icon(glyph, Color.White, 16), TextImageRelation = TextImageRelation.ImageBeforeText, Tag = t };
            b.Click += (_, _) => { SwitchWorldPill(t); _gl.Focus(); };
            _pillButtons.Add(b); row.Controls.Add(b);
        }
        return row;
    }

    void PaintPills()
    {
        foreach (var b in _pillButtons)
        {
            bool on = b.Tag is Tool t && t == _tool;
            b.BackColor = on ? UiKit.Accent : Color.FromArgb(40, 45, 54);
            b.FlatAppearance.BorderColor = on ? UiKit.Accent : Color.FromArgb(62, 70, 84);
        }
    }

    /// <summary>Troca de pílula: o que está em andamento na outra (pincel, inserção, conta-gotas) para; nada se perde.</summary>
    void SwitchWorldPill(Tool t)
    {
        if (_tool == t || !WorldEditOpen) return;
        StopPlanting(); StopDropper();
        if (_tool == Tool.Scene) SetSceneObjects(false);
        if (_tool == Tool.Terrain)
        {
            if (_texDown) TerrainMouseUp();
            _texActive = false; _sculpt = -1; _waterMode = 0;
            StopBrush();
        }
        OpenTool(t);
    }

    /// <summary>Uma pergunta só para objetos, grama, terreno e água. Falso = o usuário quis ficar.</summary>
    bool AbandonWorld()
    {
        if (!WorldDirty) { _texActive = false; return true; }
        if (MessageBox.Show(this, Loc.T("world.abandonAsk", "Há mudanças não salvas na Edição de Mundo (objetos, terreno, água, grama). Abandonar e perder essas mudanças?"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        if (SceneDirty) DiscardObjects();
        if (TerrainDirty) ResetTerrainEdit();
        _wUndo.Clear(); _wRedo.Clear();
        return true;
    }

    /// <summary>
    /// Salvar mundo: objetos e grama (com a barra e a pergunta deles), depois terreno e água; uma janela de resumo no fim.
    /// <paramref name="wait"/> = tudo na hora (fechar a janela).
    /// </summary>
    bool SaveWorld(bool interactive = true, bool wait = false)
    {
        bool obj = SceneDirty, ter = TerrainDirty;
        _objSaveThen = null;
        if (!obj && !ter) { _status.Text = Loc.T("obj.nothing", "Nada para salvar."); return false; }
        if (!obj) return SaveTerrain(interactive: interactive);
        bool chained = false, chainOk = true;
        if (ter) _objSaveThen = (res, server) => { chained = true; chainOk = SaveTerrain(res.Message, server, interactive); };
        bool async = !wait && (_draft?.Dirty == true || _grassDraft?.Dirty == true);
        bool ok = SaveScene(interactive, wait);
        if (async) return ok;                  // o terreno vem depois, quando os objetos terminarem (AfterObjectsWrite)
        if (!ok) { _objSaveThen = null; return false; }
        if (ter && !chained) { _objSaveThen = null; return SaveTerrain(interactive: interactive); }
        return chainOk;
    }

    /// <summary>Depois dos objetos gravados: o que o Salvar mundo ainda tem para fazer (o terreno). null = só os objetos.</summary>
    Action<SaveResult, bool>? _objSaveThen;

    // ---- Ctrl+Z em ordem entre as pílulas: T = terreno/água, G = grama, O = objetos, S = cenário (efeitos, sons, bichos) ----
    readonly List<char> _wUndo = new(), _wRedo = new();

    void WorldDid(char kind)
    {
        _wUndo.Add(kind); if (_wUndo.Count > 400) _wUndo.RemoveAt(0);
        _wRedo.Clear();
    }

    /// <summary>Desfaz/refaz a última ação da Edição de Mundo, seja de qual pílula for. Falso = nada para fazer aqui.</summary>
    bool WorldUndo(bool redo)
    {
        if (!WorldEditOpen) return false;
        var from = redo ? _wRedo : _wUndo; var to = redo ? _wUndo : _wRedo;
        while (from.Count > 0)
        {
            char k = from[^1]; from.RemoveAt(from.Count - 1);
            bool ok = k switch
            {
                'T' => TerrainUndoCore(redo),
                'G' => GrassUndoCore(redo),
                'O' => ObjectUndo(redo),
                'S' => SceneUndo(redo),
                _ => false,
            };
            if (ok) { to.Add(k); return true; }   // falhou = passo velho (salvo ou abandonado): sai e tenta o anterior
        }
        return false;
    }

    /// <summary>
    /// Para a captura ("dftworld"): pincelada de altura na pílula Terreno, troca para Objetos e ergue a construção mais
    /// perto; Ctrl+Z desfaz a construção, depois a pincelada (ordem das ações); Ctrl+Y refaz a pincelada; abandonar limpa
    /// as duas partes de uma vez. Não grava nada.
    /// </summary>
    internal string WorldEditTest()
    {
        string load = DraftLoadTest();
        var r = new List<string>();
        SwitchWorldPill(Tool.Terrain);
        var at = new System.Numerics.Vector3(_cam.X + Forward.X * 20, 0, -(_cam.Z + Forward.Z * 20)); at.Y = _t.HeightAt(at.X, at.Z) ?? 0;
        float h0 = _t.HeightAt(at.X, at.Z) ?? 0;
        _sculpt = 0; TerrainDraftOrNew().BeginStroke(); _texDown = true; _texLast = null; TexDab(at); TerrainMouseUp();
        float h1 = _t.HeightAt(at.X, at.Z) ?? 0;
        r.Add($"pincelada {h0:0.00}->{h1:0.00} terreno-sujo={TerrainDirty}");
        SwitchWorldPill(Tool.Scene);
        bool kept = TerrainDirty;
        string moved = "-";
        if (_buildings.Nearest(_cam) is { } near && _draft?.Find(near.File) is { } o)
            moved = DraftApply(new MoveOp(o.Key, o.Original with { Pos = o.Original.Pos + new System.Numerics.Vector3(0, 5, 0) })) ?? "ok";
        r.Add($"troca de pílula manteve o terreno={kept}; construção erguida={moved}; pílula={_tool}");
        bool u1 = UndoAny(); bool objBack = _draft?.Dirty != true; float hA = _t.HeightAt(at.X, at.Z) ?? 0;
        bool u2 = UndoAny(); float hB = _t.HeightAt(at.X, at.Z) ?? 0;
        r.Add($"Ctrl+Z 1: {u1} objetos voltaram={objBack} altura ainda {hA:0.00}; Ctrl+Z 2: {u2} altura {hB:0.00}");
        bool y1 = RedoAny(); float hC = _t.HeightAt(at.X, at.Z) ?? 0;
        r.Add($"Ctrl+Y: {y1} altura {hC:0.00}");
        bool ab = AbandonWorld();
        r.Add($"abandonar={ab} sujo depois={WorldDirty} altura {(_t.HeightAt(at.X, at.Z) ?? 0):0.00}");
        return load + " | dftworld: " + string.Join("; ", r);
    }
}
