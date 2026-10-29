using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Pacotes onde ficam os modelos: o jogo acha o arquivo pela 1ª pasta do caminho (models\… no models.pck; no PWI 1.6
/// os NPCs vêm como textures\models\npcs\… e ficam no textures.pck). Abre cada pacote uma vez, na 1ª vez que é pedido.
/// </summary>
internal sealed class ModelPacks : IDisposable
{
    readonly string? _dir;
    readonly Dictionary<string, PckArchive?> _open = new(StringComparer.OrdinalIgnoreCase);

    public ModelPacks(string? elementDir) => _dir = elementDir;

    public bool Any => _dir is not null && File.Exists(Path.Combine(_dir, "models.pck"));

    /// <summary>Pacote do caminho (pela 1ª pasta); models.pck quando o caminho não diz.</summary>
    public PckArchive? For(string path)
    {
        if (_dir is null) return null;
        var p = path.Replace('/', '\\').TrimStart('\\');
        int slash = p.IndexOf('\\');
        var root = slash > 0 ? p[..slash] : "models";
        if (!File.Exists(Path.Combine(_dir, root + ".pck"))) root = "models";
        lock (_open)
        {
            if (!_open.TryGetValue(root, out var pck))
            {
                try { pck = new PckArchive(Path.Combine(_dir, root + ".pck")); } catch { pck = null; }
                _open[root] = pck;
            }
            return pck;
        }
    }

    public void Dispose() { lock (_open) { foreach (var p in _open.Values) p?.Dispose(); _open.Clear(); } }
}
