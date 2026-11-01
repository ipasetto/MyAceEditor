import os, re, collections
S = "D:/Perfect World/source"
OUT = "C:/Godot/MyAceEditor/.claude/skills/pw-engine/SOURCE-MAP.md"
EXT = (".cpp", ".h", ".hxx", ".cxx", ".c", ".hpp", ".inl", ".xml")

# (secao, titulo, pasta relativa a S, recursivo?)
AREAS = [
    ("M1", "gs (servidor de mapa): cgame/gs", "source_server_153/cgame/gs", False),
    ("M2", "gs/item (itens, addons, equipamento)", "source_server_153/cgame/gs/item", False),
    ("M3", "gs/template (elementdataman, itemdataman, geracao de item)", "source_server_153/cgame/gs/template", True),
    ("M4", "gs/instance (instancias, battleground)", "source_server_153/cgame/gs/instance", True),
    ("M5", "gs/task (quests no servidor)", "source_server_153/cgame/gs/task", True),
    ("M6", "cgame/common + include + collision + io + libgs", "source_server_153/cgame", "cgame-misc"),
    ("M7", "cnet/gdeliveryd (login, contas, cash, GM)", "source_server_153/cnet/gdeliveryd", False),
    ("M8", "cnet/gamedbd (banco: personagens, itens)", "source_server_153/cnet/gamedbd", False),
    ("M9", "cnet outros daemons (glinkd gauthd gfaction uniquenamed logservice gacd) + rpcalls.xml", "source_server_153/cnet", "cnet-misc"),
    ("M10", "share (protocolo/rpc gerado)", "source_server_153/share", True),
    ("M11", "cliente: CElementClient (EC_* = jogo, Dlg* = janelas)", "source_client_153/CElementClient", True),
    ("M12", "cliente: CElementData (elements.data / templates)", "source_client_153/CElementData", True),
    ("M13", "cliente: CCommon + CEleDataMan + CElementSkill + CTaskTemplEditor", "source_client_153", "client-misc"),
    ("M14", "engine Angelica 2.2 (A3D render, AFile .pck, AUI)", "Angelica2.2_src/Angelica2.2", "angelica"),
    # servidor 1.7.2 (D:/Perfect World/PW/Source Client/172Source): mais novo que o 1.5.3; comparar quando a versao importar
    ("M15", "servidor 1.7.2: cgame/gs", "../PW/Source Client/172Source/cgame/gs", False),
    ("M16", "servidor 1.7.2: gs/item + gs/template + cskill (habilidades)", "../PW/Source Client/172Source", "s172-misc"),
    # ferramentas oficiais do cliente (D:/Perfect World/Estudo PW/CPW_Dev, GBK): editor de mapas e de quests; roteiro do Mundo 3D em docs/MUNDO-3D-EDITOR.md
    ("M17", "CPW_Dev: CElementEditor = editor OFICIAL de mapas (cena 3D, objetos, terreno, exportadores ecwld/ecbsd/npcgen/precinct/region/path/bht)", "../Estudo PW/CPW_Dev/CElement/CElementEditor", False),
    ("M18", "CPW_Dev: CTaskTemplEditor (editor oficial de quests) + CRandomMap_WYD + CElementData", "../Estudo PW/CPW_Dev/CElement/CTaskTemplEditor", True),
    # Angelica Core / UDE (C# net48, codigo recuperado e desofuscado em src_deobf; familia RAE): referencia de estudo de formatos e
    # funcoes (mapa 3D SharpDX, npcgen ao vivo, precinct/region/path, world_targets, domain); codigo do MyAceEditor e nosso (nao copiar)
    ("M19", "AngelicaCore_V2 (C#): ACE.PWMap = editor de mapa 3D (SharpDX) + demais modulos por namespace", "../PW/Angelica/AngelicaCore_V2/Editor/src_deobf", "angelicacore"),
]

def stems(paths):
    out = sorted({os.path.splitext(os.path.basename(p))[0] for p in paths}, key=str.lower)
    return out

def files_in(d, rec):
    res = []
    if not os.path.isdir(d): return res
    if rec:
        for r, ds, fs in os.walk(d):
            if "CVS" in r: continue
            res += [os.path.join(r, f) for f in fs if f.lower().endswith(EXT)]
    else:
        res = [os.path.join(d, f) for f in os.listdir(d) if f.lower().endswith(EXT)]
    return res

def compact(names):
    # agrupa por prefixo comum (EC_, Dlg, A3D...) para economizar
    groups = collections.OrderedDict()
    for n in names:
        m = re.match(r"^(EC_|Dlg|A3D|AUI|AF|AM|AC|A[A-Z][a-z]+|[a-z]+_)", n)
        key = m.group(1) if m and len([x for x in names if x.startswith(m.group(1))]) >= 6 else ""
        groups.setdefault(key, []).append(n[len(key):] if key else n)
    lines = []
    for k, v in groups.items():
        lines.append((f"{k}*: " if k else "") + " ".join(v))
    return lines

out = []
out.append("# Mapa das fontes do Perfect World (para pular direto ao arquivo)\n")
out.append("Raiz: `D:/Perfect World/source`. Servidor = `source_server_153` (1.5.3, cgame = gs; cnet = daemons); cliente = `source_client_153`; engine = `Angelica2.2_src`. Varias copias datadas (cgame_2014..., cnet_2015...) = mesmas fontes em outras datas: use as sem data. NAO extraidos (so arquivos .7z/.tar.gz na raiz): PWSrc 152, antihype server, perfectworld-vip, Npcgen, AngelicaSDK2.\n")
out.append("**Como usar (barato):** Grep `## §M<n>` para a area ou o nome do arquivo neste mapa; depois Grep o simbolo dentro do arquivo indicado. Nunca ler o mapa inteiro. Textos do cliente em GBK (ler com cp936).\n")
out.append("## §M0 Indice por assunto -> arquivo:simbolo (servidor S=`source_server_153/cgame/gs`, cliente C=`source_client_153/CElementClient`)\n")
topics = [
    ("velocidade de movimento / trava 3-5 m/s", "S/playertemplate.h property_policy::UpdateSpeed; S/config.h MAX_RUN_SPEED"),
    ("addons de equipamento (tipo -> handler)", "S/item/item_addon.cpp INSERT_ADDON(id, classe); handlers no mesmo arquivo"),
    ("gerar item (subtipo, arma sem animacao)", "S/template/itemdataman.cpp + generate_item_temp.h generate_armor/weapon/decoration/fashion"),
    ("normalizar chances do elements", "S/template/elementdataman.cpp elementdataman::NormalizeRandom"),
    ("loja de cash no gs (limite 65535)", "S/worldmanager.cpp world_manager::SetCashItem"),
    ("monstros/NPC: carga dos templates", "S/npc*.cpp npc_stubs_manager::LoadTemplate (grep)"),
    ("IA de monstro (aipolicy)", "S/aipolicy.cpp, S/ainpc.cpp"),
    ("comandos de GM do console (d 8888 teleporte)", "S/playercmd.cpp (case 8888) -> LongJump (S/actsession.cpp) / PlaneSwitch (S/player.cpp)"),
    ("instancias e campo de batalha", "S/instance/instance_manager.cpp, battleground_manager.cpp"),
    ("quests no servidor", "S/task/ (grep TaskInterface), S/item/item_taskdice.cpp"),
    ("path.sev (caminhos de patrulha) e npcgen (area, agressivo, caminho)", "S/template/sevbezier.cpp CSevBezierMan::Load, S/template/pathman.cpp path_manager::Init; S/npcgenerator.cpp (dwAggressive, vExts*0.5)"),
    ("objetivos de quest (enum, leitura por metodo, tirar itens ao concluir)", "source_client_153/CElementClient/Task/TaskTempl.h enumTM*, TaskTempl.cpp Unmarshal* (switch m_enumMethod), TaskProcess.cpp RemoveAcquiredItem/m_bClearAcquired; MAX_ITEM_WANTED em TaskInterface.h"),
    ("login/contas/cash/GM (daemon)", "cnet/gdeliveryd (§M7); rpc em cnet/rpcalls.xml"),
    ("imagens de texto da batalha (critico, alma...)", "C/EC_ImageRes.cpp CECImageRes::Init (nomes GBK)"),
    ("mapa da tecla M (mundo/instancia)", "C/DlgWorldMap.cpp GetPixelsByPos; C/EC_Instance.cpp (instance.txt)"),
    ("loja de cash no cliente", "C/DlgQShop*.cpp"),
    ("numeros/decals sobre a cabeca", "C/EC_Decal.cpp"),
    ("inventario e itens no cliente", "C/EC_Inventory.cpp, EC_IvtrItem.cpp, EC_IvtrTypes (grep)"),
    ("janelas (AUI: xml, dcf)", "C/EC_UIManager.cpp; engine AUI em Angelica (§M14)"),
    ("pacotes .pck", "Angelica AngelicaFile (AFilePackage) §M14"),
    ("elements.data (formato/templates)", "source_client_153/CElementData §M12 (exptypes.h)"),
    ("habilidades", "source_client_153/CElementSkill §M13"),
    ("guerra territorial: domain.data (cliente)", "source_client_153/CCommon/globaldataman.cpp GlobalData_Load + loadimpl_domain2_data"),
    ("guerra territorial: domain.sev / domain2.sev (servidor)", "source_server_153/cnet/gdeliveryd/domaindataman.cpp domain_data_load / domain2_data_load"),
    ("versao mais nova do servidor (1.7.2)", "D:/Perfect World/PW/Source Client/172Source (§M15-16)"),
    ("porte 1.8.0 -> 1.8.9 (notas de pesquisa)", "D:/Perfect World/Estudo PW/pw-189-port/docs/notes (00 = indice; 14 protocolo 1.7.6x1.8.0; 18 elements v531; 22 aipolicy 1.8.0; 23 tasks v198; 26 comandos; Diario)"),
]
for t, w in topics: out.append(f"- {t}: {w}")
# linhas que entraram a mao no §M0 (achados novos) continuam ao regerar
if os.path.exists(OUT):
    old = open(OUT, encoding="utf-8").read()
    m0 = old.split("## §M0", 1)[-1].split("\n## ", 1)[0]
    have = {f"- {t}: {w}" for t, w in topics}
    out += [l for l in m0.splitlines() if l.startswith("- ") and l not in have]
out.append("")

for sec, title, rel, rec in AREAS:
    base = os.path.join(S, rel)
    if rec == "cgame-misc":
        paths = []
        for sub in ("common", "include", "collision", "io", "libgs"): paths += [(sub, p) for p in files_in(os.path.join(base, sub), True)]
        out.append(f"## §{sec} {title} ({len(paths)} arquivos)\n")
        by = collections.defaultdict(list)
        for sub, p in paths: by[sub].append(p)
        for sub, ps in by.items(): out.append(f"- {sub}/: " + " ".join(stems(ps)))
        out.append(""); continue
    if rec == "cnet-misc":
        out.append(f"## §{sec} {title}\n")
        for sub in ("glinkd", "gauthd", "gfaction", "uniquenamed", "logservice", "gacd", "include", "gamed"):
            ps = files_in(os.path.join(base, sub), True)
            if ps: out.append(f"- {sub}/ ({len(ps)}): " + " ".join(stems(ps)[:120]))
        out.append(""); continue
    if rec == "client-misc":
        out.append(f"## §{sec} {title}\n")
        for sub in ("CCommon", "CEleDataMan", "CElementSkill", "CTaskTemplEditor", "CElementUIPck", "CElementIconTool"):
            ps = files_in(os.path.join(base, sub), True)
            if ps: out.append(f"- {sub}/ ({len(ps)}): " + " ".join(stems(ps)[:150]))
        out.append(""); continue
    if rec == "s172-misc":
        out.append(f"## §{sec} {title}\n")
        for sub in ("cgame/gs/item", "cgame/gs/template", "cskill"):   # cnet do 1.7.2 = mesmos nomes do 1.5.3 (§M7-M9)
            ps = files_in(os.path.join(base, sub), True)
            if ps:
                out.append(f"- {sub}/ ({len(ps)}):")
                out += ["  " + l for l in compact(stems(ps))]
        out.append(""); continue
    if rec == "angelicacore":
        out.append(f"## §{sec} {title}\n")
        for sub in sorted(os.listdir(base)) if os.path.isdir(base) else []:
            d = os.path.join(base, sub)
            if not os.path.isdir(d) or "Dependency" in sub or sub.startswith("_"): continue
            ps = [os.path.join(d, f) for f in os.listdir(d) if f.endswith(".cs") and not f.startswith("-")]
            if ps: out.append(f"- {sub} ({len(ps)}): " + " ".join(stems(ps)[:160]))
        out.append(""); continue
    if rec == "angelica":
        out.append(f"## §{sec} {title}\n")
        for sub in ("AngelicaCommon", "AngelicaFile", "Angelica3D", "AngelicaMedia"):
            ps = files_in(os.path.join(base, sub), True)
            if ps:
                out.append(f"- {sub}/ ({len(ps)}):")
                out += ["  " + l for l in compact(stems(ps))]
        out.append(""); continue
    ps = files_in(base, rec)
    out.append(f"## §{sec} {title} ({len(ps)} arquivos) — `{rel}`\n")
    out += compact(stems(ps))
    out.append("")

open(OUT, "w", encoding="utf-8", newline="\n").write("\n".join(out))
print(OUT, sum(len(l) for l in out), "chars")
