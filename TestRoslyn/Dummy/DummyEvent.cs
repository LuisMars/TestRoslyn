namespace TestRoslyn.Dummy;

public class DummyEvent : IEvent
{
    private const string _test = "domain.v3";
    private static readonly string test = $"{_test}";
}
