namespace TruyTimDanChu.Shared;

public static class ReadinessValidator
{
    public static StartMatchValidationResult Validate(
        RoomSnapshot? room,
        IReadOnlyList<PlayerSnapshot> players,
        IReadOnlyList<TeamSnapshot> teams)
    {
        var blocking = new List<string>();
        var warnings = new List<string>();

        if (room is null)
        {
            blocking.Add("Phòng không tồn tại.");
            return new StartMatchValidationResult(false, blocking, warnings);
        }

        if (room.Status != RoomStatus.Lobby)
        {
            blocking.Add($"Trạng thái phòng hiện tại là {room.Status}, không thể bắt đầu trận.");
            return new StartMatchValidationResult(false, blocking, warnings);
        }

        if (teams.Count == 0)
        {
            blocking.Add("Chưa có đội thi đấu nào được thiết lập.");
        }
        else if (teams.Count < 2)
        {
            blocking.Add("Cần ít nhất 2 đội thi đấu để có thể bắt đầu trận đấu.");
        }

        if (players.Count == 0)
        {
            blocking.Add("Phòng chưa có người chơi nào.");
        }

        var unassigned = players.Where(p => string.IsNullOrEmpty(p.TeamId)).ToList();
        if (unassigned.Count > 0)
        {
            blocking.Add($"Còn {unassigned.Count} người chơi chưa được phân vào đội.");
        }

        var notReady = players.Where(p => !string.IsNullOrEmpty(p.TeamId) && !p.IsReady).ToList();
        if (notReady.Count > 0)
        {
            blocking.Add($"Còn {notReady.Count} thành viên trong đội chưa sẵn sàng.");
        }

        var emptyTeams = teams.Where(t => t.MemberCount == 0).ToList();
        if (emptyTeams.Count > 0)
        {
            blocking.Add($"Đội '{emptyTeams[0].Name}' chưa có thành viên nào tham gia.");
        }

        // Warnings for uneven team sizes
        if (teams.Count >= 2 && emptyTeams.Count == 0)
        {
            var distinctCounts = teams.Select(t => t.MemberCount).Distinct().ToList();
            if (distinctCounts.Count > 1)
            {
                var summary = string.Join(", ", teams.Select(t => $"{t.Name}: {t.MemberCount} người"));
                warnings.Add($"Số lượng thành viên giữa các đội không đồng đều ({summary}).");
            }
        }

        return new StartMatchValidationResult(blocking.Count == 0, blocking, warnings);
    }
}

