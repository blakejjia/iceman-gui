using System.Collections.Generic;

namespace iceman_gui.Models;

public sealed record KeyCheckResult(
    bool AllOpen,
    int OpenSectors,
    int TotalSectors,
    IReadOnlyList<int> LockedSectors);

public sealed record WriteVerification(
    bool Success,
    int MismatchedBlocks,
    string ActualUid,
    string Message);
