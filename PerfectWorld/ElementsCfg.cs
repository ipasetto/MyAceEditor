using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>Um campo de um bloco, conforme definido no .cfg.</summary>
public sealed record CfgField(string Name, string Type, int Size);

/// <summary>Definição de um bloco do elements.data (nome + campos), vinda do .cfg.</summary>
public sealed class CfgBlock
{
    public int Number;
    public string Name = "";
    public bool Raw;                       // TALK_PROC etc. (tamanho variável)
    public List<CfgField> Fields = new();
    public int RecordSize => Fields.Sum(f => f.Size);
}

/// <summary>
/// Lê os arquivos .cfg da comunidade (sTASKedit / Rody / DataTranslator), que descrevem
/// cada campo de cada bloco do elements.data POR VERSÃO. É a fonte autoritativa do formato.
/// </summary>
public sealed class ElementsCfg
{
    public List<CfgBlock> Blocks = new();

    public static int TypeSize(string t)
    {
        t = t.Trim().ToLowerInvariant();
        if (t.StartsWith("wstring:") || t.StartsWith("string:"))
            return int.Parse(t[(t.IndexOf(':') + 1)..]);
        return t switch
        {
            "int32" or "uint32" or "dword" or "int" or "float" => 4,
            "int16" or "word" or "short" => 2,
            "int8" or "byte" or "char" or "bool" => 1,
            "int64" or "double" => 8,
            _ => -1
        };
    }

    public static ElementsCfg Load(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.Latin1);
        var cfg = new ElementsCfg();
        int i = 0;
        var rx = new System.Text.RegularExpressions.Regex(@"^(\d{2,3})\s*-\s*(.+)$");
        while (i < lines.Length)
        {
            var m = rx.Match(lines[i].Trim());
            if (!m.Success) { i++; continue; }

            var block = new CfgBlock { Number = int.Parse(m.Groups[1].Value), Name = m.Groups[2].Value.Trim() };
            // próximas linhas não-vazias: flag, depois (RAW) ou (nomes, tipos)
            var vals = new List<string>();
            int j = i + 1;
            while (j < lines.Length && vals.Count < 3)
            {
                var s = lines[j].Trim();
                if (s.Length > 0) vals.Add(s);
                j++;
            }
            if (vals.Count >= 2 && vals[1].Equals("RAW", StringComparison.OrdinalIgnoreCase))
            {
                block.Raw = true;
            }
            else if (vals.Count >= 3)
            {
                var names = vals[1].Split(';');
                var types = vals[2].Split(';');
                for (int k = 0; k < Math.Min(names.Length, types.Length); k++)
                {
                    int sz = TypeSize(types[k]);
                    if (sz < 0) { block.Fields.Clear(); break; } // tipo desconhecido -> bloco inválido
                    block.Fields.Add(new CfgField(names[k].Trim(), types[k].Trim(), sz));
                }
            }
            cfg.Blocks.Add(block);
            i = j;
        }
        return cfg;
    }
}
