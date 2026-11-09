using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;

namespace MyAceEditor.UI;

/// <summary>
/// Conta-gotas como MODO (usuário 09/11): o botão (ícone de conta-gotas) ou a tecla I liga; um círculo roxo pequeno
/// segue o mouse; o clique no chão pega a textura (Terreno) ou o tipo de grama/flor (Objetos do mundo) e já volta para o
/// pincel com o círculo dele. Botão direito ou Esc cancela; I de novo desliga. Antes o botão lia o ponto embaixo do mouse
/// enquanto o mouse estava em cima do próprio botão (não pegava nada com o pincel da grama ligado).
/// </summary>
internal sealed partial class WorldView
{
    int _dropper;                 // 0 desligado, 1 textura do chão, 2 grama
    const float DropR = 1f;       // raio do círculo roxo (fixo; não segue o tamanho do pincel)
    Button? _texDropBtn, _grassDropBtn;
    static readonly Color DropColor = Color.FromArgb(190, 110, 255);

    bool DropperOn => _dropper is 1 or 3 && _tool == Tool.Terrain && !_texActive && _sculpt < 0 && _waterMode == 0 && _lightMode == 0
                   || _dropper == 2 && _tool == Tool.Terrain && _brushPanel.Visible && !_brushActive;

    string DropperHint => _dropper switch
    {
        1 => Loc.T("drop.hintTex", "Conta-gotas: clique no chão para pegar a textura (Esc cancela)"),
        3 => Loc.T("drop.hintLight", "Conta-gotas da luz: clique no chão para pegar a luz dali (Esc cancela)"),
        _ => Loc.T("drop.hintGrass", "Conta-gotas: clique numa grama ou flor para pegar o tipo (Esc cancela)"),
    };

    void StartDropper(int kind)
    {
        if (DropperOn && _dropper == kind) { StopDropper(); return; }   // I de novo / botão de novo: desliga
        if (kind is 1 or 3) { _lightFamily = kind == 3; StopBrush(); if (_texDown) TerrainMouseUp(); _texActive = false; _sculpt = -1; _waterMode = 0; _lightMode = 0; _dropper = kind; TexLabels(); WaterLabels(); }
        else { if (_brushDown) EndDab(); _brushActive = false; _dropper = 2; BrushLabels(); }
        _status.Text = DropperHint;
        _gl.Focus(); _gl.Invalidate();
    }

    bool StopDropper()
    {
        if (_dropper == 0) return false;
        bool was = DropperOn;
        _dropper = 0; DropButtons(); _gl.Invalidate();
        return was;
    }

    /// <summary>Botão do conta-gotas aceso (roxo) enquanto o modo está ligado.</summary>
    void DropButtons()
    {
        foreach (var (b, k) in new[] { (_texDropBtn, 1), (_grassDropBtn, 2) })
        {
            if (b is null) continue;
            bool on = _dropper == k && DropperOn;
            b.BackColor = on ? Color.FromArgb(96, 60, 140) : Color.FromArgb(40, 45, 54);
            b.FlatAppearance.BorderColor = on ? DropColor : Color.FromArgb(62, 70, 84);
        }
    }

    bool DropperMouseDown(MouseEventArgs e)
    {
        if (_dropper == 0) return false;
        if (!DropperOn) { _dropper = 0; DropButtons(); return false; }
        if (e.Button == MouseButtons.Right) { StopDropper(); return true; }
        if (e.Button != MouseButtons.Left) return false;
        if (Ray(e.Location) is not { } g) return true;
        var at = new System.Numerics.Vector3(g.X, g.Y, g.Z);
        bool ok = _dropper switch { 1 => TexEyedropperAt(at), 3 => LightPickAt(at), _ => GrassEyedropperAt(at) };
        if (ok) { _dropper = 0; DropButtons(); }   // pegou: o pincel já ficou ligado com o que pegou
        _gl.Invalidate();
        return true;
    }

    /// <summary>Círculo roxo pequeno com uma cruz no meio, acompanhando o relevo.</summary>
    void DrawDropper()
    {
        if (!DropperOn || _hover is not { } h) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        GL.LineWidth(2.5f);
        GL.Color3(DropColor);
        GL.Begin(PrimitiveType.LineLoop);
        for (int i = 0; i < 40; i++)
        {
            float t = i / 40f * MathF.Tau, x = h.X + MathF.Sin(t) * DropR, z = h.Z + MathF.Cos(t) * DropR;
            GL.Vertex3(x, (_t.HeightAt(x, z) ?? h.Y) + 0.15f, -z);
        }
        GL.End();
        GL.Begin(PrimitiveType.Lines);
        float y = h.Y + 0.15f, c = DropR * 0.45f;
        GL.Vertex3(h.X - c, y, -h.Z); GL.Vertex3(h.X + c, y, -h.Z);
        GL.Vertex3(h.X, y, -(h.Z - c)); GL.Vertex3(h.X, y, -(h.Z + c));
        GL.End();
        GL.LineWidth(1); GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>
    /// Grama/flor mais perto do clique (até 1,5 m: o tufo desenhado fica em pé, o raio do mouse acerta o chão atrás dele)
    /// vira o tipo do pincel, que já liga.
    /// </summary>
    bool GrassEyedropperAt(System.Numerics.Vector3 h)
    {
        if (_grass.Scene is not { } sc) return false;
        const float R = 1.5f;
        int best = -1; float bestD = float.MaxValue;
        foreach (var g in sc.Grass)
        {
            float x0 = g.X - g.Cols * g.Cell / 2, z1 = g.Z + g.Rows * g.Cell / 2;
            int c0 = Math.Max(0, (int)MathF.Floor((h.X - R - x0) / g.Cell)), c1 = Math.Min(g.Cols - 1, (int)MathF.Floor((h.X + R - x0) / g.Cell));
            int r0 = Math.Max(0, (int)MathF.Floor((z1 - (h.Z + R)) / g.Cell)), r1 = Math.Min(g.Rows - 1, (int)MathF.Floor((z1 - (h.Z - R)) / g.Cell));
            for (int row = r0; row <= r1; row++)
                for (int col = c0; col <= c1; col++)
                {
                    if (!g.On(col, row)) continue;
                    float x = x0 + (col + 0.5f) * g.Cell, z = z1 - (row + 0.5f) * g.Cell, d = (x - h.X) * (x - h.X) + (z - h.Z) * (z - h.Z);
                    if (d < bestD && d <= R * R) { bestD = d; best = g.Type; }
                }
        }
        if (best < 0 || best >= sc.GrassTypes.Count) { _status.Text = Loc.T("grass.pickNone", "Não há grama embaixo do mouse."); return false; }
        _brushType = best; _brushErase = false; _brushActive = true; BrushLabels(); _gl.Invalidate();
        _status.Text = string.Format(Loc.T("grass.picked", "Pincel com o tipo daqui: {0}"), TextureNameGlossary.Display(sc.GrassTypes[best].Mox, _element));
        return true;
    }

    /// <summary>
    /// Para a captura ("dftdrop"): conta-gotas da grama (liga, clique numa grama perto da câmera pega o tipo e liga o pincel,
    /// I de novo desliga) e da textura (Terreno: clique pega a textura e liga o pincel); termina com o círculo roxo ligado.
    /// </summary>
    internal string DropperTest()
    {
        string load = DraftLoadTest();
        var r = new List<string>();
        OpenTool(Tool.Terrain);
        if (_grass.Scene is { } sc)
        {
            _brushPanel.Visible = true; _brushActive = false;
            StartDropper(2); bool on = DropperOn;
            System.Numerics.Vector3? at = null; int want = -1; float bestD = float.MaxValue;
            foreach (var g in sc.Grass)
            {
                float x0 = g.X - g.Cols * g.Cell / 2, z1 = g.Z + g.Rows * g.Cell / 2;
                for (int row = 0; row < g.Rows; row += 3)
                    for (int col = 0; col < g.Cols; col += 3)
                    {
                        if (!g.On(col, row)) continue;
                        float x = x0 + (col + 0.5f) * g.Cell, z = z1 - (row + 0.5f) * g.Cell, d = (x - _cam.X) * (x - _cam.X) + (z + _cam.Z) * (z + _cam.Z);
                        if (d < bestD) { bestD = d; at = new(x, _t.HeightAt(x, z) ?? 0, z); want = g.Type; }
                    }
            }
            bool got = at is { } a && GrassEyedropperAt(a);
            r.Add($"grama: ligou={on} pegou={got} pincel={_brushActive} tipo={_brushType} (perto={want}) depois-desligado={!DropperOn}");
            StartDropper(2); bool on2 = DropperOn; StartDropper(2); r.Add($"I liga={on2} I de novo desliga={!DropperOn}");
            StopBrush();
        }
        OpenTool(Tool.Terrain);
        StartDropper(1); bool tOn = DropperOn;
        var ct = new System.Numerics.Vector3(_cam.X + Forward.X * 15, 0, -(_cam.Z + Forward.Z * 15)); ct.Y = _t.HeightAt(ct.X, ct.Z) ?? 0;
        bool tGot = TexEyedropperAt(ct);
        r.Add($"textura: ligou={tOn} pegou={tGot} pincel={_texActive} textura={_texSel} desligado={!DropperOn}");
        StartDropper(1); _hover = new OpenTK.Mathematics.Vector3(ct.X, ct.Y, ct.Z);   // foto com o círculo roxo
        return load + " | dftdrop: " + string.Join("; ", r);
    }
}
