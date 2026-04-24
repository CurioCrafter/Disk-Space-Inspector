namespace DiskSpaceInspector.Core.Windows;

public sealed record FileIdentity(string VolumeSerial, string FileId, int HardLinkCount);
