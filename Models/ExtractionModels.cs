namespace AutoActivator.Models
{
    public class ExtractionResult
    {
        public string FilePath { get; set; }
        public string StatusMessage { get; set; }
        public string InternalId { get; set; }
        public string UconId { get; set; }
        public string DemandId { get; set; }
        public string LisaContent { get; set; }
        public string EliaContent { get; set; }
    }

    public class BatchProgressInfo
    {
        public string ContractId { get; set; }
        public string InternalId { get; set; }
        public string Product { get; set; }
        public string Premium { get; set; }
        public string UconId { get; set; }
        public string DemandId { get; set; }
        public string Status { get; set; }
    }
}