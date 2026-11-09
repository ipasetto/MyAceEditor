namespace MyAceEditor.UI;

/// <summary>
/// Fotos da página do GitHub (--readme-shots): cada cena abre uma ferramenta com algo escolhido e a foto junta o 3D (que o
/// DrawToBitmap da janela não pega), o painel da ferramenta e o cartão de informação. Só olha: nada é mexido nem gravado.
/// </summary>
internal sealed partial class WorldView
{
    /// <summary>Espera o carregamento em volta da câmera acalmar (até 90 s).</summary>
    internal void ShotWait(double quietSeconds = 1.5)
    {
        var until = DateTime.Now.AddSeconds(90); var quiet = DateTime.MaxValue;
        while (DateTime.Now < until)
        {
            Application.DoEvents(); Thread.Sleep(30);
            if (Loading) quiet = DateTime.MaxValue; else if (quiet == DateTime.MaxValue) quiet = DateTime.Now; else if ((DateTime.Now - quiet).TotalSeconds > quietSeconds) break;
        }
    }

    /// <summary>Janela inteira com o 3D por baixo e os painéis flutuantes por cima, na ordem em que aparecem.</summary>
    internal Bitmap ComposeShot()
    {
        var full = new Bitmap(Width, Height);
        DrawToBitmap(full, new Rectangle(0, 0, Width, Height));
        var origin = PointToScreen(Point.Empty);
        int ox = origin.X - Left, oy = origin.Y - Top;
        using var g = Graphics.FromImage(full);
        Point At(Control c) => PointToClient(c.Parent!.PointToScreen(c.Location));
        using (var frame = CaptureFrame()) if (frame is not null) { var p = At(_gl); g.DrawImage(frame, ox + p.X, oy + p.Y, _gl.Width, _gl.Height); }
        void Over(Control? c)
        {
            if (c is null || !c.Visible || c.Width <= 0 || c.Height <= 0) return;
            using var b = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
            var p = At(c); g.DrawImage(b, ox + p.X, oy + p.Y);
        }
        Over(_domains); Over(_info); Over(_tools);
        return full;
    }

    /// <summary>Monta uma cena das fotos. Devolve uma linha do que ficou na tela (para conferir).</summary>
    internal string ShotScene(string name)
    {
        Point center = new(_gl.Width / 2, _gl.Height * 3 / 5);
        string Hover() { _hover = Ray(center); return _hover is null ? "sem chão no meio" : "pincel no meio"; }
        if (_tool != Tool.None) CloseTool();
        switch (name)
        {
            case "hero":
                return "menu de ferramentas";
            case "objects":
            {
                DraftLoadTest(); ShotWait();   // abre Objetos e espera os pedaços em volta (o cartão completo precisa do rascunho)
                var ahead = Ray(center) is { } g ? new OpenTK.Mathematics.Vector3(g.X, g.Y, -g.Z) : _cam + Forward * 70;   // o que está no meio da tela
                if (_buildings.Nearest(ahead) is not { } near) return "nenhuma construção";
                _sceneSel = new BuildingSel(near.File, near.Center); ShowObjPanel(); _gl.Invalidate();
                return "construção " + Path.GetFileName(near.File);
            }
            case "terrain":
            {
                OpenTool(Tool.Terrain);
                string h = Hover();
                if (_hover is { } p) TexEyedropperAt(new System.Numerics.Vector3(p.X, p.Y, p.Z));
                return h + ", textura " + _texSel;
            }
            case "grass":
                OpenTool(Tool.Terrain); OpenGrassPanel();
                if (_grass.Scene is { GrassTypes.Count: > 0 }) { _brushType = 0; BrushPaint(); }
                return Hover() + ", grama " + _brushType;
            case "light":
                OpenTool(Tool.Terrain); SetLightMode(1);
                return Hover();
            case "paths":
                OpenTool(Tool.Paths); _gl.Invalidate();
                return "caminhos";
            case "quests":
            {
                OpenTool(Tool.Quests);
                var camGame = new System.Numerics.Vector3(_cam.X, _cam.Y, -_cam.Z);
                foreach (var m in _allMarks.Where(m => m.Id > 0).OrderBy(m => (m.X - camGame.X) * (m.X - camGame.X) + (m.Z - camGame.Z) * (m.Z - camGame.Z)).Take(80))
                    if (NpcQuests?.Invoke(m.Id) is { Gives.Count: > 0 } q) { _questNpc = m.Id; ShowQuestTool(q); return "NPC " + m.Id; }
                return "nenhum NPC com quest perto";
            }
            case "npcs":
                OpenTool(Tool.Create);
                return "criar";
            case "teleports":
                OpenTool(Tool.Teleports);
                return "teleportes";
            case "zones":
                ZoneShot(1);
                return "zona aberta";
            case "territories":
                OpenTool(Tool.Domains);
                return _domains is null ? "sem territórios" : "territórios";
        }
        return "cena desconhecida";
    }
}
