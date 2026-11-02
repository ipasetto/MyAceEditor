namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Anúncio reaproveitável (PW-KB §S18; usuário 01/11): um gatilho de aipolicy "SE (morreu | entrou em combate) ENTÃO falar
/// no canal X". Vale em TODAS as versões: a operação 2 (falar) existe em todas, o canal vai no prefixo do texto ($B anúncio
/// geral, $S sistema...) e o nome automático (máscara Name=1) só entra com dwVersion do gatilho >= 17. Os gatilhos criados
/// aqui têm nome com <see cref="Tag"/>, para achar, mudar e tirar depois. Outras funções (chance de nascer, eventos,
/// quests) devem criar anúncios por aqui.
/// </summary>
public static class AiAnnounce
{
    public enum When { Died, CombatStart }

    /// <summary>
    /// Canais (prefixo do texto, chave e texto em pt). O 1º é o padrão (usuário 02/11: Sistema primeiro). Cliente 1.5.3
    /// (EC_GameSession IsPolicyChat): $S sai no canal 9 GP_CHAT_BROADCAST (sistema, todos online; o mesmo do plugin),
    /// $B no canal 1 GP_CHAT_FARCRY (chat global/mundo).
    /// </summary>
    public static readonly (string Prefix, string Key, string Pt)[] Channels =
    {
        ("$S", "ann.chSystem", "Mensagem de sistema"),
        ("$B", "ann.chGlobal", "Mensagem Global (Chat)"),
        ("$X", "ann.chCenter", "Centro da tela (dentro da instância)"),
        ("", "ann.chNormal", "Fala normal (só quem está perto)"),
    };

    /// <summary>Marca do texto que o cliente troca pelo nome do jogador (com a máscara de nome ligada).</summary>
    public const string NameTag = "$name";

    /// <summary>Fala que a IA do monstro já tem (gatilhos do próprio jogo, não criados aqui): quando, canal, texto.</summary>
    public sealed record GameTalk(int Cond, string Prefix, string Text);

    /// <summary>Falas do jogo nesta política (operação 2 em gatilhos que não são do editor), para mostrar.</summary>
    public static List<GameTalk> GameTalks(AiPolicy p)
    {
        var list = new List<GameTalk>();
        if (AiSchema.Op(2) is not { } lay) return list;
        static int Leaf(AiCondNode? n) => n is null ? -1 : n.Type is 5 or 6 or 7 ? (Leaf(n.Left) is var l and >= 0 ? l : Leaf(n.Right)) : n.Type;
        foreach (var t in p.Triggers.Where(t => !t.Name.StartsWith(Tag, StringComparison.Ordinal)))
            foreach (var op in t.Ops.Where(o => o.Type == 2))
            {
                string raw;
                try { raw = lay.Decode(t.Version, op.Param) is { Count: > 0 } v ? v[0] as string ?? "" : ""; } catch (Exception) { continue; }
                var ch = Channels.Where(c => c.Prefix.Length > 0).FirstOrDefault(c => raw.StartsWith(c.Prefix, StringComparison.Ordinal)).Prefix ?? "";
                list.Add(new GameTalk(Leaf(t.Root), ch, raw[ch.Length..].TrimEnd('\0')));
            }
        return list;
    }

    /// <summary>Começo do nome dos gatilhos de anúncio (GBK; até 127 bytes com o resto).</summary>
    public const string Tag = "MyAce anuncio";

    public sealed record Info(AiTrigger Trigger, When When, string Prefix, string Text, bool AppendName);

    static int CondOf(When w) => w == When.Died ? 8 : 2;
    // alvo da fala: na morte, quem matou (8); no combate, o 1º da lista de ódio (0) — a máscara de nome usa o alvo [? testar]
    static int TargetOf(When w) => w == When.Died ? 8 : 0;

    /// <summary>Nome automático na fala só existe com o gatilho na versão 17 ou mais (antes, escreva o nome no texto).</summary>
    public static bool CanAppendName(int triggerVersion) => triggerVersion >= 17;

    /// <summary>Versão de gatilho a usar nesta política: a do 1º gatilho dela, senão a do 1º gatilho do arquivo.</summary>
    public static int TriggerVersion(AiPolicyFile f, AiPolicy p) =>
        p.Triggers.FirstOrDefault()?.Version ?? f.Policies.SelectMany(x => x.Triggers).FirstOrDefault()?.Version ?? 0;

    /// <summary>Anúncios desta política (gatilhos criados por aqui).</summary>
    public static List<Info> Find(AiPolicy p)
    {
        var list = new List<Info>();
        foreach (var t in p.Triggers.Where(t => t.Name.StartsWith(Tag, StringComparison.Ordinal)))
        {
            if (t.Ops.FirstOrDefault(o => o.Type == 2) is not { } op || AiSchema.Op(2) is not { } lay) continue;
            var vals = lay.Decode(t.Version, op.Param);
            var raw = vals.Count > 0 ? vals[0] as string ?? "" : "";
            var ch = Channels.Where(c => c.Prefix.Length > 0).FirstOrDefault(c => raw.StartsWith(c.Prefix, StringComparison.Ordinal)).Prefix ?? "";
            bool name = vals.Count > 1 && Convert.ToInt64(vals[1]) != 0;
            var text = raw[ch.Length..];
            if (text.StartsWith(NameTag + " ", StringComparison.Ordinal)) text = text[(NameTag.Length + 1)..];
            list.Add(new Info(t, t.Root.Type == 8 ? When.Died : When.CombatStart, ch, text, name));
        }
        return list;
    }

    /// <summary>
    /// Anúncios gravados por versões antigas do editor com Run = 1 (o gs pula esses gatilhos): devolve quantos corrigiu.
    /// </summary>
    public static int FixRun(AiPolicyFile f)
    {
        int n = 0;
        foreach (var t in f.Policies.SelectMany(p => p.Triggers))
        {
            if (!t.Name.StartsWith(Tag, StringComparison.Ordinal)) continue;
            bool fixedOne = false;
            if (t.Run != 0) { t.Run = 0; fixedOne = true; }
            // nome ligado (máscara) mas sem $name no texto: o cliente não tinha onde pôr o nome (usuário 02/11)
            if (t.Ops.FirstOrDefault(o => o.Type == 2) is { } op && AiSchema.Op(2) is { } lay)
            {
                var vals = lay.Decode(t.Version, op.Param);
                if (vals.Count > 1 && Convert.ToInt64(vals[1]) != 0 && vals[0] is string raw && !raw.Contains(NameTag, StringComparison.Ordinal))
                {
                    var ch = Channels.Where(c => c.Prefix.Length > 0).FirstOrDefault(c => raw.StartsWith(c.Prefix, StringComparison.Ordinal)).Prefix ?? "";
                    vals[0] = ch + NameTag + " " + raw[ch.Length..];
                    op.Param = lay.Encode(t.Version, vals);
                    fixedOne = true;
                }
            }
            if (fixedOne) n++;
        }
        return n;
    }

    /// <summary>Gatilho novo de anúncio (ainda fora da política: quem chama põe em p.Triggers).</summary>
    public static AiTrigger Make(AiPolicyFile f, AiPolicy p, When when, string prefix, string text, bool appendName)
    {
        int ver = TriggerVersion(f, p);
        var lay = AiSchema.Op(2) ?? throw new InvalidOperationException("operação 2 (falar) sem layout");
        var t = new AiTrigger
        {
            // Run = 0: com bRun o gs trata o gatilho como subgatilho (só roda chamado por outro) e nem o registra
            // (policy_loader.cpp "if(pTriggerData->IsRun()) continue;"); era por isso que o anúncio de morte não saía (usuário 02/11)
            Version = ver, Id = p.Triggers.Count == 0 ? 1 : p.Triggers.Max(x => x.Id) + 1, Active = 1, Run = 0, AttackValid = 1,
            Name = Tag + (when == When.Died ? " morte" : " combate"),
        };
        // parâmetro da condição e do alvo copiados de um exemplo real do mesmo tipo no arquivo (nada de bytes chutados)
        t.Root = new AiCondNode { Type = CondOf(when), Param = ExampleCond(f, CondOf(when)) ?? Array.Empty<byte>() };
        var vals = lay.Zero(ver);
        // o cliente põe o nome de quem matou SÓ onde o texto tem "$name" (EC_UIHelper ReplaceNameInPolicyChat), com a
        // máscara 1 levando o ID dele; sem a máscara (versão < 17) o $name viraria "???"
        bool name = appendName && CanAppendName(ver);
        vals[0] = prefix + (name && !text.Contains(NameTag, StringComparison.Ordinal) ? NameTag + " " : "") + text;
        if (vals.Count > 1) vals[1] = name ? 1 : 0;
        int tt = TargetOf(when);
        t.Ops.Add(new AiOp { Type = 2, Param = lay.Encode(ver, vals), Target = new AiTarget { Type = tt, Param = ExampleTarget(f, tt) ?? Array.Empty<byte>() } });
        return t;
    }

    static byte[]? ExampleCond(AiPolicyFile f, int type)
    {
        static AiCondNode? Walk(AiCondNode? n, int type) => n is null ? null : n.Type == type ? n : Walk(n.Left, type) ?? Walk(n.Right, type);
        foreach (var t in f.Policies.SelectMany(p => p.Triggers))
            if (Walk(t.Root, type) is { } n) return n.Param.ToArray();
        return null;
    }

    static byte[]? ExampleTarget(AiPolicyFile f, int type) =>
        f.Policies.SelectMany(p => p.Triggers).SelectMany(t => t.Ops).FirstOrDefault(o => o.Target.Type == type)?.Target.Param.ToArray();

    /// <summary>
    /// Política só deste monstro: se outros monstros usam a mesma, clona com ID novo (maior + 1) e devolve o ID novo
    /// (quem chama troca o common_strategy do monstro no elements, cliente e servidor). Se só ele usa, devolve a própria.
    /// </summary>
    public static AiPolicy OwnPolicy(AiPolicyFile f, int policyId, int usersOfPolicy, out bool cloned)
    {
        var p = f.Policies.FirstOrDefault(x => x.Id == policyId);
        cloned = false;
        if (p is not null && usersOfPolicy <= 1) return p;
        int id = f.Policies.Count == 0 ? 1 : f.Policies.Max(x => x.Id) + 1;
        var np = p?.Clone(id) ?? new AiPolicy { Version = f.Policies.FirstOrDefault()?.Version ?? 0, Id = id };
        f.Policies.Add(np);
        cloned = true;
        return np;
    }
}
