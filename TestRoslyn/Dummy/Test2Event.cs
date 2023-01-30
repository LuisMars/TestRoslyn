namespace TestRoslyn.Dummy;

public class Test2Event : IEvent
{
    private const string _test = "domain";
    private const string _test2 = "v1";
    private static readonly string test = $"{_test}.{_test2}";
}
