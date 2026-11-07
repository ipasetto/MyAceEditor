using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld;

public enum PairState { Ok, ClientOnly, NoServerFile, ServerBroken, StampDiffers, ListsDiffer, ReadOnly }

public enum SaveKind { ClientOnly, Both, ChangedOnDisk, Locked, Failed, RolledBack }

/// <param name="BackupPath">Cópia de segurança, quando a volta falhou e a pessoa precisa dela.</param>
public sealed record SaveResult(SaveKind Kind, string Message, string? BackupPath = null);

/// <summary>
/// O par precinct.clt (cliente) e precinct.sev (servidor) de um mapa: abre, compara, salva com cópia e volta, e guarda retratos para o
/// Ctrl+Z. Sem tela. Carimbos diferentes dão erro de dados ao entrar (CheckOutInst): mudou o .sev, grava carimbo novo nos dois; mudou
/// só nome, música ou marcas, o .sev e o carimbo ficam como estão.
/// </summary>
public sealed class ZonePair
{
    public string ClientPath { get; }
    public string? ServerPath { get; }
    public PrecinctFile Client { get; private set; } = new();
    public PrecinctServer? Server { get; private set; }
    public PairState State { get; private set; }
    public string StateText { get; private set; } = "";
    /// <summary>O mapa ainda não tem precinct.clt: salvar cria o par.</summary>
    public bool Empty { get; private set; }
    byte[]? _clientBytes, _serverBytes;

    ZonePair(string client, string? server) { ClientPath = client; ServerPath = server; }

    public IReadOnlyList<int> Instances => [.. Client.Areas.Select(a => a.SrcInstance).Distinct().Order()];
    /// <summary>Forma e regras chegam ao servidor. Falso com ListsDiffer barra; nos outros casos a tela pergunta antes.</summary>
    public bool CanEditServerFields => State is PairState.Ok or PairState.StampDiffers;
    public bool ServerUnreachable => State is PairState.ClientOnly or PairState.NoServerFile or PairState.ServerBroken;

    /// <summary>Cópia para editar: nada aqui toca em Client até salvar.</summary>
    public PrecinctFile Working() => Empty || State == PairState.ReadOnly ? new PrecinctFile { Stamp = Client.Stamp } : PrecinctFile.Parse(Client.Render());

    public static ZonePair Open(string mapDir, string? serverMapDir)
    {
        var p = new ZonePair(Path.Combine(mapDir, "precinct.clt"), serverMapDir is null ? null : Path.Combine(serverMapDir, "precinct.sev"));
        p.Load();
        return p;
    }

    void Load()
    {
        Client = new PrecinctFile(); Server = null; _clientBytes = _serverBytes = null;
        Empty = !File.Exists(ClientPath);
        if (!Empty && !LoadClient()) return;
        if (ServerPath is null) { Set(PairState.ClientOnly, Loc.T("zp.clientOnly", "Só o cliente será salvo: a pasta do servidor não está aberta.")); return; }
        if (!File.Exists(ServerPath)) { Set(PairState.NoServerFile, string.Format(Loc.T("zp.noSev", "Só o cliente será salvo: o servidor não tem as zonas do mapa {0}."), Path.GetFileName(Path.GetDirectoryName(ServerPath)))); return; }
        try { _serverBytes = File.ReadAllBytes(ServerPath); Server = PrecinctServer.Parse(_serverBytes); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            AppLog.Error("precinct.sev", ex);
            Server = null;
            Set(PairState.ServerBroken, Loc.T("zp.sevBroken", "O arquivo de zonas do servidor está com defeito: só o cliente será salvo."));
            return;
        }
        if (Empty) { Set(PairState.Ok, Loc.T("zp.empty", "Este mapa ainda não tem zonas.")); return; }
        if (Server.Areas.Count != Client.Areas.Count)
            Set(PairState.ListsDiffer, string.Format(Loc.T("zp.lists", "O cliente tem {0} zonas e o servidor tem {1}. Dá para mudar nome, música e lugares; forma e regras só depois de Usar o cliente como certo."), Client.Areas.Count, Server.Areas.Count));
        else if (Server.Stamp != Client.Stamp)
            Set(PairState.StampDiffers, Loc.T("zp.stamp", "O jogador vê um erro de dados neste mapa: cliente e servidor estão diferentes. Igualar resolve."));
        else Set(PairState.Ok, Loc.T("zp.ok", "Cliente e servidor combinam"));
    }

    bool LoadClient()
    {
        try
        {
            _clientBytes = File.ReadAllBytes(ClientPath);
            Client = PrecinctFile.Parse(_clientBytes);
            if (Client.Version != 7) { Set(PairState.ReadOnly, string.Format(Loc.T("zp.version", "Este arquivo de zonas é da versão {0}: dá para ver, mas não para mudar."), Client.Version)); return false; }
            if (!Client.Render().AsSpan().SequenceEqual(_clientBytes)) { Set(PairState.ReadOnly, Loc.T("zp.unknown", "Este arquivo tem um formato que não conheço: dá para ver, mas não para mudar.")); return false; }
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Error("precinct.clt", ex);
            Client = new PrecinctFile();
            Set(PairState.ReadOnly, Loc.T("zp.broken", "O arquivo de zonas do cliente está com defeito e não abriu. Nada será gravado por cima."));
            return false;
        }
    }

    void Set(PairState state, string text) { State = state; StateText = text; }

    /// <summary>Forma, prioridade, destino, origem, território, proteção, cidade ou ordem: tudo o que o .sev guarda.</summary>
    public static bool ServerChanged(PrecinctFile a, PrecinctFile b) =>
        !PrecinctServer.From(a, 0).Render().AsSpan().SequenceEqual(PrecinctServer.From(b, 0).Render());

    public SaveResult Save(PrecinctFile changed, bool overwriteExternal = false) => Write(changed, overwriteExternal, force: false);

    /// <summary>a40: regrava o .sev a partir do cliente. A tela mostra antes o que some.</summary>
    public SaveResult UseClientAsTruth() => Server is null ? Fail(Loc.T("zp.needServer", "Abra a pasta do servidor primeiro.")) : Write(Working(), false, force: true);

    static SaveResult Fail(string text) => new(SaveKind.Failed, text);

    SaveResult Write(PrecinctFile changed, bool overwriteExternal, bool force)
    {
        if (State == PairState.ReadOnly) return Fail(StateText);
        bool serverChange = ServerChanged(Client, changed);
        if (State == PairState.ListsDiffer && serverChange && !force) return Fail(StateText);
        // o .sev entra quando o que ele guarda mudou, quando o carimbo precisa igualar e ao criar o par do zero
        bool both = ServerPath is not null && !ServerUnreachable && (serverChange || force || State == PairState.StampDiffers) || Empty && ServerPath is not null && State != PairState.ServerBroken;

        changed.Stamp = both ? NewStamp() : Client.Stamp;
        byte[] client, server = [];
        try
        {
            client = changed.Render();
            if (!PrecinctFile.Parse(client).Render().AsSpan().SequenceEqual(client)) return Fail(Loc.T("zp.selfCheck", "A conferência antes de gravar falhou. Nada foi salvo."));
            if (both)
            {
                server = PrecinctServer.From(changed, changed.Stamp).Render();
                if (PrecinctServer.Parse(server).Areas.Count != changed.Areas.Count) return Fail(Loc.T("zp.selfCheck", "A conferência antes de gravar falhou. Nada foi salvo."));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException) { AppLog.Error("zonas: montar", ex); return Fail(ex.Message); }

        string? copy = null;
        try
        {
            if (!overwriteExternal && ChangedOutside())
                return new(SaveKind.ChangedOnDisk, Loc.T("zp.outside", "O arquivo mudou fora do editor desde que foi aberto."));
            if (File.Exists(ClientPath)) copy = BackupStore.Save(ClientPath);
            if (both && File.Exists(ServerPath!)) BackupStore.Save(ServerPath!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Refused(ex); }

        var before = _clientBytes;
        try { AtomicFileSave.Write(ClientPath, client); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Refused(ex); }
        if (both)
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ServerPath!)!);
                AtomicFileSave.Write(ServerPath!, server);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("zonas: servidor", ex);
                try
                {
                    if (before is null) File.Delete(ClientPath); else AtomicFileSave.Write(ClientPath, before);
                    return new(SaveKind.RolledBack, Loc.T("zp.rolledBack", "Nada foi salvo: o arquivo do servidor não aceitou. Feche o programa que está usando ele e tente de novo."));
                }
                catch (Exception back) when (back is IOException or UnauthorizedAccessException)
                {
                    AppLog.Error("zonas: volta", back);
                    return new(SaveKind.Failed, string.Format(Loc.T("zp.noWayBack", "O cliente foi gravado, mas não voltou. Há uma cópia em {0}."), copy ?? "?"), copy);
                }
            }

        Load();
        if (_clientBytes is null || !_clientBytes.AsSpan().SequenceEqual(client)) return Fail(Loc.T("zp.reread", "O arquivo gravado não bate com o que devia. Confira o disco."));
        return both ? new(SaveKind.Both, Loc.T("zp.savedBoth", "Salvo no cliente e no servidor. Reinicie o mapa no servidor e o jogo."))
            : new(SaveKind.ClientOnly, Loc.T("zp.savedClient", "Salvo. Feche e abra o jogo para ver."));
    }

    static SaveResult Refused(Exception ex)
    {
        AppLog.Error("zonas: cliente", ex);
        return ex is UnauthorizedAccessException ? Fail(ex.Message) : new(SaveKind.Locked, Loc.T("zp.locked", "Feche o programa que está usando o arquivo e tente de novo."));
    }

    uint NewStamp()
    {
        uint s = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        while (s == Client.Stamp || s == Server?.Stamp) s++;
        return s;
    }

    bool ChangedOutside()
    {
        static bool Differs(string? path, byte[]? opened) =>
            path is not null && (opened is null ? File.Exists(path) : !File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(opened));
        return Differs(ClientPath, _clientBytes) || ServerPath is not null && !ServerUnreachable && Differs(ServerPath, _serverBytes);
    }

    /// <summary>Retrato do par para o Ctrl+Z depois de salvar.</summary>
    public byte[] Snapshot()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach (var b in new[] { _clientBytes, _serverBytes }) { w.Write(b?.Length ?? -1); if (b is not null) w.Write(b); }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Volta o retrato com carimbo novo. Sem o servidor que o retrato tinha, não toca em nada: o par não fica pela metade.</summary>
    public SaveResult Restore(byte[] snapshot)
    {
        using var r = new BinaryReader(new MemoryStream(snapshot));
        byte[]? Part() => r.ReadInt32() is >= 0 and var n ? r.ReadBytes(n) : null;
        byte[]? client = Part(), server = Part();
        if (server is not null && (ServerPath is null || !File.Exists(ServerPath)))
            return Fail(Loc.T("zp.restoreNoServer", "Não dá para desfazer: o arquivo do servidor não está mais lá. Nada mudou."));
        return Save(client is null ? new PrecinctFile() : PrecinctFile.Parse(client), overwriteExternal: true);
    }
}
