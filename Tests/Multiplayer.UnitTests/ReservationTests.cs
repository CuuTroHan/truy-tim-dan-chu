using TruyTimDanChu.Server;
using TruyTimDanChu.Shared;
using Xunit;

namespace Multiplayer.UnitTests;

public sealed class ReservationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentReserve_OnlyOneOwnerGetsToken()
    {
        var service = new ReservationService(TimeSpan.FromSeconds(30)) { UtcNowProvider = () => Start };
        var key = new PuzzleReservationKey("match1", "team1", PuzzleIds.Mirrors);
        using var barrier = new Barrier(2);

        var a = Task.Run(() => { barrier.SignalAndWait(); return service.TryReserve(key, "a", "An"); });
        var b = Task.Run(() => { barrier.SignalAndWait(); return service.TryReserve(key, "b", "Bình"); });
        var results = await Task.WhenAll(a, b);

        Assert.Single(results, x => x.Success);
        Assert.Single(results, x => !x.Success && x.ErrorCode == PuzzleReservationErrorCodes.PuzzleOccupied);
        Assert.Single(results, x => x.ReservationToken is not null);
    }

    [Fact]
    public void ExpiredReservation_NewOwnerWins_AndOldTokenCannotSubmit()
    {
        var now = Start;
        var service = new ReservationService(TimeSpan.FromSeconds(30)) { UtcNowProvider = () => now };
        var key = new PuzzleReservationKey("match1", "team1", PuzzleIds.Mirrors);
        var first = service.TryReserve(key, "a", "An");

        now = now.AddSeconds(31);
        var second = service.TryReserve(key, "b", "Bình");

        Assert.True(second.Success);
        Assert.Equal("b", second.Reservation!.OwnerPlayerId);
        var stale = service.ValidateSubmission(key, "a", first.ReservationToken);
        Assert.False(stale.Success);
        Assert.Equal(PuzzleReservationErrorCodes.NotReservationOwner, stale.ErrorCode);
        Assert.True(service.ValidateSubmission(key, "b", second.ReservationToken).Success);
    }

    [Fact]
    public void OtherPlayerCannotReleaseOrSubmitOwnersReservation()
    {
        var service = new ReservationService { UtcNowProvider = () => Start };
        var key = new PuzzleReservationKey("match1", "team1", PuzzleIds.Mirrors);
        var owned = service.TryReserve(key, "a", "An");

        var release = service.TryRelease(key, "b", owned.ReservationToken);

        Assert.False(release.Success);
        Assert.Equal(PuzzleReservationErrorCodes.NotReservationOwner, release.ErrorCode);
        Assert.False(service.ValidateSubmission(key, "b", owned.ReservationToken).Success);
        Assert.True(service.ValidateSubmission(key, "a", owned.ReservationToken).Success);
    }

    [Fact]
    public void WrongTokenCannotReleaseOrSubmit()
    {
        var service = new ReservationService { UtcNowProvider = () => Start };
        var key = new PuzzleReservationKey("match1", "team1", PuzzleIds.Mirrors);
        service.TryReserve(key, "a", "An");

        Assert.Equal(PuzzleReservationErrorCodes.InvalidReservationToken,
            service.TryRelease(key, "a", "wrong").ErrorCode);
        Assert.Equal(PuzzleReservationErrorCodes.InvalidReservationToken,
            service.ValidateSubmission(key, "a", "wrong").ErrorCode);
    }

    [Fact]
    public void SamePuzzleInDifferentTeamsAndMatches_IsIndependent()
    {
        var service = new ReservationService { UtcNowProvider = () => Start };
        var red = service.TryReserve(new("match1", "red", PuzzleIds.Mirrors), "a", "An");
        var blue = service.TryReserve(new("match1", "blue", PuzzleIds.Mirrors), "c", "Chi");
        var nextMatch = service.TryReserve(new("match2", "red", PuzzleIds.Mirrors), "d", "Dũng");

        Assert.True(red.Success);
        Assert.True(blue.Success);
        Assert.True(nextMatch.Success);
    }

    [Fact]
    public void DisconnectRelease_OnlyRemovesOwnersLocks()
    {
        var service = new ReservationService { UtcNowProvider = () => Start };
        var aKey = new PuzzleReservationKey("match1", "red", PuzzleIds.Mirrors);
        var bKey = new PuzzleReservationKey("match1", "blue", PuzzleIds.Mirrors);
        service.TryReserve(aKey, "a", "An");
        var b = service.TryReserve(bKey, "b", "Bình");

        var released = service.ReleaseByOwner("a");

        Assert.Single(released);
        Assert.Equal(aKey, new PuzzleReservationKey(released[0].MatchId, released[0].TeamId, released[0].PuzzleId));
        Assert.True(service.ValidateSubmission(bKey, "b", b.ReservationToken).Success);
    }

    [Fact]
    public void PuzzleRequiresRangeChapterAndAllLamps()
    {
        var game = new TeamGameInstance("match1", "room1", "red", "Đội Đỏ", "#f00");
        game.AddMember("a", "An", "bao", 195, 323);

        Assert.False(game.CanReservePuzzle("a", PuzzleIds.Mirrors, out var missing));
        Assert.Equal(PuzzleReservationErrorCodes.PrerequisiteNotMet, missing);

        game.Progress.Chapter = TruyTimDanChu.Game.Chapter.Lights;
        Array.Fill(game.Progress.Lamps, true);
        Assert.True(game.CanReservePuzzle("a", PuzzleIds.Mirrors, out _));

        game.UpdateMemberPosition("a", 500, 366, 0, 0);
        Assert.False(game.CanReservePuzzle("a", PuzzleIds.Mirrors, out var far));
        Assert.Equal(InteractErrorCodes.OutOfRange, far);
    }

    [Fact]
    public void DraftPuzzleRequiresRangeAndDraftChapter()
    {
        var game = new TeamGameInstance("match1", "room1", "red", "Đội Đỏ", "#f00");
        // draft_board position is (486, 115)
        game.AddMember("a", "An", "bao", 486, 115);

        // Wrong chapter (default Opening)
        Assert.False(game.CanReservePuzzle("a", PuzzleIds.Draft, out var wrongChapter));
        Assert.Equal(PuzzleReservationErrorCodes.PrerequisiteNotMet, wrongChapter);

        // Right chapter
        game.Progress.Chapter = TruyTimDanChu.Game.Chapter.Draft;
        Assert.True(game.CanReservePuzzle("a", PuzzleIds.Draft, out _));

        // Far away from draft_board
        game.UpdateMemberPosition("a", 195, 323, 0, 0);
        Assert.False(game.CanReservePuzzle("a", PuzzleIds.Draft, out var far));
        Assert.Equal(InteractErrorCodes.OutOfRange, far);
    }

    [Fact]
    public async Task DraftReservation_TwoTeammates_OnlyOneOwner()
    {
        var service = new ReservationService(TimeSpan.FromSeconds(30)) { UtcNowProvider = () => Start };
        var key = new PuzzleReservationKey("match1", "team1", PuzzleIds.Draft);
        using var barrier = new Barrier(2);

        var a = Task.Run(() => { barrier.SignalAndWait(); return service.TryReserve(key, "a", "An"); });
        var b = Task.Run(() => { barrier.SignalAndWait(); return service.TryReserve(key, "b", "Bình"); });
        var results = await Task.WhenAll(a, b);

        Assert.Single(results, x => x.Success);
        Assert.Single(results, x => !x.Success && x.ErrorCode == PuzzleReservationErrorCodes.PuzzleOccupied);
        Assert.Single(results, x => x.ReservationToken is not null);
    }

    [Fact]
    public void DraftAndMirrors_UseIndependentReservationKeys()
    {
        var service = new ReservationService { UtcNowProvider = () => Start };
        var mirrorsKey = new PuzzleReservationKey("match1", "team1", PuzzleIds.Mirrors);
        var draftKey = new PuzzleReservationKey("match1", "team1", PuzzleIds.Draft);

        var mirrorsRes = service.TryReserve(mirrorsKey, "a", "An");
        var draftRes = service.TryReserve(draftKey, "b", "Bình");

        Assert.True(mirrorsRes.Success);
        Assert.True(draftRes.Success);
        Assert.NotEqual(mirrorsRes.ReservationToken, draftRes.ReservationToken);
        Assert.True(service.ValidateSubmission(mirrorsKey, "a", mirrorsRes.ReservationToken).Success);
        Assert.True(service.ValidateSubmission(draftKey, "b", draftRes.ReservationToken).Success);
    }
}
