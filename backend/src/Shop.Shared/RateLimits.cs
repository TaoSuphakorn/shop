namespace Shop.Shared;

/// <summary>Names of the rate limit policies the host defines and the modules apply to their endpoints.</summary>
public static class RateLimits
{
    public const string Cart = "cart";
    public const string Checkout = "checkout";
}
