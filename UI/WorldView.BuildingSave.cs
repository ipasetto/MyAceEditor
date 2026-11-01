using System.Text;
using MyAceEditor.PerfectWorld;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Gravar construções movidas no 3D (PW-KB §S6 colisão, §S17 ornamento). Uma construção vive em 3 lugares:
/// 1) o .bmd no litmodels.pck do cliente: posição no cabeçalho + brushes de colisão do cliente no fim (CELBrushBuilding);
/// 2) o ornamento do .ecbsd (posição guardada; o cliente não move o desenho por ela, mas fica igual ao .bmd);
/// 3) os mesmos brushes no map.bht do servidor (colisão do gs), achados pela caixa exata de cada brush do .bmd.
/// Cópias gêmeas (mesmos brushes, ex.: a05) andam juntas. O movemap (caminho dos NPCs) ainda não é refeito: o Salvar avisa.
/// </summary>
internal sealed partial class WorldView
{
    /// <summary>Pasta gamed\config do servidor (o map.bht do mapa fica em &lt;pasta&gt;\&lt;mapa&gt;\map.bht).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string? ServerConfigDir { get; set; }

    sealed record BuildingPlan(Dictionary<string, byte[]> Bmds, byte[] Bht, string BhtPath, int Buildings, int Twins, int Brushes, int Ornaments);

    /// <summary>Monta tudo em memória (nada é gravado aqui). Lança InvalidDataException com o motivo se não der.</summary>
    BuildingPlan PlanBuildings()
    {
        if (_element is null) throw new InvalidDataException(Loc.T("world.bldNoClient", "Pasta do cliente não encontrada."));
        var bhtPath = ServerConfigDir is { } sd ? Path.Combine(sd, _t.Name, "map.bht") : null;
        if (bhtPath is null || !File.Exists(bhtPath))
            throw new InvalidDataException(Loc.T("world.bldNoServer", "Não achei o map.bht do servidor deste mapa. Abra a pasta do servidor (aba NPCGen ou Verificação) e salve de novo."));
        var bht = new BhtFile(File.ReadAllBytes(bhtPath));
        var keys = bht.Keys();
        var bmds = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        int twins = 0, brushes = 0, orn = 0;
        using var lit = new PckArchive(Path.Combine(_element, "litmodels.pck"));
        foreach (var (file, off) in _buildings.Offsets)
        {
            var d = new Vector3(off.X, off.Y, -off.Z);   // deslocamento guardado no espaço do OpenGL → jogo
            var bytes = lit.Find(file) is { } e ? lit.Read(e) : throw new InvalidDataException(file);
            var at = BmdModel.CdBrushes(bytes) ?? throw new InvalidDataException(string.Format(Loc.T("world.bldNoBrush", "{0}: não consegui ler a colisão do .bmd; nada foi gravado."), Path.GetFileName(file)));
            var mine = at.Select(o => BhtFile.KeyAt(bytes, o)).ToHashSet();
            var group = new List<(string File, byte[] Bytes)> { (file, bytes) };
            if (mine.Count > 0)
            {
                // gêmeas: outro .bmd do mesmo pedaço com exatamente os mesmos brushes
                var dir = file[..(file.LastIndexOf('\\') + 1)];
                foreach (var o in lit.Entries.Where(x => x.Path.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && x.Path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase)
                    && !x.Path.Equals(file, StringComparison.OrdinalIgnoreCase) && x.Path.IndexOf('\\', dir.Length) < 0))
                {
                    var ob = lit.Read(o);
                    if (BmdModel.CdBrushes(ob) is { Count: > 0 } oat && oat.Count == at.Count && oat.Select(p => BhtFile.KeyAt(ob, p)).ToHashSet().SetEquals(mine))
                    { group.Add((o.Path, ob)); twins++; }
                }
            }
            foreach (var (f, b) in group) { bmds[f] = MoveBmd(b, d); if (MoveOrnament(f, d)) orn++; }
            var sel = mine.Where(keys.ContainsKey).SelectMany(k => keys[k]).Distinct().ToList();
            brushes += sel.Count;
            bht.Move(sel, d.X, d.Y, d.Z);
        }
        var newBht = bht.Write();
        _ = new BhtFile(newBht);   // relê: formato inteiro ainda certo
        return new BuildingPlan(bmds, newBht, bhtPath, _buildings.Offsets.Count, twins, brushes, orn);
    }

    /// <summary>.bmd deslocado: posição do cabeçalho + cada brush de colisão do fim. Relido antes de seguir.</summary>
    static byte[] MoveBmd(byte[] src, Vector3 d)
    {
        var b = (byte[])src.Clone();
        int sh = BitConverter.ToUInt32(b, 4) == 0x80000001 ? 5 : 0;
        for (int c = 0; c < 3; c++) BitConverter.TryWriteBytes(b.AsSpan(0x2C + sh + c * 4), BitConverter.ToSingle(b, 0x2C + sh + c * 4) + d[c]);
        var at = BmdModel.CdBrushes(b)!;
        foreach (int o in at) BhtFile.MoveAt(b, o, d.X, d.Y, d.Z);
        if (BmdModel.Read(b) is null || BmdModel.CdBrushes(b)?.Count != at.Count) throw new InvalidDataException(Loc.T("world.sceneBad", "a releitura não bateu; nada foi gravado"));
        return b;
    }

    /// <summary>
    /// Ornamento do .ecbsd que aponta para o .bmd: ECBSDFILEORNAMENT (pos 3f, dir 3f, cima 3f, 2 bools + 2 de alinhamento = 40 B, conferido no a05) seguido do
    /// nome (i32 + texto "pedaço\arquivo.lmd/.bmd"; EC_Ornament.cpp). Soma o deslocamento à posição em _bsd.
    /// </summary>
    bool MoveOrnament(string file, Vector3 d)
    {
        if (_bsd is null) return false;
        var parts = file.Split('\\');
        if (parts.Length < 2) return false;
        var rel = Encoding.ASCII.GetBytes(parts[^2] + "\\" + Path.GetFileNameWithoutExtension(parts[^1]) + ".");
        for (int i = _bsd.AsSpan().IndexOf(rel); i >= 0;)
        {
            int end = i + rel.Length + 3;
            for (int s = i; s >= Math.Max(48, i - 64); s--)
            {
                if (BitConverter.ToInt32(_bsd, s - 4) != end - s || (s != i && _bsd[i - 1] != (byte)'\\')) continue;
                int o = s - 4 - 40;   // 9 floats + 2 bools + 2 de alinhamento (lixo)
                float Len(int k) => MathF.Sqrt(Enumerable.Range(0, 3).Sum(c => BitConverter.ToSingle(_bsd, o + k + c * 4) * BitConverter.ToSingle(_bsd, o + k + c * 4)));
                if (MathF.Abs(Len(12) - 1) > 0.02f || MathF.Abs(Len(24) - 1) > 0.02f) continue;
                for (int c = 0; c < 3; c++) BitConverter.TryWriteBytes(_bsd.AsSpan(o + c * 4), BitConverter.ToSingle(_bsd, o + c * 4) + d[c]);
                return true;
            }
            int next = _bsd.AsSpan(i + 1).IndexOf(rel);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return false;
    }

    /// <summary>
    /// Salvar das construções: pergunta, grava litmodels.pck (cliente) e map.bht (servidor) com backup, e recarrega.
    /// O ornamento já foi mexido em _bsd e é gravado pelo SaveScene junto com o .ecbsd. false = nada gravado.
    /// </summary>
    bool SaveBuildings()
    {
        var bsdBefore = (byte[]?)_bsd?.Clone();
        BuildingPlan plan;
        try { plan = PlanBuildings(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _bsd = bsdBefore;
            MessageBox.Show(this, ex.Message, Loc.T("world.objSave", "Salvar cenário"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        var ask = string.Format(Loc.T("world.bldAsk",
            "Gravar {0} construção(ões) movida(s) ({1} cópia(s) gêmea(s) juntas):\n\n• cliente: litmodels.pck (posição e colisão do jogador)\n• servidor: {2} ({3} brush(es) de colisão)\n• cenário: .ecbsd ({4} ornamento(s))\n\nTudo com backup. Depois: reinicie o mapa no servidor e mande o litmodels.pck novo para os jogadores. O caminho dos NPCs (movemap) ainda não é refeito: monstros podem andar como antes ali."),
            plan.Buildings, plan.Twins, plan.BhtPath, plan.Brushes, plan.Ornaments);
        if (MessageBox.Show(this, ask, Loc.T("world.objSave", "Salvar cenário"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        { _bsd = bsdBefore; return false; }
        try
        {
            PckWriter.Write(Path.Combine(_element!, "litmodels.pck"), plan.Bmds);
            AtomicFileSave.Write(plan.BhtPath, plan.Bht);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or FileSaveException)
        {
            _bsd = bsdBefore;
            MessageBox.Show(this, ex.Message, Loc.T("world.objSave", "Salvar cenário"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        // gravado: posições novas vêm dos arquivos; desfazer do 3D não volta o que está no disco (backups ficam)
        _buildings.Reopen(_element!);
        _piece = -1;
        _sceneSel = null; _sUndo.Clear(); _sRedo.Clear();
        ShowObjPanel(); _gl.Invalidate();
        _status.Text = string.Format(Loc.T("world.bldSaved", "{0} construção(ões) gravada(s): litmodels.pck + map.bht (backup feito)."), plan.Buildings);
        return true;
    }

    /// <summary>
    /// --world-shot "bldplan…" (servidor em MYACE_SERVER): move a construção mais perto 3 m em X e 2 m em Z só na memória,
    /// monta o plano e confere: .bmd relidos, brushes do .bmd movidos = brushes movidos no map.bht. Nada é gravado.
    /// </summary>
    internal string BuildingPlanTest()
    {
        ServerConfigDir ??= Environment.GetEnvironmentVariable("MYACE_SERVER");
        if (_ecw is null) LoadSceneForEdit();   // .ecbsd (ornamentos) só carrega com a ferramenta aberta
        if (_buildings.Nearest(_cam) is not { } nb) return "bldplan: nenhuma construção perto";
        var bsd = (byte[]?)_bsd?.Clone();
        _buildings.Offsets.Clear(); _buildings.Offsets[nb.File] = new Vector3(3, 0, 2);   // GL: +2 em Z = -2 no jogo
        try
        {
            var plan = PlanBuildings();
            var moved = new BhtFile(plan.Bht).Keys();
            int ok = 0, all = 0;
            foreach (var b in plan.Bmds.Values)
                foreach (int o in BmdModel.CdBrushes(b)!) { all++; if (moved.ContainsKey(BhtFile.KeyAt(b, o))) ok++; }
            var m = BmdModel.Read(plan.Bmds[nb.File])!;
            return $"bldplan {nb.File}: {plan.Bmds.Count} .bmd ({plan.Twins} gêmea(s)), pos agora {m.Pos[0]:0.00},{m.Pos[1]:0.00},{m.Pos[2]:0.00}, brushes no map.bht movidos {plan.Brushes}, brushes dos .bmd achados no map.bht novo {ok}/{all}, ornamentos {plan.Ornaments} (.ecbsd {bsd?.Length ?? -1} B{(_ecwWhy is null ? "" : ", " + _ecwWhy)})";
        }
        catch (Exception ex) { return "bldplan ERRO: " + ex.Message; }
        finally { _bsd = bsd; _buildings.Offsets.Clear(); }
    }
}
