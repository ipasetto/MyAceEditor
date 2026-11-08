using MyAceEditor.PerfectWorld.WorldEdit;
using MyAceEditor.PerfectWorld;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Edição dos objetos do cenário no Mundo 3D (tecla O): efeitos (.gfx) e sons do .ecwld (mover, clonar, apagar) e grupos de
/// bichos de enfeite do .ecbsd (mover: o centro fica na struct, mesmo tamanho), com Ctrl+Z e Salvar cenário (backup + escrita
/// atômica). Construções, modelos e árvores têm colisão no servidor (map.bht) e ficam travados até o editor gerar a colisão
/// de novo (docs/REGRAS-DO-JOGO.md, PW-KB §S32). Gravação do .ecwld: EcwldFile (idêntica sem mudança).
/// </summary>
internal sealed partial class WorldView
{
    EcwldFile? _ecw;            // .ecwld em edição (null = só leitura ou sem arquivo)
    byte[]? _bsd;               // .ecbsd em edição (bichos: centro trocado no lugar)
    WorldScene? _objScene;      // cena relida depois de cada mudança (o que O desenha e o clique escolhe)
    string? _ecwPath, _bsdPath, _ecwWhy;   // arquivos; motivo de não editar
    byte[]? _ecwSaved, _bsdSaved;          // o que está gravado (sem mudança = nada a salvar)
    // estado antes (os dois arquivos) + profundidade das outras pilhas (ordem do Ctrl+Z); cada passo guarda ~3+3 MB no mundo
    sealed record SceneStep(byte[] Ecwld, byte[]? Bsd, int Npc, int Quests, Dictionary<string, Vector3>? Bld = null);
    /// <summary>Construção escolhida: .bmd no litmodels.pck e centro original (espaço do jogo).</summary>
    sealed record BuildingSel(string File, Vector3 Center);
    const int MaxSceneSteps = 30;
    readonly List<SceneStep> _sUndo = new(), _sRedo = new();
    readonly Panel _objPanel = new() { Width = 225, Height = 250, BackColor = Color.FromArgb(28, 32, 38), Visible = false, Padding = new Padding(4) };
    readonly Label _objTitle = new() { AutoSize = false, Height = 96, Dock = DockStyle.Top, ForeColor = Color.White, Font = new Font("Segoe UI", 8.5f) };
    readonly NumericUpDown[] _objXYZ = Enumerable.Range(0, 3).Select(_ => new NumericUpDown { Minimum = -100000, Maximum = 100000, DecimalPlaces = 1, Width = 64, Increment = 0.5m }).ToArray();
    Button? _objCloneBtn, _objDeleteBtn;
    bool _objBinding;

    WorldScene? ObjScene => _objScene ?? _trees.Scene;

    /// <summary>Cena do .ecwld + bichos e curvas do .ecbsd para o O desenhar.</summary>
    WorldScene ScanScene(byte[] ecwld)
    {
        var sc = new WorldScene(ecwld);
        if (_bsd is not null) sc.LoadCrittersAndCurves(_bsd);
        else if (_ecwPath is not null) sc.LoadCrittersAndCurves(Path.ChangeExtension(_ecwPath, ".ecbsd"));
        return sc;
    }

    bool SceneDirty => _draft?.Dirty == true || _ecw is not null && _ecwSaved is not null && (!_ecw.Write().AsSpan().SequenceEqual(_ecwSaved)
        || _bsd is not null && _bsdSaved is not null && !_bsd.AsSpan().SequenceEqual(_bsdSaved));

    /// <summary>Carrega o .ecwld (e o .ecbsd) para editar (na 1ª vez que O liga).</summary>
    void LoadSceneForEdit()
    {
        if (_ecw is not null || _ecwWhy is not null || _element is null) return;
        _ecwPath = Path.Combine(_element, "maps", _t.Name, _t.Name + ".ecwld");
        if (!File.Exists(_ecwPath)) { _ecwWhy = Loc.T("world.noScene", "Este mapa não tem .ecwld no cliente."); return; }
        try
        {
            var bytes = File.ReadAllBytes(_ecwPath);
            var ef = new EcwldFile(bytes);
            // regra: só edita formato que regrava idêntico
            if (!ef.Write().AsSpan().SequenceEqual(bytes)) { _ecwWhy = Loc.T("world.sceneNotSame", "Este .ecwld não regrava idêntico: edição desligada (só ver)."); return; }
            _ecw = ef; _ecwSaved = bytes;
            _bsdPath = Path.ChangeExtension(_ecwPath, ".ecbsd");
            if (File.Exists(_bsdPath)) { _bsd = File.ReadAllBytes(_bsdPath); _bsdSaved = (byte[])_bsd.Clone(); } else _bsdPath = null;
            _objScene = ScanScene(bytes);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { _ecwWhy = ex.Message; }
    }

    void InitSceneEdit()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 58, ColumnCount = 3, Width = 215, Margin = new Padding(0), RowCount = 2, BackColor = _objPanel.BackColor };
        string[] heads = { "X", Loc.T("world.objHeight", "altura"), "Z" };
        for (int i = 0; i < 3; i++)
        {
            grid.Controls.Add(new Label { Text = heads[i], ForeColor = Color.Silver, AutoSize = true }, i, 0);
            grid.Controls.Add(_objXYZ[i], i, 1);
            _objXYZ[i].ValueChanged += (_, _) => ObjMovedByNumbers();
        }
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = _objPanel.BackColor, Padding = new Padding(0, 4, 0, 0) };
        // tela escura: ícones brancos desenhados (UiKit.Icon), como o painel de quests do 3D
        Button B(string key, string pt, string glyph, Action a, bool main = false)
        {
            var b = new Button { AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = main ? UiKit.Accent : Color.FromArgb(44, 50, 60),
                Image = UiKit.Icon(glyph, Color.White, 16), TextImageRelation = TextImageRelation.ImageBeforeText, Padding = new Padding(4, 0, 4, 0), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = main ? UiKit.Accent : Color.FromArgb(70, 78, 90);
            b.Text = Loc.T(key, pt); b.Click += (_, _) => { a(); _gl.Focus(); }; return b;
        }
        _objCloneBtn = B("world.objClone", "Clonar", "", CloneSceneObject);
        _objDeleteBtn = B("world.objDelete", "Apagar", "", DeleteSceneObject);
        bar.Controls.AddRange(new Control[] { _objCloneBtn, _objDeleteBtn });
        var note = new Label { Dock = DockStyle.Top, Height = 60, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f),
            Text = Loc.T("world.objNote4", "Efeitos, sons e bichos: só no cliente. Construções: Salvar grava o cliente (litmodels.pck) e a colisão do servidor (map.bht).") };
        _objPanel.Controls.Add(note); _objPanel.Controls.Add(bar); InitObjectCard(); _objPanel.Controls.Add(grid); _objPanel.Controls.Add(_objTitle);
        // o painel do objeto escolhido mora na ferramenta Editar cenário do menu flutuante (WorldView.Tools.cs)
        FormClosing += (_, e) =>
        {
            if (!SceneDirty) return;
            var r = MessageBox.Show(this, Loc.T("obj.askSave", "Há mudanças não salvas nos objetos do mundo (construções, modelos, efeitos, sons ou bichos). Salvar antes de fechar?"), Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            e.Cancel = CancelClose(r, interactive: true);
        };
    }

    (EcwldFile.Kind Kind, uint Id)? SelKey => _sceneSel switch
    {
        WorldScene.SceneEffect fx => (EcwldFile.Kind.Effect, fx.Id),
        WorldScene.SceneSound sd => (EcwldFile.Kind.Sound, sd.Id),
        _ => null,
    };

    /// <summary>Mostra/esconde o painel do objeto escolhido (com as coordenadas atuais).</summary>
    void ShowObjPanel()
    {
        if (_sceneSel is null) { _objPanel.Visible = false; return; }
        var (name, x, y, z) = _sceneSel switch
        {
            WorldScene.SceneEffect fx => (Loc.T("world.objEffect", "Efeito") + ": " + fx.Gfx, fx.X, fx.Y, fx.Z),
            WorldScene.SceneSound sd => (Loc.T("world.objSound", "Som") + ": " + sd.File, sd.X, sd.Y, sd.Z),
            WorldScene.Critter cr => (Loc.T("world.objCritter", "Bichos") + ": " + cr.Model, cr.X, cr.Y, cr.Z),
            BuildingSel or ModelSel or TreeSel when SelDraft is { } so => (CardText(so), CenterOf(so).X, CenterOf(so).Y, CenterOf(so).Z),
            BuildingSel b => (Loc.T("world.objBuilding", "Construção") + ": " + Path.GetFileName(b.File), b.Center.X, b.Center.Y, b.Center.Z),
            _ => ("", 0f, 0f, 0f),
        };
        bool critter = _sceneSel is WorldScene.Critter;
        bool drafted = SelDraft is not null;
        _objBinding = true;
        _objTitle.Text = name + (drafted ? "" : _ecw is null ? "\n" + (_ecwWhy ?? "") : _sceneSel is BuildingSel ? "\n" + Loc.T("obj.waitPiece", "carregando os objetos deste pedaço…")
            : critter ? "\n" + Loc.T("world.critterMoveOnly", "bichos: só mover por enquanto") : "");
        _objXYZ[0].Value = (decimal)Math.Clamp(x, -100000, 100000); _objXYZ[1].Value = (decimal)Math.Clamp(y, -100000, 100000); _objXYZ[2].Value = (decimal)Math.Clamp(z, -100000, 100000);
        foreach (var n in _objXYZ) n.Enabled = drafted ? SelDraft is { ReadOnlyWhy: null, Deleted: false } : _sceneSel is not BuildingSel && _ecw is not null && (!critter || _bsd is not null);
        _objYaw.Enabled = SelDraft is { ReadOnlyWhy: null, Deleted: false };
        if (SelDraft is { } sy) _objYaw.Value = (decimal)Math.Round(YawOf(sy.Now));
        if (_objCloneBtn is not null) _objCloneBtn.Visible = false;
        if (_objCloneBtn is not null) _objCloneBtn.Enabled = !critter && !drafted && _sceneSel is not BuildingSel;
        if (_objDeleteBtn is not null) _objDeleteBtn.Enabled = drafted ? SelDraft is { ReadOnlyWhy: null, Deleted: false } : !critter && _sceneSel is not BuildingSel;
        _objBinding = false;
        FitObjPanel();
        _objPanel.Visible = true;
        UpdateSideState();
    }

    /// <summary>
    /// Aplica uma mudança guardando o estado antes (Ctrl+Z) e relê a cena. change devolve o ID a manter escolhido
    /// (efeito/som) ou 0; bichos são achados de novo pelo offset.
    /// </summary>
    void SceneEdit(Func<EcwldFile, uint> change, string status)
    {
        if (_ecw is null) { _status.Text = _ecwWhy ?? Loc.T("world.noScene", "Este mapa não tem .ecwld no cliente."); return; }
        var step = new SceneStep(_ecw.Write(), (byte[]?)_bsd?.Clone(), UndoDepth?.Invoke() ?? 0, _qUndo.Count, new(_buildings.Offsets, StringComparer.OrdinalIgnoreCase));
        int critterAt = (_sceneSel as WorldScene.Critter)?.Offset ?? -1;
        uint keep = change(_ecw);
        _sUndo.Add(step); if (_sUndo.Count > MaxSceneSteps) _sUndo.RemoveAt(0);
        _sRedo.Clear();
        Reselect(keep, critterAt);
        UpdateSideState();
        _status.Text = status + Loc.T("world.sceneUnsaved", " — não salvo (Salvar cenário grava; Ctrl+Z desfaz)");
    }

    void Reselect(uint id, int critterAt = -1)
    {
        _objScene = ScanScene(_ecw!.Write());
        if (_sceneSel is BuildingSel) { ShowObjPanel(); _gl.Invalidate(); return; }
        _sceneSel = critterAt >= 0 ? _objScene.Critters.FirstOrDefault(c => c.Offset == critterAt)
            : (object?)_objScene.Effects.FirstOrDefault(e => e.Id == id) ?? _objScene.Sounds.FirstOrDefault(s => s.Id == id);
        ShowObjPanel(); _gl.Invalidate();
    }

    /// <summary>Centro do grupo de bichos (ECBSDFILECRITTER5 +4) trocado no lugar do .ecbsd.</summary>
    void MoveCritter(WorldScene.Critter c, float x, float y, float z)
    {
        if (_bsd is null) return;
        SceneEdit(_ =>
        {
            BitConverter.GetBytes(x).CopyTo(_bsd, c.Offset + 4); BitConverter.GetBytes(y).CopyTo(_bsd, c.Offset + 8); BitConverter.GetBytes(z).CopyTo(_bsd, c.Offset + 12);
            return 0;
        }, string.Format(Loc.T("world.objMoved", "Objeto do cenário movido para X {0:0.0} Z {1:0.0} altura {2:0.0}"), x, z, y));
    }

    /// <summary>Deslocamento de agora da construção (espaço do OpenGL), vindo do rascunho.</summary>
    Vector3 BldOff(BuildingSel b)
    {
        if (_draft?.Find(b.File) is not { } o || o.Deleted) return Vector3.Zero;
        var d = o.Now.Pos - o.Original.Pos;
        return new Vector3(d.X, d.Y, -d.Z);
    }

    /// <summary>
    /// Construção: só o desenho anda (deslocamento guardado no 3D). Gravar = .bmd no litmodels.pck + ornamento no .ecbsd +
    /// colisão do servidor refeita, tudo no Salvar — a colisão ainda não está pronta, então o Salvar avisa e mantém aqui.
    /// </summary>
    void MoveBuilding(BuildingSel b, float x, float y, float z)
    {
        // b.Center = centro do desenho no lugar ORIGINAL (jogo); a posição nova soma o deslocamento ao cabeçalho original
        if (_draft?.Find(b.File) is not { } o) { _status.Text = Loc.T("obj.notLoaded", "Esta construção ainda está carregando; tente de novo em instantes."); return; }
        var to = o.Original with { Pos = o.Original.Pos + new System.Numerics.Vector3(x - b.Center.X, y - b.Center.Y, z - b.Center.Z) };
        if (DraftApply(new MoveOp(b.File, to)) is null)
            _status.Text = string.Format(Loc.T("world.bldMoved", "Construção movida no 3D para X {0:0.0} Z {1:0.0} altura {2:0.0} (ainda não gravada)"), x, z, y);
        ShowObjPanel(); _gl.Invalidate();
    }

    void ObjMovedByNumbers()
    {
        if (_objBinding) return;
        float x = (float)_objXYZ[0].Value, y = (float)_objXYZ[1].Value, z = (float)_objXYZ[2].Value;
        if (_sceneSel is WorldScene.Critter c) { MoveCritter(c, x, y, z); return; }
        if (SelDraft is not null) { MoveSelectedCenter(x, y, z); return; }
        if (SelKey is not { } k) return;
        SceneEdit(f => { f.Move(k.Kind, k.Id, x, y, z); return k.Id; }, string.Format(Loc.T("world.objMoved", "Objeto do cenário movido para X {0:0.0} Z {1:0.0} altura {2:0.0}"), x, z, y));
    }

    void MoveSceneObjectTo(Vector3 at)
    {
        if (_sceneSel is WorldScene.Critter c) { MoveCritter(c, at.X, at.Y + 1, at.Z); return; }
        if (SelDraft is { } sd) { var c0 = CenterOf(sd); MoveSelectedCenter(at.X, at.Y + (c0.Y - sd.Now.Pos.Y), at.Z); return; }
        if (SelKey is not { } k) return;
        SceneEdit(f => { f.Move(k.Kind, k.Id, at.X, at.Y + (k.Kind == EcwldFile.Kind.Effect ? 0.5f : 0), at.Z); return k.Id; },
            string.Format(Loc.T("world.objMoved", "Objeto do cenário movido para X {0:0.0} Z {1:0.0} altura {2:0.0}"), at.X, at.Z, at.Y));
    }

    void CloneSceneObject()
    {
        if (SelKey is not { } k) return;
        SceneEdit(f => f.Clone(k.Kind, k.Id, 3, 3), Loc.T("world.objCloned", "Objeto do cenário clonado (3 m ao lado)"));
    }

    void DeleteSceneObject()
    {
        if (SelDraft is not null) { DeleteSelected(); return; }
        if (SelKey is not { } k) return;
        SceneEdit(f => { f.Delete(k.Kind, k.Id); return 0; }, Loc.T("world.objDeleted", "Objeto do cenário apagado"));
    }

    /// <summary>Ctrl+Z/Ctrl+Y do cenário: só quando a última ação de todas foi no cenário (as outras pilhas não andaram depois).</summary>
    bool SceneUndo(bool redo)
    {
        var from = redo ? _sRedo : _sUndo; var to = redo ? _sUndo : _sRedo;
        if (_ecw is null || from.Count == 0) return false;
        var top = from[^1];
        if (top.Npc != (UndoDepth?.Invoke() ?? 0) || top.Quests != _qUndo.Count) return false;
        from.RemoveAt(from.Count - 1);
        to.Add(new SceneStep(_ecw.Write(), (byte[]?)_bsd?.Clone(), top.Npc, top.Quests, new(_buildings.Offsets, StringComparer.OrdinalIgnoreCase)));
        _ecw = new EcwldFile(top.Ecwld);
        if (top.Bsd is not null) _bsd = top.Bsd;
        if (top.Bld is not null) { _buildings.Offsets.Clear(); foreach (var (k, v) in top.Bld) _buildings.Offsets[k] = v; }
        uint id = SelKey?.Id ?? 0;
        Reselect(id, (_sceneSel as WorldScene.Critter)?.Offset ?? -1);
        UpdateSideState();
        _status.Text = redo ? Loc.T("world.sceneRedone", "Cenário: refeito.") : Loc.T("world.sceneUndone", "Cenário: desfeito (Ctrl+Y refaz).");
        return true;
    }

    /// <summary>
    /// Teste do --world-shot (foto "obj…"): liga O; efeito mais perto da câmera: clona, move, desfaz tudo; bicho mais perto:
    /// move e desfaz; confere que os dois arquivos em memória voltaram ao gravado. Não grava nada no disco.
    /// </summary>
    internal string SceneSelfTest()
    {
        _sceneObjs = true; LoadSceneForEdit();
        if (_ecw is null || ObjScene is not { } sc) return "cenário: sem edição (" + (_ecwWhy ?? "sem .ecwld") + ")";
        var fx = sc.Effects.OrderBy(e => (Gl(e.X, e.Y, e.Z) - _cam).LengthSquared).FirstOrDefault();
        if (fx is null) return "cenário: mapa sem efeitos";
        _sceneSel = fx; ShowObjPanel();
        int n0 = sc.Effects.Count;
        CloneSceneObject();
        int n1 = ObjScene!.Effects.Count; var clone = _sceneSel as WorldScene.SceneEffect;
        _objXYZ[0].Value += 2;   // mover pelos números (passo do Ctrl+Z separado)
        var moved = _sceneSel as WorldScene.SceneEffect;
        bool undo1 = SceneUndo(false), undo2 = SceneUndo(false);
        bool same = _ecw.Write().AsSpan().SequenceEqual(_ecwSaved);
        bool redo = SceneUndo(true);
        int n2 = ObjScene!.Effects.Count;
        SceneUndo(false);
        string crit = "sem bichos";
        if (ObjScene!.Critters.OrderBy(c => (Gl(c.X, c.Y, c.Z) - _cam).LengthSquared).FirstOrDefault() is { } cr)
        {
            _sceneSel = cr; ShowObjPanel();
            _objXYZ[2].Value += 4;
            var cm = _sceneSel as WorldScene.Critter;
            bool cu = SceneUndo(false);
            bool cSame = _bsd is not null && _bsdSaved is not null && _bsd.AsSpan().SequenceEqual(_bsdSaved);
            crit = $"bicho {cr.Model}: Z {cr.Z:0.0}→{cm?.Z:0.0}, Ctrl+Z {cu}, .ecbsd voltou ao gravado {cSame}";
        }
        string bld = "sem construções carregadas";
        DraftLoadTest();   // espera o rascunho ler os pedaços em volta (segundo plano)
        if (_buildings.Nearest(_cam) is { } nb)
        {
            _sceneSel = new BuildingSel(nb.File, nb.Center); ShowObjPanel();
            _objXYZ[0].Value += 3;
            int pend = _draft?.Pieces.Values.Sum(p => p.Objects.Count(o => o.Changed)) ?? 0; bool dirty = SceneDirty;
            bool bu = ObjectUndo(false);
            bld = $"construção {Path.GetFileName(nb.File)}: mover pendente {pend} (sujo {dirty}), Ctrl+Z {bu}, pendentes depois {_draft?.Pieces.Values.Sum(p => p.Objects.Count(o => o.Changed)) ?? 0}";
        }
        _sceneSel = fx; ShowObjPanel();
        return $"cenário: {bld} · {sc.Critters.Count} grupos de bichos, {sc.Curves.Count} curvas · efeito {fx.Gfx} (ID {fx.Id}) · clonar {n0}→{n1} (ID novo {clone?.Id}) · mover X {clone?.X:0.0}→{moved?.X:0.0} · Ctrl+Z×2 {undo1 && undo2} · voltou ao gravado {same} · Ctrl+Y {redo} ({n2} efeitos) · {crit} · sujo no fim {SceneDirty}";
    }

    /// <summary>Teste do Salvar (foto "objsave…", só numa CÓPIA do mapa): move o 1º efeito +1 m em X, salva e relê do disco.</summary>
    internal string SceneSaveTest()
    {
        _sceneObjs = true; LoadSceneForEdit();
        if (_ecw is null || _ecwPath is null || ObjScene is not { Effects.Count: > 0 } sc) return "salvar: sem edição ou sem efeitos";
        var fx = sc.Effects[0]; _sceneSel = fx; ShowObjPanel();
        _objXYZ[0].Value += 1;
        SaveScene();
        var disk = new WorldScene(File.ReadAllBytes(_ecwPath));
        var back = disk.Effects.FirstOrDefault(e => e.Id == fx.Id);
        return $"salvar: efeito {fx.Id} X {fx.X:0.0} → no disco {back?.X:0.0} · blocos {disk.BlocksExact}/{disk.BlocksTotal} · sujo depois {SceneDirty} · status: {_status.Text}";
    }

    /// <summary>Resposta da pergunta ao fechar → cancelar o fechamento?</summary>
    bool CancelClose(DialogResult answer, bool interactive)
    {
        if (answer == DialogResult.Cancel) return true;
        if (answer == DialogResult.Yes) return !SaveScene(interactive);   // salvar recusado/falhou: fica aberto, nada se perde
        return false;
    }

    bool SaveScene(bool interactive = true)
    {
        if (_draft?.Dirty == true) return SaveObjects(interactive);
        if (_ecw is null || _ecwPath is null) return false;
        // construções primeiro: gravam litmodels.pck + map.bht e mexem no ornamento do .ecbsd (gravado logo abaixo)
        bool built = false;
        if (_buildings.Offsets.Count > 0) { if (!SaveBuildings()) { UpdateSideState(); return false; } built = true; }
        var bytes = _ecw.Write();
        bool ecwChanged = _ecwSaved is null || !bytes.AsSpan().SequenceEqual(_ecwSaved);
        bool bsdChanged = _bsd is not null && _bsdSaved is not null && !_bsd.AsSpan().SequenceEqual(_bsdSaved);
        if (built && !ecwChanged && !bsdChanged) { UpdateSideState(); return false; }
        if (!ecwChanged && !bsdChanged) { _status.Text = Loc.T("world.sceneNothing", "Cenário: nada a salvar."); UpdateSideState(); return false; }
        try
        {
            // regra: o que vai para o disco tem que ser relido igual (formato entendido)
            var check = new WorldScene(bytes);
            if (check.BlocksExact != check.BlocksTotal) throw new InvalidDataException(Loc.T("world.sceneBad", "a releitura não bateu; nada foi gravado"));
            if (bsdChanged)
            {
                check.LoadCrittersAndCurves(_bsd!);
                if (check.Critters.Count != (_objScene?.Critters.Count ?? check.Critters.Count)) throw new InvalidDataException(Loc.T("world.sceneBad", "a releitura não bateu; nada foi gravado"));
            }
            if (ecwChanged) { AtomicFileSave.Write(_ecwPath, bytes); _ecwSaved = bytes; }   // backup fora do cliente (BackupStore)
            if (bsdChanged && _bsdPath is not null) { AtomicFileSave.Write(_bsdPath, _bsd!); _bsdSaved = (byte[])_bsd!.Clone(); }
            UpdateSideState();
            if (interactive) BeginInvoke(() => SavedDialog.Show(this, Loc.T("world.sceneSavedShort", "Cenário salvo. Só o cliente muda: feche e abra o jogo para ver.")));
            _status.Text = string.Format(Loc.T("world.sceneSaved", "Cenário salvo em {0} (backup feito). Só o cliente muda; não precisa enviar ao servidor."),
                string.Join(" + ", new[] { ecwChanged ? Path.GetFileName(_ecwPath) : null, bsdChanged ? Path.GetFileName(_bsdPath) : null }.Where(s => s is not null)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FileSaveException)
        { if (interactive) MessageBox.Show(this, ex.Message, Loc.T("world.objSave", "Salvar cenário"), MessageBoxButtons.OK, MessageBoxIcon.Warning); _status.Text = ex.Message; return false; }
        return true;
    }
}
