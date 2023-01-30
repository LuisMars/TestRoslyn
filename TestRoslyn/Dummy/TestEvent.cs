namespace TestRoslyn.Dummy;

public class TestEvent : IEvent
{
    private const string _test = "domain";
    private const string _test2 = "v2";
    private static readonly string test = $"{_test}.{_test2}";
}
