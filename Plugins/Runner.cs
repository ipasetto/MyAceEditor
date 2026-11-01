using System.Text;
using System.Text.Json.Nodes;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI
{
    /// <summary>O Gnet.cs do editor usa Loc.T só para textos de erro; aqui fica o texto em português.</summary>
    static class Loc { public static string T(string key, string pt) => pt; }
}

namespace MyAceEditor.Plugins
{
    /// <summary>
    /// Programa dos plugins de evento (docs/PLUGINS.md), sempre ligado no servidor. A cada segundo: lê os comandos do painel
    /// (plugins.cmd: "fire id", "pause id", "resume id", "stop id"), relê as regras quando o plugins.json muda e cuida de cada
    /// plugin; grava o estado (plugins-state.json) e o histórico (plugins.log). Fala com o gdeliveryd (127.0.0.1:29100):
    /// GMControlGame 380 liga/desliga o controlador do npcgen, PublicChat 79 avisa todos.
    /// </summary>
    static class Runner
    {
        sealed class Live { public DateTime NextRoll; public DateTime? ActiveUntil; public bool Paused; public DateTime ReadyAt; }

        static readonly Dictionary<string, Live> State = new();
        static List<PluginRule> _rules = new();
        static DateTime _configStamp;
        static string _host = "127.0.0.1"; static int _port = 29100;

        static int Main(string[] args)
        {
            if (args.Length >= 1) _host = args[0];
            if (args.Length >= 2) _port = int.Parse(args[1]);
            Directory.CreateDirectory(PluginFiles.Dir);
            Log("programa dos plugins ligado");
            while (true)
            {
                try { Tick(); }
                catch (Exception ex) { Log("erro: " + ex.Message); }
                Thread.Sleep(1000);
            }
        }

        static void Tick()
        {
            var now = DateTime.Now;
            if (File.Exists(PluginFiles.Config) && File.GetLastWriteTimeUtc(PluginFiles.Config) is var st && st != _configStamp)
            {
                _configStamp = st;
                _rules = PluginFiles.Parse(File.ReadAllText(PluginFiles.Config));
                foreach (var r in _rules.Where(r => !State.ContainsKey(r.Id)))
                {
                    State[r.Id] = new Live { NextRoll = now.AddMinutes(Math.Max(1, r.EveryMin)) };
                    Controller(r, on: false);   // começa desligado: o estado do jogo bate com o do programa
                }
                Log($"regras lidas: {_rules.Count} plugin(s)");
            }
            Commands(now);
            foreach (var r in _rules)
            {
                if (!State.TryGetValue(r.Id, out var s)) continue;
                if (s.ActiveUntil is { } until)
                {
                    if (now >= until) End(r, s, now, "tempo esgotado");
                    continue;
                }
                if (!r.Enabled || s.Paused || now < s.NextRoll) continue;
                double roll = Random.Shared.NextDouble() * 100;
                s.NextRoll = now.AddMinutes(Math.Max(1, r.EveryMin));
                if (now < s.ReadyAt) { Log($"[{r.Id}] sorteio pulado: ainda no tempo de renascer (até {s.ReadyAt:HH:mm:ss})"); continue; }
                if (roll < r.ChancePct) Start(r, s, now, $"sorteio {roll:0.0} < {r.ChancePct}%");
                else Log($"[{r.Id}] sorteio {roll:0.0} >= {r.ChancePct}%: não apareceu (próximo {s.NextRoll:HH:mm})");
            }
            WriteState(now);
        }

        static void Commands(DateTime now)
        {
            if (!File.Exists(PluginFiles.Commands)) return;
            var lines = File.ReadAllLines(PluginFiles.Commands);
            File.WriteAllText(PluginFiles.Commands, "");
            foreach (var line in lines)
            {
                var p = line.Trim().Split(' ', 2);
                if (p.Length < 2 || _rules.FirstOrDefault(r => r.Id == p[1].Trim()) is not { } r || !State.TryGetValue(r.Id, out var s)) continue;
                switch (p[0])
                {
                    case "fire":
                        if (s.ActiveUntil is { } au) Log($"[{r.Id}] já está ativo (até {au:HH:mm}): chamar de novo não faz nascer outro");
                        else if (now < s.ReadyAt) Log($"[{r.Id}] ainda no tempo de renascer do monstro (até {s.ReadyAt:HH:mm:ss}): não chamado");
                        else Start(r, s, now, "chamado pelo painel");
                        break;
                    case "stop": if (s.ActiveUntil is not null) End(r, s, now, "encerrado pelo painel"); break;
                    case "pause": s.Paused = true; Log($"[{r.Id}] pausado"); break;
                    case "resume": s.Paused = false; s.NextRoll = now.AddMinutes(Math.Max(1, r.EveryMin)); Log($"[{r.Id}] retomado"); break;
                }
            }
        }

        static void Start(PluginRule r, Live s, DateTime now, string why)
        {
            var ok = Controller(r, on: true);
            s.ActiveUntil = now.AddMinutes(Math.Max(1, r.DurationMin));
            if (r.StartText.Length > 0) Say(r.StartChannel, r.StartText);
            Log($"[{r.Id}] APARECEU ({why}); controlador {r.Controller} {(ok ? "ligado" : "NÃO ligou (mapa desligado ou controlador inexistente)")}; até {s.ActiveUntil:HH:mm}");
        }

        static void End(PluginRule r, Live s, DateTime now, string why)
        {
            var ok = Controller(r, on: false);
            s.ActiveUntil = null; s.NextRoll = now.AddMinutes(Math.Max(1, r.EveryMin));
            s.ReadyAt = now.AddSeconds(Math.Max(0, r.CooldownSec));   // respeita o renascer do monstro antes do próximo
            if (r.EndText.Length > 0) Say(r.EndChannel, r.EndText);
            Log($"[{r.Id}] terminou ({why}); controlador {r.Controller} {(ok ? "desligado" : "não respondeu")}; próximo sorteio {s.NextRoll:HH:mm}");
        }

        // GMControlGame (380): xid, worldtag, comando ASCII -> GMControlGame_Re (381): xid, retcode, (gsid, retcode)[]
        static bool Controller(PluginRule r, bool on)
        {
            if (r.Controller <= 0) return false;
            try
            {
                using var c = new GnetConnection(_host, _port);
                int xid = Random.Shared.Next(1, int.MaxValue);
                c.Send(380, new GnetWriter().Int(xid).Int(r.WorldTag).Octets(Encoding.ASCII.GetBytes($"{(on ? "active" : "cancel")}_npc_generator {r.Controller}")).ToArray());
                var rd = c.Receive(381, x => x.Int() == xid, timeoutMs: 20_000);
                rd.Int();
                if (rd.Int() != 0) return false;
                for (uint i = 0, n = rd.Compact(); i < n; i++) { rd.Int(); if (rd.Int() == 0) return true; }
                return false;
            }
            catch (Exception ex) { Log($"[{r.Id}] gdeliveryd: {ex.Message}"); return false; }
        }

        // PublicChat (79), como o "Aviso para todos" do painel do GM e o broadcast do iweb: canal, emoção 0, roleid -1 (sistema),
        // localsid 0, texto UTF-16LE, dados vazios. O ChatBroadCast (120) mandado ao gdeliveryd é IGNORADO (aviso não saía; 01/11)
        static void Say(int channel, string text)
        {
            try
            {
                using var c = new GnetConnection(_host, _port);
                c.Send(79, new GnetWriter().Byte((byte)channel).Byte(0).Int(-1).Int(0).Text(text).Octets(ReadOnlySpan<byte>.Empty).ToArray());
                Thread.Sleep(300);   // dá tempo de o gdeliveryd ler antes de a conexão fechar
            }
            catch (Exception ex) { Log("aviso não enviado: " + ex.Message); }
        }

        static void WriteState(DateTime now)
        {
            var o = new JsonObject { ["_running"] = now.ToString("yyyy-MM-dd HH:mm:ss") };
            foreach (var r in _rules)
                if (State.TryGetValue(r.Id, out var s))
                    o[r.Id] = s.ActiveUntil is { } u ? $"ATIVO até {u:HH:mm}" : !r.Enabled ? "desligado" : s.Paused ? "pausado" : $"próximo sorteio {s.NextRoll:HH:mm}";
            File.WriteAllText(PluginFiles.State, o.ToJsonString());
        }

        static void Log(string msg)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}";
            Console.WriteLine(line);
            try { File.AppendAllText(PluginFiles.Log, line + "\n"); } catch (IOException) { }
        }
    }
}
