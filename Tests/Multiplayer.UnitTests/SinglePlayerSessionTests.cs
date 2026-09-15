using System.Text.Json;
using TruyTimDanChu.Game;
using Xunit;

public sealed class SinglePlayerSessionTests
{
    private static void AdvanceAllDialogue(IGameSession session)
    {
        while (session.Panel == Panel.Dialogue)
            session.AdvanceDialogue();
    }

    [Fact]
    public void InitialStateAndModelsSeparation()
    {
        IGameSession session = new SinglePlayerSession();
        Assert.False(session.IsMultiplayer);
        Assert.Equal(Chapter.Opening, session.Chapter);
        Assert.Equal(Panel.None, session.Panel);
        Assert.Equal(0, session.Shards);

        // Player state
        Assert.NotNull(session.Player);
        Assert.Equal(500, session.Player.X);
        Assert.Equal(366, session.Player.Y);
        Assert.Equal("Quang", session.Player.DisplayName);

        // Team progress
        Assert.NotNull(session.Progress);
        Assert.Equal(Chapter.Opening, session.Progress.Chapter);
        Assert.All(session.Progress.Spoken, Assert.False);
        Assert.All(session.Progress.Lamps, Assert.False);

        // Local UI state
        Assert.NotNull(session.UiState);
        Assert.Equal(Panel.None, session.UiState.Panel);
        Assert.True(session.UiDirty);
    }

    [Fact]
    public void OpeningDialogueTransitionsToLights()
    {
        IGameSession session = new SinglePlayerSession();
        session.Begin();
        Assert.Equal(Panel.Dialogue, session.Panel);
        Assert.NotEmpty(session.Dialogue);

        AdvanceAllDialogue(session);
        Assert.Equal(Chapter.Lights, session.Chapter);
        Assert.Equal(Panel.None, session.Panel);
        Assert.Equal("Phương", session.NextTarget);
    }

    [Fact]
    public void CompleteJourneyThroughSessionAbstraction()
    {
        IGameSession session = new SinglePlayerSession();
        session.Begin();
        AdvanceAllDialogue(session);
        Assert.Equal(Chapter.Lights, session.Chapter);

        // Lights
        for (var i = 0; i < 4; i++) session.Spoken[i] = true;
        for (var i = 0; i < 4; i++) session.Lamps[i] = true;
        session.OpenPanel(Panel.Mirrors);
        for (var i = 0; i < 4; i++)
        {
            var targetTurns = new[] { 1, 2, 3, 0 }[i];
            for (var t = 0; t < targetTurns; t++) session.TurnMirror(i);
        }
        session.CheckMirrors();
        AdvanceAllDialogue(session);
        Assert.Equal(Chapter.Draft, session.Chapter);
        Assert.Equal(1, session.Shards);
        Assert.True(session.Progress.DraftSequence.Length == 6);

        // Draft
        for (var i = 0; i < 6; i++) session.DraftSequence[i] = i;
        session.OpenPanel(Panel.Draft);
        session.CheckDraft();
        AdvanceAllDialogue(session);
        Assert.Equal(Chapter.River, session.Chapter);
        Assert.Equal(2, session.Shards);

        // River
        for (var i = 0; i < 4; i++) session.RiverSigns[i] = new[] { 1, 2, 3, 0 }[i];
        session.OpenPanel(Panel.River);
        session.CheckRiver();
        AdvanceAllDialogue(session);
        Assert.Equal(Chapter.News, session.Chapter);
        Assert.Equal(3, session.Shards);

        // News
        Array.Fill(session.Clues, true);
        session.OpenPanel(Panel.News);
        session.ChooseNews(2);
        AdvanceAllDialogue(session);
        Assert.Equal(Chapter.Finale, session.Chapter);
        Assert.Equal(4, session.Shards);

        // Finale
        session.OpenPanel(Panel.Finale);
        for (var i = 0; i < 4; i++) session.AddFinalePiece(i);
        AdvanceAllDialogue(session);
        Assert.Contains(4, session.FinaleSequence);

        for (var i = 0; i < 4; i++) session.AdvanceReturnStep(i);
        AdvanceAllDialogue(session);
        Assert.Equal(Chapter.Complete, session.Chapter);
        Assert.Equal(Panel.Sources, session.Panel);
    }

    [Fact]
    public void SaveAndLoadRestoresSessionFaithfully()
    {
        IGameSession session1 = new SinglePlayerSession();
        session1.Begin();
        AdvanceAllDialogue(session1);

        session1.Spoken[0] = true;
        session1.Lamps[0] = true;
        session1.SetMuted(true);
        session1.SetTextScale(150);

        var saveObj = session1.CreateSave();
        var json = JsonSerializer.Serialize(saveObj);

        IGameSession session2 = new SinglePlayerSession();
        session2.Load(json);

        Assert.Equal(session1.Chapter, session2.Chapter);
        Assert.True(session2.Spoken[0]);
        Assert.True(session2.Lamps[0]);
        Assert.True(session2.Muted);
        Assert.Equal(150, session2.TextScale);
    }

    [Fact]
    public void PresenterJumpUpdatesChapterAndCoordinates()
    {
        IGameSession session = new SinglePlayerSession();
        session.PresenterJump(Chapter.River);

        Assert.Equal(Chapter.River, session.Chapter);
        Assert.Equal(815, session.X);
        Assert.Equal(334, session.Y);
        Assert.Equal(815, session.Player.X);
        Assert.Equal(334, session.Player.Y);
    }
}

