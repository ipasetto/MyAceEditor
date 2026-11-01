using MyAceEditor.PerfectWorld;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Edição dos objetos do cenário no Mundo 3D (tecla O): efeitos (.gfx) e sons do .ecwld — mover, clonar, apagar, Ctrl+Z e
/// Salvar cenário (backup + escrita atômica). Construções, modelos e árvores têm colisão no servidor (map.bht) e ficam
/// travados até o editor gerar a colisão de novo (docs/REGRAS-DO-JOGO.md, PW-KB §S32). Gravação: EcwldFile (idêntica sem mudança).
/// </summary>
internal sealed partial class WorldView
{
    EcwldFile? _ecw;            // .ecwld em edição (null = só leitura ou sem arquivo)
    WorldScene? _objScene;      // cena relida depois de cada mudança (o que O desenha e o clique escolhe)
    string? _ecwPath, _ecwWhy;  // arquivo; motivo de não editar
    byte[]? _ecwSaved;          // bytes do que está gravado (sem mudança = nada a salvar)
    sealed record SceneStep(byte[] Bytes, int Npc, int Quests);   // estado antes + profundidade das outras pilhas (ordem do Ctrl+Z)
    readonly List<SceneStep> _sUndo = new(), _sRedo = new();
    readonly Panel _objPanel = new() { Width = 390, Height = 196, BackColor = Color.FromArgb(28, 32, 38), Visible = false, Padding = new Padding(8) };
    readonly Label _objTitle = new() { AutoSize = false, Height = 40, Dock = DockStyle.Top, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9.5f) };
    readonly NumericUpDown[] _objXYZ = Enumerable.Range(0, 3).Select(_ => new NumericUpDown { Minimum = -100000, Maximum = 100000, DecimalPlaces = 1, Width = 92, Increment = 0.5m }).ToArray();
    bool _objBinding;

    WorldScene? ObjScene => _objScene ?? _trees.Scene;
    bool SceneDirty => _ecw is not null && _ecwSaved is not null && !_ecw.Write().AsSpan().SequenceEqual(_ecwSaved);

    /// <summary>Carrega o .ecwld para editar (na 1ª vez que O liga).</summary>
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
            _ecw = ef; _ecwSaved = bytes; _objScene = new WorldScene(bytes);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { _ecwWhy = ex.Message; }
    }

    void InitSceneEdit()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 58, ColumnCount = 3, RowCount = 2, BackColor = _objPanel.BackColor };
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
        bar.Controls.AddRange(new Control[] { B("world.objClone", "Clonar", "", CloneSceneObject), B("world.objDelete", "Apagar", "", DeleteSceneObject), B("world.objSave", "Salvar cenário", "", SaveScene, main: true) });
        var note = new Label { Dock = DockStyle.Top, Height = 44, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f),
            Text = Loc.T("world.objNote", "Efeitos e sons ficam só no cliente (sem colisão no servidor). Construções, modelos e árvores: travados até o editor gerar a colisão do servidor.") };
        _objPanel.Controls.Add(note); _objPanel.Controls.Add(bar); _objPanel.Controls.Add(grid); _objPanel.Controls.Add(_objTitle);
        Controls.Add(_objPanel);
        _objPanel.BringToFront();
        void Place() => _objPanel.Location = new Point(Math.Max(0, _gl.Right - _objPanel.Width - 10), _gl.Top + 10);
        _gl.Resize += (_, _) => Place(); Place();
        FormClosing += (_, e) =>
        {
            if (!SceneDirty) return;
            var r = MessageBox.Show(this, Loc.T("world.sceneAskSave", "O cenário (efeitos e sons) tem mudanças não salvas. Salvar antes de fechar?"), Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) e.Cancel = true;
            else if (r == DialogResult.Yes) SaveScene();
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
            _ => ("", 0f, 0f, 0f),
        };
        _objBinding = true;
        _objTitle.Text = name + (_ecw is null ? "\n" + (_ecwWhy ?? "") : "");
        _objXYZ[0].Value = (decimal)Math.Clamp(x, -100000, 100000); _objXYZ[1].Value = (decimal)Math.Clamp(y, -100000, 100000); _objXYZ[2].Value = (decimal)Math.Clamp(z, -100000, 100000);
        foreach (var n in _objXYZ) n.Enabled = _ecw is not null;
        _objBinding = false;
        _objPanel.Visible = true; _objPanel.BringToFront();
    }

    /// <summary>Aplica uma mudança ao .ecwld guardando o estado antes (Ctrl+Z) e relê a cena; mantém escolhido o objeto do ID dado.</summary>
    void SceneEdit(Func<EcwldFile, uint> change, string status)
    {
        if (_ecw is null) { _status.Text = _ecwWhy ?? Loc.T("world.noScene", "Este mapa não tem .ecwld no cliente."); return; }
        var before = _ecw.Write();
        uint keep = change(_ecw);
        _sUndo.Add(new SceneStep(before, UndoDepth?.Invoke() ?? 0, _qUndo.Count)); if (_sUndo.Count > 60) _sUndo.RemoveAt(0);
        _sRedo.Clear();
        Reselect(keep);
        _status.Text = status + Loc.T("world.sceneUnsaved", " — não salvo (Salvar cenário grava; Ctrl+Z desfaz)");
    }

    void Reselect(uint id)
    {
        _objScene = new WorldScene(_ecw!.Write());
        _sceneSel = (object?)_objScene.Effects.FirstOrDefault(e => e.Id == id) ?? _objScene.Sounds.FirstOrDefault(s => s.Id == id);
        ShowObjPanel(); _gl.Invalidate();
    }

    void ObjMovedByNumbers()
    {
        if (_objBinding || SelKey is not { } k) return;
        float x = (float)_objXYZ[0].Value, y = (float)_objXYZ[1].Value, z = (float)_objXYZ[2].Value;
        SceneEdit(f => { f.Move(k.Kind, k.Id, x, y, z); return k.Id; }, string.Format(Loc.T("world.objMoved", "Objeto do cenário movido para X {0:0.0} Z {1:0.0} altura {2:0.0}"), x, z, y));
    }

    void MoveSceneObjectTo(Vector3 at)
    {
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
        to.Add(new SceneStep(_ecw.Write(), top.Npc, top.Quests));
        _ecw = new EcwldFile(top.Bytes);
        uint id = SelKey?.Id ?? 0;
        Reselect(id);
        _status.Text = redo ? Loc.T("world.sceneRedone", "Cenário: refeito.") : Loc.T("world.sceneUndone", "Cenário: desfeito (Ctrl+Y refaz).");
        return true;
    }

    /// <summary>
    /// Teste do --world-shot (foto "obj…"): liga O, escolhe o efeito mais perto da câmera, clona, move o clone, desfaz tudo e
    /// confere que o .ecwld em memória voltou ao gravado. Não grava nada no disco. Devolve o relatório.
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
        _sceneSel = fx; ShowObjPanel();
        return $"cenário: efeito {fx.Gfx} (ID {fx.Id}) · clonar {n0}→{n1} (ID novo {clone?.Id}) · mover X {clone?.X:0.0}→{moved?.X:0.0} · Ctrl+Z×2 {undo1 && undo2} · voltou ao gravado {same} · Ctrl+Y {redo} ({n2} efeitos) · sujo no fim {SceneDirty}";
    }

    void SaveScene()
    {
        if (_ecw is null || _ecwPath is null) return;
        var bytes = _ecw.Write();
        if (_ecwSaved is not null && bytes.AsSpan().SequenceEqual(_ecwSaved)) { _status.Text = Loc.T("world.sceneNothing", "Cenário: nada a salvar."); return; }
        try
        {
            // regra: o que vai para o disco tem que ser relido igual (formato entendido)
            var check = new WorldScene(bytes);
            if (check.BlocksExact != check.BlocksTotal) throw new InvalidDataException(Loc.T("world.sceneBad", "a releitura não bateu; nada foi gravado"));
            AtomicFileSave.Write(_ecwPath, bytes);   // backup fora do cliente (BackupStore)
            _ecwSaved = bytes;
            _status.Text = string.Format(Loc.T("world.sceneSaved", "Cenário salvo em {0} (backup feito). Só o cliente muda; não precisa enviar ao servidor."), Path.GetFileName(_ecwPath));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FileSaveException)
        { MessageBox.Show(this, ex.Message, Loc.T("world.objSave", "Salvar cenário"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
