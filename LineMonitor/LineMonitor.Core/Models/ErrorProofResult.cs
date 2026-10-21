namespace LineMonitor.Core.Models;

public sealed record ErrorProofResult(int? DefectCode, int PcIndex, string DefectItem, long DetectedCount, int? ExpectedCount);
