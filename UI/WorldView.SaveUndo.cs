using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Ctrl+Z depois de salvar (usuário 09/11: "ctrl + z sumindo após salvar, não consigo voltar nele"): o Salvar relê tudo do
/// disco e o histórico dos passos acaba ali; voltar passo a passo por cima dele não é seguro (as cópias ganham arquivo e
/// número novos no jogo). Então, com a Edição de Mundo sem nada por salvar, Ctrl+Z pergunta e volta o ÚLTIMO SALVAR
/// inteiro (os arquivos que ele gravou, com as cópias de segurança tiradas logo antes dele), e Ctrl+Y refaz. Antes de
/// voltar guarda o estado atual, para o Ctrl+Y ter de onde trazer.
/// </summary>
internal sealed partial class WorldView
{
    readonly List<BackupSet> _saveUndo = new(), _saveRedo = new();
    int _saveSeq, _saveSeqRecorded = -1;
    /// <summary>Só testes: resposta da pergunta do desfazer o Salvar (headless não mostra janela).</summary>
    internal bool SaveUndoAnswer;

    void InitSaveUndo()
    {
        BackupSets.Saved += OnSetSaved;
        FormClosed += (_, _) => BackupSets.Saved -= OnSetSaved;
    }

    /// <summary>Começo de um Salvar mundo: os conjuntos que chegarem até o próximo viram um passo só.</summary>
    void BeginSaveStep() => _saveSeq++;

    void OnSetSaved(BackupSet s)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { if (InvokeRequired) BeginInvoke(() => RecordSave(s)); else RecordSave(s); }
        catch (InvalidOperationException) { }   // janela fechando
    }

    void RecordSave(BackupSet s)
    {
        if (IsDisposed || !s.Map.Equals(_t.Name, StringComparison.OrdinalIgnoreCase) || s.Tool is not ("objects" or "terrain" or "scene")) return;
        if (_saveSeqRecorded == _saveSeq && _saveUndo.Count > 0)
        {
            // mesmo Salvar mundo (objetos e depois terreno): junta; arquivo repetido fica com a cópia mais antiga
            var last = _saveUndo[^1];
            var have = new HashSet<string>(last.Files.Select(f => f.Original), StringComparer.OrdinalIgnoreCase);
            _saveUndo[^1] = last with { When = s.When, Files = [.. last.Files, .. s.Files.Where(f => have.Add(f.Original))], PckMain = last.PckMain ?? s.PckMain, PckHeader = last.PckHeader ?? s.PckHeader };
        }
        else
        {
            _saveUndo.Add(s); _saveSeqRecorded = _saveSeq;
            if (_saveUndo.Count > 10) _saveUndo.RemoveAt(0);
        }
        _saveRedo.Clear();
    }

    /// <summary>
    /// Ctrl+Z / Ctrl+Y do Salvar inteiro. Só com a Edição de Mundo aberta, sem nada por salvar e sem passos para desfazer
    /// na frente (falso = não era a vez; quem chamou segue para o próximo histórico).
    /// </summary>
    bool SaveUndoStep(bool redo)
    {
        if (!WorldEditOpen || WorldDirty || (redo ? _wRedo.Count : _wUndo.Count) > 0) return false;
        var from = redo ? _saveRedo : _saveUndo;
        if (from.Count == 0) return false;
        var set = from[^1];
        string files = string.Join(Environment.NewLine, set.Files.Select(f => "• " + Path.GetFileName(f.Original)).Distinct().Take(12))
            + (set.Files.Count > 12 ? Environment.NewLine + string.Format(Loc.T("obj.warnMore", "… e mais {0} aviso(s)."), set.Files.Count - 12) : "");
        string ask = (redo
                ? string.Format(Loc.T("undoSave.redoAsk", "Refazer o Salvar das {0:HH:mm} (gravar de novo o que ele tinha gravado)?"), set.When)
                : string.Format(Loc.T("undoSave.ask", "Desfazer o último Salvar (das {0:HH:mm})? Os arquivos que ele gravou voltam como estavam antes dele:"), set.When))
            + Environment.NewLine + Environment.NewLine + files + Environment.NewLine + Environment.NewLine
            + Loc.T("undoSave.note", "Feche o jogo antes. Os arquivos do servidor precisam ser enviados de novo depois.");
        bool yes = Headless ? SaveUndoAnswer : MessageBox.Show(this, ask, redo ? Loc.T("undoSave.redoTitle", "Refazer o Salvar") : Loc.T("undoSave.title", "Desfazer o Salvar"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        if (!yes) { _status.Text = Loc.T("undoSave.kept", "Nada mudou."); return true; }

        var old = Cursor.Current; Cursor.Current = Cursors.WaitCursor;
        _status.Text = redo ? Loc.T("undoSave.redoing", "Refazendo o Salvar…") : Loc.T("undoSave.undoing", "Desfazendo o Salvar… (o pacote do jogo é grande, pode levar alguns segundos)");
        _status.Refresh();
        try
        {
            // estado atual primeiro (é dele que o passo contrário vai trazer); sem ele não volta nada
            if (BackupSets.Snapshot(set) is not { } now) { Fail(Loc.T("undoSave.noCopy", "Não deu para guardar o estado atual antes de voltar; nada mudou.")); return true; }
            string? why = BackupSets.Restore(set);
            if (why is not null) { Fail(why); return true; }
            from.RemoveAt(from.Count - 1);
            (redo ? _saveUndo : _saveRedo).Add(now);
            LightBack(set);
            foreach (var f in set.Files.Select(f => f.Original))
                if (_element is null || !Path.GetFullPath(f).StartsWith(Path.GetFullPath(Path.GetDirectoryName(_element)!) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    int at = f.Replace('/', '\\').LastIndexOf(@"\config\", StringComparison.OrdinalIgnoreCase);
                    ServerNotice.Notify(at >= 0 ? f.Replace('/', '\\')[(at + 8)..] : Path.GetFileName(f));   // acende o "enviar ao servidor"
                }
            string done = redo ? Loc.T("undoSave.redone", "Salvar refeito: os arquivos voltaram ao que ele tinha gravado (Ctrl+Z desfaz de novo).")
                : Loc.T("undoSave.undone", "Último Salvar desfeito: os arquivos voltaram como estavam antes dele (Ctrl+Y refaz).");
            _status.Text = done;
            if (!Headless) SavedDialog.Show(this, done);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Error("desfazer o Salvar", ex);
            Fail(Loc.T("undoSave.fail", "Não deu para voltar o Salvar (arquivo aberto no jogo?). Veja o log."));
        }
        finally { Cursor.Current = old; }
        _gl.Invalidate();
        return true;

        void Fail(string msg)
        {
            _status.Text = msg;
            if (!Headless) MessageBox.Show(this, msg, Loc.T("undoSave.title", "Desfazer o Salvar"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Luz do chão (.t2lm/.t2lm1) que voltou: a tela relê essas áreas do disco.</summary>
    void LightBack(BackupSet set)
    {
        var pieces = set.Files.Select(f => Path.GetFileName(f.Original))
            .Where(n => (n.EndsWith(".t2lm", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".t2lm1", StringComparison.OrdinalIgnoreCase)) && n.StartsWith(_t.Name + "_", StringComparison.OrdinalIgnoreCase))
            .Select(n => { var b = Path.GetFileNameWithoutExtension(n); return int.TryParse(b[(b.LastIndexOf('_') + 1)..], out int p) ? p : 0; })
            .Where(p => p > 0).Distinct().ToList();
        if (pieces.Count == 0) return;
        foreach (var k in _t.ForgetLight(pieces)) { _lightPending.Add(k); _grass.ForgetLight(k.Item1, k.Item2); }
        if (_grass.Scene is { } gs) _grass.Invalidate(gs.Grass, true);
    }

    /// <summary>
    /// Teste ("dftundosave"), só com um arquivo temporário (nada do jogo): um Salvar registrado; Ctrl+Z volta o arquivo ao
    /// de antes; Ctrl+Y traz de novo o gravado; responder Não não muda nada.
    /// </summary>
    internal string SaveUndoTest()
    {
        string load = DraftLoadTest();
        var temp = Path.Combine(Path.GetTempPath(), "myace-undosave-" + Environment.ProcessId);
        Directory.CreateDirectory(temp);
        string? oldFolder = BackupSets.FolderOverride;
        try
        {
            BackupSets.FolderOverride = Path.Combine(temp, "conjuntos");
            string a = Path.Combine(temp, "x.dat");
            File.WriteAllText(a, "antes");
            var copy = BackupStore.Save(a)!;
            File.WriteAllText(a, "gravado");   // o "Salvar"
            BeginSaveStep(); RecordSave(new BackupSet("objects", _t.Name, DateTime.Now, [new(a, copy)]));
            SaveUndoAnswer = true;
            bool u = UndoAny(); string afterUndo = File.ReadAllText(a);
            bool y = RedoAny(); string afterRedo = File.ReadAllText(a);
            SaveUndoAnswer = false;
            bool asked = UndoAny(); string kept = File.ReadAllText(a);
            return load + $" | dftundosave: Ctrl+Z={u} arquivo=\"{afterUndo}\" (esperado antes); Ctrl+Y={y} arquivo=\"{afterRedo}\" (esperado gravado); Não: perguntou={asked} arquivo=\"{kept}\"; ok={u && afterUndo == "antes" && y && afterRedo == "gravado" && kept == "gravado"}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return load + " | dftundosave: ERRO " + ex.Message; }
        finally
        {
            BackupSets.FolderOverride = oldFolder; SaveUndoAnswer = false; _saveUndo.Clear(); _saveRedo.Clear();
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }
}
