namespace AIControl;

public static class ResponseBudget
{
    public const int SafetyTokens=256;
    public const int MaxMessageCharacters=32_000_000;

    // All output, including reasoning, must share the loaded slot with its input.
    public static int Resolve(int capacity,int used,int requested,bool automatic)
    {
        if(capacity is <512 or >1048576||used<0||requested is <1 or >1048576)
            throw new InvalidDataException("Invalid context or output budget.");
        var available=Math.Max(0,capacity-used-SafetyTokens);
        return automatic?available:Math.Min(requested,available);
    }
}
