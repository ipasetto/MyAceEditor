using MyAceEditor.UI;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace MyAceEditor.PerfectWorld;

// Tradução dos textos do aipolicy.data (nomes de trigger/ação em GBK chinês, falas em UTF-16 russo).
// REGRA: a tradução é só de EXIBIÇÃO. Fica num cache à parte (%AppData%\MyAceEditor\translations.json) e NUNCA entra no
// aipolicy.data -> o round-trip byte a byte e o texto que o cliente do jogo lê não mudam.
// Ordem de busca: cache -> glossário offline (chinês -> pt/en) -> provedor online (só quando o usuário pede).

/// <summary>Texto de fala com prefixo de canal ($F $A $B $I $S $X $W, PW-KB §S18). O prefixo não vai ao tradutor.</summary>
public static class AiChannel
{
    public static (string Prefix, string Body) Split(string s)
    {
        if (s.Length >= 2 && s[0] == '$' && s[1] < 128 && char.IsLetter(s[1])) return (s[..2], s[2..]);
        return ("", s);
    }

    /// <summary>Nome do canal. Mapeamento das letras é inferido (PW-KB §S18 só lista as letras e os 8 canais).</summary>
    public static string Name(string prefix) => prefix switch
    {
        "$F" => Loc.T("aitr.faction", "Facção"),
        "$A" => Loc.T("aitr.anonymous", "Anônimo"),
        "$B" => Loc.T("aitr.broadcast", "Global"),
        "$I" => Loc.T("aitr.instance", "Instância"),
        "$S" => Loc.T("aitr.system", "Sistema"),
        "$X" => Loc.T("aitr.centerScreen", "Centro da tela"),
        "$W" => Loc.T("aitr.whisper", "Sussurro"),
        _ => Loc.T("aitr.normal", "Normal"),
    };
}

/// <summary>Glossário offline dos termos chineses mais comuns nos nomes de trigger/ação (só pt e en).</summary>
public static class AiGlossary
{
    static readonly Dictionary<string, (string Pt, string En)> Terms = new()
    {
        ["出生"] = ("Nascimento", "Spawn"), ["死亡"] = ("Morte", "Death"), ["定时器"] = ("Timer", "Timer"),
        ["计时"] = ("Cronômetro", "Timing"), ["战斗开始"] = ("Início do combate", "Combat start"),
        ["战斗结束"] = ("Fim do combate", "Combat end"), ["进入战斗"] = ("Entrar em combate", "Enter combat"),
        ["脱离战斗"] = ("Sair do combate", "Leave combat"), ["流程"] = ("Fluxo", "Flow"),
        ["随机喊话"] = ("Grito aleatório", "Random shout"), ["随机说话"] = ("Fala aleatória", "Random talk"),
        ["随机"] = ("Aleatório", "Random"), ["计数器"] = ("Contador", "Counter"), ["大于"] = ("maior que", "greater than"),
        ["小于"] = ("menor que", "less than"), ["喊话"] = ("Grito", "Shout"), ["说话"] = ("Fala", "Talk"),
        ["怪物死亡"] = ("Morte do monstro", "Monster death"), ["怪物出生"] = ("Nascimento do monstro", "Monster spawn"),
        ["怪物"] = ("Monstro", "Monster"), ["判断模式"] = ("Modo de verificação", "Check mode"),
        ["判断数量"] = ("Verificar quantidade", "Check count"), ["判断"] = ("Verificação", "Check"),
        ["到时"] = ("Tempo esgotado", "Time is up"), ["控制器"] = ("Controlador", "Controller"),
        ["关闭"] = ("Fechar", "Close"), ["激活"] = ("Ativar", "Activate"), ["调整"] = ("Ajuste", "Adjust"),
        ["自爆"] = ("Autoexplosão", "Self-destruct"), ["路线选择"] = ("Escolha de rota", "Route choice"),
        ["奖励发放"] = ("Entrega de recompensa", "Reward delivery"), ["分钟"] = ("min", "min"),
        ["自杀"] = ("despawn", "self-despawn"), ["反外挂策略"] = ("Estratégia anti-cheat", "Anti-cheat strategy"),
        ["反外挂"] = ("Anti-cheat", "Anti-cheat"), ["开门"] = ("Abrir porta", "Open door"),
        ["刷怪"] = ("Gerar monstros", "Spawn monsters"), ["休闲"] = ("Ocioso", "Idle"), ["普攻"] = ("Ataque normal", "Normal attack"),
        ["清仇恨"] = ("Limpar ódio", "Clear hate"), ["施放技能"] = ("Usar habilidade", "Cast skill"), ["召唤"] = ("Invocar", "Summon"),
        ["开战"] = ("Iniciar batalha", "Start battle"), ["计分"] = ("Pontuação", "Scoring"), ["传送"] = ("Teleporte", "Teleport"),
        ["双方平局"] = ("Empate", "Draw"), ["清空"] = ("Limpar", "Clear"), ["报名"] = ("Inscrição", "Sign-up"),
        ["权限"] = ("permissão", "permission"), ["招小怪"] = ("Chamar monstros fracos", "Call small monsters"),
    };

    static readonly string[] KeysByLength = Terms.Keys.OrderByDescending(k => k.Length).ToArray();

    /// <summary>Traduz sem rede. false se sobrou algum caractere CJK sem termo (aí só o provedor online resolve).</summary>
    public static bool TryTranslate(string text, string lang, out string result)
    {
        result = "";
        if (lang is not ("pt" or "en") || !TextScript.HasCjk(text)) return false;
        var sb = new StringBuilder();
        int p = 0;
        while (p < text.Length)
        {
            string? hit = null;
            foreach (var k in KeysByLength)
                if (string.CompareOrdinal(text, p, k, 0, k.Length) == 0) { hit = k; break; }
            if (hit != null)
            {
                if (sb.Length > 0 && char.IsLetterOrDigit(sb[^1])) sb.Append(' ');
                sb.Append(lang == "pt" ? Terms[hit].Pt : Terms[hit].En).Append(' ');
                p += hit.Length;
                continue;
            }
            char c = text[p];
            if (TextScript.HasCjk(c.ToString())) return false;
            sb.Append(c switch { '，' or '、' => ", ", '：' => ": ", '（' => " (", '）' => ") ", '。' => ". ", _ => c.ToString() });
            if (c == ',') sb.Append(' ');
            p++;
        }
        result = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Replace(" ,", ",").Trim();
        return result.Length > 0;
    }
}

/// <summary>Cache de traduções em disco: chave = idioma|texto.</summary>
public sealed class TranslationCache
{
    readonly Dictionary<string, string> _map = new();
    readonly object _lock = new();
    readonly string _path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "translations.json");

    public TranslationCache(string? path = null)
    {
        _path = path ?? DefaultPath;
        try
        {
            if (File.Exists(_path))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path));
                if (d != null) foreach (var (k, v) in d) _map[k] = v;
            }
        }
        catch { /* cache corrompido: começa vazio (o arquivo antigo só é sobrescrito no próximo Save) */ }
    }

    static string Key(string lang, string text) => lang + "|" + text;

    public int Count { get { lock (_lock) return _map.Count; } }

    public bool TryGet(string lang, string text, out string translation)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(Key(lang, text), out var v)) { translation = v; return true; }
        }
        translation = "";
        return false;
    }

    public void Set(string lang, string text, string translation)
    {
        lock (_lock) _map[Key(lang, text)] = translation;
    }

    public void Save()
    {
        try
        {
            string json;
            lock (_lock) json = JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = false });
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_path)) File.Replace(tmp, _path, null); else File.Move(tmp, _path);
        }
        catch { /* falha de disco não pode derrubar o editor; a tradução continua em memória */ }
    }
}

/// <summary>Provedor de tradução online. Plugável: troque por DeepL/LibreTranslate implementando esta interface.</summary>
public interface ITranslationProvider
{
    string Name { get; }
    Task<string> TranslateAsync(string text, string targetLang, CancellationToken ct);
}

/// <summary>Limite de uso do serviço (HTTP 429/503). Não é erro do texto: o item volta para a fila.</summary>
public sealed class RateLimitedException : Exception
{
    public TimeSpan? RetryAfter { get; }
    public RateLimitedException(int status, TimeSpan? retryAfter) : base($"limite de uso do serviço (HTTP {status})") => RetryAfter = retryAfter;
}

/// <summary>
/// Endpoint público e NÃO oficial do Google Tradutor (sem chave). Pode limitar a taxa ou mudar sem aviso:
/// por isso o tradutor espera e tenta de novo (AiTranslator), em vez de desistir. Os textos saem do seu computador.
/// Para uso sério, prefira um provedor oficial com chave própria (implemente ITranslationProvider).
/// </summary>
public sealed class GoogleFreeTranslator : ITranslationProvider
{
    static readonly HttpClient Http = CreateClient();
    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (MyAceEditor)");
        return c;
    }

    public string Name => "Google Translate (endpoint gratuito)";

    static string Map(string lang) => lang == "zh" ? "zh-CN" : lang;

    public async Task<string> TranslateAsync(string text, string targetLang, CancellationToken ct)
    {
        var url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&dt=t&tl="
                  + Map(targetLang) + "&q=" + Uri.EscapeDataString(text);
        using var resp = await Http.GetAsync(url, ct);
        if (resp.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var ra = resp.Headers.RetryAfter;
            TimeSpan? wait = ra?.Delta ?? (ra?.Date is { } d ? d - DateTimeOffset.UtcNow : null);
            throw new RateLimitedException((int)resp.StatusCode, wait);
        }
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        foreach (var seg in doc.RootElement[0].EnumerateArray())
            if (seg.GetArrayLength() > 0 && seg[0].ValueKind == JsonValueKind.String) sb.Append(seg[0].GetString());
        var r = sb.ToString();
        if (r.Length == 0) throw new InvalidDataException("resposta vazia do tradutor");
        return r;
    }
}

/// <summary>Resultado: traduzidos, falhas definitivas e não tentados (ficam para a próxima vez).</summary>
public readonly record struct TranslateResult(int Translated, int Failed, int NotTried, string? LastError);

/// <summary>Progresso: feitos, total, segundos de espera (limite de uso) e nota para a barra de status.</summary>
public readonly record struct TranslateProgress(int Done, int Total, int WaitSeconds, string? Note);

/// <summary>
/// Traduz em lote, com fila e controle de limite de uso:
///  - limite (429/503): o item volta para a fila, todos os workers esperam o tempo pedido (ou 30 s, dobrando até 10 min);
///  - falhas de rede/texto: até 3 tentativas por item; só para tudo após 20 falhas seguidas (serviço fora do ar);
///  - o que já foi traduzido fica no cache, então "traduzir tudo" de novo continua de onde parou.
/// </summary>
public sealed class AiTranslator
{
    readonly TranslationCache _cache;
    readonly ITranslationProvider _provider;
    readonly object _gate = new();
    DateTime _pauseUntilUtc = DateTime.MinValue;
    readonly int _baseBackoffSec;
    int _backoffSec;

    public AiTranslator(TranslationCache cache, ITranslationProvider provider, int baseBackoffSeconds = 30)
    {
        _cache = cache; _provider = provider; _baseBackoffSec = baseBackoffSeconds; _backoffSec = baseBackoffSeconds;
    }

    public string ProviderName => _provider.Name;

    /// <summary>Tradução já disponível sem rede para o CORPO do texto (sem prefixo de canal), ou null.</summary>
    public string? TryGet(string body, string lang)
    {
        if (_cache.TryGet(lang, body, out var t)) return t;
        return AiGlossary.TryTranslate(body, lang, out var g) ? g : null;
    }

    public async Task<TranslateResult> TranslateAsync(IReadOnlyList<string> bodies, string lang,
        IProgress<TranslateProgress>? progress, CancellationToken ct)
    {
        var queue = new ConcurrentQueue<(string Body, int Tries)>(bodies.Select(b => (b, 0)));
        int translated = 0, failed = 0, streak = 0, outage = 0;
        string? lastError = null;
        int total = bodies.Count;

        async Task WaitIfPaused()
        {
            while (true)
            {
                DateTime until; lock (_gate) until = _pauseUntilUtc;
                var left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return;
                progress?.Report(new TranslateProgress(translated + failed, total, (int)Math.Ceiling(left.TotalSeconds), "limite de uso"));
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, left.TotalMilliseconds)), ct);
            }
        }

        async Task Worker()
        {
            while (Volatile.Read(ref outage) == 0 && queue.TryDequeue(out var item))
            {
                ct.ThrowIfCancellationRequested();
                await WaitIfPaused();
                try
                {
                    var tr = await _provider.TranslateAsync(item.Body, lang, ct);
                    _cache.Set(lang, item.Body, tr);
                    Interlocked.Exchange(ref streak, 0);
                    lock (_gate) _backoffSec = _baseBackoffSec;
                    int n = Interlocked.Increment(ref translated);
                    if (n % 50 == 0) _cache.Save();
                    progress?.Report(new TranslateProgress(translated + failed, total, 0, null));
                }
                catch (RateLimitedException ex)
                {
                    queue.Enqueue(item);                       // não é erro do texto: tenta de novo depois
                    lastError = ex.Message;
                    lock (_gate)
                    {
                        var wait = TimeSpan.FromSeconds(_backoffSec);
                        if (ex.RetryAfter is { } ra && ra > wait) wait = ra;
                        var until = DateTime.UtcNow + wait;
                        if (until > _pauseUntilUtc) _pauseUntilUtc = until;
                        _backoffSec = Math.Min(_backoffSec * 2, 600);
                    }
                }
                catch (OperationCanceledException) { queue.Enqueue(item); throw; }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    if (item.Tries + 1 < 3) queue.Enqueue((item.Body, item.Tries + 1));
                    else { int f = Interlocked.Increment(ref failed); progress?.Report(new TranslateProgress(translated + f, total, 0, null)); }
                    if (Interlocked.Increment(ref streak) >= 20) Volatile.Write(ref outage, 1);
                }
                await Task.Delay(250, ct);                     // ritmo gentil com o serviço
            }
        }

        try { await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Worker())); }
        finally { _cache.Save(); }
        return new TranslateResult(translated, failed, queue.Count, lastError);
    }
}
