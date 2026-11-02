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
        sealed class Live
        {
            public DateTime NextRoll; public DateTime? ActiveUntil; public bool Paused; public DateTime ReadyAt;
            public int Every; public bool Enabled;                       // para notar quando a regra muda
            public bool? LastOk; public string LastMsg = ""; public DateTime LastAt;   // resultado da última ação (telas mostram)
        }

        static void Result(PluginRule r, Live s, bool ok, string msg) { s.LastOk = ok; s.LastMsg = msg; s.LastAt = DateTime.Now; Log($"[{r.Id}] {msg}"); }

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
                    State[r.Id] = new Live { NextRoll = now.AddMinutes(Math.Max(1, r.EveryMin)), Every = r.EveryMin, Enabled = r.Enabled };
                    Controller(r, on: false);   // começa desligado: o estado do jogo bate com o do programa
                }
                // regra mudou (a cada X min, ligado/desligado): o próximo sorteio é refeito na hora (antes ficava o horário
                // antigo: mudar de 50 para 1 min continuava esperando os 50; usuário 01/11)
                foreach (var r in _rules)
                    if (State.TryGetValue(r.Id, out var ls) && (ls.Every != r.EveryMin || ls.Enabled != r.Enabled))
                    {
                        ls.Every = r.EveryMin; ls.Enabled = r.Enabled;
                        if (ls.ActiveUntil is null) ls.NextRoll = now.AddMinutes(Math.Max(1, r.EveryMin));
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
                else Log($"[{r.Id}] sorteio {roll:0.0} >= {r.ChancePct}%: não apareceu (próximo {s.NextRoll:HH:mm:ss})");
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
                        if (s.ActiveUntil is { } au) Result(r, s, false, $"já está ativo (até {au:HH:mm:ss}): chamar de novo não faz nascer outro");
                        else if (now < s.ReadyAt) Result(r, s, false, $"ainda no tempo de renascer do monstro (pode de novo às {s.ReadyAt:HH:mm:ss})");
                        else Start(r, s, now, "chamado pelo painel");
                        break;
                    case "stop":
                        if (s.ActiveUntil is not null) End(r, s, now, "encerrado pelo painel");
                        else Result(r, s, false, "não estava ativo: nada a encerrar");
                        break;
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
            if (!ok) s.ActiveUntil = null;   // não ligou: não fica "ativo" à toa
            Result(r, s, ok, ok ? $"APARECEU ({why}); fica até {s.ActiveUntil:HH:mm:ss}"
                : $"NÃO apareceu ({why}): o servidor não ligou o evento {r.Controller} (mapa desligado, ponto sem o evento no npcgen ou servidor sem reiniciar)");
        }

        static void End(PluginRule r, Live s, DateTime now, string why)
        {
            var ok = Controller(r, on: false);
            s.ActiveUntil = null; s.NextRoll = now.AddMinutes(Math.Max(1, r.EveryMin));
            s.ReadyAt = now.AddSeconds(Math.Max(0, r.CooldownSec));   // respeita o renascer do monstro antes do próximo
            // renascer desligado: a "Mensagem ao terminar" sai quando o monstro morre, pela IA dele (anúncio de morte que a
            // tela Plugins põe); aqui não repete (o programa não fica sabendo da morte; usuário 02/11: "avisar só na morte")
            if (r.EndText.Length > 0 && r.Respawn) Say(r.EndChannel, r.EndText);
            Result(r, s, true, $"terminou ({why}); evento {r.Controller} {(ok ? "desligado" : "não respondeu")}; pode de novo às {s.ReadyAt:HH:mm:ss}");
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
                {
                    o[r.Id] = s.ActiveUntil is { } u ? $"ATIVO até {u:HH:mm}" : !r.Enabled ? "só chamado" : s.Paused ? "pausado" : $"próximo sorteio {s.NextRoll:HH:mm}";
                    // para as telas: quando pode chamar de novo e o resultado da última ação (PluginFiles.ParseState)
                    o[r.Id + "#ready"] = (s.ActiveUntil ?? s.ReadyAt).ToString("yyyy-MM-dd HH:mm:ss");
                    o[r.Id + "#active"] = s.ActiveUntil is null ? "0" : "1";
                    if (s.LastOk is { } ok) o[r.Id + "#last"] = $"{(ok ? "ok" : "erro")}|{s.LastAt:HH:mm:ss}|{s.LastMsg}";
                }
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
