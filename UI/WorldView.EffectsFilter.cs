using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Filtro "Efeitos" da barra Mostrar (desenho aprovado 08/11): mostra os efeitos visuais do mapa (.gfx do .ecwld: fogo,
/// fumaça, brilhos…) como marcas laranja sem abrir os Objetos do mundo; o campo ao lado filtra pelo nome (traduzido pelo
/// glossário ou o original) e o nome aparece ao passar o mouse. Só mostra: mexer continua nos Objetos do mundo.
/// </summary>
internal sealed partial class WorldView
{
    bool _fxShow;
    string _fxFilter = "";
    ToolStripTextBox? _fxFind;
    ToolStripButton? _fxBtn;

    void InitEffectsFilter()
    {
        _fxBtn = new ToolStripButton(Loc.T("world.fx", "Efeitos"), DarkBarRenderer.Dot(Color.FromArgb(70, 70, 70)))
        {
            CheckOnClick = true, ForeColor = Color.White, TextImageRelation = TextImageRelation.ImageBeforeText,
            ToolTipText = Loc.T("world.fx.h", "Mostrar os efeitos visuais do mapa (fogo, fumaça, brilhos…) como marcas laranja; passe o mouse para ver o nome. Mexer neles fica nos Objetos do mundo."),
        };
        _fxFind = new ToolStripTextBox { Visible = false, Width = 140, BackColor = Color.FromArgb(40, 45, 54), ForeColor = Color.White, ToolTipText = Loc.T("world.fx.find", "Filtrar efeitos pelo nome (ex.: fogo, fumaça, água)") };
        _fxBtn.CheckedChanged += (_, _) =>
        {
            _fxShow = _fxBtn.Checked; _fxFind.Visible = _fxShow;
            _fxBtn.Image = DarkBarRenderer.Dot(_fxShow ? Color.FromArgb(255, 140, 26) : Color.FromArgb(70, 70, 70));
            _status.Text = _fxShow ? string.Format(Loc.T("world.fx.count", "{0} efeito(s) neste mapa."), _trees.Scene?.Effects.Count ?? 0) : "";
            _gl.Invalidate(); _gl.Focus();
        };
        _fxFind.TextChanged += (_, _) => { _fxFilter = _fxFind.Text.Trim(); _gl.Invalidate(); };
        _showBar.Items.Add(_fxBtn);
        _showBar.Items.Add(_fxFind);
    }

    string FxName(WorldScene.SceneEffect fx)
    {
        var b = TextureNameGlossary.Base(fx.Gfx);
        var d = TextureNameGlossary.Display(fx.Gfx, _element);
        return d == b ? b : d + " (" + b + ")";
    }

    bool FxMatch(WorldScene.SceneEffect fx) => _fxFilter.Length == 0 || fx.Gfx.Contains(_fxFilter, StringComparison.OrdinalIgnoreCase) || FxName(fx).Contains(_fxFilter, StringComparison.OrdinalIgnoreCase);

    void DrawEffectsFilter()
    {
        if (!_fxShow || _trees.Scene is not { } sc || sc.Effects.Count == 0) return;
        const float R2 = 450 * 450;
        var near = sc.Effects.Where(f => (Gl(f.X, f.Y, f.Z) - _cam).LengthSquared <= R2 && FxMatch(f)).ToList();
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Texture2D);
        GL.Enable(EnableCap.PointSmooth);
        GL.PointSize(11); GL.Color3(0f, 0f, 0f); GL.Begin(PrimitiveType.Points); foreach (var f in near) GL.Vertex3(Gl(f.X, f.Y, f.Z)); GL.End();
        GL.PointSize(8); GL.Color3(1f, 0.55f, 0.1f); GL.Begin(PrimitiveType.Points); foreach (var f in near) GL.Vertex3(Gl(f.X, f.Y, f.Z)); GL.End();
        GL.PointSize(1); GL.Disable(EnableCap.PointSmooth);
        // nome do efeito embaixo do mouse (até 14 px)
        var m = _gl.PointToClient(Cursor.Position);
        WorldScene.SceneEffect? hit = null; float bd = 14;
        foreach (var f in near)
            if (Project(new Vector3(f.X, f.Y, f.Z)) is { } q && MathF.Sqrt((q.X - m.X) * (q.X - m.X) + (q.Y - m.Y) * (q.Y - m.Y)) is var d && d < bd) { bd = d; hit = f; }
        if (hit is not null) DrawLabel(FxName(hit), new Vector3(hit.X, hit.Y + 1.5f, hit.Z), Color.FromArgb(255, 190, 90));
        GL.Enable(EnableCap.DepthTest);
    }
}
