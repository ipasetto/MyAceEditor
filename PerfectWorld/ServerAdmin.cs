using MyAceEditor.UI;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Painel de administração do servidor local (o que o iweb do pacote faz, PW-KB §S8 §S30):
///   contas (MySQL "pw", via pwctl)      — listar, criar, senha, GM, gold, banir/liberar, excluir;
///   gdeliveryd :29100 (GNET)            — online, atributos do mundo (EXP/dinheiro/drop/SP em dobro, travas), aviso geral,
///                                         expulsar e calar;
///   gamedbd :29400 (GNET)               — personagens de uma conta, dados básicos de cada um e excluir (com o
///                                         uniquenamed :29401 liberando o nome); excluir conta leva os personagens;
///   mapas (pwctl)                       — quais gs estão rodando, RAM, iniciar/parar instância.
/// Tudo na máquina local; nenhuma senha ou chave aparece aqui.
/// </summary>
public sealed class ServerAdmin
{
    public const string Host = "127.0.0.1";
    public const int DeliveryPort = 29100, DbPort = 29400;
    /// <summary>gmroleid/localsid enviados pelo painel (o iweb também usa -1: não é um personagem).</summary>
    const int PanelRole = -1;

    readonly LocalServer _srv;
    public ServerAdmin(LocalServer srv) { _srv = srv; }

    // ---------- contas ----------
    public sealed record Account(int Id, string Login, string Created, bool Gm, bool Online, string LastLogin, DateTime? BannedUntil, int PendingGold);

    public List<Account> Accounts()
    {
        var list = new List<Account>();
        foreach (var line in _srv.Bash("pwctl accounts", timeoutMs: 60_000).Output.Split('\n'))
        {
            var c = line.TrimEnd('\r').Split('\t');
            if (c.Length < 8 || !int.TryParse(c[0], out var id)) continue;
            DateTime? ban = long.TryParse(c[6], out var until) && DateTimeOffset.FromUnixTimeSeconds(until).LocalDateTime is var d && d > DateTime.Now ? d : null;
            list.Add(new(id, c[1], c[2], int.TryParse(c[3], out var g) && g > 0, c[4].Length > 0, c[5], ban, int.TryParse(c[7], out var cash) ? cash / 100 : 0));
        }
        return list;
    }

    static string? Ok(LocalServer.RunResult r) => r.Code == 0 && r.Output.Contains("##OK") ? null : r.Output.Trim();
    public string? SetGm(int uid, bool on) => Ok(_srv.Bash($"pwctl gm {uid} {(on ? "on" : "off")}", timeoutMs: 60_000));
    public string? Gold(int uid, int gold) => Ok(_srv.Bash($"pwctl gold {uid} {gold}", timeoutMs: 60_000));
    public string? Unban(int uid) => Ok(_srv.Bash($"pwctl unban {uid}", timeoutMs: 60_000));

    /// <summary>Proíbe o login por <paramref name="seconds"/> e, se a conta estiver no jogo, derruba.</summary>
    public string? Ban(int uid, int seconds, string reason)
    {
        var err = Ok(_srv.Bash($"pwctl ban {uid} {seconds}", timeoutMs: 60_000));
        if (err is null) try { Kick(uid, seconds, reason); } catch { /* fora do jogo ou serviço parado: a proibição já está gravada */ }
        return err;
    }

    // ---------- gdeliveryd ----------
    GnetConnection Delivery() => new(Host, DeliveryPort);
    GnetConnection Db() => new(Host, DbPort);

    public (int Total, int Local) OnlineCount()
    {
        using var c = Delivery();
        c.Send(350, new GnetWriter().Int(PanelRole).UInt(0).ToArray());                 // GMOnlineNum
        var r = c.Receive(351); r.Int(); r.UInt();
        return (r.Int(), r.Int());
    }

    public sealed record OnlineUser(int UserId, int RoleId, string Name, int GsId);

    public List<OnlineUser> Online()
    {
        using var c = Delivery();
        var list = new List<OnlineUser>();
        int handler = -1;
        for (int page = 0; page < 1000; page++)
        {
            c.Send(352, new GnetWriter().Int(PanelRole).UInt(0).Int(handler).Octets(ReadOnlySpan<byte>.Empty).ToArray());   // GMListOnlineUser
            var r = c.Receive(353);
            r.Int(); r.Int(); r.UInt(); handler = r.Int();
            uint n = r.Compact();
            for (int i = 0; i < n; i++)
            {
                int userid = r.Int(), roleid = r.Int(); r.Int(); r.UInt(); int gs = r.Int(); r.Byte();
                list.Add(new(userid, roleid, r.Text(), gs));
            }
            if (handler == -1 || n == 0) break;
        }
        return list;
    }

    /// <summary>Atributos do mundo (Privilege, gdeliveryd): valor 0/1, salvo o multiplicador de EXP.</summary>
    public enum Attr : byte { DoubleExp = 204, Lambda = 205, NoTrade = 207, NoAuction = 208, NoMail = 209, NoFaction = 210, DoubleMoney = 211, DoubleDrop = 212, DoubleSp = 213 }

    public byte? GetAttr(Attr a)
    {
        using var c = Delivery();
        var r = c.Rpc(376, w => w.Int(PanelRole).UInt(0).Byte((byte)a));                    // GMGetGameAttri
        var v = r.Octets();
        return v.Length > 0 ? v[0] : null;
    }

    /// <summary>Vale até o servidor reiniciar (o gdeliveryd guarda na memória, como no iweb).</summary>
    public bool SetAttr(Attr a, byte value)
    {
        using var c = Delivery();
        var r = c.Rpc(377, w => w.Int(PanelRole).UInt(0).Byte((byte)a).Octets(new[] { value }));   // GMSetGameAttri
        return r.Int() == 0;
    }

    /// <summary>Aviso para todos (canal de anúncio, como o "broadcast" do iweb).</summary>
    public void Broadcast(string message)
    {
        using var c = Delivery();
        // PublicChat (79), como o DeliveryDB.broadcast do iweb: canal 9 (anúncio), emoção 0, roleid -1 (sistema), localsid 0,
        // texto, dados vazios. O ChatBroadCast (120) é o caminho gs/gdeliveryd -> glinkd; mandado ao gdeliveryd, era ignorado.
        c.Send(79, new GnetWriter().Byte(9).Byte(0).Int(-1).Int(0).Text(message).Octets(ReadOnlySpan<byte>.Empty).ToArray());
        Thread.Sleep(300);   // dá tempo de o gdeliveryd ler antes de a conexão fechar
    }

    /// <summary>Derruba a conta do jogo e proíbe o login por <paramref name="seconds"/> (GMKickoutUser).</summary>
    public void Kick(int userid, int seconds, string reason)
    {
        using var c = Delivery();
        c.Send(354, new GnetWriter().Int(PanelRole).Int(0).Int(userid).Int(seconds).Text(reason).ToArray());
        try { c.Receive(355, timeoutMs: 4000); } catch (TimeoutException) { }
    }

    /// <summary>Proíbe o chat da conta por <paramref name="seconds"/> (GMShutup).</summary>
    public void Mute(int userid, int seconds, string reason)
    {
        using var c = Delivery();
        c.Send(356, new GnetWriter().Int(PanelRole).UInt(0).Int(userid).Int(seconds).Text(reason).ToArray());
        try { c.Receive(357, timeoutMs: 4000); } catch (TimeoutException) { }
    }

    // ---------- gamedbd (só leitura) ----------
    public sealed record Role(int Id, string Name);

    public List<Role> Roles(int userid)
    {
        using var c = Db();
        var r = c.Rpc(3401, w => w.Int(userid));                                             // GetUserRoles
        var list = new List<Role>();
        if (r.Int() != 0) return list;
        uint n = r.Compact();
        for (int i = 0; i < n; i++) list.Add(new(r.Int(), r.Text()));
        return list;
    }

    public sealed record RoleInfo(int Id, string Name, int Race, int Cls, int Gender, DateTime? Created, DateTime? LastLogin,
        int Level, int Exp, int Sp, int Hp, int Mp, float X, float Y, float Z, int WorldTag, int Reputation);

    public RoleInfo? RoleDetails(int roleid)
    {
        using var c = Db();
        // GRoleBase: só os campos do começo (iguais em todas as versões)
        var b = c.Rpc(3013, w => w.UInt((uint)roleid));                                      // GetRoleBase
        if (b.Int() != 0) return null;
        b.Byte(); int id = (int)b.UInt(); string name = b.Text(); int race = b.Int(), cls = b.Int(); int gender = b.Byte();
        b.Octets(); b.Octets(); b.UInt(); b.Byte(); b.Int();
        int created = b.Int(), last = b.Int();
        var s = c.Rpc(3015, w => w.UInt((uint)roleid));                                      // GetRoleStatus
        if (s.Int() != 0) return new(id, name, race, cls, gender, Unix(created), Unix(last), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        s.Byte();
        int level = s.Int(); s.Int(); int exp = s.Int(), sp = s.Int(); s.Int(); int hp = s.Int(), mp = s.Int();
        float x = s.Float(), y = s.Float(), z = s.Float(); int tag = s.Int(); s.Int(); s.Int(); s.Int(); int rep = s.Int();
        return new(id, name, race, cls, gender, Unix(created), Unix(last), level, exp, sp, hp, mp, x, y, z, tag, rep);
    }

    static DateTime? Unix(int t) => t > 0 ? DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime : null;

    // ---------- excluir (como o deleterole.jsp do iweb) ----------
    public const int UniqueNamePort = 29401;

    /// <summary>
    /// Exclui o personagem de vez: DBDeleteRole (3008) no gamedbd, depois PostDeleteRole (3039) no uniquenamed para o
    /// nome ficar livre. null = ok; senão, o erro. Quem estiver com ele no jogo deve sair antes.
    /// </summary>
    public string? DeleteRole(int roleid)
    {
        int userid; byte[] name;
        using (var c = Db())
        {
            var r = c.Rpc(3008, w => w.Int(roleid).Byte(0));                                  // DBDeleteRole, sem rollback
            int ret = r.Int();
            if (ret != 0) return string.Format(Loc.T("adm.deleteRoleRefused", "O banco do jogo recusou excluir o personagem (código {0})."), ret);
            userid = r.Int(); r.Int(); r.Int(); name = r.Octets();
        }
        try
        {
            using var u = new GnetConnection(Host, UniqueNamePort);
            u.Rpc(3039, w => w.Int(userid).Int(ZoneId()).Int(roleid).Octets(name));          // PostDeleteRole
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        { return Loc.T("adm.roleDeletedNameKept", "Personagem excluído, mas o nome não foi liberado (o uniquenamed não respondeu)."); }
        return null;
    }

    /// <summary>Exclui a conta e, antes, os personagens dela (o banco do jogo não apaga sozinho). null = ok.</summary>
    public string? DeleteAccount(int uid)
    {
        foreach (var role in Roles(uid))
            if (DeleteRole(role.Id) is { } err) return $"{role.Name}: {err}";
        _srv.EnsureTools();   // pwctl desta versão (com "deluser") no Linux
        return Ok(_srv.Bash($"pwctl deluser {uid}", timeoutMs: 60_000));
    }

    /// <summary>zoneid do gdeliveryd instalado (1 no pacote padrão).</summary>
    int ZoneId()
    {
        var o = _srv.Bash("grep -m1 -E '^zoneid' /home/gdeliveryd/gamesys.conf", timeoutMs: 30_000).Output;
        var m = Regex.Match(o, @"zoneid\s*=\s*(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
    }

    /// <summary>Classes (playertemplate.h USER_CLASS_*): nomes da versão brasileira onde conhecidos.</summary>
    public static string ClassName(int cls) => cls switch
    {
        0 => Loc.T("class.0", "Guerreiro"), 1 => Loc.T("class.1", "Mago"), 2 => Loc.T("class.2", "Espiritualista"),
        3 => Loc.T("class.3", "Feiticeira"), 4 => Loc.T("class.4", "Bárbaro"), 5 => Loc.T("class.5", "Mercenário"),
        6 => Loc.T("class.6", "Arqueiro"), 7 => Loc.T("class.7", "Sacerdote"), 8 => Loc.T("class.8", "Seeker"), 9 => Loc.T("class.9", "Místico"),
        10 => "Duskblade", 11 => "Stormbringer",
        _ => Loc.T("svadm.class", "classe ") + cls,
    };

    // ---------- mapas (um gs por mapa) ----------
    public sealed record MapDef(string Tag, int WorldTag, string Name, bool Instance);
    public sealed record MapProc(string Tag, int RamMb);

    /// <summary>Mapas que o gs.conf declara (world_servers + instance_servers), com o nome do configs\instance.txt do cliente.</summary>
    public static List<MapDef> MapList(string gsConf, string? clientElement)
    {
        var list = new List<MapDef>();
        if (!File.Exists(gsConf)) return list;
        var text = File.ReadAllText(gsConf, Encoding.Latin1);
        string Key(string k) => Regex.Match(text, @"(?mi)^\s*" + k + @"\s*=\s*(.+)$").Groups[1].Value;
        var names = clientElement is null ? null : MapNames(clientElement);
        foreach (var (key, inst) in new[] { ("world_servers", false), ("instance_servers", true) })
            foreach (var tag in Key(key).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (list.Any(m => m.Tag == tag)) continue;
                // a tag pode estar em qualquer uma das seções do mapa: no 1.5.x [World_is61] vem vazia e a tag fica em [Instance_is61]
                int wt = Regex.Matches(text, @"(?ms)^\[(?:World|Instance)_" + Regex.Escape(tag) + @"\](.*?)(?=^\[|\z)")
                    .Select(sect => Regex.Match(sect.Groups[1].Value, @"(?mi)^\s*tag\s*=\s*(\d+)"))
                    .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).FirstOrDefault();
                list.Add(new(tag, wt, names is not null && names.TryGetValue(wt, out var n) ? n : "", inst));
            }
        return list;
    }

    static Dictionary<int, string>? MapNames(string elementDir)
    {
        try
        {
            var pckPath = Path.Combine(elementDir, "configs.pck");
            if (!File.Exists(pckPath)) return null;
            using var pck = new PckArchive(pckPath);
            if (pck.Find(@"configs\instance.txt") is not { } e) return null;
            var bytes = pck.Read(e);
            var text = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2) : Encoding.Unicode.GetString(bytes);
            text = Regex.Replace(text, @"//[^\n]*", "");
            var map = new Dictionary<int, string>();
            // o instance.txt dos clientes 144 (inclusive o BR) ainda é chinês: nome ilegível não é mostrado
            foreach (Match m in Regex.Matches(text, @"""([^""]*)""\s*\{\s*(\d+)"))
                if (!m.Groups[1].Value.Any(ch => ch is >= '⺀' and <= '鿿'))
                    map.TryAdd(int.Parse(m.Groups[2].Value), m.Groups[1].Value);
            map.TryAdd(1, Loc.T("svadm.mainWorld", "Mundo principal"));
            return map;
        }
        catch { return null; }
    }

    public (List<MapProc> Running, int RamTotalMb, int RamFreeMb) Maps()
    {
        var running = new List<MapProc>(); int total = 0, free = 0;
        foreach (var line in _srv.Bash("pwctl maps", timeoutMs: 60_000).Output.Split('\n'))
        {
            var p = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 3 && p[0] == "##RAM") { int.TryParse(p[1], out total); int.TryParse(p[2], out free); }
            else if (p.Length == 2 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mb)) running.Add(new(p[0], mb));
        }
        return (running, total, free);
    }

    public LocalServer.RunResult StartMap(string tag) { _srv.EnsureTools(); return _srv.Bash($"pwctl gs-start {tag}", timeoutMs: 120_000); }
    public LocalServer.RunResult StopMap(string tag) => _srv.Bash($"pwctl gs-stop {tag}", timeoutMs: 60_000);
}
