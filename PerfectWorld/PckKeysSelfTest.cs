using System.Security.Cryptography;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Teste dos perfis de chaves do .pck (--pck-keys-test &lt;element&gt; &lt;saída&gt;), só em cópias numa pasta temporária:
/// todos os pacotes do cliente abrem com o perfil de fábrica; uma cópia do configs.pck convertida para um perfil inventado
/// não abre mais com o de fábrica, abre sozinha com o novo, com todos os arquivos iguais; gravar nela mantém o perfil;
/// convertida de volta fica byte a byte igual ao original. O mesmo com o pacote dividido em .pck + .pkx.
/// </summary>
public static class PckKeysSelfTest
{
    public static void Run(string element, string output)
    {
        var lines = new List<string>(); int fails = 0;
        void Check(string name, bool ok, string why) { lines.Add((ok ? "OK " : "FALHA ") + name + ": " + why); if (!ok) fails++; }
        var temp = Path.Combine(Path.GetTempPath(), "myace-pckkeys-" + Environment.ProcessId);
        Directory.CreateDirectory(temp);
        try
        {
            PckKeyStore.FileOverride = Path.Combine(temp, "pck-keys.json"); PckKeyStore.Reload();
            var test = new PckKeys("Teste", unchecked((int)0x12345678), 0x0BADF00D, unchecked((int)0xCAFEBABE), 0x1337C0DE, 0x2468ACE0, 0x13579BDF);
            Check("perfis-gravar", PckKeyStore.Save([test]) is null && PckKeyStore.User.Count == 1 && PckKeyStore.All[0].IsDefault, $"{PckKeyStore.All.Count} perfis (de fábrica primeiro)");
            Check("texto-ida-volta", PckKeys.FromText("x", test.ToText()) is { } back && back.SameKeys(test) && PckKeys.ParseNumber("0xA8937462") == PckKeys.Default.Key1, test.ToText());

            // 1) todos os pacotes do cliente: perfil de fábrica
            int all = 0, dflt = 0; var bad = new List<string>();
            foreach (var f in Directory.EnumerateFiles(element, "*.pck"))
            {
                all++;
                try { if (PckKeyStore.Detect(f)?.IsDefault == true) dflt++; else bad.Add(Path.GetFileName(f)); }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { bad.Add(Path.GetFileName(f) + " (" + ex.Message + ")"); }
            }
            Check("cliente-de-fabrica", all > 0 && dflt == all, $"{dflt} de {all} pacotes com o perfil de fábrica {string.Join(", ", bad)}");

            var src = Path.Combine(element, "configs.pck");
            var origBytes = File.ReadAllBytes(src);
            var origHash = Convert.ToHexString(SHA256.HashData(origBytes));
            Dictionary<string, string> Contents(PckArchive a) => a.Entries.ToDictionary(e => e.Path, e => Convert.ToHexString(SHA256.HashData(a.Read(e))), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> orig; using (var a0 = new PckArchive(src)) orig = Contents(a0);

            foreach (bool split in new[] { false, true })
            {
                string tag = split ? "dividido" : "inteiro";
                var dir = Path.Combine(temp, tag); Directory.CreateDirectory(dir);
                var pck = Path.Combine(dir, "configs.pck");
                if (!split) File.WriteAllBytes(pck, origBytes);
                else
                {
                    // o mesmo pacote lógico em duas partes: .pck com o começo, .pkx com o resto (a tabela fica no fim do .pkx)
                    int cut = origBytes.Length / 2;
                    File.WriteAllBytes(pck, origBytes[..cut]); File.WriteAllBytes(Path.ChangeExtension(pck, ".pkx"), origBytes[cut..]);
                }
                string? err = null;
                try { PckKeyConvert.Convert(pck, test); } catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
                bool refuses;
                try { using var d = new PckArchive(pck, keys: PckKeys.Default); refuses = false; } catch (InvalidDataException) { refuses = true; }
                string keysName = "-"; bool same = false; int n = 0;
                try { using var a = new PckArchive(pck); keysName = a.Keys.Name; var c = Contents(a); n = c.Count; same = c.Count == orig.Count && c.All(kv => orig.TryGetValue(kv.Key, out var h) && h == kv.Value); }
                catch (Exception ex) { keysName = "ERRO " + ex.Message; }
                Check("converter-" + tag, err is null && refuses && keysName == test.Name && same, $"{err ?? "convertido"}; de fábrica recusa={refuses}; abre sozinho com \"{keysName}\"; {n} arquivos iguais={same}");

                // volta ao de fábrica: idêntico ao original
                string? err2 = null;
                try { PckKeyConvert.Convert(pck, PckKeys.Default); } catch (Exception ex) { err2 = ex.GetType().Name + ": " + ex.Message; }
                byte[] now = split ? [.. File.ReadAllBytes(pck), .. File.ReadAllBytes(Path.ChangeExtension(pck, ".pkx"))] : File.ReadAllBytes(pck);
                Check("voltar-" + tag, err2 is null && Convert.ToHexString(SHA256.HashData(now)) == origHash, $"{err2 ?? "convertido de volta"}; byte a byte igual ao original={Convert.ToHexString(SHA256.HashData(now)) == origHash}");
            }

            // gravar num pacote com chaves trocadas: continua com elas e o arquivo novo é lido
            {
                var dir = Path.Combine(temp, "gravar"); Directory.CreateDirectory(dir);
                var pck = Path.Combine(dir, "configs.pck"); File.WriteAllBytes(pck, origBytes);
                PckKeyConvert.Convert(pck, test);
                var content = System.Text.Encoding.UTF8.GetBytes("MyAce: teste de gravação com chaves trocadas " + DateTime.Now.Ticks);
                string? err = null;
                try { PckWriter.Write(pck, new Dictionary<string, byte[]> { [@"configs\myace_teste.txt"] = content }); } catch (Exception ex) { err = ex.Message; }
                bool ok = false; string keysName = "-";
                try { using var a = new PckArchive(pck); keysName = a.Keys.Name; ok = a.Find(@"configs\myace_teste.txt") is { } e && a.Read(e).AsSpan().SequenceEqual(content) && a.Entries.Count == orig.Count + 1; }
                catch (Exception ex) { keysName = "ERRO " + ex.Message; }
                Check("gravar-com-chaves", err is null && ok && keysName == test.Name, $"{err ?? "gravado"}; relido com \"{keysName}\"; arquivo novo e os antigos={ok}");
            }

            // pacote que não bate em nenhum perfil: recusa com a explicação
            {
                var pck = Path.Combine(temp, "estranho.pck"); var b = (byte[])origBytes.Clone();
                BitConverter.TryWriteBytes(b.AsSpan(0), 0x01020304); File.WriteAllBytes(pck, b);
                string msg = "";
                try { using var a = new PckArchive(pck); } catch (InvalidDataException ex) { msg = ex.Message; }
                Check("chave-desconhecida", msg.Contains("PCK Key"), msg);
            }
        }
        catch (Exception ex) { Check("sem-excecao", false, ex.ToString()); }
        finally
        {
            PckKeyStore.FileOverride = null; PckKeyStore.Reload();
            try { Directory.Delete(temp, true); } catch (IOException) { }
            lines.Add("falhas: " + fails);
            File.WriteAllLines(output, lines);
        }
    }
}
