namespace TFTStats.Core.Entities
{
    public record SetTimeRange
    {

        public long PatchStartTime { get; set; }
        public long PatchEndTime { get; set; }

        public SetTimeRange(TFTPatch patch)
        {
            PatchStartTime = ((DateTimeOffset)patch.StartDate).ToUnixTimeSeconds();
            PatchEndTime = ((DateTimeOffset)patch.EndDate!).ToUnixTimeSeconds();
        }

        public SetTimeRange(TFTPatch latestPatch, TFTPatch earliestPatch)
        {
            PatchStartTime = ((DateTimeOffset)earliestPatch.StartDate).ToUnixTimeSeconds();
            PatchEndTime = ((DateTimeOffset)latestPatch.EndDate!).ToUnixTimeSeconds();
        }
    }
}
