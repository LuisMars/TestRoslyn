namespace TestRoslyn.Dummy;
public class Subscriber
{
    public Subscriber()
    {
    }

    public void Execute(Test2Event e)
    {
        var c = new Caller();
        c.CallParent();
    }
}
