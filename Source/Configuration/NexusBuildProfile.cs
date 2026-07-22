namespace ComfyUI_Nexus.Configuration;

internal static class NexusBuildProfile
{
#if NEXUS_STORE_DISTRIBUTION
	internal const string DistributionProfile = "Store";
	internal const bool IsStoreDistribution = true;
#else
	internal const string DistributionProfile = "Portable";
	internal const bool IsStoreDistribution = false;
#endif
}
