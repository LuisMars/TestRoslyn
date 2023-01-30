namespace TestRoslyn.Dummy;
public class SubscriberTest
{

    public void Execute(TestEvent e)
    {
        var c = new Caller();
        c.Call();
    }
}
