using System.Diagnostics;
using MyAceEditor.PerfectWorld;
using OpenTK.GLControl;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Mundo em 3D só com a câmera (como espectador): terreno (WorldTerrain), construções (WorldBuildings), água, modelos
/// parados dos pontos do npcgen (WorldModels) — tudo carregado em segundo plano (WorldLoader) para não travar.
/// Esquerdo em cima de um ponto arrasta; esquerdo no vazio ou botão do meio gira; direito abre o menu (novo, clonar,
/// mover, excluir, editar no software). Teclas: W/A/S/D, Espaço/Z (voo), Shift, roda (aproxima), M (mapa),
/// Ctrl+Z/Ctrl+Y, Ctrl+C/Ctrl+V, Delete, T (esconde o terreno), H (legenda e atalhos).
/// </summary>
internal sealed partial class WorldView : Form
{
    /// <summary>LabelColor = cor do nome (legenda); OnGround = "no chão" (desenha na altura do terreno quando perto dela).</summary>
    public sealed record Marker(float X, float Y, float Z, Color Color, string Name, object Tag, string? Model = null, float Yaw = 0,
        Color LabelColor = default, bool OnGround = false, int Id = 0, MarkKind Kind = 0);

    /// <summary>Tipo do ponto para o filtro da barra (tipo + gatilho). 0 = sempre visível.</summary>
    [Flags] public enum MarkKind { Monster = 1, Npc = 2, Merchant = 4, Guard = 8, Mine = 16, Event = 32, Trigger = 64 }
    const MarkKind Types = MarkKind.Monster | MarkKind.Npc | MarkKind.Merchant | MarkKind.Guard | MarkKind.Mine, Triggers = MarkKind.Event | MarkKind.Trigger;
    /// <summary>Filtro "Mostrar" (vale para a sessão toda): por padrão tudo menos eventos e gatilhos.</summary>
    static MarkKind _show = MarkKind.Monster | MarkKind.Npc | MarkKind.Merchant | MarkKind.Guard | MarkKind.Mine;
    List<Marker> _allMarks = new();
    static bool Visible(Marker m) => m.Kind == 0 || (m.Kind & Types & _show) != 0 && ((m.Kind & Triggers) == 0 || (m.Kind & Triggers & _show) != 0);

    const System.ComponentModel.DesignerSerializationVisibility Hidden = System.ComponentModel.DesignerSerializationVisibility.Hidden;
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<object, Vector3>? CloneHere { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<object>? EditPoint { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<Vector3, int>? CreateNewHere { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<object>? DeletePoint { get; set; }
    /// <summary>Anel de giro: novo ângulo do ponto (graus; 0 = olhando para +Z, 90 = +X).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<object, float>? RotatePoint { get; set; }
    /// <summary>Novo tamanho total (X, Z) da área de nascimento do ponto (alça do canto da área arrastada no chão).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<object, float, float>? ResizeArea { get; set; }
    /// <summary>Caminhos de patrulha do path.sev do mapa (ID → pontos da curva). O do ponto selecionado aparece sempre; P mostra todos.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Dictionary<int, List<Vec3>>? Paths { get; set; }
    bool _allPaths;
    Action? _syncPathsBtn;   // tecla P liga/desliga o botão Patrulhas do Mostrar
    bool _sceneObjs;   // tecla O: efeitos (.gfx) e sons do cenário (.ecwld)
    /// <summary>Clique no vazio: nenhum ponto selecionado.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action? ClearSelection { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Func<bool>? UndoAction { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Func<int>? UndoDepth { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Func<int>? RedoDepth { get; set; }
    // quests criadas/ligadas aqui: cada uma guarda quantos passos de pontos havia antes, para o Ctrl+Z seguir a ordem real
    readonly List<int> _qUndo = new(), _qRedo = new();
    int _questNpc;
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Func<bool>? RedoAction { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public IReadOnlyList<string> Maps { get; set; } = Array.Empty<string>();
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<string>? SwitchMap { get; set; }
    /// <summary>Pasta watermap do servidor: N.wmap = i32 versão, 2f lado, i32 n, n × (centro x, z relativos ao centro do pedaço, meia-largura x, z, altura).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public string? WaterDir { get; set; }
    /// <summary>Quests que o NPC (ID do elements) dá e recebe; null = não é NPC. Clicar num NPC abre o painel de baixo.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Func<int, NpcQuestInfo?>? NpcQuests { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<int>? OpenQuest { get => _quests.OpenQuest; set { _quests.OpenQuest = value; _creature.OpenQuest = value; } }
    readonly WorldQuestPanel _quests = new();
    readonly WorldCreaturePanel _creature = new();
    /// <summary>Monstro escolhido: preenche o painel da criatura (false = não é ponto de criatura).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] internal Func<object, WorldCreaturePanel, bool>? ShowCreatureInfo { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<int>? EditCreature { get => _creature.EditCreature; set => _creature.EditCreature = value; }
    IQuestHost? _host;
    WorldQuestEditor? _editor;
    Action<int>? _pickNpc;   // painel de criação esperando um clique num NPC
    Action<int>? _pickMonster;   // etapa "matar monstros" esperando um clique num ponto de monstro
    Action<int>? _pickMine;   // etapa "obter/entregar itens" esperando um clique num ponto de coleta
    Action<QuestArea>? _pickArea;              // painel de criação esperando uma área arrastada no chão
    Vector3? _areaA, _areaB;                   // área sendo arrastada (coordenadas do jogo)
    IReadOnlyList<QuestArea> _areas = Array.Empty<QuestArea>(); int _areaSel = -1;   // áreas da quest em edição
    /// <summary>Tag do mapa aberto (gs.conf / instance.txt; mundo = 1); 0 = não sei.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public int MapTag { get; set; }
    /// <summary>Criar quest e dar quest existente (botões da barra do NPC).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)]
    internal IQuestHost? QuestHost
    {
        get => _host;
        set
        {
            _host = value;   // criar/dar/editar/excluir ficam só na ferramenta Quests; o painel de baixo do NPC só mostra (usuário 01/11)
        }
    }

    const float Speed = 40;           // m/s andando; Shift = ×4

    readonly TerrainFile _t;
    readonly Func<IReadOnlyList<Marker>> _markerSrc;
    readonly Action<Vector3, int> _newHere;
    readonly Action<object, Vector3>? _moveHere;
    readonly Func<object?> _selected;
    readonly Action<object> _select;
    readonly GLControl _gl;
    readonly StatusLine _status = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(6, 3, 0, 0), BackColor = Color.FromArgb(28, 32, 38), ForeColor = Color.Gainsboro };
    readonly ToolStrip _showBar = new() { Dock = DockStyle.Bottom, LayoutStyle = ToolStripLayoutStyle.Flow, GripStyle = ToolStripGripStyle.Hidden, Renderer = new DarkBarRenderer(),
        BackColor = Color.FromArgb(28, 32, 38), ForeColor = Color.White, Padding = new Padding(6, 3, 6, 3) };
    readonly ToolStrip _bar = new() { GripStyle = ToolStripGripStyle.Hidden, Renderer = new DarkBarRenderer(), BackColor = Color.FromArgb(28, 32, 38), ForeColor = Color.White, Padding = new Padding(6, 4, 6, 4) };
    readonly ToolStripTextBox _find = new() { Width = 220, BackColor = Color.Black, ForeColor = Color.White };
    readonly ToolStripComboBox _mapBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, BackColor = Color.Black, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly WorldLoader _load = new();
    readonly WorldTerrain _terrain;
    readonly WorldModels _models;
    readonly WorldBuildings _buildings;
    readonly WorldTrees _trees;
    readonly WorldGrass _grass;
    readonly ToolStripButton _grassBtn = new(Loc.T("world.grass", "Grama"), DarkBarRenderer.Dot(Color.FromArgb(124, 179, 66))) { CheckOnClick = true, Checked = true,
        ToolTipText = Loc.T("world.grass.h", "Mostrar ou esconder a grama (G)."), ForeColor = Color.White, TextImageRelation = TextImageRelation.ImageBeforeText };
    readonly WorldSky _sky;
    bool _showBuildings = true;
    bool _gameFog = true;   // N: neblina da área (jogo) ou a longe
    // dia/noite (L): fator 0 = dia, 1 = noite, como CECSunMoon::GetDNFactor; vai aos poucos (~2 s). Na metade troca céu,
    // luz do chão (.t2lm1), cores das construções e a luz das árvores; neblina e luz dos modelos misturam pelo fator.
    bool _nightOn; float _nf; bool _nightApplied;
    readonly HashSet<Keys> _keys = new();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 10 };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Dictionary<string, (int Tex, int W, int H)> _labels = new();
    List<Marker> _marks = new(), _nearMarks = new();
    Vector3 _nearAt = new(float.MaxValue);
    bool _marksDirty; double _marksAt;
    Vector3 _cam;
    float _yaw = -90, _pitch = -25;
    Point _drag, _down; bool _dragging; double _last;
    Vector3? _hover;
    bool _busy;
    int _findAt = -1, _piece = -1;
    Bitmap? _worldMap; float _mapMPerPx = 2;
    InstanceMap? _instMap;   // instância com o mapa grande do jogo (surfaces\ingame\<pasta>.dds): conversão própria
    string? _element;
    object? _movingTag; Vector3? _movePos;   // arrastando um ponto com o botão esquerdo
    // setas do ponto selecionado: 1 = X, 2 = Y (altura), 3 = Z, 4 = anel de giro; 0 = nenhuma
    int _gizmoAxis, _gizmoHover; object? _gizmoTag; Vector3 _gizmoStart; Point _gizmoDown; Vector3? _gizmoPos; float? _gizmoYaw;
    (float X, float Z)? _gizmoSize;   // alça da área (eixo 5): tamanho total novo enquanto arrasta
    object? _clip;                            // Ctrl+C
    int _newLabels;
    bool _legendOpen = LoadLegend(); int _legendTex, _legendW, _legendH; bool _legendState;
    List<(float X0, float Z0, float X1, float Z1, float Y)>? _water;

    public WorldView(TerrainFile t, Vector3 start, Func<IReadOnlyList<Marker>> markers, Func<object?> selected, Action<object> select,
        Action<Vector3, int> newHere, Action<object, Vector3>? moveHere)
    {
        _t = t; _markerSrc = markers; _selected = selected; _select = select; _newHere = newHere; _moveHere = moveHere;
        Text = string.Format(Loc.T("world.title", "Mundo 3D — {0}"), t.Name);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(1280, wa.Width - 40), Math.Min(860, wa.Height - 40)); StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
        _gl = new GLControl(new GLControlSettings { Profile = OpenTK.Windowing.Common.ContextProfile.Compatability, APIVersion = new Version(3, 3) }) { Dock = DockStyle.Fill };
        ToolStripButton Btn(string key, string pt, Action a)
        {
            // com ícone desenhado, o símbolo do começo do texto ("🗺 Mapa", "❔ Atalhos") sai
            var gl = UiKit.GlyphFor(key + " " + pt);
            var t = Loc.T(key, pt); int k = 0;
            while (gl is not null && k < t.Length && !char.IsLetterOrDigit(t[k])) k++;
            return new(t[k..], gl is null ? null : UiKit.Icon(gl, Color.White, 16), (_, _) => a()) { ForeColor = Color.White, Font = new Font("Segoe UI", 9f, FontStyle.Bold), TextImageRelation = TextImageRelation.ImageBeforeText };
        }
        _bar.Items.Add(new ToolStripLabel(UiKit.Icon(UiKit.Search, Color.Silver, 16)) { ToolTipText = Loc.T("world.goto", "Ir para:") });
        _bar.Items.Add(_find);
        _bar.Items.Add(Btn("world.next", "Próximo ▶", FindNext));
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(Btn("world.map", "🗺 Mapa (M)", ShowMap));
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(new ToolStripLabel(Loc.T("world.mapPick", "Mapa:")) { ForeColor = Color.Silver });
        _bar.Items.Add(_mapBox);
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(Btn("world.help2", "❔ Atalhos (H)", ToggleLegend));
        // "Mostrar" numa barra própria EMBAIXO (pedido do usuário 01/11: em cima não cabia em tela pequena); quebra em
        // linhas quando falta largura, em grupos: pontos | só com gatilho | mundo
        _showBar.Items.Add(new ToolStripLabel(Loc.T("world.show", "Mostrar:"), UiKit.Icon("", Color.Silver, 16)) { ForeColor = Color.Silver, TextImageRelation = TextImageRelation.ImageBeforeText });
        foreach (var f in _filterDefs)
        {
            // botão liga/desliga com a cor do tipo de ponto (como uma legenda que também filtra)
            var tb = new ToolStripButton(Loc.T(f.Key, f.Pt), DarkBarRenderer.Dot(f.Color)) { CheckOnClick = true, Checked = (_show & f.Kind) != 0, ToolTipText = Loc.T(f.Key + ".h", f.Tip),
                ForeColor = Color.White, TextImageRelation = TextImageRelation.ImageBeforeText, Margin = new Padding(1, 0, 1, 0) };
            tb.CheckedChanged += (_, _) => { _show = tb.Checked ? _show | f.Kind : _show & ~f.Kind; tb.Image = DarkBarRenderer.Dot(tb.Checked ? f.Color : Color.FromArgb(70, 70, 70)); RefreshMarkers(); _gl.Focus(); };
            if (!tb.Checked) tb.Image = DarkBarRenderer.Dot(Color.FromArgb(70, 70, 70));
            if (f.Kind == MarkKind.Event) _showBar.Items.Add(new ToolStripSeparator());
            _showBar.Items.Add(tb);
        }
        // grama: só mostrar/esconder (G); desenhar grama é pincel do Objetos do mundo
        _showBar.Items.Add(new ToolStripSeparator());
        _showBar.Items.Add(new ToolStripLabel(Loc.T("world.showWorld", "Mundo:")) { ForeColor = Color.Silver });
        _grassBtn.CheckedChanged += (_, _) => { _grass.Visible = _grassBtn.Checked; _grassBtn.Image = DarkBarRenderer.Dot(_grassBtn.Checked ? Color.FromArgb(124, 179, 66) : Color.FromArgb(70, 70, 70)); _gl.Invalidate(); _gl.Focus(); };
        _showBar.Items.Add(_grassBtn);
        // caminhos de patrulha de todos os monstros (riscos rosas; antes só pela tecla P, sem nada dizendo o que eram)
        var pathsBtn = new ToolStripButton(Loc.T("world.allPaths", "Patrulhas"), DarkBarRenderer.Dot(Color.FromArgb(70, 70, 70)))
        { CheckOnClick = true, ForeColor = Color.White, TextImageRelation = TextImageRelation.ImageBeforeText,
          ToolTipText = Loc.T("world.allPaths.h", "Mostrar os caminhos de patrulha de todos os monstros perto (riscos rosas) — tecla P. O do monstro escolhido aparece em laranja na ferramenta Caminhos e áreas.") };
        pathsBtn.CheckedChanged += (_, _) => { _allPaths = pathsBtn.Checked; pathsBtn.Image = DarkBarRenderer.Dot(_allPaths ? Color.FromArgb(205, 140, 255) : Color.FromArgb(70, 70, 70)); _gl.Invalidate(); _gl.Focus(); };
        _syncPathsBtn = () => { if (pathsBtn.Checked != _allPaths) pathsBtn.Checked = _allPaths; };
        _showBar.Items.Add(pathsBtn);
        _find.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(); } };
        _find.TextChanged += (_, _) => _findAt = -1;
        Controls.Add(_gl); Controls.Add(_bar); Controls.Add(_quests); Controls.Add(_creature); Controls.Add(_showBar); Controls.Add(_status);   // painel de quests acima da barra de status
        var element = Path.GetDirectoryName(Path.GetDirectoryName(t.Dir));
        _element = element;
        if (element is not null) _clientNames = MapTags.ClientMapNames(element);
        _terrain = new WorldTerrain(t, _load, element, start);
        _models = new WorldModels(element, _load);
        _buildings = new WorldBuildings(element, t.Name, _load);
        _trees = new WorldTrees(element, t.Name);
        _grass = new WorldGrass(element, t);
        _sky = new WorldSky(element, t.Dir);
        if (_sky.SunDir is { } sun) WorldTrees.Sun = sun;   // luz do mapa nas árvores (geradas depois, ao aparecer)
        // teste do --world-shot: esconder partes para achar quem desenha o quê
        var hide = Environment.GetEnvironmentVariable("MYACE_HIDE") ?? "";
        if (hide.Contains("terrain")) _terrain.Visible = false;
        if (hide.Contains("buildings")) _showBuildings = false;
        if (hide.Contains("grass")) _grassBtn.Checked = false;
        RefreshMarkers();
        HideLidTerrain(start.Y);
        LookAt(start);
        Shown += (_, _) =>
        {
            Activate(); _gl.Focus();   // já abre pronta para W/A/S/D
            _mapBox.Items.AddRange(Maps.Select(m => (object)new MapItem(m)).ToArray());
            _mapBox.SelectedItem = _mapBox.Items.Cast<MapItem>().FirstOrDefault(i => i.Key == Tag as string);
            _mapBox.DropDownWidth = 300;
            // troca só quando a escolha é confirmada (clique, Enter ou lista fechada): com a lista aberta, as setas só andam
            void Commit() { if (!IsDisposed && _mapBox.SelectedItem is MapItem m && m.Key != Tag as string) SwitchMap?.Invoke(m.Key); }
            _mapBox.ComboBox.SelectionChangeCommitted += (_, _) => { if (!_mapBox.DroppedDown) BeginInvoke(Commit); };
            _mapBox.DropDownClosed += (_, _) => BeginInvoke(Commit);
        };
        _gl.Paint += (_, _) => Render();
        _gl.Resize += (_, _) => _gl.Invalidate();
        _gl.MouseDown += (_, e) =>
        {
            _gl.Focus(); _down = _drag = e.Location;
            if (e.Button == MouseButtons.Left && _legendOpen && LegendRect().Contains(e.Location)) { ToggleLegend(); return; }
            if (PathMouseDown(e)) return;   // ferramenta Caminhos: pontos do caminho / desenho
            if (e.Button == MouseButtons.Left && _pickArea is not null && Ray(e.Location) is { } a) { _areaA = _areaB = a; return; }   // marcar área: arrastar no chão
            // setas / anel do ponto selecionado têm preferência
            if (e.Button == MouseButtons.Left && GizmoHit(e.Location) is > 0 and var ax && SelectedMark() is { } gm)
            {
                _gizmoAxis = ax; _gizmoTag = gm.Tag; _gizmoStart = PosOf(gm); _gizmoDown = e.Location; _gizmoPos = null; _gizmoYaw = null; _gizmoSize = null;
                return;
            }
            // esquerdo em cima de um ponto (na tela): arrasta o ponto; no vazio ou botão do meio: gira a câmera
            if (e.Button == MouseButtons.Left && PickScreen(e.Location) is { } mk) { _movingTag = mk.Tag; _movePos = null; return; }
            if (e.Button is MouseButtons.Left or MouseButtons.Middle) _dragging = true;
        };
        _gl.MouseUp += (_, e) =>
        {
            _dragging = false;
            if (PathMouseUp(e)) return;
            if (_gizmoAxis > 0)
            {
                var gt = _gizmoTag!;
                if (_gizmoPos is { } gp && _moveHere is not null) _moveHere(gt, gp);
                if (_gizmoYaw is { } gy && RotatePoint is not null) RotatePoint(gt, gy);
                if (_gizmoSize is { } gsz && ResizeArea is not null) ResizeArea(gt, gsz.X, gsz.Z);
                _gizmoAxis = 0; _gizmoTag = null; _gizmoPos = null; _gizmoYaw = null; _gizmoSize = null;
                RefreshMarkers(); _gl.Invalidate();
                return;
            }
            if (_areaA is { } a && _pickArea is { } done)
            {
                var area = AreaOf(a, _areaB ?? a);
                _areaA = _areaB = null; _pickArea = null;
                _status.Text = string.Format(Loc.T("qe.areaDone", "Área marcada: {0:0} × {1:0} m, altura {2:0} a {3:0}."), area.X1 - area.X0, area.Z1 - area.Z0, area.Y0, area.Y1);
                done(area); _gl.Invalidate();
                return;
            }
            // objetos do cenário (O ligado): clique perto de um efeito/som mostra o que é
            if (_sceneObjs && e.Button == MouseButtons.Left && _movingTag is null && Math.Abs(e.X - _down.X) <= 4 && Math.Abs(e.Y - _down.Y) <= 4 && PickSceneObject(e.Location) is { } info)
            {
                _status.Text = info; ShowObjPanel(); _gl.Invalidate();
                return;
            }
            if (_movingTag is { } tag)
            {
                bool moved = _movePos is not null && (Math.Abs(e.X - _down.X) > 4 || Math.Abs(e.Y - _down.Y) > 4);
                if (moved && _moveHere is not null) { _moveHere(tag, _movePos!.Value); RefreshMarkers(); }
                else
                {
                    _select(tag);
                    var mk = _marks.FirstOrDefault(m => m.Tag == tag);
                    _status.Text = mk?.Name ?? "";
                    if (_pickMine is not null && mk is { Id: > 0 } && _host?.MineProducts(mk.Id).Count > 0)
                    {
                        var pickC = _pickMine; _pickMine = null; pickC(mk.Id);   // etapa de itens pediu esta coleta
                        _movingTag = null; _movePos = null; _gl.Invalidate();
                        return;
                    }
                    if (_pickMonster is not null && mk is { Id: > 0 } && _host?.IsMonster(mk.Id) == true)
                    {
                        var pickM = _pickMonster; _pickMonster = null; pickM(mk.Id);   // etapa de matar pediu este monstro
                        _movingTag = null; _movePos = null; _gl.Invalidate();
                        return;
                    }
                    if (_pickNpc is not null && mk is { Id: > 0 } && _host?.IsNpc(mk.Id) == true)
                    {
                        var pick = _pickNpc; _pickNpc = null; pick(mk.Id);   // painel de criação pediu este NPC
                        _movingTag = null; _movePos = null; _gl.Invalidate();
                        return;
                    }
                    if (mk is { Id: > 0 } && NpcQuests is not null)
                    {
                        if (NpcQuests(mk.Id) is { } qi)
                        {
                            // NPC: com a ferramenta Quests, no painel dela; sem ferramenta, as quests dele no painel de baixo (como a criatura)
                            _questNpc = mk.Id; HideBottomPanels();
                            if (_tool == Tool.Quests) ShowQuestTool(qi);
                            else if (BottomPanelsAllowed) _quests.ShowNpc(qi);
                        }
                        else
                        {
                            HideBottomPanels();
                            // monstro: painel da criatura (renascer, quantidade, quests que pedem para matar)
                            if (BottomPanelsAllowed && ShowCreatureInfo?.Invoke(mk.Tag, _creature) != true) _creature.HidePanel();
                        }
                    }
                }
                _movingTag = null; _movePos = null; _gl.Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Right) RightClick(e.Location);
            // clique esquerdo no vazio (sem arrastar a câmera): o NPC perde a seleção e a barra de quests some
            else if (e.Button == MouseButtons.Left && Math.Abs(e.X - _down.X) <= 4 && Math.Abs(e.Y - _down.Y) <= 4)
            {
                HideBottomPanels();
                _questNpc = 0;   // a ferramenta Quests volta a pedir "Clique em um NPC"
                if (_tool == Tool.Quests) ShowQuestTool(null);
                if (_selected() is not null) { ClearSelection?.Invoke(); _gizmoHover = 0; _gl.Invalidate(); }
            }
        };
        _gl.MouseMove += (_, e) =>
        {
            if (_areaA is not null) { if (Ray(e.Location) is { } b) _areaB = b; _gl.Invalidate(); return; }
            if (_gizmoAxis > 0) { GizmoDrag(e.Location); return; }
            if (PathMouseMove(e.Location)) return;
            if (!_dragging && GizmoHit(e.Location) is var gh && gh != _gizmoHover) { _gizmoHover = gh; _gl.Invalidate(); }
            if (_dragging) { _yaw += (e.X - _drag.X) * 0.25f; _pitch = Math.Clamp(_pitch - (e.Y - _drag.Y) * 0.25f, -89, 89); _drag = e.Location; _gl.Invalidate(); return; }
            if (_movingTag is not null)
            {
                if (_tool != Tool.Paths && (Math.Abs(e.X - _down.X) > 4 || Math.Abs(e.Y - _down.Y) > 4)) { if (Ray(e.Location) is { } mp) _movePos = mp; }
                _gl.Invalidate(); return;
            }
            _hover = Ray(e.Location);
            var near = PickScreen(e.Location);
            // o jogo mostra ((int)x + 4000) / 10 e ((int)z + 5500) / 10 (CECUIHelper::GetShowPos)
            _status.Text = _hover is { } h ? string.Format(Loc.T("world.coord", "X {0:0.0}   altura {1:0.0}   Z {2:0.0}"), h.X, h.Y, h.Z)
                + string.Format(Loc.T("world.gamePos", "   (no jogo: {0} {1})"), ((int)h.X + 4000) / 10, ((int)h.Z + 5500) / 10)
                + (near is null ? "" : "   ·   " + near.Name) : "";
        };
        // roda: aproxima/afasta a câmera para onde ela olha (Shift = 4×)
        _gl.MouseWheel += (_, e) => { _cam += Forward * (e.Delta / 120f) * 12f * ((ModifierKeys & Keys.Shift) != 0 ? 4 : 1); ClampCamera(); _gl.Invalidate(); };
        KeyDown += (_, e) => OnKey(e);
        KeyUp += (_, e) => _keys.Remove(e.KeyCode);
        Deactivate += (_, _) => _keys.Clear();
        Activated += (_, _) => MarkDirty();   // mudanças feitas no editor aparecem ao voltar
        _tick.Tick += (_, _) => Step();
        _tick.Start();
        InitSceneEdit();
        InitToolMenu();    // menu flutuante de ferramentas por último: usa a grama e os outros desenhos já criados
        FormClosed += (_, _) =>
        {
            _tick.Dispose(); _load.Dispose(); _gl.MakeCurrent();
            _terrain.Dispose(); _models.Dispose(); _buildings.Dispose(); _trees.Dispose(); _grass.Dispose(); _sky.Dispose(); _worldMap?.Dispose(); _instMap?.Dispose();
            foreach (var l in _labels.Values) GL.DeleteTexture(l.Tex);
            if (_legendTex != 0) GL.DeleteTexture(_legendTex);
        };
    }

    void OnKey(KeyEventArgs e)
    {
        if (_find.Focused || ActiveControl is TextBoxBase or NumericUpDown or ListView || _editor?.ContainsFocus == true) return;   // digitando no painel: sem andar
        if (PathKey(e.KeyCode)) { e.Handled = e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.Escape && _pickNpc is not null) { _pickNpc = null; _status.Text = Loc.T("qe.pickCancel", "Escolha de NPC cancelada."); return; }
        if (e.KeyCode == Keys.Escape && _pickMine is not null) { _pickMine = null; _status.Text = Loc.T("qe.pickMineCancel", "Escolha de coleta cancelada."); return; }
        if (e.KeyCode == Keys.Escape && _pickMonster is not null) { _pickMonster = null; _status.Text = Loc.T("qe.pickMonsterCancel", "Escolha de monstro cancelada."); return; }
        if (e.KeyCode == Keys.Escape && _pickArea is not null) { _pickArea = null; _areaA = _areaB = null; _status.Text = Loc.T("qe.areaCancel", "Marcação de área cancelada."); _gl.Invalidate(); return; }
        if (e.Control)
        {
            switch (e.KeyCode)
            {
                case Keys.Z or Keys.Y:
                    bool ok = e.KeyCode == Keys.Z ? UndoAny() : RedoAny();
                    _status.Text = ok ? "" : e.KeyCode == Keys.Z ? Loc.T("status.nothingUndo", "Nada para desfazer") : Loc.T("status.nothingRedo", "Nada para refazer");
                    RefreshMarkers(); break;
                case Keys.C:
                    _clip = _selected();
                    _status.Text = _clip is null ? "" : Loc.T("world.copied", "Ponto copiado — Ctrl+V cola onde está o mouse");
                    break;
                case Keys.V:
                    if (_tool == Tool.None && _clip is not null && CloneHere is not null && (_hover ?? Ray(_gl.PointToClient(Cursor.Position))) is { } at) { CloneHere(_clip, at); RefreshMarkers(); }
                    break;
            }
            e.Handled = true; return;
        }
        switch (e.KeyCode)
        {
            case Keys.M: ShowMap(); return;
            case Keys.H or Keys.F1: ToggleLegend(); return;
            case Keys.T: _terrain.Visible = !_terrain.Visible; _gl.Invalidate(); return;   // ver por dentro de cavernas
            case Keys.B: _showBuildings = !_showBuildings; _gl.Invalidate(); return;     // esconde construções (pontos dentro de cidades)
            case Keys.G: _grassBtn.Checked = !_grassBtn.Checked; return;    // esconde a grama (botão Grama de cima acompanha)
            case Keys.O:   // ferramenta Editar cenário (abre/fecha, como o botão do menu de ferramentas)
                if (_sceneObjs) CloseTool(); else OpenTool(Tool.Scene);
                return;
            case Keys.P:   // caminhos de patrulha (path.sev) perto da câmera
                _allPaths = !_allPaths; _syncPathsBtn?.Invoke(); _gl.Invalidate();
                _status.Text = Paths is null ? Loc.T("world.noPaths", "Este mapa não tem path.sev no servidor.")
                    : _allPaths ? string.Format(Loc.T("world.pathsOn", "Caminhos de patrulha: todos ({0}) — P esconde"), Paths.Count) : Loc.T("world.pathsOff", "Caminhos de patrulha: só o do ponto selecionado — P mostra todos");
                return;
            case Keys.N: _gameFog = !_gameFog; _gl.Invalidate(); return;
            case Keys.L: _nightOn = !_nightOn; _gl.Invalidate(); return;               // dia / noite               // neblina do jogo (por área) ou longe
            case Keys.Delete:   // só sem ferramenta: dentro de uma ferramenta o Delete não apaga a criatura escolhida
                if (_tool == Tool.None && _selected() is { } s && DeletePoint is not null && Confirm()) { DeletePoint(s); RefreshMarkers(); }
                return;
            case Keys.Space: e.SuppressKeyPress = true; break;
        }
        _keys.Add(e.KeyCode);
    }

    // ajuda (H) aberta ou minimizada: fica como o usuário deixou, também ao trocar de mapa e reabrir
    static string LegendFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "world_help.txt");
    static bool LoadLegend() { try { return !File.Exists(LegendFile) || File.ReadAllText(LegendFile).Trim() != "0"; } catch { return true; } }
    void ToggleLegend()
    {
        _legendOpen = !_legendOpen; _gl.Invalidate();
        try { Directory.CreateDirectory(Path.GetDirectoryName(LegendFile)!); File.WriteAllText(LegendFile, _legendOpen ? "1" : "0"); } catch { }
    }

    bool Confirm() => MessageBox.Show(this, Loc.T("world.deleteAsk", "Excluir este ponto? (Ctrl+Z desfaz)"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>Relê os pontos (depois de criar, mover, excluir ou trocar o arquivo).</summary>
    public void RefreshMarkers() { _allMarks = _markerSrc().ToList(); _marks = _allMarks.Where(Visible).ToList(); _nearAt = new(float.MaxValue); _marksDirty = false; _marksAt = _clock.Elapsed.TotalSeconds; _gl?.Invalidate(); }

    /// <summary>Algo mudou fora: relê os pontos no próximo quadro (no máximo ~3 vezes por segundo).</summary>
    public void MarkDirty() { _marksDirty = true; _gl?.Invalidate(); }

    /// <summary>Leva a câmera até o ponto (lista da aba NPCGen ou "Ir para").</summary>
    public void GoTo(object tag)
    {
        if ((_marks.FirstOrDefault(m => m.Tag == tag) ?? _allMarks.FirstOrDefault(m => m.Tag == tag)) is { } m) { LookAt(PosOf(m)); _gl.Invalidate(); }
    }

    /// <summary>Câmera acima do ponto, olhando para baixo (abrir sem nada selecionado). high: vista alta do mapa inteiro.</summary>
    /// <param name="toward">instância: para onde olhar (centro dos monstros/NPCs); a câmera fica logo atrás da entrada,
    /// onde o jogo põe o jogador. Antes: 35 m atrás e 25 m acima do maior entre o ponto e o terreno — nas instâncias
    /// fechadas o terreno é uma tampa acima do piso (a26: tampa a 800 m, piso perto de 22 m) e a câmera ia parar em cima
    /// da tampa ou dentro da rocha.</param>
    public void Overview(Vector3 p, bool high, Vector3? toward = null)
    {
        if (!high) HideLidTerrain(p.Y);
        float ground = _terrain.Visible ? _t.HeightAt(p.X, p.Z) ?? p.Y : p.Y;
        if (high) { _cam = Gl(p.X, ground + 400, p.Z - 400); _pitch = -40; _yaw = -90; }
        else
        {
            // terreno bem acima do ponto = tampa/teto: não conta; chão um pouco acima (ponto enterrado) sobe o ponto
            float floor = ground > p.Y + 20 ? p.Y : Math.Max(p.Y, ground);
            var dir = toward is { } t0 && new Vector2(t0.X - p.X, t0.Z - p.Z) is { LengthSquared: > 4 } h ? h.Normalized() : new Vector2(0, 1);
            _cam = Gl(p.X - dir.X * 8, floor + 4, p.Z - dir.Y * 8);
            _yaw = MathHelper.RadiansToDegrees(MathF.Atan2(-dir.Y, dir.X));
            _pitch = -14;
        }
        ClampCamera(); _gl.Invalidate();
    }

    // câmera 14 m ao sul e 7 m acima, olhando para o ponto
    void LookAt(Vector3 p) { _cam = Gl(p.X, p.Y + 7, p.Z - 14); _yaw = -90; _pitch = -22; ClampCamera(); }

    // "terreno tampa": mapas internos cujo terreno é uma placa plana ACIMA de tudo (a26, Palácio do Nirvana: 82% a 800 m,
    // construções e portal entre -33 e 384 m). O jogo não mostra; aqui ele começa escondido (T mostra) e o clique usa um
    // piso na altura de referência (entrada ou ponto escolhido).
    float? _floorY;
    // Piso de referência para cliques quando a câmera está abaixo do terreno (cavernas feitas de construções, com o
    // terreno por cima como tampa). O terreno não é mais escondido: como no jogo (Direct3D descarta faces de trás),
    // ele só aparece visto de cima, então a tampa some sozinha quando se olha de baixo.
    void HideLidTerrain(float refY) => _floorY = refY;

    // dentro do mapa e acima do chão
    void ClampCamera()
    {
        _cam.X = Math.Clamp(_cam.X, -_t.Width / 2, _t.Width / 2);
        _cam.Z = Math.Clamp(_cam.Z, -_t.Height / 2, _t.Height / 2);
        // não deixa entrar no chão. Abaixo dele só onde o terreno é tampa (bem acima do piso da entrada da instância:
        // a26, cavernas). Antes: liberado a mais de 12 m abaixo em qualquer mapa — descendo rápido (Shift) a câmera
        // passava o chão e via o terreno por baixo (só a faixa do horizonte, a20)
        if (_terrain.Visible && _t.HeightAt(_cam.X, -_cam.Z) is { } ground && _cam.Y < ground + 2)
        {
            bool lid = _floorY is { } fy && ground > fy + 20;
            if (!lid || _cam.Y > ground - 12) _cam.Y = ground + 2;
        }
    }

    Vector3 Forward => new(MathF.Cos(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch)),
        MathF.Sin(MathHelper.DegreesToRadians(_pitch)), MathF.Sin(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch)));

    // movimento pelo tempo real do quadro; redesenha enquanto há carga em andamento
    void Step()
    {
        if (_wavesNear && _clock.Elapsed.TotalSeconds - _wavesAt > 0.05) { _wavesAt = _clock.Elapsed.TotalSeconds; _gl.Invalidate(); }
        double now = _clock.Elapsed.TotalSeconds; float dt = (float)Math.Min(0.1, now - _last); _last = now;
        // tecla solta sem KeyUp (Espaço com o foco num botão/caixa da barra vira clique e o KeyUp some): confere com o
        // Windows se ainda está apertada — senão a câmera subia sem parar até trocar de mapa
        if (_keys.Count > 0) _keys.RemoveWhere(k => (GetAsyncKeyState((int)k) & 0x8000) == 0);
        bool moving = _keys.Count > 0;
        if (moving)
        {
            float s = Speed * dt * (_keys.Contains(Keys.ShiftKey) ? 4 : 1);
            var f = Forward; var right = Vector3.Normalize(Vector3.Cross(f, Vector3.UnitY));
            if (_keys.Contains(Keys.W)) _cam += f * s;
            if (_keys.Contains(Keys.S)) _cam -= f * s;
            if (_keys.Contains(Keys.D)) _cam += right * s;
            if (_keys.Contains(Keys.A)) _cam -= right * s;
            if (_keys.Contains(Keys.Space)) _cam.Y += s;   // E não sobe mais: marca ponto na ferramenta Caminhos (usuário 01/11)
            if (_keys.Contains(Keys.Q) || (_keys.Contains(Keys.Z) && !_keys.Contains(Keys.ControlKey))) _cam.Y -= s;
            ClampCamera();
        }
        if (moving || _busy || _marksDirty) _gl.Invalidate();
    }

    // o mundo do PW usa Z para o norte; aqui -Z do OpenGL vira +Z do jogo (espelha Z)
    static Vector3 Gl(float x, float y, float z) => new(x, y, -z);

    Matrix4 View => Matrix4.LookAt(_cam, _cam + Forward, Vector3.UnitY);
    Matrix4 Proj => Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60), Math.Max(1, _gl.Width) / (float)Math.Max(1, _gl.Height), 1, 9000);

    void Render()
    {
        _gl.MakeCurrent();
        bool loading = _load.Pump(6);   // resultados do segundo plano: no máximo 6 ms por quadro
        GL.Viewport(0, 0, _gl.Width, _gl.Height);
        GL.ClearColor(0.62f, 0.76f, 0.9f, 1);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);
        GL.MatrixMode(MatrixMode.Projection); var p = Proj; GL.LoadMatrix(ref p);
        GL.MatrixMode(MatrixMode.Modelview); var v = View; GL.LoadMatrix(ref v);
        StepDayNight();
        _sky.Draw(_cam, _nightApplied);   // céu da área (tetos das instâncias); sem céu fica a cor de fundo
        GL.Enable(EnableCap.Fog);
        GL.Fog(FogParameter.FogMode, (int)FogMode.Linear);
        // neblina da área da câmera (.ecbsd, como o jogo); N troca pela neblina longe, para editar enxergando o mapa todo
        if (_gameFog && _sky.Fog(_cam, _nf) is { } fog)
        {
            GL.Fog(FogParameter.FogStart, fog.Start); GL.Fog(FogParameter.FogEnd, fog.End);
            GL.Fog(FogParameter.FogColor, new[] { fog.Color.X, fog.Color.Y, fog.Color.Z, 1f });
            _skyTint = fog.Color;
        }
        else
        {
            GL.Fog(FogParameter.FogStart, 1500f); GL.Fog(FogParameter.FogEnd, 8000f);
            GL.Fog(FogParameter.FogColor, new[] { 0.62f, 0.76f, 0.9f, 1f });
        }

        // terreno só de cima (faces da frente sobem: a→d→b), como o jogo: visto de baixo, a tampa das cavernas some.
        // O "teto" das instâncias (a35, a24, a27) não é terreno — é o céu da área (A3DSkySphere com texturas de pedra).
        GL.Enable(EnableCap.CullFace); GL.CullFace(CullFaceMode.Back); GL.FrontFace(FrontFaceDirection.Ccw);
        bool terrainBusy = _terrain.Draw(_cam, Forward);
        GL.Disable(EnableCap.CullFace);

        // construções dos pedaços de 1 km em volta da câmera
        int piece = _t.PieceAt(_cam.X, -_cam.Z);
        if (piece != _piece)
        {
            _piece = piece;
            var pieces = new List<int>();
            if (piece > 0)
            {
                int pr = (piece - 1) / _t.Cols, pc = (piece - 1) % _t.Cols;
                for (int r = pr - 1; r <= pr + 1; r++)
                    for (int c = pc - 1; c <= pc + 1; c++)
                        if (r >= 0 && c >= 0 && r < _t.Rows && c < _t.Cols) pieces.Add(r * _t.Cols + c + 1);
            }
            _buildings.Update(pieces);
        }
        if (_showBuildings) _buildings.Draw(_cam, Forward);
        _trees.Draw(_cam, Forward);
        _grass.Draw(_cam);   // grama do .ecwld/.ecbsd perto da câmera   // árvores do .ecwld (nossa versão do SpeedTree)
        DrawWater();

        // pontos perto da câmera (refeitos a cada 100 m andados ou quando algo mudou)
        if (_marksDirty && _clock.Elapsed.TotalSeconds - _marksAt > 0.3) RefreshMarkers();
        if ((_cam - _nearAt).LengthSquared > 100 * 100) { _nearAt = _cam; _nearMarks = _marks.Where(m => Math.Abs(m.X - _cam.X) < 1500 && Math.Abs(-m.Z - _cam.Z) < 1500).ToList(); }

        // modelos parados até 300 m; quem não tem modelo vira um ponto pequeno
        var sel = _selected();
        GL.Enable(EnableCap.Lighting); GL.Enable(EnableCap.Light0); GL.Enable(EnableCap.Normalize); GL.Enable(EnableCap.ColorMaterial);
        GL.Light(LightName.Light0, LightParameter.Position, new Vector4(-0.4f, 1f, 0.3f, 0));
        var (amb, sun) = _sky.Light(_cam, _nf) ?? (new Vector3(0.5f, 0.5f, 0.55f), new Vector3(0.8f, 0.8f, 0.75f));
        GL.Light(LightName.Light0, LightParameter.Diffuse, new Color4(sun.X, sun.Y, sun.Z, 1f));
        GL.Light(LightName.Light0, LightParameter.Ambient, new Color4(amb.X, amb.Y, amb.Z, 1f));
        GL.Enable(EnableCap.AlphaTest); GL.AlphaFunc(AlphaFunction.Greater, 0.3f);
        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.NormalArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
        GL.Color3(1f, 1f, 1f);
        var noModel = new List<Marker>();
        var hiddenTag = HiddenModelTag();   // uma vez por quadro (antes, por marcador: travava a câmera)
        foreach (var m in _nearMarks)
        {
            if (Dist2(m) > 300 * 300) continue;
            if (hiddenTag is not null && ReferenceEquals(m.Tag, hiddenTag)) continue;   // Caminhos em patrulha: o círculo fica no lugar do modelo
            if (m.Model is null || !_models.Draw(m.Model, PosOf(m), m.Tag == _gizmoTag && _gizmoYaw is { } gy ? gy : m.Yaw)) noModel.Add(m);
        }
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        GL.DisableClientState(ArrayCap.VertexArray); GL.DisableClientState(ArrayCap.NormalArray); GL.DisableClientState(ArrayCap.TextureCoordArray);
        GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.ColorMaterial); GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.AlphaTest);
        GL.PointSize(7);
        GL.Begin(PrimitiveType.Points);
        foreach (var m in noModel) { var mp = PosOf(m); Col(m.Tag == sel ? Color.Yellow : m.Color); GL.Vertex3(Gl(mp.X, mp.Y + 1.2f, mp.Z)); }
        GL.End();
        DrawAreas();
        DrawSpawnArea();
        DrawPaths();
        DrawPathEdit();
        DrawSceneObjects();
        DrawGizmo();

        // nomes e coordenadas sobre os pontos até 70 m (os 40 mais perto)
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); GL.DepthMask(false);
        GL.Enable(EnableCap.Texture2D); GL.Color3(1f, 1f, 1f);
        _newLabels = 0;
        foreach (var m in _nearMarks.Where(m => Dist2(m) < 70 * 70 || m.Tag == sel && Dist2(m) < 400 * 400).OrderBy(Dist2).Take(40))
        {
            var lp = PosOf(m);
            float top = m.Model is not null && _models.Top(m.Model) is { } tp ? tp + 1.2f : 3f;
            DrawLabel($"{m.Name}\n{lp.X:0}, {lp.Y:0}, {lp.Z:0}", new Vector3(lp.X, lp.Y + top, lp.Z), m.Tag == sel ? Color.Yellow : m.LabelColor.IsEmpty ? Color.White : m.LabelColor);
        }
        GizmoLabels();
        DrawLegend();
        GL.Disable(EnableCap.Texture2D); GL.DepthMask(true); GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.Fog);
        if (_grab) { _grab = false; _grabbed = ReadFrame(); }
        _gl.SwapBuffers();
        _busy = loading || terrainBusy;
    }

    // ---------- foto para diagnóstico (--world-shot): câmera fixa, espera carregar, lê o quadro ----------
    bool _grab; Bitmap? _grabbed;
    internal bool Loading => _busy || _trees.Pending || _grass.Pending;
    internal void SetCamera(Vector3 game, float yaw, float pitch) { _cam = Gl(game.X, game.Y, game.Z); _yaw = yaw; _pitch = pitch; _legendOpen = false; _gl.Invalidate(); }
    internal void OpenLegend() { _legendOpen = true; _gl.Invalidate(); }
    internal void SetNight(bool on) { _nightOn = on; _nf = on ? 1 : 0; _gl.Invalidate(); }
    internal Bitmap? CaptureFrame() { _grab = true; Render(); var b = _grabbed; _grabbed = null; return b; }

    Bitmap ReadFrame()
    {
        int w = _gl.Width, h = _gl.Height;
        var px = new byte[w * h * 4];
        GL.ReadBuffer(ReadBufferMode.Back);
        GL.ReadPixels(0, 0, w, h, PixelFormat.Bgra, PixelType.UnsignedByte, px);
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(px, (h - 1 - y) * w * 4, data.Scan0 + y * data.Stride, w * 4);
        bmp.UnlockBits(data);
        return bmp;
    }

    float Dist2(Marker m) { float dx = m.X - _cam.X, dz = m.Z + _cam.Z, dy = m.Y - _cam.Y; return dx * dx + dy * dy + dz * dz; }
    static void Col(Color c) => GL.Color3(c.R / 255f, c.G / 255f, c.B / 255f);

    // "no chão": o servidor põe na altura do terreno; só segue o terreno se estiver perto dele (em cima de construção
    // ou dentro de caverna, fica na altura gravada)
    Vector3 PosOf(Marker m)
    {
        if (m.Tag == _movingTag && _movePos is { } mp) return mp;
        if (m.Tag == _gizmoTag && _gizmoPos is { } gp) return gp;
        float y = m.Y;
        if (m.OnGround && _t.HeightAt(m.X, m.Z) is { } h && Math.Abs(h - m.Y) < 3) y = h;
        return new Vector3(m.X, y, m.Z);
    }

    /// <summary>
    /// Áreas da quest em edição (caixas translúcidas; a selecionada mais forte) e a que está sendo arrastada. Altura "qualquer"
    /// (±9999) é desenhada só perto da câmera.
    /// </summary>
    void DrawAreas()
    {
        var list = _areas.Select((a, i) => (a, i == _areaSel)).ToList();
        if (_areaA is { } a0) list.Add((AreaOf(a0, _areaB ?? a0), true));
        if (list.Count == 0) return;
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); GL.DepthMask(false);
        foreach (var (r, strong) in list)
        {
            float y0 = MathF.Max(r.Y0, _cam.Y - 400), y1 = MathF.Min(r.Y1, _cam.Y + 400);
            if (y1 <= y0) continue;
            var c = new[] { Gl(r.X0, y0, r.Z0), Gl(r.X1, y0, r.Z0), Gl(r.X1, y0, r.Z1), Gl(r.X0, y0, r.Z1), Gl(r.X0, y1, r.Z0), Gl(r.X1, y1, r.Z0), Gl(r.X1, y1, r.Z1), Gl(r.X0, y1, r.Z1) };
            GL.Color4(1f, 0.78f, 0.25f, strong ? 0.28f : 0.14f);
            GL.Begin(PrimitiveType.Quads);
            foreach (var f in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7 }) GL.Vertex3(c[f]);
            GL.End();
            GL.LineWidth(strong ? 2.5f : 1.5f);
            GL.Color4(1f, 0.82f, 0.3f, strong ? 1f : 0.7f);
            GL.Begin(PrimitiveType.Lines);
            foreach (var f in new[] { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 }) GL.Vertex3(c[f]);
            GL.End();
        }
        GL.LineWidth(1); GL.DepthMask(true); GL.Disable(EnableCap.Blend); GL.Color3(1f, 1f, 1f);
    }

    // objeto do cenário clicado por último (destacado e com a distância do som)
    object? _sceneSel;

    /// <summary>Efeitos (.gfx, laranja) e sons (azul; o escolhido mostra o raio máximo) do .ecwld a até 300 m da câmera.</summary>
    void DrawSceneObjects()
    {
        if (!_sceneObjs || ObjScene is not { } sc) return;
        const float R2 = 300 * 300;
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Texture2D);
        GL.PointSize(8);
        GL.Begin(PrimitiveType.Points);
        foreach (var fx in sc.Effects)
        {
            var g = Gl(fx.X, fx.Y, fx.Z);
            if ((g - _cam).LengthSquared > R2) continue;
            if (fx == _sceneSel) GL.Color3(1f, 1f, 0.4f); else GL.Color3(1f, 0.55f, 0.1f);
            GL.Vertex3(g);
        }
        foreach (var sd in sc.Sounds)
        {
            var g = Gl(sd.X, sd.Y, sd.Z);
            if ((g - _cam).LengthSquared > R2) continue;
            if (sd == _sceneSel) GL.Color3(1f, 1f, 0.4f); else GL.Color3(0.3f, 0.7f, 1f);
            GL.Vertex3(g);
        }
        GL.End();
        GL.PointSize(1);
        // bichos de enfeite (verde; o escolhido mostra a área) e curvas do cenário (ciano fino)
        GL.PointSize(8); GL.Begin(PrimitiveType.Points);
        foreach (var cr in sc.Critters)
        {
            var g = Gl(cr.X, cr.Y, cr.Z);
            if ((g - _cam).LengthSquared > R2) continue;
            if (cr == _sceneSel) GL.Color3(1f, 1f, 0.4f); else GL.Color3(0.4f, 0.95f, 0.4f);
            GL.Vertex3(g);
        }
        GL.End(); GL.PointSize(1);
        GL.Color3(0.3f, 0.9f, 0.9f);
        foreach (var cv in _sceneSel is WorldScene.Critter ? sc.Curves : new())
        {
            if (cv.Points.Count < 2 || !cv.Points.Any(q => (Gl(q.X, q.Y, q.Z) - _cam).LengthSquared < R2)) continue;
            GL.Begin(PrimitiveType.LineStrip);
            foreach (var q in cv.Points) GL.Vertex3(Gl(q.X, q.Y, q.Z));
            GL.End();
        }
        if (_sceneSel is WorldScene.Critter c0 && c0.Radius > 0)
        {
            GL.Color3(0.4f, 0.95f, 0.4f);
            GL.Begin(PrimitiveType.LineLoop);
            for (int i = 0; i < 64; i++) { float t = i / 64f * MathF.Tau; GL.Vertex3(Gl(c0.X + MathF.Cos(t) * c0.Radius, c0.Y, c0.Z + MathF.Sin(t) * c0.Radius)); }
            GL.End();
        }
        if (_sceneSel is WorldScene.SceneSound s && s.MaxDist > 0)
        {
            GL.Color3(0.3f, 0.7f, 1f);
            GL.Begin(PrimitiveType.LineLoop);
            for (int i = 0; i < 64; i++) { float t = i / 64f * MathF.Tau; GL.Vertex3(Gl(s.X + MathF.Cos(t) * s.MaxDist, s.Y, s.Z + MathF.Sin(t) * s.MaxDist)); }
            GL.End();
        }
        GL.Enable(EnableCap.DepthTest); GL.Color3(1f, 1f, 1f);
    }

    /// <summary>Efeito ou som mais perto do clique (até 10 px na tela): texto para a barra de status.</summary>
    string? PickSceneObject(Point p)
    {
        if (ObjScene is not { } sc) return null;
        object? best = null; float bd = 10;
        void Try(object o, float x, float y, float z)
        {
            if ((Gl(x, y, z) - _cam).LengthSquared > 300 * 300 || Project(new Vector3(x, y, z)) is not { } q) return;
            float d = MathF.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
            if (d < bd) { bd = d; best = o; }
        }
        foreach (var fx in sc.Effects) Try(fx, fx.X, fx.Y, fx.Z);
        foreach (var sd in sc.Sounds) Try(sd, sd.X, sd.Y, sd.Z);
        foreach (var cr in sc.Critters) Try(cr, cr.X, cr.Y, cr.Z);
        // nenhum ponto perto: construção sob o clique (mover fica guardado no 3D até a colisão do servidor; ver SceneEdit)
        if (best is null && _buildings.Pick(Project, p, _cam) is { } bp)
        {
            _sceneSel = new BuildingSel(bp.File, bp.Center);
            return string.Format(Loc.T("world.bldInfo", "Construção: {0} · X {1:0} Z {2:0} altura {3:0}"), Path.GetFileName(bp.File), bp.Center.X, bp.Center.Z, bp.Center.Y);
        }
        _sceneSel = best;
        string When(int v) => v switch { 0 => Loc.T("world.whenDay", "de dia"), 1 => Loc.T("world.whenNight", "de noite"), _ => Loc.T("world.whenAlways", "sempre") };
        return best switch
        {
            WorldScene.SceneEffect fx => string.Format(Loc.T("world.fxInfo", "Efeito: {0} · escala {1:0.##} · {2} · X {3:0} Z {4:0} altura {5:0}"), fx.Gfx, fx.Scale, When(fx.ValidTime), fx.X, fx.Z, fx.Y),
            WorldScene.Critter cr => string.Format(Loc.T("world.critterInfo", "Bichos de enfeite: {0} · {1} na área de {2:0} m · {3} · X {4:0} Z {5:0}"), cr.Model, cr.Count, cr.Radius,
                cr.Behaviour switch { 0 => Loc.T("world.critWander", "vagam"), 1 => Loc.T("world.critHover", "pairam"), _ => string.Format(Loc.T("world.critOther", "comportamento {0}"), cr.Behaviour) }, cr.X, cr.Z),
            WorldScene.SceneSound sd => string.Format(Loc.T("world.sndInfo", "Som: {0} · ouve de {1:0} a {2:0} m · {3} · X {4:0} Z {5:0}"), sd.File, sd.MinDist, sd.MaxDist, When(sd.ValidTime), sd.X, sd.Z),
            _ => null,
        };
    }

    /// <summary>
    /// Caminhos de patrulha: o(s) do ponto selecionado (campo Caminho dos monstros dele) em laranja forte com setas no sentido
    /// da ida; com P, todos os caminhos a até 600 m da câmera em lilás fraco.
    /// </summary>
    /// <summary>Altura para desenhar sobre o chão: o terreno (+0,5) quando está acima do ponto, senão o próprio ponto.</summary>
    float OnGround(float x, float y, float z) => MathF.Max(y, _t.HeightAt(x, z) ?? y) + 0.5f;

    void DrawPaths()
    {
        if (Paths is null) return;
        var sel = _tool == Tool.Paths && SelectedMark()?.Tag is NpcGenSpawn s ? s.Creatures.Select(c => c.PathId).Where(id => id > 0).ToHashSet() : new HashSet<int>();
        // redesenhando: o caminho atual some da tela (o novo toma o lugar ao terminar; usuário 01/11)
        var hide = _drawPts is not null ? sel : null;
        if (hide is not null) sel = new HashSet<int>();
        if (!_allPaths && sel.Count == 0) return;
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); GL.DepthMask(false);
        foreach (var (id, pts) in Paths)
        {
            if (hide?.Contains(id) == true) continue;
            bool strong = sel.Contains(id);
            if (!strong && (!_allPaths || pts.Count == 0 || !pts.Any(p => (Gl(p.X, p.Y, p.Z) - _cam).LengthSquared < 600 * 600))) continue;
            if (strong) { GL.LineWidth(4); GL.Color4(1f, 0.6f, 0.15f, 1f); } else { GL.LineWidth(2); GL.Color4(0.8f, 0.55f, 1f, 0.6f); }
            // entre dois pontos do caminho a reta passava por baixo dos morros: subdivide a cada ~2 m e põe no chão
            GL.Begin(PrimitiveType.LineStrip);
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                if (i == 0) { GL.Vertex3(Gl(a.X, OnGround(a.X, a.Y, a.Z), a.Z)); continue; }
                var b = pts[i - 1];
                float dx = a.X - b.X, dz = a.Z - b.Z;
                int n = Math.Clamp((int)(MathF.Sqrt(dx * dx + dz * dz) / 2), 1, 200);
                for (int k = 1; k <= n; k++)
                {
                    float t = k / (float)n, x = b.X + dx * t, z = b.Z + dz * t;
                    GL.Vertex3(Gl(x, OnGround(x, b.Y + (a.Y - b.Y) * t, z), z));
                }
            }
            GL.End();
            // circuito (fim = recomeça): mostra a volta do último ao primeiro ponto
            if (strong && pts.Count > 2 && SelectedMark()?.Tag is NpcGenSpawn ls && ls.Creatures.FirstOrDefault()?.PathLoop is not (0 or 1))
            {
                GL.Begin(PrimitiveType.LineStrip);
                var e0 = pts[^1]; var e1 = pts[0]; float ex = e1.X - e0.X, ez = e1.Z - e0.Z;
                int en = Math.Clamp((int)(MathF.Sqrt(ex * ex + ez * ez) / 2), 1, 200);
                for (int k = 0; k <= en; k++) { float t = k / (float)en, x = e0.X + ex * t, z = e0.Z + ez * t; GL.Vertex3(Gl(x, OnGround(x, e0.Y + (e1.Y - e0.Y) * t, z), z)); }
                GL.End();
            }
            if (strong && pts.Count > 1)
            {
                // começo do caminho: ponto grande (o monstro anda daqui para o fim)
                GL.PointSize(12); GL.Begin(PrimitiveType.Points); GL.Vertex3(Gl(pts[0].X, pts[0].Y + 0.5f, pts[0].Z)); GL.End(); GL.PointSize(1);
            }
        }
        GL.LineWidth(1); GL.DepthMask(true); GL.Disable(EnableCap.Blend); GL.Color3(1f, 1f, 1f);
    }

    /// <summary>
    /// Área de nascimento do ponto selecionado (npcgen vExts / fExtX-fExtZ = tamanho TOTAL da área; o gs usa pos ± metade,
    /// npcgenerator.cpp): retângulo azul que segue o chão, como o gerador de área do editor oficial.
    /// </summary>
    void DrawSpawnArea()
    {
        if (_tool != Tool.Paths || PatrolMode || SelectedMark() is not { } m || AreaOf(m.Tag) is not { } ar) return;   // patrulha: a área some
        var (cx, cz, sx, sz) = ar;
        if (m.Tag == _gizmoTag && _gizmoSize is { } gs) (sx, sz) = gs;
        sx /= 2; sz /= 2;
        if (AreaHandle(m) is { } h)
        {
            bool hot = _gizmoAxis == 5 || _gizmoHover == 5;
            GL.Disable(EnableCap.DepthTest); GL.PointSize(hot ? 13 : 10);
            GL.Color3(hot ? 1f : 0.35f, hot ? 0.9f : 0.75f, hot ? 0.3f : 1f);
            GL.Begin(PrimitiveType.Points); GL.Vertex3(Gl(h.X, h.Y, h.Z)); GL.End();
            GL.PointSize(1); GL.Enable(EnableCap.DepthTest); GL.Color3(1f, 1f, 1f);
        }
        if (sx < 0.25f && sz < 0.25f) return;
        float Y(float x, float z) => (_t.HeightAt(x, z) ?? m.Y) + 0.4f;
        var pts = new List<Vector3>();
        void Edge(float x0, float z0, float x1, float z1)
        {
            int n = Math.Clamp((int)(MathF.Max(MathF.Abs(x1 - x0), MathF.Abs(z1 - z0)) / 2), 1, 400);
            for (int i = 0; i < n; i++) { float t = i / (float)n, x = x0 + (x1 - x0) * t, z = z0 + (z1 - z0) * t; pts.Add(Gl(x, Y(x, z), z)); }
        }
        Edge(cx - sx, cz - sz, cx + sx, cz - sz); Edge(cx + sx, cz - sz, cx + sx, cz + sz);
        Edge(cx + sx, cz + sz, cx - sx, cz + sz); Edge(cx - sx, cz + sz, cx - sx, cz - sz);
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); GL.DepthMask(false);
        GL.LineWidth(2.5f); GL.Color4(0.35f, 0.75f, 1f, 0.95f);
        GL.Begin(PrimitiveType.LineLoop);
        foreach (var p in pts) GL.Vertex3(p);
        GL.End();
        GL.LineWidth(1); GL.DepthMask(true); GL.Disable(EnableCap.Blend); GL.Color3(1f, 1f, 1f);
    }

    /// <summary>
    /// Retângulo arrastado no chão → caixa da quest (coordenadas do jogo). Clique sem arrastar = 10 × 10 m. Altura: do ponto
    /// mais baixo do chão no retângulo (terreno ou piso clicado) − 10 m até o mais alto + 50 m (montaria voando baixo conta).
    /// </summary>
    QuestArea AreaOf(Vector3 a, Vector3 b)
    {
        float x0 = MathF.Min(a.X, b.X), x1 = MathF.Max(a.X, b.X), z0 = MathF.Min(a.Z, b.Z), z1 = MathF.Max(a.Z, b.Z);
        if (x1 - x0 < 2 && z1 - z0 < 2) { x0 = a.X - 5; x1 = a.X + 5; z0 = a.Z - 5; z1 = a.Z + 5; }
        float lo = MathF.Min(a.Y, b.Y), hi = MathF.Max(a.Y, b.Y);
        for (int i = 0; i <= 8; i++)
            for (int j = 0; j <= 8; j++)
                if (_t.HeightAt(x0 + (x1 - x0) * i / 8, z0 + (z1 - z0) * j / 8) is float h) { lo = MathF.Min(lo, h); hi = MathF.Max(hi, h); }
        static float R(float v) => MathF.Round(v, 1);
        return new QuestArea { X0 = R(x0), Z0 = R(z0), X1 = R(x1), Z1 = R(z1), Y0 = R(lo - 10), Y1 = R(hi + 50) };
    }

    /// <summary>
    /// Água como o cliente: áreas do bsdata do mapa (cor de cada área, altura, retângulo) com a textura de ondas do jogo
    /// (textures\water\wave1.dds) tingida pela cor. Mapa sem água no cliente (a09: a lava é o chão) não ganha plano; só sem
    /// a pasta bsdata usa a água do servidor (watermap), na cor padrão.
    /// </summary>
    // só as células com água da máscara da área (linha 0 ao norte: conferido com o chão abaixo/acima da água na a18 e no
    // mundo), juntas em faixas por linha
    static IEnumerable<(float, float, float, float, float, int)> WetStrips(ClientWater.Area a)
    {
        float left = a.X - a.Cols * a.Cell / 2, top = a.Z + a.Rows * a.Cell / 2;
        for (int r = 0; r < a.Rows; r++)
            for (int c = 0; c < a.Cols; c++)
            {
                if (!a.Wet(c, r)) continue;
                int c0 = c;
                while (c + 1 < a.Cols && a.Wet(c + 1, r)) c++;
                yield return (left + c0 * a.Cell, top - (r + 1) * a.Cell, left + (c + 1) * a.Cell, top - r * a.Cell, a.Height, a.Argb);
            }
    }

    void DrawWater()
    {
        _clientWater ??= ClientWater.Read(_t.Dir) is { } cw ? cw.SelectMany(WetStrips).ToList()
            : Water().Select(w => (w.Item1, w.Item2, w.Item3, w.Item4, w.Item5, unchecked((int)0xFF2E6B8C))).ToList();
        if (_clientWater.Count == 0) return;
        if (_waveTex == 0) _waveTex = WaveTexture();
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); GL.DepthMask(false);
        const float Tile = 24;   // metros por repetição da onda
        void Quad(float x0, float z0, float x1, float z1, float y)
        {
            GL.Begin(PrimitiveType.Quads);
            GL.TexCoord2(x0 / Tile, z0 / Tile); GL.Vertex3(x0, y, -z0);
            GL.TexCoord2(x1 / Tile, z0 / Tile); GL.Vertex3(x1, y, -z0);
            GL.TexCoord2(x1 / Tile, z1 / Tile); GL.Vertex3(x1, y, -z1);
            GL.TexCoord2(x0 / Tile, z1 / Tile); GL.Vertex3(x0, y, -z1);
            GL.End();
        }
        // Água como a do jogo (A3DTerrainWaterRender), aproximada sem segunda câmera: em grade de 8 m perto da câmera,
        //  · cor = cor da área misturada com o reflexo do céu (cor da neblina/céu), mais céu quanto mais rasante o olhar;
        //  · transparente na beira e mais densa no fundo (profundidade = água − chão);
        //  · de noite escurece com a luz da área (o jogo passa o fator dia/noite ao material da água);
        //  · duas camadas de onda andando em direções diferentes por cima.
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.AlphaTest);
        var (amb, sun) = _sky.Light(_cam, _nf) ?? (new Vector3(0.55f), new Vector3(0.8f));
        var light = Vector3.Clamp((amb + sun) * 0.65f, new Vector3(0.15f), new Vector3(1.1f));
        float camX = _cam.X, camZ = -_cam.Z;
        const float G = 8, Near = 520;
        bool nearWater = false;
        foreach (var (x0, z0, x1, z1, y, argb) in _clientWater)
        {
            float gx0 = MathF.Max(x0, camX - Near), gx1 = MathF.Min(x1, camX + Near), gz0 = MathF.Max(z0, camZ - Near), gz1 = MathF.Min(z1, camZ + Near);
            var c = Color.FromArgb(argb);
            var water = new Vector3(c.R, c.G, c.B) / 255f;
            if (gx0 < gx1 && gz0 < gz1)
            {
                nearWater = true;
                int nx = Math.Max(1, (int)MathF.Ceiling((gx1 - gx0) / G)), nz = Math.Max(1, (int)MathF.Ceiling((gz1 - gz0) / G));
                for (int j = 0; j < nz; j++)
                {
                    GL.Begin(PrimitiveType.QuadStrip);
                    for (int i = 0; i <= nx; i++)
                        for (int k = 1; k >= 0; k--)
                        {
                            float x = MathF.Min(gx0 + i * G, gx1), z = MathF.Min(gz0 + (j + k) * G, gz1);
                            var to = new Vector3(x - camX, y - _cam.Y, z - camZ);
                            float graze = 1 - MathF.Abs(to.Y) / MathF.Max(to.Length, 0.01f);
                            float fres = 0.15f + 0.75f * graze * graze * graze;
                            var col = Vector3.Lerp(water, _skyTint, fres) * light;
                            float depth = _t.HeightAt(x, z) is float h ? y - h : 5;
                            float a = Math.Clamp(0.25f + depth * 0.18f, 0.25f, 0.88f);
                            GL.Color4(col.X, col.Y, col.Z, a);
                            GL.Vertex3(x, y, -z);
                        }
                    GL.End();
                }
            }
            // longe: o resto da área em até 4 retângulos em volta da grade, sem grade (fica sob a neblina)
            var far = Vector3.Lerp(water, _skyTint, 0.7f) * light;
            GL.Color4(far.X, far.Y, far.Z, 0.85f);
            if (gx0 >= gx1 || gz0 >= gz1) Quad(x0, z0, x1, z1, y);
            else
            {
                if (x0 < gx0) Quad(x0, z0, gx0, z1, y);
                if (gx1 < x1) Quad(gx1, z0, x1, z1, y);
                if (z0 < gz0) Quad(gx0, z0, gx1, gz0, y);
                if (gz1 < z1) Quad(gx0, gz1, gx1, z1, y);
            }
        }
        // ondas: duas camadas somadas, andando (o jogo anda a textura de relevo da água)
        if (_waveTex > 0)
        {
            GL.Enable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, _waveTex);
            GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            float tsec = (float)_clock.Elapsed.TotalSeconds;
            GL.MatrixMode(MatrixMode.Texture);
            for (int layer = 0; layer < 2; layer++)
            {
                GL.LoadIdentity();
                if (layer == 0) GL.Translate(tsec * 0.010f, tsec * 0.006f, 0); else { GL.Scale(0.6f, 0.6f, 1); GL.Translate(-tsec * 0.007f, tsec * 0.009f, 0); }
                GL.Color4(light.X, light.Y, light.Z, 0.28f);
                foreach (var (x0, z0, x1, z1, y, _) in _clientWater) Quad(x0, z0, x1, z1, y + 0.02f + layer * 0.01f);
            }
            GL.LoadIdentity(); GL.MatrixMode(MatrixMode.Modelview);
            GL.Disable(EnableCap.Texture2D);
            _wavesNear = nearWater;   // ondas andando: o cronômetro (Step) redesenha, nunca de dentro do desenho
        }
        GL.DepthMask(true); GL.Disable(EnableCap.Blend); GL.Color4(1f, 1f, 1f, 1f);
    }

    List<(float X0, float Z0, float X1, float Z1, float Y, int Argb)>? _clientWater;
    Vector3 _skyTint = new(0.62f, 0.76f, 0.9f);   // cor do céu/neblina da área (reflexo na água)
    bool _wavesNear; double _wavesAt;
    int _waveTex;

    int WaveTexture()
    {
        try
        {
            var pck = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_t.Dir))!, "textures.pck");
            if (!File.Exists(pck)) return -1;
            using var a = new PckArchive(pck);
            if (a.Find(@"textures\water\wave1.dds") is not { } e || DdsDecoder.DecodeMax(a.Read(e), 256) is not { } img) return -1;
            int id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, img.Width, img.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, img.Pixels);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            return id;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return -1; }
    }

    List<(float, float, float, float, float)> Water()
    {
        if (_water is not null) return _water;
        _water = new();
        if (WaterDir is null || !Directory.Exists(WaterDir)) return _water;
        float pieceM = _t.CellsPerPiece * _t.Cell;
        for (int r = 0; r < _t.Rows; r++)
            for (int c = 0; c < _t.Cols; c++)
            {
                var f = Path.Combine(WaterDir, $"{r * _t.Cols + c + 1}.wmap");
                if (!File.Exists(f)) continue;
                var d = File.ReadAllBytes(f);
                if (d.Length < 16) continue;
                int n = BitConverter.ToInt32(d, 12);
                float cx = -_t.Width / 2 + (c + 0.5f) * pieceM, cz = _t.Height / 2 - (r + 0.5f) * pieceM;
                for (int i = 0; i < n && 16 + i * 20 + 20 <= d.Length; i++)
                {
                    int o = 16 + i * 20;
                    float x = cx + BitConverter.ToSingle(d, o), z = cz + BitConverter.ToSingle(d, o + 4), hx = BitConverter.ToSingle(d, o + 8), hz = BitConverter.ToSingle(d, o + 12), y = BitConverter.ToSingle(d, o + 16);
                    _water.Add((x - hx, z - hz, x + hx, z + hz, y));
                }
            }
        return _water;
    }

    // texto do nome numa textura (em cache) desenhada de frente para a câmera, do mesmo tamanho na tela
    void DrawLabel(string text, Vector3 at, Color color)
    {
        var key = color.ToArgb() + text;
        if (!_labels.TryGetValue(key, out var l))
        {
            // poucas texturas de nome novas por quadro (muitos NPCs juntos não travam)
            if (_newLabels++ >= 4) { _busy = true; return; }
            if (_labels.Count > 3000) { foreach (var x in _labels.Values) GL.DeleteTexture(x.Tex); _labels.Clear(); }
            using var font = new Font("Segoe UI", 11, FontStyle.Bold);
            var size = TextRenderer.MeasureText(text, font);
            using var bmp = new Bitmap(size.Width + 8, size.Height + 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var sf = new StringFormat { Alignment = StringAlignment.Center };
                var r = new RectangleF(0, 2, bmp.Width, bmp.Height);
                g.DrawString(text, font, Brushes.Black, new RectangleF(r.X + 1, r.Y + 1, r.Width, r.Height), sf);
                using var brush = new SolidBrush(color);
                g.DrawString(text, font, brush, r, sf);
            }
            l = _labels[key] = (UploadBitmap(bmp), bmp.Width, bmp.Height);
        }
        var right = Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY)); var up = Vector3.Cross(right, Forward);
        var c = Gl(at.X, at.Y, at.Z);
        float k = (c - _cam).Length * 0.0011f, hw = l.W * k / 2, hh = l.H * k;
        GL.BindTexture(TextureTarget.Texture2D, l.Tex);
        GL.Begin(PrimitiveType.Quads);
        GL.TexCoord2(0f, 1f); GL.Vertex3(c - right * hw);
        GL.TexCoord2(1f, 1f); GL.Vertex3(c + right * hw);
        GL.TexCoord2(1f, 0f); GL.Vertex3(c + right * hw + up * hh);
        GL.TexCoord2(0f, 0f); GL.Vertex3(c - right * hw + up * hh);
        GL.End();
    }

    static int UploadBitmap(Bitmap bmp)
    {
        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bd.Scan0);
        bmp.UnlockBits(bd);
        return tex;
    }

    // ---------- legenda e atalhos (canto superior esquerdo; H ou clique abre/fecha) ----------
    Rectangle LegendRect() => new(10, 10, _legendW, _legendH);

    // linha da legenda: cor (amostra de cor do ponto), teclas (desenhadas como teclas), texto; Header = título de grupo
    readonly record struct LegendRow(string Text, Color Swatch = default, string[]? Keys = null, bool Header = false);

    List<LegendRow>[] LegendColumns()
    {
        string K(string key, string pt) => Loc.T(key, pt);
        var colors = new List<LegendRow>
        {
            new(K("legend.gColors", "Cores dos pontos"), Header: true),
            new(K("legend.normal", "Branco — monstro ou NPC normal (nasce sempre)"), Color.White),
            new(K("legend.event", "Verde — evento com data/hora (gatilho agendado)"), Color.LimeGreen),
            new(K("legend.trigger", "Lilás — ativado por gatilho (fase de DG, GM)"), Color.Violet),
            new(K("legend.guard", "Vermelho — guarda (NPC em ponto de monstro)"), Color.OrangeRed),
            new(K("legend.gather", "Ciano — coletável"), Color.Cyan),
            new(K("legend.selected", "Amarelo — selecionado"), Color.Yellow),
            new(""),
            new(K("legend.gCamera", "Câmera"), Header: true),
            new(K("legend.kMove", "andar"), Keys: new[] { "W", "A", "S", "D" }),
            new(K("legend.kUpDown", "subir / descer"), Keys: new[] { K("legend.keySpace", "Espaço"), "Z" }),
            new(K("legend.kRun", "correr (segurando)"), Keys: new[] { "Shift" }),
            new(K("legend.kZoom", "aproximar / afastar"), Keys: new[] { K("legend.keyWheel", "Roda") }),
            new(K("legend.kTurn", "girar a câmera (arrastar no vazio)"), Keys: new[] { K("legend.keyMiddle", "Botão do meio") }),
            new(K("legend.kMap", "mapa"), Keys: new[] { "M" }),
        };
        var keys = new List<LegendRow>
        {
            new(K("legend.gView", "Visão"), Header: true),
            new(K("legend.kTerrain", "esconder o terreno (ver cavernas)"), Keys: new[] { "T" }),
            new(K("legend.kBuildings", "esconder as construções"), Keys: new[] { "B" }),
            new(K("legend.kGrass", "esconder a grama"), Keys: new[] { "G" }),
            new(K("legend.kPaths", "caminhos de patrulha (todos / só o do ponto)"), Keys: new[] { "P" }),
            new(K("legend.kObjs", "efeitos e sons do cenário (clique para ver)"), Keys: new[] { "O" }),
            new(K("legend.kFog", "neblina do jogo / neblina longe"), Keys: new[] { "N" }),
            new(K("legend.kDayNight", "dia / noite"), Keys: new[] { "L" }),
            new(K("legend.kLegend", "abrir / fechar esta legenda"), Keys: new[] { "H" }),
            new(""),
            new(K("legend.gPoints", "Pontos"), Header: true),
            new(K("legend.kPick", "selecionar / arrastar o ponto"), Keys: new[] { K("legend.keyLeft", "Esquerdo") }),
            new(K("legend.kGizmo", "setas: mover no eixo · anel: girar (Shift = 15°)"), Keys: new[] { K("legend.keyGizmo", "Setas / anel") }),
            new(K("legend.kRight", "novo, clonar, mover, excluir, editar"), Keys: new[] { K("legend.keyRight", "Direito") }),
            new(""),
            new(K("legend.gEdit", "Editar"), Header: true),
            new(K("legend.kCopy", "copiar e colar o ponto"), Keys: new[] { "Ctrl+C", "Ctrl+V" }),
            new(K("legend.kDelete", "excluir o ponto"), Keys: new[] { "Delete" }),
            new(K("legend.kUndo", "desfazer / refazer"), Keys: new[] { "Ctrl+Z", "Ctrl+Y" }),
        };
        return new[] { colors, keys };
    }

    void DrawLegend()
    {
        if (!_legendOpen) return;   // legenda só pelo botão Atalhos de cima (nada flutuando por padrão)
        if (_legendTex == 0 || _legendState != _legendOpen)
        {
            if (_legendTex != 0) GL.DeleteTexture(_legendTex);
            using var font = new Font("Segoe UI", 10f);
            using var bold = new Font("Segoe UI", 10f, FontStyle.Bold);
            using var keyFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            const int Row = 24, Pad = 10, Gap = 28;
            int KeyW(string k) => TextRenderer.MeasureText(k, keyFont).Width + 8;
            int KeysW(string[]? ks) => ks is null ? 0 : ks.Sum(KeyW) + (ks.Length - 1) * 4;
            string title = _legendOpen ? Loc.T("legend.title", "▾ Legenda e atalhos (clique ou H para fechar)") : Loc.T("legend.closed", "▸ Legenda e atalhos (H)");
            var cols = _legendOpen ? LegendColumns() : Array.Empty<List<LegendRow>>();
            // largura de cada coluna: a coluna das teclas alinha o texto depois da tecla mais larga
            var keyCol = cols.Select(c => c.Count == 0 ? 0 : c.Max(r => KeysW(r.Keys))).ToArray();
            var colW = cols.Select((c, i) => c.Max(r => (r.Keys is null ? r.Swatch.IsEmpty ? 0 : 18 : keyCol[i] + 10)
                + TextRenderer.MeasureText(r.Text, r.Header ? bold : font).Width)).ToArray();
            int w = Math.Max(TextRenderer.MeasureText(title, bold).Width + 2 * Pad, colW.Sum() + Gap * Math.Max(0, cols.Length - 1) + 2 * Pad);
            int h = Row + 8 + (cols.Length == 0 ? 0 : cols.Max(c => c.Count) * Row + Pad);
            using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var bg = new SolidBrush(Color.FromArgb(200, 12, 14, 18));
                g.FillRectangle(bg, 0, 0, w, h);
                using var white = new SolidBrush(Color.White);
                using var text = new SolidBrush(Color.FromArgb(225, 228, 235));
                using var gold = new SolidBrush(Color.Gold);
                using var cap = new SolidBrush(Color.FromArgb(70, 76, 90));
                using var capEdge = new Pen(Color.FromArgb(150, 160, 180));
                g.DrawString(title, bold, white, Pad, 6);
                int x0 = Pad;
                for (int c = 0; c < cols.Length; c++)
                {
                    int y = Row + 8;
                    foreach (var r in cols[c])
                    {
                        if (r.Header)
                        {
                            g.DrawString(r.Text, bold, gold, x0, y);
                            g.DrawLine(Pens.DimGray, x0, y + Row - 3, x0 + colW[c], y + Row - 3);
                        }
                        else if (r.Keys is { } ks)
                        {
                            int kx = x0;
                            foreach (var k in ks)
                            {
                                int kw = KeyW(k);
                                var rect = new Rectangle(kx, y + 2, kw, Row - 6);
                                g.FillRectangle(cap, rect); g.DrawRectangle(capEdge, rect);
                                TextRenderer.DrawText(g, k, keyFont, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                                kx += kw + 4;
                            }
                            g.DrawString(r.Text, font, text, x0 + keyCol[c] + 10, y + 1);
                        }
                        else if (!r.Swatch.IsEmpty)
                        {
                            using var b = new SolidBrush(r.Swatch);
                            g.FillRectangle(b, x0, y + 6, 11, 11);
                            g.DrawString(r.Text, font, b, x0 + 18, y + 1);
                        }
                        y += Row;
                    }
                    x0 += colW[c] + Gap;
                }
            }
            _legendTex = UploadBitmap(bmp); _legendW = w; _legendH = h; _legendState = _legendOpen;
        }
        GL.MatrixMode(MatrixMode.Projection); GL.PushMatrix(); GL.LoadIdentity(); GL.Ortho(0, _gl.Width, _gl.Height, 0, -1, 1);
        GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix(); GL.LoadIdentity();
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Fog);
        GL.BindTexture(TextureTarget.Texture2D, _legendTex); GL.Color4(1f, 1f, 1f, 1f);
        GL.Begin(PrimitiveType.Quads);
        GL.TexCoord2(0f, 0f); GL.Vertex2(10, 10); GL.TexCoord2(1f, 0f); GL.Vertex2(10 + _legendW, 10);
        GL.TexCoord2(1f, 1f); GL.Vertex2(10 + _legendW, 10 + _legendH); GL.TexCoord2(0f, 1f); GL.Vertex2(10, 10 + _legendH);
        GL.End();
        GL.Enable(EnableCap.DepthTest);
        GL.MatrixMode(MatrixMode.Projection); GL.PopMatrix(); GL.MatrixMode(MatrixMode.Modelview); GL.PopMatrix();
    }

    // ---------- dia e noite ----------
    double _dnAt;
    void StepDayNight()
    {
        double now = _clock.Elapsed.TotalSeconds, dt = Math.Min(0.1, now - _dnAt); _dnAt = now;
        float target = _nightOn ? 1 : 0;
        if (_nf != target) { _nf = Math.Clamp(_nf + MathF.Sign(target - _nf) * (float)dt / 2f, 0, 1); _gl.Invalidate(); }
        bool night = _nf >= 0.5f;
        if (night == _nightApplied) return;
        _nightApplied = night;
        _terrain.Night = night; _grass.Night = night; _buildings.Night = night;
        if (_sky.Light(_cam, night ? 1 : 0) is var (a, s)) { WorldTrees.SceneAmbient = a; WorldTrees.SceneSun = s; }
        _trees.Relight();
        _status.Text = night ? Loc.T("world.night", "Noite (L volta para o dia)") : Loc.T("world.day", "Dia (L para a noite)");
    }

    // ---------- setas de mover e anel de girar (ponto selecionado) ----------
    // X = leste, Z = norte (o mapa do jogo cresce para cima com Z), Y = altura; o anel gira o ponto em volta do Y e o
    // traço amarelo mostra para onde ele olha. Só aparecem com um ponto selecionado e visível.
    // chamado dezenas de vezes por quadro (Caminhos, gizmo, rótulos): guarda o último achado enquanto a escolha e a lista
    // de perto são as mesmas (antes varria os marcadores a cada chamada: a câmera travava na ferramenta Caminhos)
    object? _smTag; List<Marker>? _smList; Marker? _smMark;
    Marker? SelectedMark()
    {
        if (_selected() is not { } s) return null;
        if (ReferenceEquals(s, _smTag) && ReferenceEquals(_nearMarks, _smList) && _smMark is { } cm && Dist2(cm) < 400 * 400) return cm;
        _smTag = s; _smList = _nearMarks;
        return _smMark = _nearMarks.FirstOrDefault(m => m.Tag == s && Dist2(m) < 400 * 400);
    }

    float GizmoSize(Vector3 c) => Math.Clamp((Gl(c.X, c.Y, c.Z) - _cam).Length * 0.12f, 1.5f, 40f);

    static readonly Vector3[] GizmoDirs = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };

    /// <summary>Centro e tamanho total (X, Z) da área de nascimento de um ponto do npcgen; null se não é ponto com área.</summary>
    static (float Cx, float Cz, float Sx, float Sz)? AreaOf(object tag) => tag switch
    {
        NpcGenSpawn s => (s.Position.X, s.Position.Z, s.Spread.X, s.Spread.Z),
        NpcGenResource r => (r.Position.X, r.Position.Z, r.SpreadX, r.SpreadZ),
        _ => null,
    };

    /// <summary>Alça no canto +X/+Z da área (área zerada: 1,5 m do centro, para dar onde puxar).</summary>
    Vector3? AreaHandle(Marker m)
    {
        if (ResizeArea is null || AreaOf(m.Tag) is not { } a) return null;
        var (sx, sz) = m.Tag == _gizmoTag && _gizmoSize is { } g ? g : (a.Sx, a.Sz);
        float hx = a.Cx + MathF.Max(sx / 2, 1.5f), hz = a.Cz + MathF.Max(sz / 2, 1.5f);
        return new Vector3(hx, (_t.HeightAt(hx, hz) ?? m.Y) + 0.4f, hz);
    }

    int GizmoHit(Point p)
    {
        if (SelectedMark() is not { } m) return 0;
        // ferramenta Caminhos: só a alça da área (modo área); fora dela, só setas e anel
        if (_tool == Tool.Paths) return SelPath is null && AreaHandle(m) is { } h && Project(h) is { } hp && MathF.Abs(hp.X - p.X) < 9 && MathF.Abs(hp.Y - p.Y) < 9 ? 5 : 0;
        var c = PosOf(m); float L = GizmoSize(c);
        if (Project(c) is not { } a) return 0;
        for (int k = 0; k < 3; k++)
            if (Project(c + GizmoDirs[k] * L * 1.18f) is { } b && SegDist(p, a, b, 0.2f) < 9) return k + 1;
        PointF? prev = null;
        for (int i = 0; i <= 48; i++)
        {
            float t = i / 48f * MathF.Tau;
            var q = Project(c + new Vector3(MathF.Sin(t), 0, MathF.Cos(t)) * L * 0.8f);
            if (q is { } qq && prev is { } pp && SegDist(p, pp, qq, 0) < 7) return 4;
            prev = q;
        }
        return 0;
    }

    static float SegDist(Point p, PointF a, PointF b, float from)
    {
        float abx = b.X - a.X, aby = b.Y - a.Y, len2 = abx * abx + aby * aby;
        float u = len2 < 1e-3f ? 0 : Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, from, 1);
        float dx = a.X + u * abx - p.X, dy = a.Y + u * aby - p.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    void GizmoDrag(Point p)
    {
        var c = _gizmoStart; float L = GizmoSize(c);
        if (_gizmoAxis == 5)
        {
            // alça da área: o ponto do chão sob o mouse é o novo canto; a área cresce igual para os dois lados (passo de 10 cm)
            if (Ray(p) is not { } hit || _gizmoTag is null || AreaOf(_gizmoTag) is not { } ar) return;
            float nx = MathF.Round(MathF.Abs(hit.X - ar.Cx) * 2 * 10) / 10, nz = MathF.Round(MathF.Abs(hit.Z - ar.Cz) * 2 * 10) / 10;
            _gizmoSize = (nx, nz);
            _status.Text = string.Format(Loc.T("gizmo.area", "Área de nascimento: {0:0.0} × {1:0.0} m"), nx, nz);
            _gl.Invalidate();
            return;
        }
        if (_gizmoAxis <= 3)
        {
            // quanto o mouse andou ao longo da seta na tela vira metros ao longo do eixo (passo de 10 cm)
            var dir = GizmoDirs[_gizmoAxis - 1];
            if (Project(c) is not { } a || Project(c + dir * L) is not { } b) return;
            float sx = b.X - a.X, sy = b.Y - a.Y, len2 = sx * sx + sy * sy;
            if (len2 < 4) return;
            float t = MathF.Round(((p.X - _gizmoDown.X) * sx + (p.Y - _gizmoDown.Y) * sy) / len2 * L * 10) / 10;
            var g = c + dir * t;
            _gizmoPos = g;
            _status.Text = string.Format(Loc.T("gizmo.moving", "Movendo: X {0:0.0}   altura {1:0.0}   Z {2:0.0}"), g.X, g.Y, g.Z);
        }
        else
        {
            // o mouse no plano horizontal do ponto: o ponto passa a olhar para ele (Shift = de 15 em 15 graus)
            float nx = 2f * p.X / _gl.Width - 1, ny = 1 - 2f * p.Y / _gl.Height;
            var inv = Matrix4.Invert(View * Proj);
            var a4 = new Vector4(nx, ny, -1, 1) * inv; var b4 = new Vector4(nx, ny, 1, 1) * inv;
            var o = a4.Xyz / a4.W; var d = Vector3.Normalize(b4.Xyz / b4.W - o);
            if (MathF.Abs(d.Y) < 1e-4f) return;
            float s0 = (c.Y - o.Y) / d.Y;
            if (s0 <= 0) return;
            var hit = o + d * s0;   // OpenGL: z do jogo invertido
            float yaw = MathHelper.RadiansToDegrees(MathF.Atan2(hit.X - c.X, -hit.Z - c.Z));
            if ((ModifierKeys & Keys.Shift) != 0) yaw = MathF.Round(yaw / 15) * 15;
            _gizmoYaw = (yaw + 360) % 360;
            _status.Text = string.Format(Loc.T("gizmo.turning", "Girando: {0:0}° (Shift = de 15 em 15°)"), _gizmoYaw);
        }
        _gl.Invalidate();
    }

    void DrawGizmo()
    {
        if (_tool == Tool.Paths || SelectedMark() is not { } m) return;   // Caminhos: sem setas/anel, só o caminho e a área
        var c = PosOf(m); float L = GizmoSize(c);
        int hot = _gizmoAxis > 0 ? _gizmoAxis : _gizmoHover;
        float yaw = MathHelper.DegreesToRadians(m.Tag == _gizmoTag && _gizmoYaw is { } gy ? gy : m.Yaw);
        void V(Vector3 q) => GL.Vertex3(Gl(q.X, q.Y, q.Z));
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting);
        GL.LineWidth(3);
        Color[] cols = { Color.Red, Color.LimeGreen, Color.DodgerBlue };
        for (int k = 0; k < 3; k++)
        {
            var d = GizmoDirs[k]; var tip = c + d * L;
            Col(hot == k + 1 ? Color.White : cols[k]);
            GL.Begin(PrimitiveType.Lines); V(c); V(tip); GL.End();
            // ponta: cone de 8 lados
            var u = Vector3.Normalize(Vector3.Cross(d, k == 1 ? Vector3.UnitX : Vector3.UnitY)); var w = Vector3.Cross(d, u);
            GL.Begin(PrimitiveType.TriangleFan); V(c + d * L * 1.18f);
            for (int i = 0; i <= 8; i++) { float t = i / 8f * MathF.Tau; V(tip + (u * MathF.Cos(t) + w * MathF.Sin(t)) * L * 0.06f); }
            GL.End();
        }
        Col(hot == 4 ? Color.White : Color.Gold);
        GL.Begin(PrimitiveType.LineLoop);
        for (int i = 0; i < 48; i++) { float t = i / 48f * MathF.Tau; V(c + new Vector3(MathF.Sin(t), 0, MathF.Cos(t)) * L * 0.8f); }
        GL.End();
        GL.Begin(PrimitiveType.Lines); V(c); V(c + new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw)) * L * 0.8f); GL.End();
        GL.LineWidth(1);
        GL.Enable(EnableCap.DepthTest);
    }

    void GizmoLabels()
    {
        if (_tool == Tool.Paths || SelectedMark() is not { } m) return;
        var c = PosOf(m); float L = GizmoSize(c) * 1.35f;
        DrawLabel(Loc.T("gizmo.x", "X · leste"), c + Vector3.UnitX * L, Color.Red);
        DrawLabel(Loc.T("gizmo.y", "Y · altura"), c + Vector3.UnitY * L, Color.LimeGreen);
        DrawLabel(Loc.T("gizmo.z", "Z · norte"), c + Vector3.UnitZ * L, Color.DodgerBlue);
    }

    // ---------- escolha na tela ----------
    // ponto da tela de uma posição do jogo; null se atrás da câmera
    PointF? Project(Vector3 game)
    {
        var c = new Vector4(Gl(game.X, game.Y, game.Z), 1) * View * Proj;
        if (c.W <= 0.1f) return null;
        return new PointF((c.X / c.W + 1) / 2 * _gl.Width, (1 - c.Y / c.W) / 2 * _gl.Height);
    }

    /// <summary>Ponto do npcgen debaixo do mouse: a até 12 px do segmento pé–cabeça do modelo na tela (o mais perto da câmera).</summary>
    Marker? PickScreen(Point p)
    {
        Marker? best = null; float bestD = float.MaxValue;
        foreach (var m in _nearMarks)
        {
            if (Dist2(m) > 400 * 400) continue;
            var pos = PosOf(m);
            float top = m.Model is not null && _models.Top(m.Model) is { } t ? Math.Max(t, 1) : 2;
            if (Project(pos) is not { } a || Project(pos + new Vector3(0, top, 0)) is not { } b) continue;
            float abx = b.X - a.X, aby = b.Y - a.Y, len2 = abx * abx + aby * aby;
            float u = len2 < 1e-3f ? 0 : Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, 0, 1);
            float dx = a.X + u * abx - p.X, dy = a.Y + u * aby - p.Y;
            float pix = MathF.Sqrt(dx * dx + dy * dy);
            float halfW = Math.Max(10, Math.Min(40, MathF.Sqrt(len2) * 0.3f));   // modelo mais largo de perto
            if (pix > halfW) continue;
            float score = pix + MathF.Sqrt(Dist2(m)) * 0.05f;
            if (score < bestD) { bestD = score; best = m; }
        }
        return best;
    }

    // raio do mouse até o chão: passos de 4 m e depois refina
    Vector3? Ray(Point p)
    {
        if (_gl.Width < 2 || _gl.Height < 2) return null;
        float nx = 2f * p.X / _gl.Width - 1, ny = 1 - 2f * p.Y / _gl.Height;
        var inv = Matrix4.Invert(View * Proj);
        var a = new Vector4(nx, ny, -1, 1) * inv; var b = new Vector4(nx, ny, 1, 1) * inv;
        var o = a.Xyz / a.W; var d = Vector3.Normalize(b.Xyz / b.W - o);
        // terreno escondido (tampa): o clique cai no piso de referência
        if ((!_terrain.Visible || _t.HeightAt(o.X, -o.Z) is float oh && o.Y < oh) && _floorY is float fy)
        {
            if (MathF.Abs(d.Y) < 1e-4f) return null;
            float s0 = (fy - o.Y) / d.Y;
            if (s0 <= 0) return null;
            var r0 = o + d * s0;
            return new Vector3(r0.X, fy, -r0.Z);
        }
        float prev = 0;
        for (float s = 0; s < 4000; s += 4)
        {
            var q = o + d * s;
            if (_t.HeightAt(q.X, -q.Z) is { } h && q.Y <= h)
            {
                float lo = prev, hi = s;
                for (int i = 0; i < 12; i++) { float m = (lo + hi) / 2; var qm = o + d * m; if (_t.HeightAt(qm.X, -qm.Z) is { } hm && qm.Y <= hm) hi = m; else lo = m; }
                var r = o + d * hi;
                return new Vector3(r.X, _t.HeightAt(r.X, -r.Z) ?? r.Y, -r.Z);
            }
            prev = s;
        }
        return null;
    }

    void FindNext()
    {
        var q = _find.Text.Trim();
        if (q.Length == 0) return;
        // coordenadas: "x z" ou "x y z" (com vírgula, ponto e vírgula ou espaço)
        var nums = q.Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(part => part.Split(", ", StringSplitOptions.RemoveEmptyEntries)).ToList();
        if (nums.Count is 2 or 3 && nums.All(n => float.TryParse(n.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
        {
            var f = nums.Select(n => float.Parse(n.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture)).ToList();
            float x = f[0], z = f[^1];
            float y = f.Count == 3 ? f[1] : _t.HeightAt(x, z) ?? 0;
            LookAt(new Vector3(x, y, z)); _gl.Invalidate();
            _status.Text = string.Format(Loc.T("world.wentTo", "Câmera em X {0:0} Z {1:0}"), x, z);
            return;
        }
        var hits = _marks.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count == 0) { _status.Text = Loc.T("world.notFound", "Nenhum ponto com esse nome ou ID."); return; }
        _findAt = (_findAt + 1) % hits.Count;
        var m = hits[_findAt];
        _select(m.Tag); LookAt(PosOf(m));
        _status.Text = $"{m.Name}   ({_findAt + 1}/{hits.Count})";
        _gl.Invalidate();
    }

    void RightClick(Point p)
    {
        var under = PickScreen(p);
        var g = Ray(p) ?? (under is not null ? PosOf(under) : null);
        if (g is not { } at) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add(string.Format(Loc.T("world.copy", "Copiar coordenada ({0:0.0}, {1:0.0}, {2:0.0})"), at.X, at.Y, at.Z), null,
            (_, _) => Clipboard.SetText(FormattableString.Invariant($"{at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}")));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("world.newMob", "Novo monstro aqui…"), null, (_, _) => { _newHere(at, 0); RefreshMarkers(); });
        // NPC novo = registro novo (cópia limpa de um NPC base, ver EditorForm.ClearNpc); pôr um que já existe é outra opção
        if (CreateNewHere is not null)
            menu.Items.Add(Loc.T("world.newNpc", "Novo NPC aqui…"), null, (_, _) => CreateNewHere(at, 1));
        menu.Items.Add(Loc.T("world.existingNpc", "NPC que já existe aqui…"), null, (_, _) => { _newHere(at, 1); RefreshMarkers(); });
        menu.Items.Add(Loc.T("world.newGather", "Novo coletável aqui…"), null, (_, _) => { _newHere(at, 2); RefreshMarkers(); });
        if (CreateNewHere is not null)
            menu.Items.Add(Loc.T("world.createHere", "Criar monstro NOVO (a partir de um modelo) aqui…"), null, (_, _) => CreateNewHere(at, 0));
        // em cima de um ponto: ele vira o selecionado e dá para editar no software ou excluir
        if (under is not null)
        {
            _select(under.Tag);
            menu.Items.Add(new ToolStripSeparator());
            if (EditPoint is not null)
                menu.Items.Add(string.Format(Loc.T("world.edit", "Editar {0} no software…"), under.Name), null, (_, _) => EditPoint(under.Tag));
            if (DeletePoint is not null)
                menu.Items.Add(string.Format(Loc.T("world.delete", "Excluir {0}"), under.Name), null, (_, _) => { if (Confirm()) { DeletePoint(under.Tag); RefreshMarkers(); } });
            menu.Items.Add(Loc.T("world.copyPoint", "Copiar ponto (Ctrl+C)"), null, (_, _) => _clip = under.Tag);
        }
        if (_selected() is { } s)
        {
            if (_moveHere is not null)
                menu.Items.Add(Loc.T("world.moveHere", "Mover o ponto selecionado para cá"), null, (_, _) => { _moveHere(s, at); RefreshMarkers(); });
            if (CloneHere is not null)
                menu.Items.Add(Loc.T("world.cloneHere", "Clonar o ponto selecionado aqui"), null, (_, _) => { CloneHere(s, at); RefreshMarkers(); });
        }
        if (_clip is not null && CloneHere is not null)
            menu.Items.Add(Loc.T("world.paste", "Colar ponto copiado aqui (Ctrl+V)"), null, (_, _) => { CloneHere(_clip, at); RefreshMarkers(); });
        // objeto do cenário escolhido (O ligado): efeito ou som vai para o chão clicado
        if (_sceneObjs && _ecw is not null && _sceneSel is not null)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Loc.T("world.objMoveHere", "Mover o efeito/som escolhido para cá"), null, (_, _) => MoveSceneObjectTo(at));
        }
        menu.Show(_gl, p);
    }

    // ---------- mapa-múndi (M): o mapa pintado do jogo (mundo) ou o minimapa (instâncias) ----------
    const int MapTilePx = 256;

    Bitmap WorldMap()
    {
        if (_worldMap is not null) return _worldMap;
        // instância: o pergaminho da tecla M do jogo, quando o cliente tem (senão, as peças do minimapa como antes)
        if (_t.Name != "world" && _element is not null && InstanceMap.Load(_element, MapTag) is { } inst) { _instMap = inst; return _worldMap = inst.Image; }
        bool painted = _t.Name == "world" && _terrain.ReadSurface(@"surfaces\maps\000.dds") is not null;
        float tileM = painted ? 1024 : WorldTerrain.TileM;
        int rows = (int)Math.Ceiling(_t.Height / tileM), cols = (int)Math.Ceiling(_t.Width / tileM);
        var bmp = new Bitmap(cols * MapTilePx, rows * MapTilePx);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(40, 52, 64));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var img = painted ? (_terrain.ReadSurface($@"surfaces\maps\{r:00}{c}.dds") is { } d ? DdsDecoder.Decode(d) : null) : _terrain.MinimapImage(r, c);
                if (img is not { } im) continue;
                using var tile = new Bitmap(im.Width, im.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bd = tile.LockBits(new Rectangle(0, 0, im.Width, im.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, tile.PixelFormat);
                System.Runtime.InteropServices.Marshal.Copy(im.Pixels, 0, bd.Scan0, im.Pixels.Length);
                tile.UnlockBits(bd);
                g.DrawImage(tile, c * MapTilePx, r * MapTilePx, MapTilePx, MapTilePx);
            }
        // só a área do mapa (o resto da última linha/coluna fica de fora)
        _mapMPerPx = tileM / MapTilePx;
        int w = (int)(_t.Width / _mapMPerPx), h = (int)(_t.Height / _mapMPerPx);
        if (w < bmp.Width || h < bmp.Height) { var crop = bmp.Clone(new Rectangle(0, 0, Math.Min(w, bmp.Width), Math.Min(h, bmp.Height)), bmp.PixelFormat); bmp.Dispose(); bmp = crop; }
        return _worldMap = bmp;
    }

    // ponto do jogo (x, z) ↔ pixel da imagem do mapa
    PointF MapPx(float x, float z) => _instMap?.ToPixel(x, z) ?? new((x + _t.Width / 2) / _mapMPerPx, (_t.Height / 2 - z) / _mapMPerPx);
    (float X, float Z) MapGame(float px, float py) => _instMap?.ToGame(px, py) ?? (px * _mapMPerPx - _t.Width / 2, _t.Height / 2 - py * _mapMPerPx);

    void ShowMap()
    {
        _keys.Clear();
        Cursor = Cursors.WaitCursor;
        var img = WorldMap();
        Cursor = Cursors.Default;
        var wa = Screen.FromControl(this).WorkingArea;
        using var f = new Form { StartPosition = FormStartPosition.CenterParent, Size = new Size(Math.Min(1000, wa.Width - 60), Math.Min(900, wa.Height - 60)), KeyPreview = true,
            FormBorderStyle = FormBorderStyle.SizableToolWindow, Text = Loc.T("world.mapTitle", "Mapa — roda: zoom · arrastar: mover · M ou Esc fecha") };
        var pic = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 28, 34) };
        f.Controls.Add(pic);
        float minZoom = 1, zoom = 1;
        PointF center = MapPx(_cam.X, -_cam.Z);   // em px do mapa, começa na câmera
        // o mapa não sai da janela: zoom mínimo = cabe inteiro; o centro fica preso às bordas
        void Fit()
        {
            if (pic.Width <= 0 || pic.Height <= 0) return;   // janela minimizada: nada a encaixar
            minZoom = Math.Min(pic.Width / (float)img.Width, pic.Height / (float)img.Height);
            zoom = Math.Clamp(float.IsFinite(zoom) ? zoom : 1, minZoom, 6);
            // câmera fora do mapa (ou conversão inválida) deixava o centro NaN/gigante e o GDI+ estourava (OverflowException)
            if (!float.IsFinite(center.X) || !float.IsFinite(center.Y)) center = new(img.Width / 2f, img.Height / 2f);
            float hw = pic.Width / 2f / zoom, hh = pic.Height / 2f / zoom;
            center = new(img.Width <= hw * 2 ? img.Width / 2f : Math.Clamp(center.X, hw, img.Width - hw),
                         img.Height <= hh * 2 ? img.Height / 2f : Math.Clamp(center.Y, hh, img.Height - hh));
        }
        PointF ToScreen(float x, float z) { var q = MapPx(x, z); return new((q.X - center.X) * zoom + pic.Width / 2f, (q.Y - center.Y) * zoom + pic.Height / 2f); }
        (float X, float Z) World(Point p) => MapGame((p.X - pic.Width / 2f) / zoom + center.X, (p.Y - pic.Height / 2f) / zoom + center.Y);
        pic.Paint += (_, e) =>
        {
            var gr = e.Graphics;
            gr.InterpolationMode = zoom < 1 ? System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear : System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            gr.DrawImage(img, pic.Width / 2f - center.X * zoom, pic.Height / 2f - center.Y * zoom, img.Width * zoom, img.Height * zoom);
            var c = ToScreen(_cam.X, -_cam.Z);
            gr.FillEllipse(Brushes.Red, c.X - 6, c.Y - 6, 12, 12); gr.DrawEllipse(Pens.White, c.X - 6, c.Y - 6, 12, 12);
            // aviso fixo embaixo
            using var font = new Font("Segoe UI", 10, FontStyle.Bold);
            var tip = Loc.T("world.mapTip", "Clique com o botão direito para teleportar");
            var sz = gr.MeasureString(tip, font);
            using var bg = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            var box = new RectangleF((pic.Width - sz.Width) / 2 - 10, pic.Height - sz.Height - 16, sz.Width + 20, sz.Height + 8);
            gr.FillRectangle(bg, box);
            gr.DrawString(tip, font, Brushes.White, box.X + 10, box.Y + 4);
        };
        pic.Resize += (_, _) => { Fit(); pic.Invalidate(); };
        Point drag = Point.Empty; bool panning = false;
        pic.MouseDown += (_, e) => { if (e.Button is MouseButtons.Left or MouseButtons.Middle) { panning = true; drag = e.Location; } };
        pic.MouseMove += (_, e) =>
        {
            if (panning) { center = new(center.X - (e.X - drag.X) / zoom, center.Y - (e.Y - drag.Y) / zoom); drag = e.Location; Fit(); pic.Invalidate(); }
            var (x, z) = World(e.Location);
            f.Text = string.Format(Loc.T("world.mapCoord", "Mapa — X {0:0}  Z {1:0} · roda: zoom · arrastar: mover · M ou Esc fecha"), x, z);
        };
        pic.MouseUp += (_, e) =>
        {
            panning = false;
            if (e.Button != MouseButtons.Right) return;
            var (x, z) = World(e.Location);
            if (_t.HeightAt(x, z) is not { } h) return;
            LookAt(new Vector3(x, h, z));
            f.Close();
        };
        pic.MouseWheel += (_, e) =>
        {
            // o ponto sob o mouse fica parado
            PointF under = new((e.X - pic.Width / 2f) / zoom + center.X, (e.Y - pic.Height / 2f) / zoom + center.Y);
            zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.25f : 0.8f), minZoom, 6f);
            center = new(under.X - (e.X - pic.Width / 2f) / zoom, under.Y - (e.Y - pic.Height / 2f) / zoom);
            Fit(); pic.Invalidate();
        };
        f.KeyDown += (_, e) => { if (e.KeyCode is Keys.M or Keys.Escape) f.Close(); };
        f.Shown += (_, _) => { zoom = Math.Max(1.5f, Math.Min(pic.Width / (float)img.Width, pic.Height / (float)img.Height)); Fit(); pic.Focus(); pic.Invalidate(); };
        f.ShowDialog(this);
        _gl.Invalidate();
    }

    // nome do mapa SÓ do instance.txt do cliente aberto, no idioma dele (usuário 01/11: tradução nossa ficaria diferente do
    // jogo); sem nome no cliente, só a sigla da pasta (a01...). A tabela fixa em inglês saiu.
    static Dictionary<string, string> _clientNames = new();
    sealed record MapItem(string Key)
    {
        public override string ToString() => _clientNames.TryGetValue(Key, out var c) ? $"{Key} — {c}" : Key;
    }

    /// <summary>Barra de status sem piscar: desenha com buffer duplo e só repinta quando o texto muda.</summary>
    // ---------- criar / ligar quests ----------

    void OpenCreator(int npcId)
    {
        if (_host is null) return;
        if (_editor is not null) { _editor.Focus(); return; }
        if (_host.NewDraft(npcId, out var why) is not { } draft) { MessageBox.Show(this, why, Loc.T("qe.cantCreate", "Não dá para criar quest agora"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        OpenDraft(draft, npcId);
    }

    /// <summary>Editar quest: o mesmo assistente, já preenchido com a quest (Concluir troca a quest no lugar).</summary>
    void OpenQuestEditor(int questId)
    {
        if (_host is null) return;
        if (_editor is not null) { _editor.Focus(); return; }
        if (_host.EditDraft(questId, out var why) is not { } draft) { MessageBox.Show(this, why, Loc.T("qe.cantEdit", "Não dá para editar esta quest aqui"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        OpenDraft(draft, _questNpc);
    }

    void DeleteQuest(int questId)
    {
        if (_host is null || _editor is not null) return;
        if (_host.DeleteAsk(questId) is not { } ask)
        {
            // a quest já não existe (apagada fora daqui): só sobrou o ID no serviço do NPC
            if (MessageBox.Show(this, string.Format(Loc.T("qe.missingAsk", "A quest {0} não existe no tasks.data, mas o serviço deste NPC ainda aponta para ela (no jogo ele ofereceria uma quest que não existe).\n\nTirar o ID {0} dos serviços dos NPCs? Ctrl+Z desfaz."), questId),
                    Loc.T("wq.removeMissing", "Tirar do NPC"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (_host.RemoveMissing(questId) is { } e2) { MessageBox.Show(this, e2, Loc.T("wq.removeMissing", "Tirar do NPC"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            QuestDone(_questNpc);
            _status.Text = string.Format(Loc.T("qe.missingDone", "ID {0} tirado dos serviços dos NPCs (Ctrl+Z desfaz) — clique em Salvar no editor para gravar."), questId);
            return;
        }
        if (MessageBox.Show(this, ask, Loc.T("quest.delete", "Excluir quest"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        // quest com seguintes na cadeia (nova ao concluir / quest anterior): pergunta se saem junto (usuário 01/11)
        bool withNext = false;
        if (_host.NextQuests(questId) is { Count: > 0 } next)
        {
            var list = string.Join("\n", next.Take(15).Select(n => $"   {n.Id}  {n.Name}")) + (next.Count > 15 ? "\n   …" : "");
            var r = MessageBox.Show(this, string.Format(Loc.T("qe.askNext", "Esta quest abre {0} quest(s) seguinte(s) na cadeia:\n\n{1}\n\nExcluir também essas quests e tirá-las dos NPCs e áreas onde estão?\n\nSim = exclui todas · Não = só esta (as seguintes ficam esperando uma quest que não existe) · Cancelar = nada"), next.Count, list),
                Loc.T("quest.delete", "Excluir quest"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            withNext = r == DialogResult.Yes;
        }
        if (_host.Delete(questId, withNext) is { } err) { MessageBox.Show(this, err, Loc.T("quest.delete", "Excluir quest"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        QuestDone(_questNpc);
        _status.Text = string.Format(Loc.T("qe.deleted", "Quest {0} excluída e tirada dos NPCs (Ctrl+Z desfaz) — clique em Salvar no editor para gravar."), questId);
    }

    void OpenDraft(QuestDraft draft, int npcId)
    {
        bool editing = draft.Editing > 0;
        var world = new QuestWorld(
            act => { _pickNpc = act; _pickArea = null; _pickMonster = null; _pickMine = null; _status.Text = Loc.T("qe.picking", "Clique no NPC no mundo… (Esc cancela)"); _gl.Focus(); },
            act => { _pickArea = act; _pickNpc = null; _status.Text = Loc.T("qe.areaPicking", "Arraste no chão para marcar a área (clique sem arrastar = 10 × 10 m; Esc cancela)…"); _gl.Focus(); },
            (areas, sel) => { _areas = areas; _areaSel = sel; _gl.Invalidate(); },
            () => MapTag,
            act => { _pickMonster = act; _pickNpc = null; _pickArea = null; _pickMine = null; _status.Text = Loc.T("qe.pickingMonster", "Clique num ponto de monstro no mundo… (Esc cancela)"); _gl.Focus(); },
            act => { _pickMine = act; _pickNpc = null; _pickArea = null; _pickMonster = null; _status.Text = Loc.T("qe.pickingMine", "Clique num ponto de coleta (minério, erva…) no mundo… (Esc cancela)"); _gl.Focus(); });
        var ed = _editor = new WorldQuestEditor(_host, draft, world);
        // durante a edição só o assistente fica na tela: o menu/painel de ferramentas some e volta ao concluir ou cancelar (usuário 01/11)
        _tools.Visible = false;
        void Close() { _pickNpc = null; _pickMonster = null; _pickMine = null; _pickArea = null; _areaA = _areaB = null; _areas = Array.Empty<QuestArea>(); Controls.Remove(ed); ed.Dispose(); _editor = null; _tools.Visible = true; LayoutTools(); _gl.Focus(); _gl.Invalidate(); }
        ed.Created += id =>
        {
            Close();
            QuestDone(npcId);
            _status.Text = editing ? string.Format(Loc.T("qe.edited", "Quest {0} alterada (Ctrl+Z desfaz) — clique em Salvar no editor para gravar."), id)
                : string.Format(Loc.T("qe.created2", "Quest {0} criada e ligada aos NPCs (Ctrl+Z desfaz) — clique em Salvar no editor para gravar."), id);
        };
        ed.Cancelled += Close;
        Controls.Add(ed);
    }

    // a ação mais recente é desfeita primeiro: quest (se nada de ponto mudou depois dela) ou ponto do NPCGen
    bool UndoAny()
    {
        if (SceneUndo(redo: false)) return true;   // cenário só se foi a última ação
        if (PathUndo(redo: false)) return true;    // caminhos idem
        if (_qUndo.Count > 0 && _qUndo[^1] == (UndoDepth?.Invoke() ?? 0) && _host?.Undo() == true)
        {
            _qUndo.RemoveAt(_qUndo.Count - 1); _qRedo.Add(RedoDepth?.Invoke() ?? 0);
            RefreshQuestPanel(); _status.Text = Loc.T("qe.undone", "Quest desfeita (Ctrl+Y refaz).");
            return true;
        }
        return UndoAction?.Invoke() ?? false;
    }

    bool RedoAny()
    {
        if (SceneUndo(redo: true)) return true;
        if (PathUndo(redo: true)) return true;
        if (_qRedo.Count > 0 && _qRedo[^1] == (RedoDepth?.Invoke() ?? 0) && _host?.Redo() == true)
        {
            _qRedo.RemoveAt(_qRedo.Count - 1); _qUndo.Add(UndoDepth?.Invoke() ?? 0);
            RefreshQuestPanel(); _status.Text = Loc.T("qe.redone", "Quest refeita.");
            return true;
        }
        return RedoAction?.Invoke() ?? false;
    }

    void QuestDone(int npcId)
    {
        _qUndo.Add(UndoDepth?.Invoke() ?? 0); _qRedo.Clear();
        _questNpc = npcId; RefreshQuestPanel();
    }

    void RefreshQuestPanel() { if (_questNpc > 0 && NpcQuests?.Invoke(_questNpc) is { } qi && _tool == Tool.Quests) ShowQuestTool(qi); }

    void LinkExisting(int npcId)
    {
        if (_host is null) return;
        using var dlg = new QuestLinkDialog(_host, _host.NameOf(npcId) ?? npcId.ToString());
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.QuestId <= 0) return;
        if (_host.LinkExisting(npcId, dlg.QuestId) is { } err) { MessageBox.Show(this, err, Loc.T("ql.failed", "Não foi possível dar a quest"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        QuestDone(npcId);
        _status.Text = string.Format(Loc.T("ql.done2", "Quest {0} agora é dada por este NPC (Ctrl+Z desfaz) — clique em Salvar no editor para gravar."), dlg.QuestId);
    }

    /// <summary>
    /// Linha de status desenhada à mão: trocar o texto só invalida e repinta com buffer duplo.
    /// (Label manda WM_SETTEXT e apaga o fundo antes de pintar — era o piscar branco ao mover o mouse.)
    /// </summary>
    sealed class StatusLine : Control
    {
        string _text = "";
        public StatusLine() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string Text { get => _text; set { value ??= ""; if (value == _text) return; _text = value; Invalidate(); } }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            TextRenderer.DrawText(e.Graphics, _text, Font, new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    sealed class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; SetStyle(ControlStyles.Selectable, true); } }
}
