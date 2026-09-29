namespace Defender_Cab_Verification_Tool
{
    public class FileVerificationResult
    {
        public string FilePath { get; set; }
        public bool IsValid { get; set; }
        public string Thumbprint { get; set; }
        public string ErrorDetail { get; set; }
    }
}