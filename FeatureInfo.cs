namespace ScanSkodaMirrorLinkCatalog
{
    public class FeatureInfo
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public bool Supported { get; set; }

        public SubFeatureInfo[] SubFeatures { get; set; }
    }
}