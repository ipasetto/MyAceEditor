using System.Text;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Mapa do elements.data original: bytes do arquivo + onde cada bloco de registros começa e termina.
/// Tudo que fica fora desses blocos (cabeçalho, blocos de assinatura, TALK_PROC, cauda não lida)
/// é copiado byte a byte na gravação.
/// </summary>
public sealed class ElementsLayout
{
    public sealed record BlockSpan(CfgBlock Block, ItemList List, int CountPos, int EndPos);

    public byte[] Original { get; }
    public List<BlockSpan> Blocks { get; } = new();

    public ElementsLayout(byte[] original) => Original = original;
}

/// <summary>
/// Grava o elements.data a partir do módulo lido por <see cref="CfgElementsReader"/>.
/// Cada registro parte dos bytes originais (Item.Raw) e só os campos cujo valor mudou são
/// recodificados — sem mudanças, a saída é idêntica ao arquivo lido (ENGINE-KB §E3).
/// </summary>
public static class ElementsWriter
{
    static readonly Encoding Gbk;
    static ElementsWriter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Gbk = Encoding.GetEncoding(936);
    }

    public static byte[] Build(Module module)
    {
        if (module.Source is not ElementsLayout layout)
            throw new InvalidOperationException("Este módulo não veio de um elements.data (sem layout de origem).");

        var src = layout.Original;
        using var ms = new MemoryStream(src.Length + 4096);
        int copied = 0;
        foreach (var span in layout.Blocks)
        {
            ms.Write(src, copied, span.CountPos - copied);            // trecho intacto antes do bloco
            ms.Write(BitConverter.GetBytes(span.List.Items.Count));
            foreach (var item in span.List.Items)
                ms.Write(EncodeRecord(span.Block, item));
            copied = span.EndPos;
        }
        ms.Write(src, copied, src.Length - copied);                    // resto do arquivo intacto
        return ms.ToArray();
    }

    /// <summary>
    /// Grava com segurança: backup com data/hora ao lado do original e escrita atômica
    /// (arquivo temporário + substituição). Retorna o caminho do backup (ou null se o arquivo não existia).
    /// </summary>
    public static string? Save(Module module, string path)
    {
        var bytes = Build(module);
        string? backup = null;
        if (File.Exists(path))
        {
            backup = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(path, backup, overwrite: false);
        }
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
        return backup;
    }

    static byte[] EncodeRecord(CfgBlock block, Item item)
    {
        var rec = new byte[block.RecordSize];
        if (item.Raw is { } raw) Array.Copy(raw, rec, Math.Min(raw.Length, rec.Length));

        int p = 0;
        foreach (var f in block.Fields)
        {
            if (item.Fields.TryGetValue(f.Name, out var field))
            {
                bool changed = item.Raw is null || CfgElementsReader.DecodeField(f, rec, p) != field.Value;
                if (changed) EncodeField(f, field.Value, rec, p);
            }
            p += f.Size;
        }
        return rec;
    }

    static void EncodeField(CfgField f, string value, byte[] rec, int p)
    {
        string t = f.Type.ToLowerInvariant();
        if (t.StartsWith("wstring:") || t.StartsWith("string:"))
        {
            var bytes = t.StartsWith("w") ? Encoding.Unicode.GetBytes(value) : Gbk.GetBytes(value);
            int max = f.Size - (t.StartsWith("w") ? 2 : 1);              // deixa espaço para o terminador
            int n = Math.Min(bytes.Length, max);
            if (t.StartsWith("w")) n &= ~1;
            Array.Clear(rec, p, f.Size);
            Array.Copy(bytes, 0, rec, p, n);
            return;
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        switch (t)
        {
            case "float":
                float fv = float.TryParse(value.Replace(',', '.'), System.Globalization.NumberStyles.Float, inv, out var x) ? x : 0f;
                BitConverter.GetBytes(fv).CopyTo(rec, p);
                break;
            case "int16" or "word" or "short":
                BitConverter.GetBytes(short.TryParse(value, out var s) ? s : (short)0).CopyTo(rec, p);
                break;
            case "int8" or "byte" or "char" or "bool":
                rec[p] = byte.TryParse(value, out var b) ? b : (byte)0;
                break;
            case "int64" or "double":
                BitConverter.GetBytes(long.TryParse(value, out var l) ? l : 0L).CopyTo(rec, p);
                break;
            default:
                int iv = int.TryParse(value, out var i) ? i : (uint.TryParse(value, out var u) ? unchecked((int)u) : 0);
                BitConverter.GetBytes(iv).CopyTo(rec, p);
                break;
        }
    }
}
