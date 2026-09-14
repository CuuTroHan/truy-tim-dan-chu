using TruyTimDanChu.Game;

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void FinishDialogue(GameEngine game)
{
    while (game.Panel == Panel.Dialogue) game.AdvanceDialogue();
}

var game = new GameEngine();
game.Begin();
Require(game.Panel == Panel.Dialogue, "Mở đầu cần hội thoại.");
FinishDialogue(game);
Require(game.Chapter == Chapter.Lights, "Hội thoại mở đầu phải vào nhiệm vụ ánh sáng.");
Require(game.NextTarget == "Phương", "Mục tiêu đầu tiên phải chỉ rõ Phương.");
game.Spoken[0] = true;
Require(game.NextTarget == "Dũng", "Mục tiêu phải chuyển lần lượt sang Dũng.");

for (var i = 0; i < 4; i++) game.Spoken[i] = true;
Require(game.NextTarget == "Bệ đèn của Phương", "Sau hội thoại phải chỉ rõ bệ đèn đầu tiên.");
for (var i = 0; i < 4; i++) game.Lamps[i] = true;
Require(game.NextTarget == "Gương trung tâm", "Sau khi đặt đèn phải chỉ gương trung tâm.");
game.OpenPanel(Panel.Mirrors);
for (var i = 0; i < 4; i++)
    for (var turn = 0; turn < new[] { 1, 2, 3, 0 }[i]; turn++) game.TurnMirror(i);
game.CheckMirrors();
FinishDialogue(game);
Require(game.Chapter == Chapter.Draft && game.Shards == 1, "Câu đố gương cần trao mảnh thứ nhất.");

for (var i = 0; i < 6; i++) game.DraftSequence[i] = i;
game.OpenPanel(Panel.Draft);
game.CheckDraft();
FinishDialogue(game);
Require(game.Chapter == Chapter.River && game.Shards == 2, "Câu đố dự thảo cần trao mảnh thứ hai.");

game.OpenPanel(Panel.River);
game.CheckRiver();
Require(game.Toast?.Contains("trạm 1") == true, "Biển sai đầu tiên phải được báo đúng trạm.");
for (var i = 0; i < 4; i++) game.RiverSigns[i] = new[] { 1, 2, 3, 0 }[i];
game.CheckRiver();
FinishDialogue(game);
Require(game.Chapter == Chapter.News && game.Shards == 3, "Câu đố ven sông cần trao mảnh thứ ba.");

game.OpenPanel(Panel.News);
game.ChooseNews(0);
Require(game.NewsNoise == 1 && game.Chapter == Chapter.News, "Chia sẻ ngay phải có hậu quả đảo ngược được.");
game.ChooseNews(2);
Require(game.Chapter == Chapter.News, "Không được gửi phản ánh khi thiếu chứng cứ.");
Array.Fill(game.Clues, true);
game.ChooseNews(2);
FinishDialogue(game);
Require(game.Chapter == Chapter.Finale && game.NewsNoise == 0, "Xác minh phải mở câu đố cuối và giảm nhiễu.");

game.OpenPanel(Panel.Finale);
for (var i = 0; i < 4; i++) game.AddFinalePiece(i);
FinishDialogue(game);
Require(game.Panel == Panel.Finale && game.FinaleSequence.Contains(4), "Nút thắt phản hồi phải xuất hiện.");
for (var i = 0; i < 4; i++) game.AdvanceReturnStep(i);
FinishDialogue(game);
Require(game.Chapter == Chapter.Complete && game.Panel == Panel.Sources, "Game cần tới màn kết thúc.");

var save = game.CreateSave();
var resumed = new GameEngine();
resumed.Load(System.Text.Json.JsonSerializer.Serialize(save));
Require(resumed.Chapter == Chapter.Complete && resumed.Shards == 4, "Bản lưu phải khôi phục tiến độ.");

Console.WriteLine("PASS: mở đầu, 4 nhiệm vụ, lựa chọn sai, phản hồi cuối và lưu/tiếp tục.");
