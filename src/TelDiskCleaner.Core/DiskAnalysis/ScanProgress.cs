namespace TelDiskCleaner.Core.DiskAnalysis;

public sealed record ScanProgress(long FilesScanned, long BytesScanned, string CurrentPath);
