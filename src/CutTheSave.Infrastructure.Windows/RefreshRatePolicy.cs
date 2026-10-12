namespace CutTheSave.Infrastructure.Windows;

public enum RefreshRateAdvice
{
    Unknown,
    NoExtraLimitNeeded,
    RecommendLimit
}

public static class RefreshRatePolicy
{
    public static RefreshRateAdvice GetAdvice(int refreshRate) => refreshRate switch
    {
        60 => RefreshRateAdvice.NoExtraLimitNeeded,
        > 60 => RefreshRateAdvice.RecommendLimit,
        _ => RefreshRateAdvice.Unknown
    };

    public static bool CanApplyLimit(int refreshRate, bool hasRtssHooks) => refreshRate > 60 && hasRtssHooks;
}
