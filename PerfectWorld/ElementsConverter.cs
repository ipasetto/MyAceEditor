using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Converte o elements.data entre versões (issue #24), para cima ou para baixo, casando bloco e campo pelo NOME
/// dos .cfg das duas versões. Descer: campos e blocos que a versão antiga não tem são descartados. Subir: o que a
/// versão nova tem a mais nasce zerado. É o que as regras .rules do sELedit faziam à mão (só para baixo,
/// "REMOVEVALUE:83:27"), mas para qualquer par de versões que tenha .cfg. Trechos entre blocos (marcas de
/// exportação) e o TALK_PROC são copiados da origem; o resultado é relido com o .cfg de destino para provar.
/// </summary>
public static class ElementsConverter
{
    public sealed record Report(int TargetVersion, int Blocks, int Items, int FieldsDropped, int FieldsAdded, int BlocksDropped, int BlocksAdded, List<string> Notes);

    public static (byte[] Data, Report Report) Convert(Module source, ElementsCfg target, int targetVersion)
    {
        if (source.Source is not ElementsLayout layout || !layout.Complete)
            throw new InvalidOperationException(UI.Loc.T("conv.srcIncomplete", "O elements.data de origem precisa estar lido por completo."));
        if (layout.Format != ElementsReadFormat.CountOnly)
            throw new NotSupportedException(UI.Loc.T("conv.plainOnly", "Conversão só para o formato comum de listas (sem MPPC e sem tamanho por lista, v191+)."));
        var src = layout.Original;
        var notes = new List<string>();

        // trechos da origem por nome de bloco: prefixo (antes do count) e seções brutas (TALK_PROC, cauda)
        var prefix = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var opaque = layout.OpaqueSections.ToDictionary(o => o.Name, o => src.AsSpan(o.Start, o.End - o.Start).ToArray(), StringComparer.OrdinalIgnoreCase);
        var ends = layout.Blocks.Select(b => (b.Block.Name, Start: b.CountPos, End: b.EndPos))
            .Concat(layout.OpaqueSections.Select(o => (o.Name, o.Start, o.End))).OrderBy(x => x.Start).ToList();
        int prev = 4;
        foreach (var (name, start, end) in ends)
        {
            if (start >= prev) prefix[name] = src.AsSpan(prev, start - prev).ToArray();
            prev = end;
        }
        var lists = layout.Blocks.ToDictionary(b => b.Block.Name, b => b, StringComparer.OrdinalIgnoreCase);

        using var ms = new MemoryStream(src.Length);
        uint signature = BitConverter.ToUInt32(src, 0) & 0xFFFF0000;
        ms.Write(BitConverter.GetBytes(signature | (uint)(targetVersion & 0xFFFF)));
        int items = 0, dropped = 0, added = 0, blocksAdded = 0;
        var targetNames = new HashSet<string>(target.Blocks.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
        int blocksDropped = layout.Blocks.Count(b => !targetNames.Contains(b.Block.Name));

        foreach (var block in target.Blocks)
        {
            // deslocamento antes da lista: o mesmo trecho da origem quando o tamanho bate; senão zeros
            if (block.Offset == "AUTO")
            {
                if (prefix.TryGetValue(block.Name, out var p) && p.Length >= 8) ms.Write(p);
                else { ms.Write(new byte[8]); notes.Add(string.Format(UI.Loc.T("conv.noteMark", "{0}: marca de exportação criada vazia"), block.Name)); }
            }
            else if (int.TryParse(block.Offset, out var n) && n > 0)
                ms.Write(prefix.TryGetValue(block.Name, out var p) && p.Length == n ? p : new byte[n]);

            if (block.Raw)
            {
                if (opaque.TryGetValue(block.Name, out var raw)) ms.Write(raw);
                else { ms.Write(BitConverter.GetBytes(0)); notes.Add(string.Format(UI.Loc.T("conv.noteRaw", "{0}: bloco bruto criado vazio"), block.Name)); }
                continue;
            }
            if (!lists.TryGetValue(block.Name, out var span))
            {
                ms.Write(BitConverter.GetBytes(0)); blocksAdded++;
                notes.Add(string.Format(UI.Loc.T("conv.noteNewList", "{0}: lista nova nesta versão (vazia)"), block.Name));
                continue;
            }
            var srcFields = new HashSet<string>(span.Block.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            var dstFields = new HashSet<string>(block.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            // nome do campo na origem para cada campo do destino (igual ou equivalente: renomeações entre versões)
            var from = block.Fields.ToDictionary(f => f.Name, f => Aliases(f.Name).FirstOrDefault(srcFields.Contains), StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(from.Values.Where(v => v is not null)!, StringComparer.OrdinalIgnoreCase);
            dropped += srcFields.Count(f => !used.Contains(f));
            added += from.Count(kv => kv.Value is null);
            // posição de cada campo no registro de origem: campo de mesmo tipo e tamanho é copiado byte a byte
            // (preserva o "lixo" depois do fim dos textos; ida e volta entre versões fica idêntica)
            var srcAt = new Dictionary<string, (int At, CfgField F)>(StringComparer.OrdinalIgnoreCase);
            { int o = 0; foreach (var f in span.Block.Fields) { srcAt.TryAdd(f.Name, (o, f)); o += f.Size; } }
            ms.Write(BitConverter.GetBytes(span.List.Items.Count));
            foreach (var item in span.List.Items)
            {
                var rec = new byte[block.RecordSize];
                int at = 0;
                foreach (var f in block.Fields)
                {
                    var name = from[f.Name];
                    if (name is null) { at += f.Size; continue; }
                    if (item.Raw is { } raw && srcAt.TryGetValue(name, out var s) && s.F.Size == f.Size
                        && s.F.Type.Equals(f.Type, StringComparison.OrdinalIgnoreCase) && s.At + f.Size <= raw.Length
                        && CfgElementsReader.DecodeField(s.F, raw, s.At) == item.Fields[name].Value)   // não foi editado
                        Buffer.BlockCopy(raw, s.At, rec, at, f.Size);
                    else if (item.Fields.TryGetValue(name, out var v) && !string.IsNullOrEmpty(v.Value))
                    {
                        try { ElementsWriter.EncodeField(f, v.Value, rec, at); }
                        catch (Exception ex) { notes.Add(string.Format(UI.Loc.T("conv.noteZeroed", "{0} · ID {1} · {2}: {3} (ficou zerado)"), block.Name, item.Id, f.Name, ex.Message)); }
                    }
                    at += f.Size;
                }
                ms.Write(rec); items++;
            }
        }
        // o que vier depois do último bloco na origem (raro) é mantido
        if (prev < src.Length && !target.Blocks[^1].Raw) ms.Write(src, prev, src.Length - prev);
        return (ms.ToArray(), new Report(targetVersion, target.Blocks.Count, items, dropped, added, blocksDropped, blocksAdded, notes));
    }

    static readonly System.Text.RegularExpressions.Regex GoodsNew = new(@"^(pages_\d+)_goods_(\d+)_id$"), GoodsOld = new(@"^(pages_\d+)_id_goods_(\d+)$");

    /// <summary>
    /// Nomes equivalentes do mesmo campo em versões diferentes (o próprio nome primeiro). Vistos nos .cfg:
    /// loja do NPC "pages_1_id_goods_N" (v27) = "pages_1_goods_N_id" (v60+); "id_object_need" (v60) = "id_object_need_1" (v101).
    /// </summary>
    static IEnumerable<string> Aliases(string name)
    {
        yield return name;
        if (GoodsNew.Match(name) is { Success: true } a) yield return $"{a.Groups[1].Value}_id_goods_{a.Groups[2].Value}";
        if (GoodsOld.Match(name) is { Success: true } b) yield return $"{b.Groups[1].Value}_goods_{b.Groups[2].Value}_id";
        if (name.EndsWith("_1")) yield return name[..^2];
        else if (!char.IsDigit(name[^1])) yield return name + "_1";
    }

    /// <summary>Prova: o resultado precisa ser lido por inteiro com o .cfg de destino e ter os mesmos itens por bloco.</summary>
    public static string? Verify(byte[] converted, ElementsCfg target, Module source)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "myace-convert-" + Guid.NewGuid().ToString("N") + ".data");
        try
        {
            File.WriteAllBytes(tmp, converted);
            var mod = new CfgElementsReader(tmp).Parse(target);
            if (mod.Source is not ElementsLayout l || !l.Complete) return UI.Loc.T("conv.proofIncomplete", "o resultado não foi lido por inteiro com o .cfg de destino");
            foreach (var list in mod.Lists)
                if (source.FindListByName(list.Name) is { } s && s.Items.Count != list.Items.Count)
                    return string.Format(UI.Loc.T("conv.proofCount", "{0}: {1} itens na origem, {2} no resultado"), list.Name, s.Items.Count, list.Items.Count);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
