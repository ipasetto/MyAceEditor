using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>Teste --wq-shot: painel do NPC e editor de quest do Mundo 3D com dados de exemplo, desenhados fora da tela.</summary>
internal static class QuestShotTest
{
    sealed class Stub : IQuestHost
    {
        public QuestDraft? NewDraft(int giverNpc, out string? why) { why = null; return new QuestDraft { Id = 40001, Giver = giverNpc }; }
        public List<string> Validate(QuestDraft d) => d.Name.Length == 0 ? new() { "Falta o nome da quest", "Falta o NPC da etapa 1" } : new();
        public string Summary(QuestDraft d) => "Quest " + d.Id;
        public string? Commit(QuestDraft d) => null;
        public QuestDraft? EditDraft(int questId, out string? why) { why = null; return new QuestDraft { Id = questId, Editing = questId }; }
        public string? DeleteAsk(int questId) => "Excluir " + questId;
        public string? Delete(int questId) => null;
        public string? NameOf(int id) => id == 2001 ? "Ferreiro Wang" : id == 2002 ? "Guarda Li" : $"Item {id}";
        public bool IsNpc(int id) => id is 2001 or 2002;
        public IReadOnlyList<(int Id, string Name, int Giver)> SearchQuests(string text, int max) => Array.Empty<(int, string, int)>();
        public string? LinkExisting(int npcId, int questId) => null;
        public IReadOnlyList<QuestOption> Options(bool reward) => Array.Empty<QuestOption>();
        public IReadOnlyList<(string Key, string Label, string Tip)> FreeFields(bool reward) => Array.Empty<(string, string, string)>();
        public string? QuestName(int id) => "Quest " + id;
        public bool Undo() => false;
        public bool Redo() => false;
    }

    public static void Run(string png, int page = 0)
    {
        using var f = new Form { Width = 1100, Height = 760, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false, BackColor = Color.FromArgb(60, 80, 70) };
        var host = new Stub();
        var ed = new WorldQuestEditor(host, host.NewDraft(2001, out _)!, new QuestWorld(_ => { }, _ => { }, (_, _) => { }, () => 1));
        var panel = new WorldQuestPanel { CreateQuest = _ => { }, LinkQuest = _ => { }, EditQuest = _ => { }, DeleteQuest = _ => { } };
        f.Controls.Add(ed); f.Controls.Add(panel);
        f.Show();
        typeof(WorldQuestEditor).GetMethod("Show", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, new[] { typeof(int) })!.Invoke(ed, new object[] { page });
        panel.ShowNpc(new NpcQuestInfo(2001, "Ferreiro Wang", new[] { new NpcQuestRef(1001, "Carta para o ferreiro", true, true), new NpcQuestRef(1002, "Minério raro", false, true) },
            new[] { new NpcQuestRef(1003, "Espada quebrada", true, true) }));
        var until = DateTime.Now.AddSeconds(2);
        while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(20); }
        panel.GetType().GetMethod("SetMinimized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(panel, new object[] { false });
        until = DateTime.Now.AddSeconds(2);
        while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(20); }
        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
    }
}
